using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace KeywordOcr.App.Services;

public sealed class KeywordFeedbackChangeItem : INotifyPropertyChanged
{
    private bool _includeInPrompt = true;

    public bool IncludeInPrompt
    {
        get => _includeInPrompt;
        set
        {
            if (_includeInPrompt == value)
                return;
            _includeInPrompt = value;
            OnPropertyChanged();
        }
    }

    public string GsCode { get; set; } = "";
    public string SheetName { get; set; } = "";
    public string ColumnName { get; set; } = "";
    public string BeforeValue { get; set; } = "";
    public string AfterValue { get; set; } = "";
    public string OriginalProductName { get; set; } = "";
    public string OcrSnippet { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class KeywordFeedbackSessionResult
{
    public string SessionDir { get; init; } = "";
    public string LocalFeedbackDir { get; init; } = "";
    public string RawChangesPath { get; init; } = "";
    public string RawChangesWorkbookPath { get; init; } = "";
    public string CodexCommandPath { get; init; } = "";
    public string RulesPath { get; init; } = "";
    public string CommandText { get; init; } = "";
    public int ChangeCount { get; init; }
    public int IncludedCount { get; init; }
}

public sealed class KeywordFeedbackService
{
    public const string DefaultFeedbackRoot = @"C:\code\keywordocr_feedback";

    private static readonly string[] PreferredSheetNames = { "분리추출후", "B마켓" };
    private static readonly Regex GsCodeRegex = new(@"GS\d{6,10}[A-Z]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public IReadOnlyList<KeywordFeedbackChangeItem> Compare(string originalPath, string editedPath)
    {
        if (!File.Exists(originalPath))
            throw new FileNotFoundException("원본 엑셀 파일을 찾을 수 없습니다.", originalPath);
        if (!File.Exists(editedPath))
            throw new FileNotFoundException("수정 엑셀 파일을 찾을 수 없습니다.", editedPath);

        var original = ReadWorkbook(originalPath);
        var edited = ReadWorkbook(editedPath);
        var sheetNames = original.Keys
            .Intersect(edited.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(sheet => Array.IndexOf(PreferredSheetNames, sheet) < 0 ? 99 : Array.IndexOf(PreferredSheetNames, sheet))
            .ThenBy(sheet => sheet, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var changes = new List<KeywordFeedbackChangeItem>();
        foreach (var sheetName in sheetNames)
        {
            var originalRows = original[sheetName];
            var editedRows = edited[sheetName];
            var gsCodes = originalRows.Keys
                .Intersect(editedRows.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase);

            foreach (var gsCode in gsCodes)
            {
                var beforeRow = originalRows[gsCode];
                var afterRow = editedRows[gsCode];
                var columnNames = beforeRow.Values.Keys
                    .Union(afterRow.Values.Keys, StringComparer.OrdinalIgnoreCase)
                    .Where(IsFeedbackColumn)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

                foreach (var columnName in columnNames)
                {
                    beforeRow.Values.TryGetValue(columnName, out var beforeValue);
                    afterRow.Values.TryGetValue(columnName, out var afterValue);
                    beforeValue = NormalizeCellForDisplay(beforeValue);
                    afterValue = NormalizeCellForDisplay(afterValue);

                    if (NormalizeCellForCompare(beforeValue) == NormalizeCellForCompare(afterValue))
                        continue;

                    changes.Add(new KeywordFeedbackChangeItem
                    {
                        IncludeInPrompt = true,
                        GsCode = gsCode,
                        SheetName = sheetName,
                        ColumnName = columnName,
                        BeforeValue = beforeValue,
                        AfterValue = afterValue,
                        OriginalProductName = FirstNonEmpty(beforeRow.ProductName, afterRow.ProductName),
                        OcrSnippet = Shorten(FirstNonEmpty(beforeRow.OcrSnippet, afterRow.OcrSnippet), 500)
                    });
                }
            }
        }

        return changes;
    }

    public KeywordFeedbackSessionResult SaveSession(
        string originalPath,
        string editedPath,
        IReadOnlyCollection<KeywordFeedbackChangeItem> changes,
        string? feedbackRoot = null)
    {
        if (!File.Exists(originalPath))
            throw new FileNotFoundException("원본 엑셀 파일을 찾을 수 없습니다.", originalPath);
        if (!File.Exists(editedPath))
            throw new FileNotFoundException("수정 엑셀 파일을 찾을 수 없습니다.", editedPath);

        var root = string.IsNullOrWhiteSpace(feedbackRoot) ? DefaultFeedbackRoot : feedbackRoot.Trim();
        var createdAt = DateTime.Now;
        var timestamp = createdAt.ToString("yyyyMMdd_HHmmss");
        var sessionDir = Path.Combine(root, "sessions", timestamp);
        var backupDir = Path.Combine(sessionDir, "backups");
        var rulesDir = Path.Combine(root, "rules");
        Directory.CreateDirectory(sessionDir);
        Directory.CreateDirectory(backupDir);
        Directory.CreateDirectory(rulesDir);

        var backupOriginal = Path.Combine(backupDir, "source_original.xlsx");
        var backupEdited = Path.Combine(backupDir, "source_edited.xlsx");
        File.Copy(originalPath, backupOriginal, true);
        File.Copy(editedPath, backupEdited, true);

        var allChanges = changes.ToList();
        var includedCount = allChanges.Count(change => change.IncludeInPrompt);
        var rawChangesPath = Path.Combine(sessionDir, "raw_changes.md");
        var rawChangesWorkbookPath = Path.Combine(sessionDir, "raw_changes.xlsx");
        var metadataPath = Path.Combine(sessionDir, "metadata.json");
        var commandPath = Path.Combine(sessionDir, "codex_command.txt");
        var rulesPath = Path.Combine(rulesDir, "keyword_rule_feedback.md");

        EnsureRulesFile(rulesPath);
        File.WriteAllText(rawChangesPath, BuildRawChangesMarkdown(createdAt, originalPath, editedPath, allChanges), Encoding.UTF8);
        SaveChangeWorkbook(rawChangesWorkbookPath, allChanges);

        var commandText = BuildCodexCommand(sessionDir);
        File.WriteAllText(commandPath, commandText, Encoding.UTF8);

        var metadata = new
        {
            createdAt = createdAt.ToString("O"),
            originalPath,
            editedPath,
            sessionDir,
            rawChangesPath,
            rawChangesWorkbookPath,
            codexCommandPath = commandPath,
            rulesPath,
            changeCount = allChanges.Count,
            includedCount
        };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8);
        AppendIndex(root, createdAt, originalPath, editedPath, sessionDir, allChanges.Count, includedCount);

        var localFeedbackDir = ResolveLocalFeedbackDir(originalPath);
        Directory.CreateDirectory(localFeedbackDir);
        File.Copy(rawChangesPath, Path.Combine(localFeedbackDir, $"raw_changes_{timestamp}.md"), true);
        File.Copy(commandPath, Path.Combine(localFeedbackDir, $"codex_command_{timestamp}.txt"), true);
        File.WriteAllText(
            Path.Combine(localFeedbackDir, "latest_feedback_session.txt"),
            $"session_dir={sessionDir}{Environment.NewLine}rules={rulesPath}{Environment.NewLine}command={commandPath}{Environment.NewLine}",
            Encoding.UTF8);

        return new KeywordFeedbackSessionResult
        {
            SessionDir = sessionDir,
            LocalFeedbackDir = localFeedbackDir,
            RawChangesPath = rawChangesPath,
            RawChangesWorkbookPath = rawChangesWorkbookPath,
            CodexCommandPath = commandPath,
            RulesPath = rulesPath,
            CommandText = commandText,
            ChangeCount = allChanges.Count,
            IncludedCount = includedCount
        };
    }

    private static Dictionary<string, Dictionary<string, FeedbackRow>> ReadWorkbook(string path)
    {
        using var workbook = new XLWorkbook(path);
        var availablePreferredSheets = PreferredSheetNames
            .Where(name => workbook.Worksheets.Any(ws => ws.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var worksheets = availablePreferredSheets.Count > 0
            ? availablePreferredSheets.Select(name => workbook.Worksheets.First(ws => ws.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            : workbook.Worksheets;

        var result = new Dictionary<string, Dictionary<string, FeedbackRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var worksheet in worksheets)
        {
            var range = worksheet.RangeUsed();
            if (range is null)
                continue;

            var firstRow = range.FirstRowUsed().RowNumber();
            var lastRow = range.LastRowUsed().RowNumber();
            var firstColumn = range.FirstColumnUsed().ColumnNumber();
            var lastColumn = range.LastColumnUsed().ColumnNumber();
            var headers = new List<(int Column, string Name)>();

            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var header = NormalizeHeaderForDisplay(worksheet.Cell(firstRow, column).GetString());
                if (string.IsNullOrWhiteSpace(header))
                    header = $"Column{column}";
                headers.Add((column, header));
            }

            var rows = new Dictionary<string, FeedbackRow>(StringComparer.OrdinalIgnoreCase);
            for (var row = firstRow + 1; row <= lastRow; row++)
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (column, header) in headers)
                {
                    if (values.ContainsKey(header))
                        continue;
                    values[header] = NormalizeCellForDisplay(worksheet.Cell(row, column).GetFormattedString());
                }

                var gsCode = ExtractGsCode(values);
                if (string.IsNullOrWhiteSpace(gsCode) || rows.ContainsKey(gsCode))
                    continue;

                rows[gsCode] = new FeedbackRow(
                    values,
                    FindProductName(values),
                    FindOcrSnippet(values));
            }

            result[worksheet.Name] = rows;
        }

        return result;
    }

    private static string BuildRawChangesMarkdown(
        DateTime createdAt,
        string originalPath,
        string editedPath,
        IReadOnlyList<KeywordFeedbackChangeItem> changes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# KeywordOCR 키워드 피드백 변경점");
        builder.AppendLine();
        builder.AppendLine($"- 생성시각: {createdAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"- 원본 파일: {originalPath}");
        builder.AppendLine($"- 수정 파일: {editedPath}");
        builder.AppendLine($"- 전체 변경점: {changes.Count}");
        builder.AppendLine($"- 프롬프트 반영 대상: {changes.Count(change => change.IncludeInPrompt)}");
        builder.AppendLine();
        builder.AppendLine("아래 변경점은 사용자가 실제 판매 경험과 감으로 수정한 결과입니다. Codex는 변경 전/후 차이만 근거로 규칙을 추론합니다.");
        builder.AppendLine("IncludeInPrompt=false 항목은 원문 보관용이며, 새 규칙 반영 판단에서는 보류합니다.");

        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            builder.AppendLine();
            builder.AppendLine($"## {index + 1}. {change.GsCode} / {change.SheetName} / {change.ColumnName}");
            builder.AppendLine();
            builder.AppendLine($"- IncludeInPrompt: {change.IncludeInPrompt}");
            builder.AppendLine($"- 원본 상품명: {change.OriginalProductName}");
            if (!string.IsNullOrWhiteSpace(change.OcrSnippet))
                builder.AppendLine($"- OCR 일부: {change.OcrSnippet}");
            builder.AppendLine();
            builder.AppendLine("변경 전:");
            AppendIndentedBlock(builder, change.BeforeValue);
            builder.AppendLine();
            builder.AppendLine("변경 후:");
            AppendIndentedBlock(builder, change.AfterValue);
        }

        return builder.ToString();
    }

    private static void SaveChangeWorkbook(string path, IReadOnlyList<KeywordFeedbackChangeItem> changes)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("피드백변경점");
        var headers = new[]
        {
            "반영대상",
            "GS코드",
            "시트",
            "컬럼",
            "변경전",
            "변경후",
            "원본상품명",
            "OCR일부"
        };

        for (var column = 0; column < headers.Length; column++)
            worksheet.Cell(1, column + 1).Value = headers[column];

        for (var index = 0; index < changes.Count; index++)
        {
            var row = index + 2;
            var change = changes[index];
            worksheet.Cell(row, 1).Value = change.IncludeInPrompt ? "Y" : "N";
            worksheet.Cell(row, 2).Value = change.GsCode;
            worksheet.Cell(row, 3).Value = change.SheetName;
            worksheet.Cell(row, 4).Value = change.ColumnName;
            worksheet.Cell(row, 5).Value = change.BeforeValue;
            worksheet.Cell(row, 6).Value = change.AfterValue;
            worksheet.Cell(row, 7).Value = change.OriginalProductName;
            worksheet.Cell(row, 8).Value = change.OcrSnippet;
        }

        worksheet.Row(1).Style.Font.Bold = true;
        worksheet.Columns().AdjustToContents(1, Math.Min(changes.Count + 1, 80));
        worksheet.Column(5).Width = 55;
        worksheet.Column(6).Width = 55;
        worksheet.Column(8).Width = 45;
        worksheet.Columns(5, 8).Style.Alignment.WrapText = true;
        workbook.SaveAs(path);
    }

    private static void EnsureRulesFile(string rulesPath)
    {
        if (File.Exists(rulesPath))
            return;

        var text = """
                   # KeywordOCR 누적 피드백 규칙

                   이 파일은 사용자가 수정한 키워드 결과를 Codex CLI로 분석해 누적하는 작업 노트입니다.

                   ## 운영 원칙

                   - 변경 전/후 차이에서 반복 패턴이 보이는 규칙만 추가한다.
                   - 상품 사실, 재질, 규격, 호환성, 사용처는 새로 만들지 않는다.
                   - 영어, 로마자, 중문 표현은 한국어 대체어가 명확할 때만 한국어 검색어로 바꾼다.
                   - keyword_skill.md 본문은 바로 덮어쓰지 않고, 검토 가능한 규칙 후보를 먼저 누적한다.
                   - 애매한 항목은 보류 규칙으로 남기고 다음 피드백 세션에서 재검토한다.

                   """;
        File.WriteAllText(rulesPath, text, Encoding.UTF8);
    }

    private static string BuildCodexCommand(string sessionDir)
    {
        var prompt = "raw_changes.md 파일을 읽고 사용자가 고친 키워드 변경 패턴을 분석해. "
            + "반드시 스스로 3회 이상 반복 검토해. 1회차는 변경 전/후 차이와 반복 패턴 추출, 2회차는 반례와 과잉 일반화 제거, 3회차는 keyword_skill.md에 넣을 수 있는 명령형 규칙으로 정제해. "
            + "상품 사실, 재질, 규격, 호환성, 사용처를 새로 만들지 말고 변경점에 있는 근거만 사용해. "
            + "결과는 codex_analysis.md에 저장하고, 반영 추천 규칙은 ..\\..\\rules\\keyword_rule_feedback.md 파일 끝에 날짜 섹션으로 누적 추가해. "
            + "IncludeInPrompt=false 항목은 보류 자료로만 참고해. raw_changes.md와 backups 폴더의 원본 파일은 덮어쓰지 마. "
            + "최종 응답에는 전체 변경 건수, 반영 규칙, 보류 규칙, 저장 파일 경로를 요약해.";
        return $"cd \"{sessionDir}\"; codex --full-auto \"{prompt}\"";
    }

    private static void AppendIndex(
        string root,
        DateTime createdAt,
        string originalPath,
        string editedPath,
        string sessionDir,
        int changeCount,
        int includedCount)
    {
        var indexPath = Path.Combine(root, "feedback_index.json");
        List<FeedbackIndexEntry> entries;
        try
        {
            entries = File.Exists(indexPath)
                ? JsonSerializer.Deserialize<List<FeedbackIndexEntry>>(File.ReadAllText(indexPath, Encoding.UTF8)) ?? new List<FeedbackIndexEntry>()
                : new List<FeedbackIndexEntry>();
        }
        catch
        {
            entries = new List<FeedbackIndexEntry>();
        }

        entries.Add(new FeedbackIndexEntry
        {
            CreatedAt = createdAt.ToString("O"),
            OriginalPath = originalPath,
            EditedPath = editedPath,
            SessionDir = sessionDir,
            ChangeCount = changeCount,
            IncludedCount = includedCount
        });
        File.WriteAllText(indexPath, JsonSerializer.Serialize(entries, JsonOptions), Encoding.UTF8);
    }

    private static string ResolveLocalFeedbackDir(string originalPath)
    {
        var dir = Path.GetDirectoryName(originalPath) ?? Environment.CurrentDirectory;
        var dirName = Path.GetFileName(dir);
        if (dirName.StartsWith("llm_result", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(dir);
            if (parent is not null)
                dir = parent.FullName;
        }

        return Path.Combine(dir, "prompt_feedback");
    }

    private static bool IsFeedbackColumn(string header)
    {
        var normalized = NormalizeHeader(header);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;
        if (normalized.Contains("원본상품명", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("기존상품명", StringComparison.OrdinalIgnoreCase))
            return false;

        return normalized.Contains("상품명", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("검색어", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("검색키워드", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("태그", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractGsCode(Dictionary<string, string> values)
    {
        var preferredKeys = new[] { "GS코드", "상품코드", "자체상품코드", "자체 상품코드", "상품명" };
        foreach (var key in preferredKeys)
        {
            var value = FindValue(values, key);
            var code = ExtractGsCodeFromText(value);
            if (!string.IsNullOrWhiteSpace(code))
                return code;
        }

        foreach (var value in values.Values)
        {
            var code = ExtractGsCodeFromText(value);
            if (!string.IsNullOrWhiteSpace(code))
                return code;
        }

        return "";
    }

    private static string ExtractGsCodeFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var match = GsCodeRegex.Match(text);
        return match.Success ? match.Value.ToUpperInvariant() : "";
    }

    private static string FindProductName(Dictionary<string, string> values)
    {
        var exact = FindValue(values, "상품명");
        if (!string.IsNullOrWhiteSpace(exact))
            return exact;

        var candidate = values.FirstOrDefault(pair => NormalizeHeader(pair.Key).Contains("상품명", StringComparison.OrdinalIgnoreCase));
        return candidate.Value ?? "";
    }

    private static string FindOcrSnippet(Dictionary<string, string> values)
    {
        foreach (var pair in values)
        {
            var header = NormalizeHeader(pair.Key);
            if (header.Contains("ocr", StringComparison.OrdinalIgnoreCase)
                || header.Contains("vision", StringComparison.OrdinalIgnoreCase)
                || header.Contains("비전", StringComparison.OrdinalIgnoreCase)
                || header.Contains("상세텍스트", StringComparison.OrdinalIgnoreCase))
            {
                return Shorten(pair.Value, 500);
            }
        }

        return "";
    }

    private static string FindValue(Dictionary<string, string> values, string key)
    {
        if (values.TryGetValue(key, out var exact))
            return exact;

        var normalizedKey = NormalizeHeader(key);
        foreach (var pair in values)
        {
            if (NormalizeHeader(pair.Key) == normalizedKey)
                return pair.Value;
        }

        return "";
    }

    private static string NormalizeHeaderForDisplay(string? value)
        => Regex.Replace(value ?? "", @"\s+", "").Trim();

    private static string NormalizeHeader(string? value)
        => Regex.Replace(value ?? "", @"[\s:_\-./\\()\[\]{}]+", "").Trim();

    private static string NormalizeCellForDisplay(string? value)
        => Regex.Replace((value ?? "").Replace("\r\n", "\n").Replace("\r", "\n"), @"[ \t]+", " ").Trim();

    private static string NormalizeCellForCompare(string? value)
        => Regex.Replace(value ?? "", @"\s+", " ").Trim();

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string Shorten(string? value, int maxLength)
    {
        value = NormalizeCellForDisplay(value);
        if (value.Length <= maxLength)
            return value;
        return value[..maxLength] + "...";
    }

    private static void AppendIndentedBlock(StringBuilder builder, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine("    (빈 값)");
            return;
        }

        foreach (var line in value.Split('\n'))
            builder.AppendLine("    " + line);
    }

    private sealed record FeedbackRow(
        Dictionary<string, string> Values,
        string ProductName,
        string OcrSnippet);

    private sealed class FeedbackIndexEntry
    {
        public string CreatedAt { get; set; } = "";
        public string OriginalPath { get; set; } = "";
        public string EditedPath { get; set; } = "";
        public string SessionDir { get; set; } = "";
        public int ChangeCount { get; set; }
        public int IncludedCount { get; set; }
    }
}
