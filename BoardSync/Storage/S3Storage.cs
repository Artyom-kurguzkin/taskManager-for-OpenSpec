using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BoardSync.PublicDTOs;

namespace BoardSync.Storage;

/// <summary>
/// An S3-compatible bucket (Cloudflare R2, Amazon S3, MinIO) over plain HttpClient with a hand-written AWS Signature Version 4.
/// Path-style URLs: {endpoint}/{bucket}/{prefix}/{path}. The version is the ETag exactly as the server returns it (quotes included).
/// </summary>
internal sealed partial class S3Storage : IBoardStorage, IDisposable
{
    private const string EmptyPayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private readonly Uri _endpoint;
    private readonly string _bucket;
    private readonly string _prefix;   // "" or "a/b/" (trailing slash)
    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly string _region;
    private readonly HttpClient _http;
    private readonly HttpMessageHandler _handler;

    /// <summary>
    /// One connection pool for every storage built without a handler of its own (a pass builds a new S3Storage each time; each would otherwise open a new TLS connection).
    /// Connections are renewed after 5 minutes so a changed address is picked up. A signed request is for one address: redirects are never followed, and no cookies are kept.
    /// Never disposed: it lives as long as the process, and a storage's Dispose leaves it alone.
    /// </summary>
    private static readonly SocketsHttpHandler SharedHandler = new() { AllowAutoRedirect = false, UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };

    /// <summary>The handler this storage sends through (a test seam): the shared pool unless the caller passed its own.</summary>
    internal HttpMessageHandler Handler => _handler;

    // ponytail: constants; make them settings if a board ever outgrows them.
    private const int MaxListPages = 1000;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    public S3Storage(string endpoint, string bucket, string? prefix, string accessKey, string secretKey, string region, HttpMessageHandler? handler)
    {
        // What is typed into the endpoint field may hold a password, so no message below repeats it.
        if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/'), UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new BoardStorageException("The endpoint is not a web address. Use the storage's endpoint, for example https://<account>.r2.cloudflarestorage.com.");
        if (uri.UserInfo.Length > 0) throw new BoardStorageException("The endpoint must not contain a user name or password. Enter the access key id and the secret in their own fields.");
        if (uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new BoardStorageException("The endpoint must be an address only, without a query (?) or a fragment (#).");
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            throw new BoardStorageException("The endpoint uses plain http, which sends the board and the signed requests in clear text. Use an https address (http is only accepted for localhost).");
        if (string.IsNullOrWhiteSpace(bucket)) throw new BoardStorageException("The bucket name is empty.");
        _endpoint = uri;
        _bucket = bucket.Trim();
        CheckParts(_bucket, "bucket name");
        string trimmed = (prefix ?? "").Trim().Trim('/');
        if (trimmed.Length > 0) CheckParts(trimmed, "prefix");
        _prefix = trimmed.Length == 0 ? "" : trimmed + "/";
        _accessKey = accessKey;
        _secretKey = secretKey;
        _region = string.IsNullOrWhiteSpace(region) ? "auto" : region;
        // A signed request is for one address: a redirect is never followed (it would also re-send the board to wherever it points).
        _handler = handler ?? SharedHandler;
        _http = new HttpClient(_handler, disposeHandler: false);
        _http.Timeout = RequestTimeout;
    }

    /// <summary>Releases this storage's HttpClient. The connection pool (the shared handler, or the caller's own) stays.</summary>
    public void Dispose() => _http.Dispose();

    /// <summary>A bucket name or prefix is plain names between slashes: no empty, "." or ".." part (those would climb out of the configured place).</summary>
    private static void CheckParts(string value, string what)
    {
        foreach (string part in value.Split('/'))
            if (part.Length == 0 || part is "." or "..") throw new BoardStorageException($"The {what} has an empty, \".\" or \"..\" part. Use plain names separated by single slashes.");
    }

    public bool ConditionalWrites => true;

    public async Task<StoredFile?> Get(string path, CancellationToken ct = default)
    {
        S3Response r = await Send(HttpMethod.Get, ObjectPath(path), [], null, null, "read", path, ct);
        if (r.Status == HttpStatusCode.NotFound) return null;
        if (!r.Ok) throw Unexpected(r, "read", path);
        return new StoredFile(r.Body, r.ETag ?? FallbackVersion(r.Body));
    }

    /// <summary>Streams the object into <paramref name="destination"/> as it arrives: same request, errors and cap as <see cref="Get"/>, but the body is never held whole in memory.</summary>
    public async Task<bool> GetTo(string path, Stream destination, CancellationToken ct = default)
    {
        S3Response r = await Send(HttpMethod.Get, ObjectPath(path), [], null, null, "read", path, ct, destination);
        if (r.Status == HttpStatusCode.NotFound) return false;
        if (!r.Ok) throw Unexpected(r, "read", path);
        return true;
    }

    public async Task<StoredFile?> GetIfChanged(string path, string? version, CancellationToken ct = default)
    {
        S3Response r = await Send(HttpMethod.Get, ObjectPath(path), [], null, req =>
        {
            if (version is not null) req.Headers.TryAddWithoutValidation("If-None-Match", version);
        }, "read", path, ct);
        if (r.Status == HttpStatusCode.NotModified) return StoredFile.NotModified;
        if (r.Status == HttpStatusCode.NotFound) return null;
        if (!r.Ok) throw Unexpected(r, "read", path);
        return new StoredFile(r.Body, r.ETag ?? FallbackVersion(r.Body));
    }

    public async Task<PutResult> Put(string path, byte[] bytes, PutCondition condition, CancellationToken ct = default)
    {
        S3Response r = await Send(HttpMethod.Put, ObjectPath(path), [], bytes, req =>
        {
            if (condition.Kind == PutKind.OnlyIfAbsent) req.Headers.TryAddWithoutValidation("If-None-Match", "*");
            else if (condition.Kind == PutKind.OnlyIfVersion) req.Headers.TryAddWithoutValidation("If-Match", condition.Version);
        }, "write", path, ct);
        if (r.Ok) return new PutResult(true, r.ETag ?? FallbackVersion(bytes));
        if (r.Status is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict) return new PutResult(false, null);
        if (r.Status == HttpStatusCode.NotFound && condition.Kind == PutKind.OnlyIfVersion) return new PutResult(false, null);
        throw Unexpected(r, "write", path);
    }

    public async Task Delete(string path, CancellationToken ct = default)
    {
        S3Response r = await Send(HttpMethod.Delete, ObjectPath(path), [], null, null, "delete", path, ct);
        if (!r.Ok && r.Status != HttpStatusCode.NotFound) throw Unexpected(r, "delete", path);
    }

    public async Task<IReadOnlyList<StoredEntry>> List(string prefix, CancellationToken ct = default)
    {
        StoragePath.CheckPrefix(prefix);
        List<StoredEntry> entries = [];
        string? token = null;
        HashSet<string> tokens = new(StringComparer.Ordinal);
        for (int page = 1; ; page++)
        {
            if (page > MaxListPages) throw new BoardStorageException($"The storage's listing of \"{prefix}\" did not end after {MaxListPages} pages, so it was given up on.");
            List<KeyValuePair<string, string>> query = [new("list-type", "2"), new("prefix", _prefix + prefix)];
            if (token is not null) query.Add(new("continuation-token", token));
            S3Response r = await Send(HttpMethod.Get, "/" + _bucket, query, null, null, "list", prefix, ct);
            if (!r.Ok) throw Unexpected(r, "list", prefix);

            XDocument xml;
            try
            {
                // No DOCTYPE at all: entity declarations have no place in a bucket listing.
                using System.Xml.XmlReader reader = System.Xml.XmlReader.Create(new StringReader(System.Text.Encoding.UTF8.GetString(r.Body)), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
                xml = XDocument.Load(reader);
            }
            catch (System.Xml.XmlException e) { throw new BoardStorageException($"The storage answered the listing with something that is not a bucket listing. Check that the endpoint {_endpoint} is an S3-compatible address.", e); }
            XElement root = xml.Root ?? throw new BoardStorageException("The storage answered the listing with an empty document.");
            foreach (XElement item in root.Elements().Where(e => e.Name.LocalName == "Contents"))
            {
                string key = Child(item, "Key") ?? "";
                if (key.StartsWith(_prefix, StringComparison.Ordinal)) key = key[_prefix.Length..];
                entries.Add(new StoredEntry(key, Child(item, "ETag") ?? ""));
            }
            token = Child(root, "IsTruncated") == "true" ? Child(root, "NextContinuationToken") : null;
            if (token is null) return entries;
            if (!tokens.Add(token)) throw new BoardStorageException($"The storage answered the listing of \"{prefix}\" with the same continuation token again, so it was given up on.");
        }
    }

    private static string? Child(XElement parent, string name) => parent.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    private string ObjectPath(string path) => "/" + _bucket + "/" + _prefix + StoragePath.Check(path);

    private static string FallbackVersion(byte[] bytes) => "\"" + Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant() + "\"";

    private sealed record S3Response(HttpStatusCode Status, byte[] Body, string? ETag)
    {
        public bool Ok => (int)Status is >= 200 and < 300;
    }

    /// <summary>Signs and sends one request. Refused keys, a missing bucket, server errors and network failures throw; everything else comes back for the caller to read.</summary>
    private async Task<S3Response> Send(HttpMethod method, string path, IReadOnlyList<KeyValuePair<string, string>> query, byte[]? body,
        Action<HttpRequestMessage>? headers, string what, string subject, CancellationToken ct, Stream? sink = null)
    {
        string canonicalQuery = S3Signer.CanonicalQuery(query);
        string encodedPath = S3Signer.EncodePath(path.TrimStart('/'));
        string basePath = _endpoint.AbsolutePath.TrimEnd('/');
        Uri uri = new($"{_endpoint.GetLeftPart(UriPartial.Authority)}{basePath}/{encodedPath}{(canonicalQuery.Length > 0 ? "?" + canonicalQuery : "")}");
        string host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string payloadHash = body is null ? EmptyPayloadHash : S3Signer.Sha256Hex(body);

        using HttpRequestMessage request = new(method, uri);
        if (body is not null) request.Content = new ByteArrayContent(body);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", S3Signer.AmzDate(now));
        request.Headers.TryAddWithoutValidation("Authorization",
            S3Signer.Authorization(method.Method, uri.AbsolutePath, canonicalQuery, new Dictionary<string, string> { ["host"] = host }, payloadHash, now, _region, _accessKey, _secretKey));
        headers?.Invoke(request);

        S3Response result;
        long cap = what == "read" ? StorageLimits.For(subject) : StorageLimits.Listing;
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(RequestTimeout);       // the whole exchange, body included: a trickling answer does not hold the sync for ever
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            string? etag = response.Headers.TryGetValues("ETag", out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
            byte[] content;
            if (sink is not null && response.IsSuccessStatusCode)
            {
                // Streamed: the announced length is checked first so an oversized object writes nothing; the cap is then enforced chunk by chunk.
                if (response.Content.Headers.ContentLength > cap) throw StorageLimits.TooBig(subject, cap);
                await using Stream received = await response.Content.ReadAsStreamAsync(deadline.Token);
                await StorageLimits.ReadCapped(received, sink, cap, subject, deadline.Token);
                content = [];
            }
            else content = await StorageLimits.ReadCapped(response.Content, cap, subject, deadline.Token);
            result = new S3Response(response.StatusCode, content, etag);
        }
        catch (HttpRequestException e)
        {
            throw new BoardStorageException($"Could not reach the storage at {_endpoint.GetLeftPart(UriPartial.Authority)}: {e.Message} Check the endpoint address and the network connection.", e);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new BoardStorageException($"The storage at {_endpoint.GetLeftPart(UriPartial.Authority)} did not answer in time. Check the network connection and try again.", e);
        }

        if ((int)result.Status is >= 300 and < 400 && result.Status != HttpStatusCode.NotModified)
            throw new BoardStorageException($"The storage answered with a redirect to another address ({(int)result.Status}) when trying to {what} \"{subject}\". Repo Tasks does not follow redirects: check that the endpoint is the storage's own address (and its region).");
        if (result.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            || (result.Status == HttpStatusCode.NotFound && ErrorCode(result) == "NoSuchBucket")
            || (int)result.Status >= 500)
            throw Unexpected(result, what, subject);
        return result;
    }

    private BoardStorageException Unexpected(S3Response r, string what, string subject)
    {
        int code = (int)r.Status;
        string detail = ErrorCode(r) is { } c ? $" ({c})" : "";
        if (r.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new BoardStorageException($"The storage refused the key ending in {KeyEnd()} ({code}){detail} when trying to {what} \"{subject}\". Check the access key id and secret, and that the key may read and write bucket \"{_bucket}\".");
        if (r.Status == HttpStatusCode.NotFound && ErrorCode(r) == "NoSuchBucket")
            return new BoardStorageException($"The bucket \"{_bucket}\" does not exist at {_endpoint.GetLeftPart(UriPartial.Authority)} ({code}). Check the bucket name and the endpoint.");
        if (code >= 500)
            return new BoardStorageException($"The storage had a problem ({code}){detail} when trying to {what} \"{subject}\". Try again in a moment.");
        return new BoardStorageException($"The storage answered {code}{detail} when trying to {what} \"{subject}\", which was not expected. Check the endpoint, the bucket and the key's permissions.");
    }

    /// <summary>Only the last 4 characters of the access key id are ever shown.</summary>
    private string KeyEnd() => _accessKey.Length <= 4 ? "…" + _accessKey : "…" + _accessKey[^4..];

    private static string? ErrorCode(S3Response r)
    {
        if (r.Body.Length == 0 || r.Body.Length > 16_384) return null;
        Match m = ErrorCodePattern().Match(System.Text.Encoding.UTF8.GetString(r.Body));
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex("<Code>([^<]+)</Code>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ErrorCodePattern();
}
