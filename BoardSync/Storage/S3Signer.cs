using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BoardSync.Storage;

/// <summary>AWS Signature Version 4 for the S3 API, written by hand (no SDK). The time is a parameter so the published AWS examples can be reproduced.</summary>
internal static class S3Signer
{
    private const string Algorithm = "AWS4-HMAC-SHA256";

    public static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string AmzDate(DateTimeOffset now) => now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// The Authorization header value. <paramref name="headers"/> holds "host" and any other header to sign (names in lower case);
    /// x-amz-content-sha256 and x-amz-date are added here and signed too, and the caller sends them with the same values.
    /// <paramref name="canonicalPath"/> is the URI-encoded path as sent, <paramref name="canonicalQuery"/> comes from <see cref="CanonicalQuery"/>.
    /// </summary>
    public static string Authorization(string method, string canonicalPath, string canonicalQuery, IReadOnlyDictionary<string, string> headers,
        string payloadHash, DateTimeOffset now, string region, string accessKey, string secretKey)
    {
        string amzDate = AmzDate(now);
        string day = amzDate[..8];
        SortedDictionary<string, string> signed = new(StringComparer.Ordinal);
        foreach ((string name, string value) in headers) signed[name.ToLowerInvariant()] = value.Trim();
        signed["x-amz-content-sha256"] = payloadHash;
        signed["x-amz-date"] = amzDate;

        string signedHeaders = string.Join(';', signed.Keys);
        StringBuilder canonical = new();
        canonical.Append(method).Append('\n').Append(canonicalPath).Append('\n').Append(canonicalQuery).Append('\n');
        foreach ((string name, string value) in signed) canonical.Append(name).Append(':').Append(value).Append('\n');
        canonical.Append('\n').Append(signedHeaders).Append('\n').Append(payloadHash);

        string scope = $"{day}/{region}/s3/aws4_request";
        string stringToSign = $"{Algorithm}\n{amzDate}\n{scope}\n{Sha256Hex(Encoding.UTF8.GetBytes(canonical.ToString()))}";

        byte[] key = Hmac(Encoding.UTF8.GetBytes("AWS4" + secretKey), day);
        key = Hmac(key, region);
        key = Hmac(key, "s3");
        key = Hmac(key, "aws4_request");
        string signature = Convert.ToHexString(Hmac(key, stringToSign)).ToLowerInvariant();

        return $"{Algorithm} Credential={accessKey}/{scope},SignedHeaders={signedHeaders},Signature={signature}";
    }

    /// <summary>The query as it is both sent and signed: names and values encoded, sorted by name.</summary>
    public static string CanonicalQuery(IEnumerable<KeyValuePair<string, string>> parameters) =>
        string.Join('&', parameters
            .Select(p => (Name: Encode(p.Key), Value: Encode(p.Value)))
            .OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value}"));

    /// <summary>A path with each segment URI-encoded and the slashes kept.</summary>
    public static string EncodePath(string path) => string.Join('/', path.Split('/').Select(Encode));

    /// <summary>URI-encodes everything except A-Z a-z 0-9 - _ . ~ (UTF-8, upper-case hex), as SigV4 requires.</summary>
    public static string Encode(string value)
    {
        StringBuilder result = new();
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            char c = (char)b;
            if (b < 128 && (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~')) result.Append(c);
            else result.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return result.ToString();
    }

    private static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
}
