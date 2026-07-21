using System.Net.Http;
using System.Net.Http.Headers;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// v2.0: S3-compatible storage sync provider.
/// Works with AWS S3, MinIO, Cloudflare R2, Wasabi, and any S3-compatible service.
/// Uses simple PUT/GET with presigned-style authentication.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class S3SyncService : IDisposable
{
    private readonly HttpClient _httpClient;

    public string? Endpoint { get; set; }
    public string? Bucket { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string? Region { get; set; } = "us-east-1";
    public string RemoteFilePath { get; set; } = "vault.okv";
    public bool IsConfigured => !string.IsNullOrEmpty(Endpoint) && !string.IsNullOrEmpty(Bucket)
        && !string.IsNullOrEmpty(AccessKey) && !string.IsNullOrEmpty(SecretKey);

    public S3SyncService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>Uploads the vault file to S3 storage.</summary>
    public async Task<bool> PushAsync(string localVaultPath, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("S3 sync is not configured");
        if (!File.Exists(localVaultPath)) throw new ValidationException($"Vault file not found: {localVaultPath}");

        var data = await File.ReadAllBytesAsync(localVaultPath, ct);
        var url = BuildUrl(RemoteFilePath);
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(data) };
        SignRequest(request, "PUT", RemoteFilePath, data);

        var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Downloads the vault file from S3 storage.</summary>
    public async Task<bool> PullAsync(string localVaultPath, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("S3 sync is not configured");

        var url = BuildUrl(RemoteFilePath);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        SignRequest(request, "GET", RemoteFilePath, null);

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return false;

        var data = await response.Content.ReadAsByteArrayAsync(ct);
        await File.WriteAllBytesAsync(localVaultPath, data, ct);
        return true;
    }

    private string BuildUrl(string key)
    {
        var endpoint = Endpoint!.TrimEnd('/');
        return $"{endpoint}/{Bucket}/{key}";
    }

    /// <summary>Simple SigV4-style signing (simplified for S3-compatible services).</summary>
    private void SignRequest(HttpRequestMessage request, string method, string key, byte[]? body)
    {
        // Use AWS Signature Version 4 (simplified)
        var timestamp = DateTime.UtcNow;
        var dateStamp = timestamp.ToString("yyyyMMdd");
        var amzDate = timestamp.ToString("yyyyMMddTHHmmssZ");

        var payloadHash = body != null
            ? ComputeSha256Hex(body)
            : "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"; // empty string SHA-256

        request.Headers.Add("x-amz-date", amzDate);
        request.Headers.Add("x-amz-content-sha256", payloadHash);
        request.Headers.Add("x-amz-storage-class", "STANDARD");

        // Build canonical request
        var canonicalUri = $"/{Bucket}/{key}";
        var canonicalQueryString = "";
        var canonicalHeaders = $"host:{new Uri(BuildUrl(key)).Host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n";
        var signedHeaders = "host;x-amz-content-sha256;x-amz-date";
        var canonicalRequest = $"{method}\n{canonicalUri}\n{canonicalQueryString}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";

        // Build string to sign
        var scope = $"{dateStamp}/{Region}/s3/aws4_request";
        var stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{ComputeSha256Hex(System.Text.Encoding.UTF8.GetBytes(canonicalRequest))}";

        // Calculate signature
        var signingKey = GetSigningKey(dateStamp);
        var signature = ComputeHmacHex(signingKey, System.Text.Encoding.UTF8.GetBytes(stringToSign));

        var authHeader = $"AWS4-HMAC-SHA256 Credential={AccessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}";
        request.Headers.Authorization = new AuthenticationHeaderValue("AWS4-HMAC-SHA256",
            $"Credential={AccessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
    }

    private byte[] GetSigningKey(string dateStamp)
    {
        var kSecret = System.Text.Encoding.UTF8.GetBytes($"AWS4{SecretKey}");
        var kDate = ComputeHmac(kSecret, System.Text.Encoding.UTF8.GetBytes(dateStamp));
        var kRegion = ComputeHmac(kDate, System.Text.Encoding.UTF8.GetBytes(Region!));
        var kService = ComputeHmac(kRegion, System.Text.Encoding.UTF8.GetBytes("s3"));
        var kSigning = ComputeHmac(kService, System.Text.Encoding.UTF8.GetBytes("aws4_request"));
        return kSigning;
    }

    private static string ComputeSha256Hex(byte[] data)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] ComputeHmac(byte[] key, byte[] data)
        => System.Security.Cryptography.HMACSHA256.HashData(key, data);

    private static string ComputeHmacHex(byte[] key, byte[] data)
        => Convert.ToHexString(ComputeHmac(key, data)).ToLowerInvariant();

    public void Dispose() => _httpClient.Dispose();
}
