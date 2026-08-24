using System.Net.Http.Headers;
using System.Text.Json;

namespace Shigure;

public sealed record ModuleShareSummary(
    string Id,
    string Filename,
    string Sharer,
    string Author,
    string Version,
    string Profession,
    string Specialization,
    string Description,
    int Size,
    int DownloadCount,
    DateTimeOffset CreatedAt);

public sealed class ModuleMarketplaceClient
{
    public const string WebsiteUrl = "https://www.shigure.club";
    private const int MaximumListBytes = 1024 * 1024;
    private const int MaximumModuleBytes = 200 * 1024;
    private static readonly Uri ServiceRoot = new(WebsiteUrl, UriKind.Absolute);
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public ModuleMarketplaceClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public async Task<IReadOnlyList<ModuleShareSummary>> GetSharesAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync("api/shares", cancellationToken).ConfigureAwait(false);
        var bytes = await ReadBoundedAsync(response, MaximumListBytes, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Deserialize<ShareListResponse>(bytes, JsonOptions)
            ?? throw new InvalidDataException("模块清单为空。");

        return (payload.Shares ?? [])
            .Where(share => Guid.TryParse(share.Id, out _) && !string.IsNullOrWhiteSpace(share.Filename))
            .ToArray();
    }

    public async Task<ModuleDefinition> DownloadAsync(
        string shareId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(shareId, out var id))
        {
            throw new ArgumentException("模块下载 ID 无效。", nameof(shareId));
        }

        using var response = await SendAsync(
            $"api/shares/{id:D}/download",
            cancellationToken).ConfigureAwait(false);
        var bytes = await ReadBoundedAsync(response, MaximumModuleBytes, cancellationToken).ConfigureAwait(false);
        return ModuleStore.Parse(bytes);
    }

    private async Task<HttpResponseMessage> SendAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ServiceRoot, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new HttpRequestException(
                $"模块服务返回 HTTP {(int)response.StatusCode}。",
                null,
                response.StatusCode);
        }

        return response;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException($"模块服务响应超过 {maximumBytes / 1024} KiB 限制。");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return destination.ToArray();
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new InvalidDataException($"模块服务响应超过 {maximumBytes / 1024} KiB 限制。");
            }

            destination.Write(buffer, 0, read);
        }
    }

    private sealed class ShareListResponse
    {
        public List<ModuleShareSummary>? Shares { get; init; }
    }
}
