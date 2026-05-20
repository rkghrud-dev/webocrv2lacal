using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeywordOcr.App.Services;

public sealed class ProductProgressEntry
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("completed_at")]
    public DateTime CompletedAt { get; set; } = DateTime.Now;
}

public sealed class ProductProgressState
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("source_file")]
    public string SourceFile { get; set; } = "";

    [JsonPropertyName("source_length")]
    public long SourceLength { get; set; }

    [JsonPropertyName("source_last_write_utc")]
    public DateTime SourceLastWriteUtc { get; set; }

    [JsonPropertyName("completed_codes")]
    public Dictionary<string, ProductProgressEntry> CompletedCodes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProductProgressService
{
    private readonly string _path;
    private readonly Dictionary<string, ProductProgressState> _states = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public ProductProgressService(string appRoot)
    {
        _path = Path.Combine(appRoot, "product_progress_by_source.json");
        Load();
    }

    public ProductProgressState GetOrCreate(string sourceFile)
    {
        var key = BuildSourceKey(sourceFile);
        if (_states.TryGetValue(key, out var state))
            return state;

        var info = File.Exists(sourceFile) ? new FileInfo(sourceFile) : null;
        state = new ProductProgressState
        {
            Key = key,
            SourceFile = sourceFile,
            SourceLength = info?.Length ?? 0,
            SourceLastWriteUtc = info?.LastWriteTimeUtc ?? DateTime.MinValue,
            CompletedCodes = new Dictionary<string, ProductProgressEntry>(StringComparer.OrdinalIgnoreCase),
        };
        _states[key] = state;
        Save();
        return state;
    }

    public void MarkCompleted(string sourceFile, IEnumerable<string> codes, DateTime? completedAt = null)
    {
        var state = GetOrCreate(sourceFile);
        var timestamp = completedAt ?? DateTime.Now;
        foreach (var code in codes.Select(NormalizeCode).Where(code => !string.IsNullOrWhiteSpace(code)))
        {
            state.CompletedCodes[code] = new ProductProgressEntry
            {
                Code = code,
                CompletedAt = timestamp,
            };
        }
        Save();
    }

    public void Reset(string sourceFile)
    {
        var state = GetOrCreate(sourceFile);
        state.CompletedCodes.Clear();
        Save();
    }

    public DateTime? GetCompletedAt(ProductProgressState? state, string code)
    {
        if (state is null)
            return null;
        return state.CompletedCodes.TryGetValue(NormalizeCode(code), out var entry)
            ? entry.CompletedAt
            : null;
    }

    private static string NormalizeCode(string code)
        => (code ?? "").Trim().ToUpperInvariant();

    private static string BuildSourceKey(string sourceFile)
    {
        var fullPath = Path.GetFullPath(sourceFile ?? "").Trim().ToLowerInvariant();
        var info = File.Exists(fullPath) ? new FileInfo(fullPath) : null;
        var raw = $"{fullPath}|{info?.Length ?? 0}|{info?.LastWriteTimeUtc.Ticks ?? 0}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private void Load()
    {
        if (!File.Exists(_path))
            return;

        try
        {
            var json = File.ReadAllText(_path);
            var states = JsonSerializer.Deserialize<List<ProductProgressState>>(json, JsonOptions)
                         ?? new List<ProductProgressState>();
            foreach (var state in states.Where(s => !string.IsNullOrWhiteSpace(s.Key)))
            {
                state.CompletedCodes = new Dictionary<string, ProductProgressEntry>(
                    state.CompletedCodes ?? new Dictionary<string, ProductProgressEntry>(),
                    StringComparer.OrdinalIgnoreCase);
                _states[state.Key] = state;
            }
        }
        catch
        {
            _states.Clear();
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_states.Values.ToList(), JsonOptions);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // 진행상태 저장 실패는 작업 자체를 막지 않는다.
        }
    }
}
