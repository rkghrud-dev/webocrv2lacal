using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace KeywordOcr.App.Services;

public static class ExcelSourceReader
{
    private static readonly Regex GsCodePattern =
        new(@"GS\d{7}[A-Z0-9]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<Dictionary<string, object?>> ReadSourceRows(
        string filePath,
        params string[] sheetPreferences)
    {
        using var wb = new XLWorkbook(filePath);
        IXLWorksheet ws = ResolveSheet(wb, sheetPreferences);

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;

        var headers = new Dictionary<int, string>();
        for (int c = 1; c <= lastCol; c++)
        {
            var val = ws.Cell(1, c).GetString().Trim();
            if (!string.IsNullOrEmpty(val))
                headers[c] = val;
        }

        var rows = new List<Dictionary<string, object?>>();
        for (int r = 2; r <= lastRow; r++)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (col, name) in headers)
            {
                var cell = ws.Cell(r, col);
                row[name] = cell.IsEmpty()
                    ? null
                    : cell.Value.IsNumber ? cell.Value.GetNumber() : cell.GetString();
            }
            row["_row_num"] = r;
            row["_source_file_path"] = filePath;
            row["_export_root"] = ResolveExportRoot(filePath);
            rows.Add(row);
        }

        return rows;
    }

    public static string ExtractGsCodeFromRow(IReadOnlyDictionary<string, object?> row)
    {
        foreach (var key in new[] { "자체 상품코드", "GS코드", "상품코드", "상품명" })
        {
            var value = GetStr(row, key);
            var match = GsCodePattern.Match(value);
            if (match.Success)
                return match.Value.Trim().ToUpperInvariant();
        }

        foreach (var value in row.Values)
        {
            var match = GsCodePattern.Match(value?.ToString() ?? "");
            if (match.Success)
                return match.Value.Trim().ToUpperInvariant();
        }

        return "";
    }

    public static string NormalizeGsCode(string value)
    {
        var match = GsCodePattern.Match(value ?? "");
        return match.Success ? match.Value.ToUpperInvariant() : (value ?? "").Trim().ToUpperInvariant();
    }

    public static string ResolveExportRoot(string sourceFilePath)
    {
        var path = Path.GetFullPath(sourceFilePath);
        var parent = Path.GetDirectoryName(path) ?? "";
        var parentName = Path.GetFileName(parent).ToLowerInvariant();
        var grandParent = Path.GetDirectoryName(parent) ?? "";
        var grandName = Path.GetFileName(grandParent).ToLowerInvariant();

        if (parentName.StartsWith("llm_result", StringComparison.OrdinalIgnoreCase)
            && grandName == "llm_chunks")
            return Path.GetDirectoryName(grandParent) ?? grandParent;
        if (parentName.StartsWith("llm_result", StringComparison.OrdinalIgnoreCase))
            return grandParent;
        return parent;
    }

    public static string GetStr(IReadOnlyDictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var v) && v is not null ? v.ToString()?.Trim() ?? "" : "";

    public static int GetInt(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v is null)
            return 0;
        if (v is double d) return (int)d;
        return int.TryParse(v.ToString(), out var n) ? n : 0;
    }

    private static IXLWorksheet ResolveSheet(XLWorkbook wb, string[] preferences)
    {
        foreach (var name in preferences)
        {
            if (wb.TryGetWorksheet(name, out var ws))
                return ws;
        }
        return wb.Worksheets.First();
    }
}
