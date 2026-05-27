using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;

namespace KeywordOcr.App.Services;

public sealed record CoupangUploadOptions
{
    public int RowStart { get; set; }
    public int RowEnd { get; set; }
    public bool DryRun { get; set; } = true;
    public IReadOnlySet<string>? AllowedGsCodes { get; set; }
    public string? Cafe24TokenPath { get; set; }
}

public sealed record CoupangUploadResultItem(
    int Row,
    string Name,
    string Status,
    string Category,
    string SellerProductId,
    string Error);

public sealed record CoupangUploadResult(
    IReadOnlyList<CoupangUploadResultItem> Items,
    int SuccessCount,
    int FailCount,
    int TotalCount);

/// <summary>
/// 쿠팡 상품 업로드 서비스 (순수 C# — Python 의존성 없음)
/// </summary>
public sealed class CoupangUploadService
{
    private const int CategoryBatchSize = 5;
    private const int RegisterBatchSize = 5;
    private const int CoupangMinimumUploadPrice = 1;

    public async Task<CoupangUploadResult> UploadAsync(
        string sourcePath,
        CoupangUploadOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        IReadOnlySet<int>? allowedRowNums = null)
    {
        void Log(string msg) => progress?.Report(msg);

        // 키 로드 + API 클라이언트
        using var api = CoupangApiClient.FromKeyFile();

        // 엑셀 읽기
        Log("가공파일 읽는 중...");
        var allRows = CoupangProductBuilder.ReadSourceFile(sourcePath);
        Log($"{allRows.Count}개 상품 로드 완료");

        // 행 필터
        IEnumerable<Dictionary<string, object?>> filteredRows = allRows;
        if (options.AllowedGsCodes is { Count: > 0 } allowedGsCodes)
        {
            filteredRows = filteredRows.Where(row => allowedGsCodes.Contains(ExtractGsCode(row)));
        }

        List<Dictionary<string, object?>> targetRows;
        if (allowedRowNums is not null)
        {
            targetRows = filteredRows.Where(r => allowedRowNums.Contains((int)r["_row_num"]!)).ToList();
        }
        else if (options.RowStart > 0)
        {
            var end = options.RowEnd > 0 ? options.RowEnd : options.RowStart;
            targetRows = filteredRows.Where(r =>
            {
                var rowNum = (int)r["_row_num"]! - 1; // 0-based
                return rowNum >= options.RowStart && rowNum <= end;
            }).ToList();
        }
        else
        {
            targetRows = filteredRows.ToList();
        }

        Log($"처리 대상: {targetRows.Count}개");
        var results = new List<CoupangUploadResultItem>();
        var categoryCache = new Dictionary<long, JsonElement>();
        var deliveryTemplate = await LoadReferenceDeliveryTemplateAsync(api, Log, ct);
        var cafe24TokenPath = string.IsNullOrWhiteSpace(options.Cafe24TokenPath)
            ? ResolveDefaultHomeCafe24TokenPath()
            : options.Cafe24TokenPath;
        var cafe24MarketData = options.DryRun
            ? null
            : await Cafe24MarketDataService.TryCreateAsync(sourcePath, Log, ct, cafe24TokenPath);

        if (cafe24MarketData is not null)
        {
            Log("Cafe24 상품 데이터 동기화 중...");
            foreach (var row in targetRows)
                await cafe24MarketData.TryApplyAsync(row, ct);
        }

        var lowPriceRows = targetRows
            .Where(row => GetInt(row, "판매가") < CoupangMinimumUploadPrice)
            .ToList();
        if (lowPriceRows.Count > 0)
        {
            var reportPath = WriteLowPriceSkipReport(sourcePath, lowPriceRows);
            foreach (var row in lowPriceRows)
            {
                var rowNum = (int)row["_row_num"]!;
                var shortName = GetStr(row, "상품명");
                if (shortName.Length > 50) shortName = shortName[..50];
                var price = GetInt(row, "판매가");
                results.Add(new CoupangUploadResultItem(
                    rowNum,
                    shortName,
                    "SKIP_PRICE_ZERO_OR_LESS",
                    "",
                    "",
                    $"쿠팡 판매가 0원 이하 업로드 제외: {price:#,0}원"));
            }

            var skipSet = lowPriceRows
                .Select(row => (int)row["_row_num"]!)
                .ToHashSet();
            targetRows = targetRows
                .Where(row => !skipSet.Contains((int)row["_row_num"]!))
                .ToList();
            Log($"쿠팡 판매가 0원 이하 {lowPriceRows.Count}개 업로드 제외");
            Log($"제외 목록 저장: {reportPath}");
            Log($"실제 업로드 진행 대상: {targetRows.Count}개");
        }

        // ── 1단계: 카테고리 추천 ──────────────────

        // 카테고리 결과 저장 (index → categoryCode, categoryName)
        var catResults = new (long Code, string Name, bool Ok)[targetRows.Count];

        // 엑셀에 카테고리 지정된 행 처리
        var needPredict = new List<int>(); // index into targetRows
        for (int i = 0; i < targetRows.Count; i++)
        {
            var row = targetRows[i];
            var presetCat = GetStr(row, "쿠팡카테고리코드").OrIfEmpty(GetStr(row, "쿠팡카테고리"));
            if (!string.IsNullOrEmpty(presetCat) && long.TryParse(presetCat, out var code))
            {
                catResults[i] = (code, $"엑셀지정({code})", true);
                Log($"  행{row["_row_num"]}: 엑셀 카테고리 사용 ({code})");
            }
            else
            {
                needPredict.Add(i);
            }
        }

        // API로 카테고리 추천 (배치)
        if (needPredict.Count > 0)
        {
            Log($"카테고리 추천 중... ({needPredict.Count}건 API 호출)");

            for (int batchStart = 0; batchStart < needPredict.Count; batchStart += CategoryBatchSize)
            {
                ct.ThrowIfCancellationRequested();
                var batchEnd = Math.Min(batchStart + CategoryBatchSize, needPredict.Count);
                var batchT0 = DateTime.UtcNow;

                var tasks = new List<Task>();
                for (int b = batchStart; b < batchEnd; b++)
                {
                    var idx = needPredict[b];
                    var row = targetRows[idx];
                    var productName = GetStr(row, "상품명");
                    var capturedIdx = idx;

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            using var doc = await api.PredictCategoryAsync(productName, ct);
                            var root = doc.RootElement;

                            if (root.TryGetProperty("code", out var codeProp))
                            {
                                var codeStr = codeProp.ValueKind == System.Text.Json.JsonValueKind.String
                                    ? codeProp.GetString() : codeProp.ToString();
                                if (codeStr == "ERROR")
                                {
                                    var errMsg = root.TryGetProperty("message", out var mp) ? mp.ToString() : "API ERROR";
                                    catResults[capturedIdx] = (0, errMsg, false);
                                    return;
                                }
                            }

                            if (!root.TryGetProperty("data", out var data))
                            {
                                catResults[capturedIdx] = (0, "응답에 data 없음", false);
                                return;
                            }

                            var resultType = data.GetProperty("autoCategorizationPredictionResultType").GetString();
                            if (resultType == "SUCCESS")
                            {
                                var catIdProp = data.GetProperty("predictedCategoryId");
                                long code = catIdProp.ValueKind == System.Text.Json.JsonValueKind.Number
                                    ? catIdProp.GetInt64()
                                    : long.Parse(catIdProp.GetString() ?? "0");
                                var name = data.GetProperty("predictedCategoryName").GetString() ?? "";
                                catResults[capturedIdx] = (code, name, true);
                            }
                            else
                            {
                                catResults[capturedIdx] = (0, resultType ?? "UNKNOWN", false);
                            }
                        }
                        catch (Exception ex)
                        {
                            catResults[capturedIdx] = (0, ex.Message, false);
                        }
                    }, ct));
                }

                await Task.WhenAll(tasks);
                Log($"[{batchEnd}/{needPredict.Count}] 카테고리 추천 중...");

                var elapsed = (DateTime.UtcNow - batchT0).TotalSeconds;
                if (elapsed < 1.5)
                    await Task.Delay(TimeSpan.FromSeconds(1.5 - elapsed), ct);
            }
        }

        Log("카테고리 추천 완료");

        // 카테고리 실패 처리 + 메타 로드
        for (int i = 0; i < targetRows.Count; i++)
        {
            var row = targetRows[i];
            var rowNum = (int)row["_row_num"]!;
            var shortName = GetStr(row, "상품명");
            if (shortName.Length > 50) shortName = shortName[..50];

            if (!catResults[i].Ok)
            {
                results.Add(new CoupangUploadResultItem(rowNum, shortName, "CATEGORY_FAIL", "", "", catResults[i].Name));
                continue;
            }

            var catCode = catResults[i].Code;
            var categoryLabel = catResults[i].Name;
            if (TryGetCategoryLockViolation(row, catCode, categoryLabel, out var lockReason))
            {
                results.Add(new CoupangUploadResultItem(
                    rowNum,
                    shortName,
                    "SKIP_CATEGORY_LOCK",
                    $"[{catCode}] {categoryLabel}",
                    "",
                    lockReason));
                continue;
            }

            if (!categoryCache.ContainsKey(catCode))
            {
                try
                {
                    using var metaDoc = await api.GetCategoryMetaAsync(catCode, ct);
                    categoryCache[catCode] = metaDoc.RootElement.Clone();
                }
                catch
                {
                    categoryCache[catCode] = JsonDocument.Parse("""{"data":{"attributes":[],"noticeCategories":[]}}""").RootElement.Clone();
                }
            }

            row["_category_code"] = catCode;
            row["_category_name"] = categoryLabel;
            row["_category_meta"] = categoryCache[catCode];
        }

        var categoryLockRows = results
            .Where(item => item.Status == "SKIP_CATEGORY_LOCK")
            .ToList();
        if (categoryLockRows.Count > 0)
        {
            var blockedRowNums = categoryLockRows.Select(item => item.Row).ToHashSet();
            var reportPath = WriteCategoryLockSkipReport(
                sourcePath,
                targetRows.Where(row => blockedRowNums.Contains((int)row["_row_num"]!)).ToList(),
                categoryLockRows);
            Log($"카테고리 강력 고정 위반 {categoryLockRows.Count}개 업로드 제외");
            Log($"카테고리 검수 목록 저장: {reportPath}");
        }

        // ── 1.5단계: 대표/추가 선택이 반영된 listing_images 우선, 없을 때만 Cafe24 URL 사용 ──

        Log("상품 이미지 URL 준비 중...");
        try
        {
            using var naverApi = NaverCommerceApiClient.FromKeyFile();

            foreach (var row in targetRows)
            {
                var sku = GetStr(row, "자체 상품코드");
                var exportRoot = GetStr(row, "_export_root");
                var (sourceImages, sourceLabel, alreadyUploadReady) = ResolveImageSources(row, sku, exportRoot);
                if (alreadyUploadReady)
                {
                    Log($"  {sku}: {sourceLabel} 이미지 URL 사용 {sourceImages.Count}장");
                    continue;
                }

                if (sourceImages.Count == 0)
                {
                    if (!string.IsNullOrEmpty(sku) && !string.IsNullOrEmpty(exportRoot))
                        Log($"  {sku}: listing_images/Cafe24 이미지 없음 (엑셀 URL fallback)");
                    continue;
                }

                var uploadedUrls = new List<string>();
                foreach (var imageSource in sourceImages)
                {
                    try
                    {
                        var cdnUrl = await UploadImageWithRetryAsync(naverApi, imageSource, Log, ct);
                        uploadedUrls.Add(cdnUrl);
                        await Task.Delay(300, ct);
                    }
                    catch (Exception imageEx)
                    {
                        Log($"  {sku}: 이미지 업로드 실패: {ShortImageLabel(imageSource)} | {ShortError(imageEx.Message)}");
                    }
                }

                if (uploadedUrls.Count > 0)
                {
                    row["_cafe24_image_urls"] = uploadedUrls;
                    Log($"  {sku}: {sourceLabel} -> 네이버 CDN {uploadedUrls.Count}장");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"가공이미지 업로드 실패 (엑셀 URL fallback): {ex.Message}");
        }

        // ── 2단계: JSON 생성 ─────────────────────

        Log("상품 JSON 생성 중...");
        var products = new List<(int Row, string GsCode, string Name, string Category, JsonObject Json, List<string> ImageUrls)>();

        foreach (var row in targetRows)
        {
            if (!row.ContainsKey("_category_code")) continue;
            var catCode = (long)row["_category_code"]!;
            var catName = (string)row["_category_name"]!;
            var catMeta = (JsonElement)row["_category_meta"]!;

            var productJson = CoupangProductBuilder.BuildProduct(
                row,
                catCode,
                catMeta,
                api.VendorId,
                api.VendorUserId,
                deliveryTemplate,
                api.OutboundShippingPlaceCode,
                api.ReturnCenterCode);
            var shortName = GetStr(row, "상품명");
            if (shortName.Length > 50) shortName = shortName[..50];
            var productGsCode = ExtractGsCode(row);
            var stateImages = row.TryGetValue("_cafe24_image_urls", out var imageValue) && imageValue is IEnumerable<string> imageUrls
                ? imageUrls.Where(MarketImageUrlGuard.IsAllowedUploadUrl).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();
            products.Add(((int)row["_row_num"]!, productGsCode, shortName, $"[{catCode}] {catName}", productJson, stateImages));
        }

        Log($"JSON 생성 완료: {products.Count}개");

        // ── 3단계: 등록 또는 DRY RUN ──────────────

        if (!options.DryRun)
        {
            Log($"쿠팡 등록 시작 ({products.Count}개)...");
            var regResults = new CoupangUploadResultItem[products.Count];

            for (int batchStart = 0; batchStart < products.Count; batchStart += RegisterBatchSize)
            {
                ct.ThrowIfCancellationRequested();
                var batchEnd = Math.Min(batchStart + RegisterBatchSize, products.Count);
                var batchT0 = DateTime.UtcNow;

                var tasks = new List<Task>();
                for (int b = batchStart; b < batchEnd; b++)
                {
                    var p = products[b];
                    var capturedIdx = b;

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            using var respDoc = await api.CreateProductAsync(
                                JsonSerializer.Deserialize<JsonElement>(p.Json.ToJsonString()), ct);
                            var root = respDoc.RootElement;
                            var code = root.TryGetProperty("code", out var codeProp)
                                ? (codeProp.ValueKind == JsonValueKind.String ? codeProp.GetString() : codeProp.ToString())
                                : null;

                            if (code == "SUCCESS")
                            {
                                var spid = root.TryGetProperty("data", out var dataProp) ? dataProp.ToString() : "";
                                regResults[capturedIdx] = new(p.Row, p.Name, "SUCCESS", p.Category, spid, "");
                                MarketUploadStateStore.Upsert(p.GsCode, p.Name, "쿠팡", "SUCCESS", spid, p.ImageUrls);
                            }
                            else
                            {
                                var msg = root.TryGetProperty("message", out var msgProp) ? msgProp.ToString() : "";
                                if (msg.Length > 200) msg = msg[..200];
                                regResults[capturedIdx] = new(p.Row, p.Name, $"FAIL_{code}", p.Category, "", msg);
                                MarketUploadStateStore.Upsert(p.GsCode, p.Name, "쿠팡", $"FAIL_{code}", "", p.ImageUrls, msg);
                            }
                        }
                        catch (Exception ex)
                        {
                            var error = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
                            regResults[capturedIdx] = new(p.Row, p.Name, "REGISTER_FAIL", p.Category, "", error);
                            MarketUploadStateStore.Upsert(p.GsCode, p.Name, "쿠팡", "REGISTER_FAIL", "", p.ImageUrls, error);
                        }
                    }, ct));
                }

                await Task.WhenAll(tasks);
                Log($"[{batchEnd}/{products.Count}] 등록 중...");

                var elapsed = (DateTime.UtcNow - batchT0).TotalSeconds;
                if (elapsed < 1.5)
                    await Task.Delay(TimeSpan.FromSeconds(1.5 - elapsed), ct);
            }

            results.AddRange(regResults);
        }
        else
        {
            Log("DRY RUN 완료 - 등록하지 않음");
            foreach (var p in products)
            {
                // 옵션별 가격 로그 출력 (확인용)
                if (p.Json.TryGetPropertyValue("items", out var itemsNode) && itemsNode is JsonArray itemsArr)
                {
                    var baseSale = itemsArr.Count > 0 ? (int)(itemsArr[0]?["salePrice"]?.GetValue<int>() ?? 0) : 0;
                    foreach (var item in itemsArr)
                    {
                        var name = item?["itemName"]?.GetValue<string>() ?? "";
                        var sale = item?["salePrice"]?.GetValue<int>() ?? 0;
                        var diff = sale - baseSale;
                        var diffStr = diff >= 0 ? $"+{diff:#,0}원" : $"{diff:#,0}원";
                        Log($"  옵션: {name} = {sale:#,0}원 ({diffStr})");
                    }
                }

                // 이미지 수 로그
                if (p.Json.TryGetPropertyValue("items", out var items2) && items2 is JsonArray arr2 && arr2.Count > 0)
                {
                    var firstItem = arr2[0];
                    var imgCount = 0;
                    if (firstItem?["images"] is JsonArray imgs) imgCount = imgs.Count;
                    var contentCount = 0;
                    if (firstItem?["contents"] is JsonArray contents)
                    {
                        foreach (var c in contents)
                        {
                            if (c?["contentDetails"] is JsonArray details) contentCount += details.Count;
                        }
                    }
                    Log($"  이미지: 대표+추가 {imgCount}장, 상세 {contentCount}장");
                }

                results.Add(new CoupangUploadResultItem(p.Row, p.Name, "DRY_RUN", p.Category, "", ""));
            }
        }

        var successCount = results.Count(r => r.Status is "SUCCESS" or "DRY_RUN" or "SKIP_PRICE_ZERO_OR_LESS");
        var failCount = results.Count - successCount;
        return new CoupangUploadResult(results, successCount, failCount, results.Count);
    }

    private static string GetStr(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var v) && v is not null ? v.ToString()?.Trim() ?? "" : "";

    private static int GetInt(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v is null)
            return 0;
        if (v is double d)
            return (int)d;
        if (v is int i)
            return i;

        var text = v.ToString()?.Trim().Replace(",", "") ?? "";
        if (int.TryParse(text, out var parsedInt))
            return parsedInt;
        if (double.TryParse(text, out var parsedDouble))
            return (int)parsedDouble;
        return 0;
    }

    private static string WriteLowPriceSkipReport(
        string sourcePath,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var sourceDir = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Directory.GetCurrentDirectory();
        var reportDir = Path.Combine(sourceDir, "reports");
        Directory.CreateDirectory(reportDir);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var reportPath = Path.Combine(reportDir, $"쿠팡_0원이하_업로드제외_{stamp}.xlsx");

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("업로드제외");
        var headers = new[]
        {
            "원본행",
            "상품코드",
            "상품명",
            "쿠팡상품명",
            "판매가",
            "상품가",
            "소비자가",
            "옵션입력",
            "옵션추가금",
            "쿠팡카테고리코드",
            "제외사유",
            "원본파일"
        };

        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE4D6");
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var excelRow = r + 2;
            var productName = GetStr(row, "상품명");
            var coupangName = GetStr(row, "홈런_쿠팡상품명")
                .OrIfEmpty(GetStr(row, "쿠팡상품명"));

            ws.Cell(excelRow, 1).Value = (int)row["_row_num"]!;
            ws.Cell(excelRow, 2).Value = GetStr(row, "자체 상품코드").OrIfEmpty(GetStr(row, "상품코드"));
            ws.Cell(excelRow, 3).Value = productName;
            ws.Cell(excelRow, 4).Value = coupangName;
            ws.Cell(excelRow, 5).Value = GetInt(row, "판매가");
            ws.Cell(excelRow, 6).Value = GetInt(row, "상품가");
            ws.Cell(excelRow, 7).Value = GetInt(row, "소비자가");
            ws.Cell(excelRow, 8).Value = GetStr(row, "옵션입력");
            ws.Cell(excelRow, 9).Value = GetStr(row, "옵션추가금");
            ws.Cell(excelRow, 10).Value = GetStr(row, "쿠팡카테고리코드").OrIfEmpty(GetStr(row, "쿠팡카테고리"));
            ws.Cell(excelRow, 11).Value = "쿠팡 판매가 0원 이하라 API 업로드 제외";
            ws.Cell(excelRow, 12).Value = sourcePath;
        }

        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()?.SetAutoFilter();
        ws.Columns().AdjustToContents(8, 60);
        workbook.SaveAs(reportPath);
        return reportPath;
    }

    private static bool TryGetCategoryLockViolation(
        Dictionary<string, object?> row,
        long coupangCategoryCode,
        string categoryLabel,
        out string reason)
    {
        var basis = CompactCategoryText(string.Join(" ", new[]
        {
            GetStr(row, "상품명"),
            GetStr(row, "공급사 상품명"),
            GetStr(row, "홈런_공통마켓상품명"),
            GetStr(row, "홈런_쿠팡상품명"),
            GetStr(row, "쿠팡상품명"),
            GetStr(row, "쿠팡검색태그"),
            GetStr(row, "검색키워드"),
            GetStr(row, "옵션입력"),
            GetStr(row, "상품 상세설명"),
            GetStr(row, "상세설명"),
        }));
        var categoryText = CompactCategoryText($"{coupangCategoryCode} {categoryLabel} {GetStr(row, "쿠팡카테고리경로")} {GetStr(row, "쿠팡카테고리")}");

        if (HasAny(basis, "분무기", "압축분무기", "농약분무기", "원예분무기")
            && HasAny(basis, "원예", "정원", "농업", "농사", "농약", "가드닝", "화분", "식물", "물조리개", "급수")
            && (coupangCategoryCode == 64310
                || HasAny(categoryText, "나사", "앙카", "볼트", "너트", "체결", "철물", "못/콘크리트못", "경첩")))
        {
            reason = "원예/농업 분무기 상품이 나사/앙카/철물 계열 쿠팡 카테고리로 매칭되어 차단";
            return true;
        }

        if (HasAny(basis, "세탁기거름망", "세탁기필터", "먼지거름망", "세탁거름망")
            && HasAny(basis, "세탁", "세탁기", "먼지", "보풀", "거름망", "필터")
            && (coupangCategoryCode == 64310
                || HasAny(categoryText, "나사", "앙카", "볼트", "너트", "체결", "철물", "원예", "가드닝")))
        {
            reason = "세탁기 거름망/필터 상품이 무관 카테고리로 매칭되어 차단";
            return true;
        }

        reason = "";
        return false;
    }

    private static string CompactCategoryText(string value)
        => Regex.Replace(value ?? "", @"\s+", "").ToLowerInvariant();

    private static bool HasAny(string text, params string[] terms)
        => terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string WriteCategoryLockSkipReport(
        string sourcePath,
        IReadOnlyList<Dictionary<string, object?>> rows,
        IReadOnlyList<CoupangUploadResultItem> skipped)
    {
        var sourceDir = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Directory.GetCurrentDirectory();
        var reportDir = Path.Combine(sourceDir, "reports");
        Directory.CreateDirectory(reportDir);

        var reasonByRow = skipped.ToDictionary(item => item.Row);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var reportPath = Path.Combine(reportDir, $"쿠팡_카테고리강력고정_검수필요_{stamp}.xlsx");

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("카테고리검수필요");
        var headers = new[]
        {
            "원본행",
            "상품코드",
            "상품명",
            "쿠팡상품명",
            "현재쿠팡카테고리",
            "옵션입력",
            "검색키워드",
            "차단사유",
            "원본파일"
        };

        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE4D6");
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var rowNum = (int)row["_row_num"]!;
            var excelRow = r + 2;
            reasonByRow.TryGetValue(rowNum, out var skippedItem);

            ws.Cell(excelRow, 1).Value = rowNum;
            ws.Cell(excelRow, 2).Value = GetStr(row, "자체 상품코드").OrIfEmpty(GetStr(row, "상품코드"));
            ws.Cell(excelRow, 3).Value = GetStr(row, "상품명");
            ws.Cell(excelRow, 4).Value = GetStr(row, "홈런_쿠팡상품명").OrIfEmpty(GetStr(row, "쿠팡상품명"));
            ws.Cell(excelRow, 5).Value = skippedItem?.Category ?? GetStr(row, "쿠팡카테고리코드").OrIfEmpty(GetStr(row, "쿠팡카테고리"));
            ws.Cell(excelRow, 6).Value = GetStr(row, "옵션입력");
            ws.Cell(excelRow, 7).Value = GetStr(row, "검색키워드").OrIfEmpty(GetStr(row, "쿠팡검색태그"));
            ws.Cell(excelRow, 8).Value = skippedItem?.Error ?? "카테고리 강력 고정 위반";
            ws.Cell(excelRow, 9).Value = sourcePath;
        }

        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()?.SetAutoFilter();
        ws.Columns().AdjustToContents(8, 80);
        workbook.SaveAs(reportPath);
        return reportPath;
    }

    private static string? ResolveDefaultHomeCafe24TokenPath()
    {
        var path = DesktopKeyStore.GetPath("cafe24_token_rkghrud1.json");
        return System.IO.File.Exists(path) ? path : null;
    }

    private static (List<string> SourceImages, string SourceLabel, bool AlreadyUploadReady) ResolveImageSources(
        Dictionary<string, object?> row,
        string sku,
        string exportRoot)
    {
        var selectedImages = CollectSelectedImageSources(row, exportRoot);
        if (selectedImages.Count > 0)
            return (selectedImages, "selected_images", false);

        if (!string.IsNullOrEmpty(sku) && !string.IsNullOrEmpty(exportRoot))
        {
            var imageFiles = FindListingImages(exportRoot, sku).Take(9).ToList();
            if (imageFiles.Count > 0)
                return (imageFiles, "listing_images", false);
        }

        if (row.TryGetValue("_cafe24_image_urls", out var cafe24Images) && cafe24Images is IEnumerable<string> cafe24List)
        {
            var cafe24SourceImages = cafe24List
                .Where(MarketImageUrlGuard.IsAllowedUploadUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(9)
                .ToList();
            if (cafe24SourceImages.Count > 0)
            {
                row["_cafe24_image_urls"] = cafe24SourceImages;
                return (cafe24SourceImages, "Cafe24", true);
            }
        }

        return (new List<string>(), "", false);
    }

    private static List<string> CollectSelectedImageSources(Dictionary<string, object?> row, string exportRoot)
    {
        var images = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in new[] { "이미지등록(목록)", "이미지등록(추가)" })
        {
            foreach (var raw in Regex.Split(GetStr(row, column), @"[|\n]"))
                AddImage(raw);
        }

        return images.Take(9).ToList();

        void AddImage(string? raw)
        {
            var image = ResolveLocalDataPath((raw ?? "").Trim(), exportRoot);
            if (string.IsNullOrWhiteSpace(image))
                return;
            if (!File.Exists(image) && !MarketImageUrlGuard.IsAllowedUploadUrl(image))
                return;
            if (seen.Add(image))
                images.Add(image);
        }
    }

    private static string ResolveLocalDataPath(string value, string exportRoot)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        if (File.Exists(value) || MarketImageUrlGuard.IsAllowedUploadUrl(value))
            return value;
        if (string.IsNullOrWhiteSpace(exportRoot))
            return value;

        var normalized = value.Replace('\\', '/');
        var dataRoot = Directory.GetParent(exportRoot)?.FullName ?? exportRoot;
        const string dataPrefix = "/data/";
        if (normalized.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(dataRoot, normalized[dataPrefix.Length..].Replace('/', Path.DirectorySeparatorChar));

        return value;
    }

    private static string ExtractGsCode(Dictionary<string, object?> row)
    {
        foreach (var value in new[] { GetStr(row, "자체 상품코드"), GetStr(row, "상품코드"), GetStr(row, "상품명") })
        {
            var match = Regex.Match(value ?? "", @"GS\d{7}[A-Z0-9]*", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Value.Trim().ToUpperInvariant();
        }

        return "";
    }

    /// <summary>listing_images 폴더에서 GS코드 가공이미지 파일 찾기 (이미지 선택 반영)</summary>
    private static List<string> FindListingImages(string exportRoot, string gsCode)
    {
        // GS코드 정규화: GS3500169A → GS3500169
        var gsBase = Regex.Replace(gsCode.Trim(), @"[A-Z]$", "", RegexOptions.IgnoreCase);

        // image_selections.json 로드
        ImageSelection? selection = null;
        var selectionsPath = System.IO.Path.Combine(exportRoot, "image_selections.json");
        if (System.IO.File.Exists(selectionsPath))
        {
            try
            {
                var json = System.IO.File.ReadAllText(selectionsPath);
                using var doc = JsonDocument.Parse(json);
                // GS9 키로 검색 (GS3500169)
                var gs9 = gsBase.Length >= 9 ? gsBase[..9] : gsBase;
                if (doc.RootElement.TryGetProperty(gs9, out var sel))
                {
                    int? mainIdx = sel.TryGetProperty("main", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : null;
                    int? mainIdxB = sel.TryGetProperty("mainB", out var mb) && mb.ValueKind == JsonValueKind.Number ? mb.GetInt32() : null;
                    var addIndices = new List<int>();
                    if (sel.TryGetProperty("additional", out var addArr) && addArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var a in addArr.EnumerateArray())
                            if (a.ValueKind == JsonValueKind.Number) addIndices.Add(a.GetInt32());
                    }
                    selection = new ImageSelection(mainIdx, addIndices, mainIdxB);
                }
            }
            catch { }
        }

        // listing_images 폴더 탐색
        var listingRoot = System.IO.Path.Combine(exportRoot, "listing_images");
        if (!System.IO.Directory.Exists(listingRoot))
            return new List<string>();

        var searchDirs = new List<string> { listingRoot };
        try
        {
            foreach (var sub in System.IO.Directory.GetDirectories(listingRoot).OrderByDescending(path => System.IO.Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                searchDirs.Add(sub);
        }
        catch { }

        foreach (var dir in searchDirs)
        {
            var gsFolder = System.IO.Path.Combine(dir, gsBase);
            if (!System.IO.Directory.Exists(gsFolder))
                gsFolder = System.IO.Path.Combine(dir, gsCode);
            if (!System.IO.Directory.Exists(gsFolder)) continue;

            var allFiles = System.IO.Directory.GetFiles(gsFolder)
                .Where(f => Regex.IsMatch(f, @"\.(jpg|jpeg|png|bmp|webp)$", RegexOptions.IgnoreCase))
                .OrderBy(f => f)
                .ToList();

            if (allFiles.Count == 0) continue;

            // 이미지 선택이 있으면 선택된 순서대로 (대표 → 추가)
            if (selection?.MainIndex is not null)
            {
                var (mainPath, addPaths) = Cafe24UploadSupport.PickImagesBySelection(gsFolder, selection);
                if (mainPath is not null)
                {
                    var result = new List<string> { mainPath };
                    result.AddRange(addPaths);
                    return result;
                }
            }

            // 선택 없으면 전체 파일 순서대로
            return allFiles;
        }

        return new List<string>();
    }
    private static async Task<string> UploadImageWithRetryAsync(
        NaverCommerceApiClient api,
        string imageUrl,
        Action<string> log,
        CancellationToken ct)
    {
        var delayMs = 1200;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await api.UploadImageAsync(imageUrl, ct);
            }
            catch (Exception ex) when (attempt < 4 && IsRateLimitError(ex))
            {
                log($"    이미지 업로드 재시도({attempt}/3): {ShortImageLabel(imageUrl)} | {delayMs}ms 대기");
                await Task.Delay(delayMs, ct);
                delayMs *= 2;
            }
        }
    }

    private static bool IsRateLimitError(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("GW.RATE_LIMIT", StringComparison.OrdinalIgnoreCase)
            || message.Contains("요청이 많아", StringComparison.OrdinalIgnoreCase);
    }

    private static string ShortImageLabel(string imageSource)
    {
        if (string.IsNullOrWhiteSpace(imageSource))
            return "(빈 이미지)";

        if (Uri.TryCreate(imageSource, UriKind.Absolute, out var uri))
        {
            var fileName = System.IO.Path.GetFileName(uri.LocalPath);
            return string.IsNullOrWhiteSpace(fileName) ? uri.Host : fileName;
        }

        return System.IO.Path.GetFileName(imageSource);
    }

    private static async Task<JsonObject?> LoadReferenceDeliveryTemplateAsync(
        CoupangApiClient api,
        Action<string> log,
        CancellationToken ct)
    {
        if (api.ReferenceSellerProductId is not long sellerProductId)
            return null;

        try
        {
            using var doc = await api.GetProductAsync(sellerProductId, ct);
            var template = ExtractDeliveryTemplate(doc.RootElement);
            if (template is not null)
                log($"쿠팡 배송 기준상품 로드: {sellerProductId}");
            else
                log($"쿠팡 배송 기준상품 로드 실패: {sellerProductId} (배송 필드 없음)");
            return template;
        }
        catch (Exception ex)
        {
            log($"쿠팡 배송 기준상품 로드 실패: {sellerProductId} | {ShortError(ex.Message)}");
            return null;
        }
    }

    private static JsonObject? ExtractDeliveryTemplate(JsonElement root)
    {
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            root = data;

        var template = new JsonObject();
        foreach (var key in new[]
        {
            "deliveryMethod",
            "deliveryCompanyCode",
            "deliveryChargeType",
            "deliveryCharge",
            "freeShipOverAmount",
            "deliveryChargeOnReturn",
            "returnCharge",
            "outboundShippingPlaceCode",
            "returnCenterCode",
            "returnChargeName",
            "companyContactNumber",
            "returnZipCode",
            "returnAddress",
            "returnAddressDetail",
            "remoteAreaDeliverable",
            "unionDeliveryType",
            "vendorUserId",
            "afterServiceInformation",
            "afterServiceContactNumber"
        })
        {
            if (root.TryGetProperty(key, out var value))
                template[key] = JsonNode.Parse(value.GetRawText());
        }

        return template.Count > 0 ? template : null;
    }

    private static string ShortError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "알 수 없는 오류";

        return message.Length > 140 ? message[..140] + "..." : message;
    }
}
