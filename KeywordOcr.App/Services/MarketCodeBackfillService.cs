using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;

namespace KeywordOcr.App.Services;

public sealed record MarketCodeBackfillOptions
{
    public bool Apply { get; init; }
    public string? StatePath { get; init; }
    public string? OutputPath { get; init; }
}

public sealed record MarketCodeBackfillReport(string ReportPath, int Total, int Inspectable, int NeedsUpdate, int Blocked);

public sealed class MarketCodeBackfillService
{
    private static readonly Regex ProductCodeRegex = new(@"\b(GS\d{7}[A-Z0-9]*)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<MarketCodeBackfillReport> BuildReportAsync(
        MarketCodeBackfillOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var statePath = string.IsNullOrWhiteSpace(options.StatePath)
            ? MarketUploadStateStore.DefaultPath
            : options.StatePath!;
        if (!File.Exists(statePath))
            throw new FileNotFoundException("market_upload_state.json 파일을 찾지 못했습니다.", statePath);

        var entries = LoadEntries(statePath);
        var rows = new List<BackfillRow>();

        using var naver = NaverCommerceApiClient.FromKeyFile();
        using var coupang = CoupangApiClient.FromKeyFile();

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var market in entry.Markets)
            {
                var target = market.Value;
                if (!IsSuccess(target.Status) || string.IsNullOrWhiteSpace(target.ProductId))
                    continue;

                var row = new BackfillRow
                {
                    Market = market.Key,
                    GsCode = entry.GsCode,
                    ProductName = entry.ProductName,
                    ProductId = target.ProductId,
                    UploadStatus = target.Status,
                };

                try
                {
                    if (IsNaver(market.Key))
                        await InspectNaverAsync(naver, row, ct);
                    else if (IsCoupang(market.Key))
                        await InspectCoupangAsync(coupang, row, ct);
                    else if (IsLotteOn(market.Key))
                    {
                        row.InspectStatus = "SKIP";
                        row.Decision = "MANUAL";
                        row.Reason = "롯데ON은 현재 조회/수정 API 클라이언트가 없어 리포트 대상만 기록";
                    }
                    else
                    {
                        row.InspectStatus = "SKIP";
                        row.Decision = "MANUAL";
                        row.Reason = "11번가/ESM은 추후 업로드 엑셀 정리 후 처리";
                    }
                }
                catch (Exception ex)
                {
                    row.InspectStatus = "ERROR";
                    row.Decision = "MANUAL";
                    row.Reason = Short(ex.Message);
                }

                rows.Add(row);
                progress?.Report($"{row.Market} {row.GsCode} {row.ProductId}: {row.Decision}");
                await Task.Delay(180, ct);
            }
        }

        var reportPath = WriteReport(options.OutputPath, rows);
        return new MarketCodeBackfillReport(
            reportPath,
            rows.Count,
            rows.Count(r => r.InspectStatus == "OK"),
            rows.Count(r => r.Decision == "UPDATE_READY"),
            rows.Count(r => r.Decision == "BLOCK"));
    }

    private static async Task InspectNaverAsync(NaverCommerceApiClient api, BackfillRow row, CancellationToken ct)
    {
        if (!long.TryParse(row.ProductId, out var originProductNo))
        {
            row.InspectStatus = "ERROR";
            row.Decision = "MANUAL";
            row.Reason = "네이버 originProductNo 숫자 변환 실패";
            return;
        }

        using var doc = await api.GetOriginProductAsync(originProductNo, ct);
        row.InspectStatus = "OK";
        var root = doc.RootElement;
        row.CurrentCode = FindFirstString(root, "sellerManagementCode", "sellerCustomCode", "sellerCode");
        row.OptionCodes = string.Join(" | ", FindAllOptionCodeLines(root).Take(30));
        DecideByCode(row);
    }

    private static async Task InspectCoupangAsync(CoupangApiClient api, BackfillRow row, CancellationToken ct)
    {
        if (!long.TryParse(row.ProductId, out var sellerProductId))
        {
            row.InspectStatus = "ERROR";
            row.Decision = "MANUAL";
            row.Reason = "쿠팡 sellerProductId 숫자 변환 실패";
            return;
        }

        using var doc = await api.GetProductAsync(sellerProductId, ct);
        row.InspectStatus = "OK";
        var root = doc.RootElement;
        row.CurrentCode = FindFirstString(root, "sellerProductName", "externalVendorSku", "externalVendorSkuCode");
        row.OptionCodes = string.Join(" | ", FindAllOptionCodeLines(root).Take(50));
        DecideByOptionCodes(row);
    }

    private static void DecideByCode(BackfillRow row)
    {
        if (ContainsGs(row.CurrentCode) || ContainsGs(row.OptionCodes))
        {
            row.Decision = "OK";
            row.Reason = "이미 GS코드 확인됨";
            return;
        }

        row.Decision = "UPDATE_READY";
        row.Reason = "GS코드 미확인, 상품 단위 코드 추가 후보";
    }

    private static void DecideByOptionCodes(BackfillRow row)
    {
        if (ContainsGs(row.OptionCodes))
        {
            row.Decision = "OK";
            row.Reason = "옵션 SKU에 GS코드 확인됨";
            return;
        }

        if (ContainsGs(row.CurrentCode))
        {
            row.Decision = "BLOCK";
            row.Reason = "상품명에는 GS코드가 있으나 옵션 SKU 확인 안됨. 옵션별 A/B/C 검수 필요";
            return;
        }

        row.Decision = "UPDATE_READY";
        row.Reason = "GS코드 미확인, 옵션별 SKU 추가 후보";
    }

    private static string WriteReport(string? outputPath, IReadOnlyList<BackfillRow> rows)
    {
        var targetPath = outputPath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "key", "reports");
            Directory.CreateDirectory(dir);
            targetPath = Path.Combine(dir, $"마켓상품코드_역주입_검수_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("검수");
        var headers = new[]
        {
            "마켓", "GS코드", "상품ID", "업로드상태", "조회상태", "판정", "현재대표코드/명", "옵션코드", "상품명", "사유"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAD3");
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var row = i + 2;
            ws.Cell(row, 1).Value = r.Market;
            ws.Cell(row, 2).Value = r.GsCode;
            ws.Cell(row, 3).Value = r.ProductId;
            ws.Cell(row, 4).Value = r.UploadStatus;
            ws.Cell(row, 5).Value = r.InspectStatus;
            ws.Cell(row, 6).Value = r.Decision;
            ws.Cell(row, 7).Value = r.CurrentCode;
            ws.Cell(row, 8).Value = r.OptionCodes;
            ws.Cell(row, 9).Value = r.ProductName;
            ws.Cell(row, 10).Value = r.Reason;
        }

        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()?.SetAutoFilter();
        ws.Columns().AdjustToContents(8, 80);
        wb.SaveAs(targetPath);
        return targetPath;
    }

    private static List<StateEntry> LoadEntries(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var result = new List<StateEntry>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var gs = ExtractProductCode(prop.Name);
            if (string.IsNullOrWhiteSpace(gs)) continue;

            var entry = prop.Value;
            var item = new StateEntry
            {
                GsCode = gs,
                ProductName = GetString(entry, "ProductName", "productName"),
            };

            if (entry.TryGetProperty("Markets", out var markets) || entry.TryGetProperty("markets", out markets))
            {
                foreach (var marketProp in markets.EnumerateObject())
                {
                    item.Markets[marketProp.Name] = new StateMarket
                    {
                        Status = GetString(marketProp.Value, "Status", "status"),
                        ProductId = GetString(marketProp.Value, "ProductId", "productId"),
                    };
                }
            }

            result.Add(item);
        }

        return result;
    }

    private static string ExtractProductCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var match = ProductCodeRegex.Match(value);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : "";
    }

    private static bool ContainsGs(string? value) => !string.IsNullOrWhiteSpace(ExtractProductCode(value));

    private static bool IsSuccess(string value)
        => value.Equals("OK", StringComparison.OrdinalIgnoreCase)
           || value.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)
           || value.Equals("SKIP_DUP", StringComparison.OrdinalIgnoreCase);

    private static bool IsNaver(string value) => value.Contains("네이버", StringComparison.OrdinalIgnoreCase) || value.Contains("NAVER", StringComparison.OrdinalIgnoreCase);
    private static bool IsCoupang(string value) => value.Contains("쿠팡", StringComparison.OrdinalIgnoreCase) || value.Contains("COUPANG", StringComparison.OrdinalIgnoreCase);
    private static bool IsLotteOn(string value) => value.Contains("롯데", StringComparison.OrdinalIgnoreCase) || value.Contains("LOTTE", StringComparison.OrdinalIgnoreCase);

    private static string GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var prop))
                return prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? "" : prop.ToString();
        }
        return "";
    }

    private static string FindFirstString(JsonElement element, params string[] names)
    {
        var nameSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        foreach (var found in FindStrings(element, nameSet))
            return found;
        return "";
    }

    private static IEnumerable<string> FindStrings(JsonElement element, IReadOnlySet<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (names.Contains(prop.Name) && prop.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    yield return prop.Value.ToString();
                foreach (var nested in FindStrings(prop.Value, names))
                    yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            foreach (var nested in FindStrings(item, names))
                yield return nested;
        }
    }

    private static IEnumerable<string> FindAllOptionCodeLines(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var code = GetString(element, "externalVendorSku", "externalVendorSkuCode", "sellerManagementCode", "sellerCustomCode");
            var name = GetString(element, "itemName", "sellerProductItemName", "optionName", "name");
            if (!string.IsNullOrWhiteSpace(code))
                yield return string.IsNullOrWhiteSpace(name) ? code : $"{name}={code}";

            foreach (var prop in element.EnumerateObject())
            foreach (var nested in FindAllOptionCodeLines(prop.Value))
                yield return nested;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            foreach (var nested in FindAllOptionCodeLines(item))
                yield return nested;
        }
    }

    private static string Short(string value)
        => string.IsNullOrWhiteSpace(value) ? "" : value.Length > 220 ? value[..220] : value;

    private sealed class StateEntry
    {
        public string GsCode { get; init; } = "";
        public string ProductName { get; init; } = "";
        public Dictionary<string, StateMarket> Markets { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class StateMarket
    {
        public string Status { get; init; } = "";
        public string ProductId { get; init; } = "";
    }

    private sealed class BackfillRow
    {
        public string Market { get; init; } = "";
        public string GsCode { get; init; } = "";
        public string ProductId { get; init; } = "";
        public string ProductName { get; init; } = "";
        public string UploadStatus { get; init; } = "";
        public string InspectStatus { get; set; } = "";
        public string Decision { get; set; } = "";
        public string CurrentCode { get; set; } = "";
        public string OptionCodes { get; set; } = "";
        public string Reason { get; set; } = "";
    }
}
