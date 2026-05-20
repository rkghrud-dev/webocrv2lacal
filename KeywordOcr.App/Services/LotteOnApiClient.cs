using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace KeywordOcr.App.Services;

internal sealed class LotteOnApiClient : IDisposable
{
    private const string ApiBase = "https://openapi.lotteon.com";

    private readonly string _apiKey;
    private readonly HttpClient _http;

    public LotteOnApiClient(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("롯데ON API 키가 비어 있습니다.", nameof(apiKey));

        _apiKey = apiKey.Trim();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    }

    public static LotteOnApiClient FromKeyFile()
    {
        var apiKey = ReadApiKeyFromJson(DesktopKeyStore.GetPath("lotteon_upload_id.json"))
            .OrIfEmpty(ReadApiKeyFromText(DesktopKeyStore.GetPath("lotteon_api.txt")))
            .OrIfEmpty(Environment.GetEnvironmentVariable("LOTTEON_API_KEY") ?? "");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("롯데ON API 키를 찾을 수 없습니다. Desktop\\key\\lotteon_upload_id.json 또는 lotteon_api.txt를 확인하세요.");

        return new LotteOnApiClient(apiKey);
    }

    public async Task<JsonDocument> GetIdentityAsync(CancellationToken ct)
        => await SendAsync(HttpMethod.Get, "/v1/openapi/common/v1/identity", null, ct);

    public async Task<JsonDocument> RegisterProductAsync(JsonObject payload, CancellationToken ct)
        => await SendAsync(HttpMethod.Post, "/v1/openapi/product/v1/product/registration/request", payload, ct);

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, JsonObject? payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, ApiBase + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (payload is not null)
        {
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        }
        catch (JsonException)
        {
            throw new HttpRequestException($"롯데ON API 응답 파싱 실패 ({(int)response.StatusCode}): {Short(body)}");
        }

        if (!response.IsSuccessStatusCode)
        {
            using (doc)
            {
                throw new HttpRequestException($"롯데ON API 오류 ({(int)response.StatusCode}): {Short(body)}");
            }
        }

        return doc;
    }

    private static string ReadApiKeyFromJson(string path)
    {
        if (!File.Exists(path)) return "";

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = doc.RootElement;
            foreach (var key in new[] { "api_key", "LOTTEON_API_KEY", "ApiKey", "apiKey" })
            {
                if (root.TryGetProperty(key, out var value))
                    return value.GetString()?.Trim() ?? "";
            }
        }
        catch
        {
            return "";
        }

        return "";
    }

    private static string ReadApiKeyFromText(string path)
    {
        if (!File.Exists(path)) return "";

        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;
            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx <= 0) continue;

            var key = trimmed[..eqIdx].Trim();
            var value = trimmed[(eqIdx + 1)..].Trim();
            if (key.Equals("api_key", StringComparison.OrdinalIgnoreCase)
                || key.Equals("LOTTEON_API_KEY", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return "";
    }

    private static string Short(string value)
        => string.IsNullOrWhiteSpace(value) ? "" : value.Length > 300 ? value[..300] + "..." : value;

    public void Dispose() => _http.Dispose();
}
