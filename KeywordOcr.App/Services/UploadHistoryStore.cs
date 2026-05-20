using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace KeywordOcr.App.Services;

public sealed class UploadHistoryEntry
{
    public DateTime? HomeMarket { get; set; }
    public DateTime? ReadyMarket { get; set; }
    public DateTime? Coupang { get; set; }
}

public sealed class UploadHistoryStore
{
    public static readonly string DefaultPath = DesktopKeyStore.GetPath("upload_history.json");

    private readonly string _path;
    private Dictionary<string, UploadHistoryEntry> _data;

    public UploadHistoryStore(string? path = null)
    {
        _path = path ?? DefaultPath;
        _data = Load();
    }

    public UploadHistoryEntry? Get(string gsCode) =>
        _data.TryGetValue(gsCode.ToUpperInvariant(), out var e) ? e : null;

    public void Mark(string gsCode, string market)
    {
        var key = gsCode.ToUpperInvariant();
        if (!_data.ContainsKey(key))
            _data[key] = new UploadHistoryEntry();

        var entry = _data[key];
        var now = DateTime.Now;
        switch (market)
        {
            case "homemarket": entry.HomeMarket = now; break;
            case "readymarket": entry.ReadyMarket = now; break;
            case "coupang": entry.Coupang = now; break;
        }
        Save();
    }

    public void ExportTo(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
        catch { }
    }

    public void ImportFrom(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var json = File.ReadAllText(path, Encoding.UTF8);
            var incoming = JsonSerializer.Deserialize<Dictionary<string, UploadHistoryEntry>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new Dictionary<string, UploadHistoryEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var (rawKey, incomingEntry) in incoming)
            {
                if (string.IsNullOrWhiteSpace(rawKey))
                    continue;
                var key = rawKey.ToUpperInvariant();
                if (!_data.TryGetValue(key, out var current))
                {
                    _data[key] = incomingEntry;
                    continue;
                }

                current.HomeMarket = Latest(current.HomeMarket, incomingEntry.HomeMarket);
                current.ReadyMarket = Latest(current.ReadyMarket, incomingEntry.ReadyMarket);
                current.Coupang = Latest(current.Coupang, incomingEntry.Coupang);
            }

            Save();
        }
        catch { }
    }

    private Dictionary<string, UploadHistoryEntry> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path, Encoding.UTF8);
                return JsonSerializer.Deserialize<Dictionary<string, UploadHistoryEntry>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new Dictionary<string, UploadHistoryEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { }
        return new Dictionary<string, UploadHistoryEntry>(StringComparer.OrdinalIgnoreCase);
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json, new UTF8Encoding(false));
        }
        catch { }
    }

    private static DateTime? Latest(DateTime? a, DateTime? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a >= b ? a : b;
    }
}
