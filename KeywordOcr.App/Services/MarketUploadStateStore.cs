using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace KeywordOcr.App.Services;

public sealed class MarketUploadStateEntry
{
    public string GsCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public Dictionary<string, MarketUploadTargetState> Markets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MarketUploadTargetState
{
    public string Status { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string RepresentativeImageUrl { get; set; } = "";
    public List<string> ImageUrls { get; set; } = new();
    public string Error { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public static class MarketUploadStateStore
{
    public static readonly string DefaultPath = DesktopKeyStore.GetPath("market_upload_state.json");
    private static readonly object Gate = new();

    public static void Upsert(
        string gsCode,
        string productName,
        string market,
        string status,
        string? productId,
        IEnumerable<string>? imageUrls,
        string? error = null)
    {
        if (string.IsNullOrWhiteSpace(gsCode) || string.IsNullOrWhiteSpace(market))
            return;

        lock (Gate)
        {
            var data = Load();
            var key = gsCode.Trim().ToUpperInvariant();
            if (!data.TryGetValue(key, out var entry))
            {
                entry = new MarketUploadStateEntry { GsCode = key };
                data[key] = entry;
            }

            if (!string.IsNullOrWhiteSpace(productName))
                entry.ProductName = productName.Trim();

            var urls = (imageUrls ?? Array.Empty<string>())
                .Select(url => url?.Trim() ?? "")
                .Where(MarketImageUrlGuard.IsAllowedUploadUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            entry.Markets[market.Trim()] = new MarketUploadTargetState
            {
                Status = status,
                ProductId = productId ?? "",
                RepresentativeImageUrl = urls.FirstOrDefault() ?? "",
                ImageUrls = urls,
                Error = error ?? "",
                UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            };

            Save(data);
        }
    }

    private static Dictionary<string, MarketUploadStateEntry> Load()
    {
        try
        {
            if (File.Exists(DefaultPath))
            {
                var json = File.ReadAllText(DefaultPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<Dictionary<string, MarketUploadStateEntry>>(json,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? new Dictionary<string, MarketUploadStateEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // 손상된 상태파일은 이번 실행에서 새로 쓴다.
        }

        return new Dictionary<string, MarketUploadStateEntry>(StringComparer.OrdinalIgnoreCase);
    }

    private static void Save(Dictionary<string, MarketUploadStateEntry> data)
    {
        var dir = Path.GetDirectoryName(DefaultPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(DefaultPath, json, new UTF8Encoding(false));
    }
}
