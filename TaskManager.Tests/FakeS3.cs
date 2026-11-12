using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using BoardSync.Storage;

namespace TaskManager.Tests;

/// <summary>
/// An in-memory S3 server for tests: objects in a dictionary, ETag = quoted MD5, If-Match / If-None-Match on PUT and GET,
/// ListObjectsV2 with two keys per page, DELETE. It recomputes every request's AWS Signature Version 4 from what was sent and refuses a mismatch.
/// </summary>
internal sealed class FakeS3 : HttpMessageHandler
{
    public const string AccessKey = "AKIAFAKEKEYFORTESTS";
    public const string SecretKey = "fakeSecretKeyForTests/abc123";
    public const string Region = "auto";
    public const string Bucket = "board-bucket";

    public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);   // key: "<bucket>/<key>"
    public int PageSize { get; set; } = 2;
    public bool RefuseEverything { get; set; }
    public int Requests { get; private set; }

    public static string ETagOf(byte[] bytes) => "\"" + Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant() + "\"";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Objects) { Requests++; return Task.FromResult(Handle(request)); }
    }

    private HttpResponseMessage Handle(HttpRequestMessage request)
    {
        string? problem = CheckSignature(request);
        if (problem is not null) return Error(HttpStatusCode.Forbidden, "SignatureDoesNotMatch", problem);
        if (RefuseEverything) return Error(HttpStatusCode.Forbidden, "AccessDenied", "Access Denied");

        Uri uri = request.RequestUri!;
        string path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        string[] split = path.Split('/', 2);
        if (split[0] != Bucket) return Error(HttpStatusCode.NotFound, "NoSuchBucket", "The specified bucket does not exist");
        string key = split.Length > 1 ? split[1] : "";
        Dictionary<string, string> query = ParseQuery(uri);

        if (request.Method == HttpMethod.Get && key == "" && query.ContainsKey("list-type")) return List(query);

        string objectKey = Bucket + "/" + key;
        bool exists = Objects.TryGetValue(objectKey, out byte[]? current);
        string? ifMatch = Header(request, "If-Match");
        string? ifNoneMatch = Header(request, "If-None-Match");

        if (request.Method == HttpMethod.Get)
        {
            if (!exists) return Error(HttpStatusCode.NotFound, "NoSuchKey", "The specified key does not exist.");
            if (ifNoneMatch is not null && ifNoneMatch == ETagOf(current!)) return WithETag(new HttpResponseMessage(HttpStatusCode.NotModified), current!);
            return WithETag(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(current!) }, current!);
        }
        if (request.Method == HttpMethod.Put)
        {
            if (ifNoneMatch == "*" && exists) return Error(HttpStatusCode.PreconditionFailed, "PreconditionFailed", "At least one of the pre-conditions you specified did not hold");
            if (ifMatch is not null)
            {
                if (!exists) return Error(HttpStatusCode.NotFound, "NoSuchKey", "The specified key does not exist.");
                if (ifMatch != ETagOf(current!)) return Error(HttpStatusCode.PreconditionFailed, "PreconditionFailed", "At least one of the pre-conditions you specified did not hold");
            }
            byte[] body = request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            Objects[objectKey] = body;
            return WithETag(new HttpResponseMessage(HttpStatusCode.OK), body);
        }
        if (request.Method == HttpMethod.Delete)
        {
            Objects.Remove(objectKey);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
        return Error(HttpStatusCode.MethodNotAllowed, "MethodNotAllowed", request.Method.Method);
    }

    private HttpResponseMessage List(Dictionary<string, string> query)
    {
        string prefix = query.GetValueOrDefault("prefix", "");
        string? after = query.GetValueOrDefault("continuation-token");
        List<string> keys = Objects.Keys
            .Where(k => k.StartsWith(Bucket + "/", StringComparison.Ordinal)).Select(k => k[(Bucket.Length + 1)..])
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Where(k => after is null || string.CompareOrdinal(k, after) > 0)
            .ToList();
        List<string> page = keys.Take(PageSize).ToList();
        bool truncated = keys.Count > page.Count;
        XNamespace ns = "http://s3.amazonaws.com/doc/2006-03-01/";
        XElement root = new(ns + "ListBucketResult",
            new XElement(ns + "Name", Bucket), new XElement(ns + "Prefix", prefix),
            new XElement(ns + "KeyCount", page.Count), new XElement(ns + "IsTruncated", truncated ? "true" : "false"));
        if (truncated) root.Add(new XElement(ns + "NextContinuationToken", page[^1]));
        foreach (string k in page)
            root.Add(new XElement(ns + "Contents", new XElement(ns + "Key", k), new XElement(ns + "ETag", ETagOf(Objects[Bucket + "/" + k])), new XElement(ns + "Size", Objects[Bucket + "/" + k].Length)));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(root.ToString(), Encoding.UTF8, "application/xml") };
    }

    private static HttpResponseMessage WithETag(HttpResponseMessage response, byte[] bytes)
    {
        response.Headers.TryAddWithoutValidation("ETag", ETagOf(bytes));
        return response;
    }

    private static HttpResponseMessage Error(HttpStatusCode status, string code, string message) => new(status)
    {
        Content = new StringContent($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Error><Code>{code}</Code><Message>{message}</Message></Error>", Encoding.UTF8, "application/xml"),
    };

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(",", values) : null;

    private static Dictionary<string, string> ParseQuery(Uri uri) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");

    /// <summary>Null when the request carries a valid signature for the key pair, else what is wrong.</summary>
    private static string? CheckSignature(HttpRequestMessage request)
    {
        string? auth = Header(request, "Authorization");
        string expectedStart = $"AWS4-HMAC-SHA256 Credential={AccessKey}/";
        if (auth is null || !auth.StartsWith(expectedStart, StringComparison.Ordinal)) return "Authorization header missing or not AWS4-HMAC-SHA256 for the access key";
        if (!auth.Contains("SignedHeaders=host;x-amz-content-sha256;x-amz-date,", StringComparison.Ordinal)) return "SignedHeaders must be host;x-amz-content-sha256;x-amz-date";

        string? date = Header(request, "x-amz-date");
        string? payloadHash = Header(request, "x-amz-content-sha256");
        if (date is null || payloadHash is null) return "x-amz-date and x-amz-content-sha256 are required";
        byte[] body = request.Content is null ? [] : request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        if (payloadHash != S3Signer.Sha256Hex(body)) return "x-amz-content-sha256 does not match the body";

        Uri uri = request.RequestUri!;
        string host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        string canonicalQuery = S3Signer.CanonicalQuery(ParseQuery(uri));
        DateTimeOffset when = DateTimeOffset.ParseExact(date, "yyyyMMdd'T'HHmmss'Z'", null, System.Globalization.DateTimeStyles.AssumeUniversal);
        string expected = S3Signer.Authorization(request.Method.Method, uri.AbsolutePath, canonicalQuery, new Dictionary<string, string> { ["host"] = host }, payloadHash, when, Region, AccessKey, SecretKey);
        return auth == expected ? null : "The signature does not match the request that was sent";
    }
}
