using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace KeywordOcr.App.Services;

public sealed class WorkspaceWorkbookEditResult
{
    public string WorkbookPath { get; init; } = "";
    public List<WorkspaceKeywordEditRow> Rows { get; init; } = new();
}

public sealed class WorkspaceKeywordEditRow : INotifyPropertyChanged
{
    private string _aProductName = "";
    private string _aSearchTags = "";
    private string _aSearchKeywords = "";
    private string _homeNaverProductName = "";
    private string _homeNaverTags = "";
    private string _homeLotteOnProductName = "";
    private string _homeLotteOnKeywords = "";
    private string _homeCommonProductName = "";
    private string _homeCommonKeywords = "";
    private string _bProductName = "";
    private string _bSearchTags = "";
    private string _bSearchKeywords = "";
    private string _candidateExactKeywords = "";
    private string _candidateUseKeywords = "";
    private string _candidateExpandKeywords = "";
    private string _reviewNeeded = "";
    private string _reviewMemo = "";
    private string _uploadStatus = "-";

    public string GsCode { get; set; } = "";
    public int ARowNumber { get; set; }
    public int BRowNumber { get; set; }
    public int OcrRowNumber { get; set; }
    public string UploadStatus
    {
        get => _uploadStatus;
        set => SetField(ref _uploadStatus, value ?? "-", nameof(UploadStatus));
    }

    public string AProductName
    {
        get => _aProductName;
        set => SetField(ref _aProductName, value ?? "", nameof(AProductName));
    }

    public string ASearchTags
    {
        get => _aSearchTags;
        set => SetField(ref _aSearchTags, value ?? "", nameof(ASearchTags));
    }

    public string ASearchKeywords
    {
        get => _aSearchKeywords;
        set => SetField(ref _aSearchKeywords, value ?? "", nameof(ASearchKeywords));
    }

    public string HomeNaverProductName
    {
        get => _homeNaverProductName;
        set => SetField(ref _homeNaverProductName, value ?? "", nameof(HomeNaverProductName));
    }

    public string HomeNaverTags
    {
        get => _homeNaverTags;
        set => SetField(ref _homeNaverTags, value ?? "", nameof(HomeNaverTags));
    }

    public string HomeLotteOnProductName
    {
        get => _homeLotteOnProductName;
        set => SetField(ref _homeLotteOnProductName, value ?? "", nameof(HomeLotteOnProductName));
    }

    public string HomeLotteOnKeywords
    {
        get => _homeLotteOnKeywords;
        set => SetField(ref _homeLotteOnKeywords, value ?? "", nameof(HomeLotteOnKeywords));
    }

    public string HomeCommonProductName
    {
        get => _homeCommonProductName;
        set => SetField(ref _homeCommonProductName, value ?? "", nameof(HomeCommonProductName));
    }

    public string HomeCommonKeywords
    {
        get => _homeCommonKeywords;
        set => SetField(ref _homeCommonKeywords, value ?? "", nameof(HomeCommonKeywords));
    }

    public string BProductName
    {
        get => _bProductName;
        set => SetField(ref _bProductName, value ?? "", nameof(BProductName));
    }

    public string BSearchTags
    {
        get => _bSearchTags;
        set => SetField(ref _bSearchTags, value ?? "", nameof(BSearchTags));
    }

    public string BSearchKeywords
    {
        get => _bSearchKeywords;
        set => SetField(ref _bSearchKeywords, value ?? "", nameof(BSearchKeywords));
    }

    public string CandidateExactKeywords
    {
        get => _candidateExactKeywords;
        set => SetField(ref _candidateExactKeywords, value ?? "", nameof(CandidateExactKeywords));
    }

    public string CandidateUseKeywords
    {
        get => _candidateUseKeywords;
        set => SetField(ref _candidateUseKeywords, value ?? "", nameof(CandidateUseKeywords));
    }

    public string CandidateExpandKeywords
    {
        get => _candidateExpandKeywords;
        set => SetField(ref _candidateExpandKeywords, value ?? "", nameof(CandidateExpandKeywords));
    }

    public string ReviewNeeded
    {
        get => _reviewNeeded;
        set => SetField(ref _reviewNeeded, value ?? "", nameof(ReviewNeeded));
    }

    public string ReviewMemo
    {
        get => _reviewMemo;
        set => SetField(ref _reviewMemo, value ?? "", nameof(ReviewMemo));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(ref string field, string value, string propertyName)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public static class WorkspaceWorkbookEditService
{
    private static readonly Regex GsRegex = new(@"GS\d{7}[A-Z]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static WorkspaceWorkbookEditResult Load(string workbookPath)
    {
        if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
            throw new FileNotFoundException("편집할 엑셀 파일을 찾을 수 없습니다.", workbookPath);

        using var workbook = WorkbookFileLoader.OpenReadOnly(workbookPath);
        var aSheet = FindSheet(workbook, "분리추출후") ?? workbook.Worksheets.FirstOrDefault();
        if (aSheet is null)
            throw new InvalidDataException("엑셀에 시트가 없습니다.");

        var bSheet = FindSheet(workbook, "B마켓");
        var ocrSheet = FindSheet(workbook, "OCR결과");

        var aMap = ReadRows(aSheet);
        var bMap = bSheet is null ? new Dictionary<string, SheetRow>(StringComparer.OrdinalIgnoreCase) : ReadRows(bSheet);
        var ocrMap = ocrSheet is null ? new Dictionary<string, SheetRow>(StringComparer.OrdinalIgnoreCase) : ReadRows(ocrSheet);
        var keys = aMap.Keys
            .Concat(bMap.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<WorkspaceKeywordEditRow>();
        foreach (var key in keys)
        {
            aMap.TryGetValue(key, out var a);
            bMap.TryGetValue(key, out var b);
            ocrMap.TryGetValue(key, out var ocr);

            var item = new WorkspaceKeywordEditRow
            {
                GsCode = key,
                ARowNumber = a?.RowNumber ?? 0,
                BRowNumber = b?.RowNumber ?? 0,
                OcrRowNumber = ocr?.RowNumber ?? 0,
                AProductName = a?.ProductName ?? "",
                ASearchTags = a?.SearchTags ?? "",
                ASearchKeywords = a?.SearchKeywords ?? "",
                HomeNaverProductName = a?.HomeNaverProductName ?? "",
                HomeNaverTags = a?.HomeNaverTags ?? "",
                HomeLotteOnProductName = a?.HomeLotteOnProductName ?? "",
                HomeLotteOnKeywords = a?.HomeLotteOnKeywords ?? "",
                HomeCommonProductName = a?.HomeCommonProductName ?? "",
                HomeCommonKeywords = a?.HomeCommonKeywords ?? "",
                BProductName = b?.ProductName ?? "",
                BSearchTags = b?.SearchTags ?? "",
                BSearchKeywords = b?.SearchKeywords ?? "",
                CandidateExactKeywords = ocr?.CandidateExactKeywords ?? "",
                CandidateUseKeywords = ocr?.CandidateUseKeywords ?? "",
                CandidateExpandKeywords = ocr?.CandidateExpandKeywords ?? "",
                ReviewNeeded = ocr?.ReviewNeeded ?? "",
                ReviewMemo = ocr?.ReviewMemo ?? "",
            };
            rows.Add(item);
        }

        return new WorkspaceWorkbookEditResult
        {
            WorkbookPath = workbookPath,
            Rows = rows,
        };
    }

    public static void Save(string workbookPath, IEnumerable<WorkspaceKeywordEditRow> rows)
    {
        if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
            throw new FileNotFoundException("저장할 엑셀 파일을 찾을 수 없습니다.", workbookPath);

        using var workbook = new XLWorkbook(workbookPath);
        var aSheet = FindSheet(workbook, "분리추출후") ?? workbook.Worksheets.FirstOrDefault();
        if (aSheet is null)
            throw new InvalidDataException("엑셀에 시트가 없습니다.");

        var bSheet = FindSheet(workbook, "B마켓");
        var ocrSheet = FindSheet(workbook, "OCR결과");
        var aColumns = GetColumnMap(aSheet);
        var bColumns = bSheet is null ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) : GetColumnMap(bSheet);
        var ocrColumns = ocrSheet is null ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) : GetColumnMap(ocrSheet);

        foreach (var row in rows)
        {
            if (row.ARowNumber > 1)
            {
                SetCell(aSheet, aColumns, row.ARowNumber, "상품명", row.AProductName);
                SetCell(aSheet, aColumns, row.ARowNumber, "검색어설정", row.ASearchTags);
                SetCell(aSheet, aColumns, row.ARowNumber, "검색키워드", row.ASearchKeywords);
                SetCell(aSheet, aColumns, row.ARowNumber, "홈런_네이버상품명", row.HomeNaverProductName, createColumn: true);
                SetCell(aSheet, aColumns, row.ARowNumber, "홈런_네이버태그", row.HomeNaverTags, createColumn: true);
                SetCell(aSheet, aColumns, row.ARowNumber, "홈런_롯데ON상품명", row.HomeLotteOnProductName, createColumn: true);
                SetCell(aSheet, aColumns, row.ARowNumber, "홈런_롯데ON검색키워드", row.HomeLotteOnKeywords, createColumn: true);
                SetCell(aSheet, aColumns, row.ARowNumber, "홈런_공통마켓상품명", row.HomeCommonProductName, createColumn: true);
                SetCell(aSheet, aColumns, row.ARowNumber, "홈런_공통마켓검색키워드", row.HomeCommonKeywords, createColumn: true);
            }

            if (bSheet is not null && row.BRowNumber > 1)
            {
                SetCell(bSheet, bColumns, row.BRowNumber, "상품명", row.BProductName);
                SetCell(bSheet, bColumns, row.BRowNumber, "검색어설정", row.BSearchTags);
                SetCell(bSheet, bColumns, row.BRowNumber, "검색키워드", row.BSearchKeywords);
            }

            if (ocrSheet is not null && row.OcrRowNumber > 1)
            {
                SetCell(ocrSheet, ocrColumns, row.OcrRowNumber, "후보키워드_정확형", row.CandidateExactKeywords, createColumn: true);
                SetCell(ocrSheet, ocrColumns, row.OcrRowNumber, "후보키워드_용도형", row.CandidateUseKeywords, createColumn: true);
                SetCell(ocrSheet, ocrColumns, row.OcrRowNumber, "후보키워드_확장형", row.CandidateExpandKeywords, createColumn: true);
                SetCell(ocrSheet, ocrColumns, row.OcrRowNumber, "검수필요", row.ReviewNeeded);
                SetCell(ocrSheet, ocrColumns, row.OcrRowNumber, "검수메모", row.ReviewMemo);
            }
        }

        workbook.SaveAs(workbookPath);
    }

    private static IXLWorksheet? FindSheet(XLWorkbook workbook, string name)
        => workbook.Worksheets.FirstOrDefault(ws => string.Equals(ws.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, SheetRow> ReadRows(IXLWorksheet sheet)
    {
        var columns = GetColumnMap(sheet);
        var rows = new Dictionary<string, SheetRow>(StringComparer.OrdinalIgnoreCase);
        var used = sheet.RangeUsed();
        if (used is null)
            return rows;

        var firstDataRow = used.FirstRow().RowNumber() + 1;
        var lastRow = used.LastRow().RowNumber();
        for (var rowNo = firstDataRow; rowNo <= lastRow; rowNo++)
        {
            var gs = FindGsCode(sheet, columns, rowNo);
            if (string.IsNullOrWhiteSpace(gs) || rows.ContainsKey(gs))
                continue;

            rows[gs] = new SheetRow(
                rowNo,
                GetCell(sheet, columns, rowNo, "상품명"),
                GetCell(sheet, columns, rowNo, "검색어설정"),
                GetCell(sheet, columns, rowNo, "검색키워드"),
                GetCell(sheet, columns, rowNo, "홈런_네이버상품명"),
                GetCell(sheet, columns, rowNo, "홈런_네이버태그"),
                GetCell(sheet, columns, rowNo, "홈런_롯데ON상품명"),
                GetCell(sheet, columns, rowNo, "홈런_롯데ON검색키워드"),
                GetCell(sheet, columns, rowNo, "홈런_공통마켓상품명"),
                GetCell(sheet, columns, rowNo, "홈런_공통마켓검색키워드"),
                GetCell(sheet, columns, rowNo, "후보키워드_정확형"),
                GetCell(sheet, columns, rowNo, "후보키워드_용도형"),
                GetCell(sheet, columns, rowNo, "후보키워드_확장형"),
                GetCell(sheet, columns, rowNo, "검수필요"),
                GetCell(sheet, columns, rowNo, "검수메모"));
        }

        return rows;
    }

    private static Dictionary<string, int> GetColumnMap(IXLWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var used = sheet.RangeUsed();
        if (used is null)
            return map;

        var headerRow = used.FirstRow();
        foreach (var cell in headerRow.CellsUsed())
        {
            var name = NormalizeHeader(cell.GetString());
            if (!string.IsNullOrWhiteSpace(name) && !map.ContainsKey(name))
                map[name] = cell.Address.ColumnNumber;
        }

        return map;
    }

    private static string FindGsCode(IXLWorksheet sheet, Dictionary<string, int> columns, int rowNo)
    {
        foreach (var name in new[] { "자체상품코드", "상품코드", "GS코드", "자체코드" })
        {
            if (columns.TryGetValue(name, out var col))
            {
                var fromCode = NormalizeGs(sheet.Cell(rowNo, col).GetString());
                if (!string.IsNullOrWhiteSpace(fromCode))
                    return fromCode;
            }
        }

        if (columns.TryGetValue("상품명", out var nameCol))
        {
            var fromName = NormalizeGs(sheet.Cell(rowNo, nameCol).GetString());
            if (!string.IsNullOrWhiteSpace(fromName))
                return fromName;
        }

        foreach (var cell in sheet.Row(rowNo).CellsUsed())
        {
            var value = NormalizeGs(cell.GetString());
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return "";
    }

    private static string NormalizeGs(string value)
    {
        var match = GsRegex.Match(value ?? "");
        return match.Success ? match.Value.ToUpperInvariant() : "";
    }

    private static string GetCell(IXLWorksheet sheet, Dictionary<string, int> columns, int rowNo, string header)
    {
        return columns.TryGetValue(NormalizeHeader(header), out var col)
            ? sheet.Cell(rowNo, col).GetString()
            : "";
    }

    private static void SetCell(IXLWorksheet sheet, Dictionary<string, int> columns, int rowNo, string header, string value, bool createColumn = false)
    {
        var normalized = NormalizeHeader(header);
        if (!columns.TryGetValue(normalized, out var col))
        {
            if (!createColumn)
                return;

            var used = sheet.RangeUsed();
            col = (used?.LastColumn().ColumnNumber() ?? 0) + 1;
            sheet.Cell(1, col).Value = header;
            columns[normalized] = col;
        }

        sheet.Cell(rowNo, col).Value = value ?? "";
    }

    private static string NormalizeHeader(string value)
        => Regex.Replace(value ?? "", @"\s+", "").Trim();

    private sealed record SheetRow(
        int RowNumber,
        string ProductName,
        string SearchTags,
        string SearchKeywords,
        string HomeNaverProductName,
        string HomeNaverTags,
        string HomeLotteOnProductName,
        string HomeLotteOnKeywords,
        string HomeCommonProductName,
        string HomeCommonKeywords,
        string CandidateExactKeywords,
        string CandidateUseKeywords,
        string CandidateExpandKeywords,
        string ReviewNeeded,
        string ReviewMemo);
}
