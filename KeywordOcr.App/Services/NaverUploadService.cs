using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;

namespace KeywordOcr.App.Services;

public sealed record NaverUploadOptions
{
    public int RowStart { get; set; }
    public int RowEnd { get; set; }
    public bool DryRun { get; set; } = true;
    public IReadOnlySet<string>? AllowedGsCodes { get; set; }
    public string? Cafe24TokenPath { get; set; }
}

public sealed record NaverUploadResultItem(
    int Row,
    string Name,
    string Status,
    string ProductId,
    string Error);

public sealed record NaverUploadResult(
    IReadOnlyList<NaverUploadResultItem> Items,
    int SuccessCount,
    int FailCount,
    int TotalCount,
    string LogDirectory = "");

/// <summary>
/// 네이버 스마트스토어 상품 업로드 서비스 (순수 C# — Python 의존성 없음)
/// </summary>
public sealed class NaverUploadService
{
    public async Task<NaverUploadResult> UploadAsync(
        string sourcePath,
        NaverUploadOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        void Log(string msg) => progress?.Report(msg);

        using var api = NaverCommerceApiClient.FromKeyFile();
        var logDir = CreateLogDirectory(sourcePath);
        Directory.CreateDirectory(Path.Combine(logDir, "requests"));
        Directory.CreateDirectory(Path.Combine(logDir, "responses"));
        Log($"네이버 로그 폴더: {logDir}");

        // 엑셀 읽기
        Log("가공파일 읽는 중...");
        var allRows = ReadSourceFile(sourcePath);
        Log($"{allRows.Count}개 상품 로드 완료");

        // 행 필터
        List<Dictionary<string, object?>> targetRows;
        if (options.RowStart > 0)
        {
            var end = options.RowEnd > 0 ? options.RowEnd : options.RowStart;
            targetRows = allRows.Where(r =>
            {
                var rowNum = (int)r["_row_num"]! - 1;
                return rowNum >= options.RowStart && rowNum <= end;
            }).ToList();
        }
        else
        {
            targetRows = allRows;
        }
        if (options.AllowedGsCodes is { Count: > 0 } allowedGsCodes)
        {
            targetRows = targetRows
                .Where(row =>
                {
                    var gsCode = ExtractGsCode(row);
                    return !string.IsNullOrWhiteSpace(gsCode) && allowedGsCodes.Contains(gsCode);
                })
                .ToList();
        }

        Log($"처리 대상: {targetRows.Count}개");
        var results = new List<NaverUploadResultItem>();
        var referenceDeliveryInfo = options.DryRun ? null : await LoadReferenceDeliveryInfoAsync(api, Log, ct);
        HashSet<string> existingNaverGsCodes = new(StringComparer.OrdinalIgnoreCase);
        if (!options.DryRun && targetRows.Count > 0)
        {
            try
            {
                var existingCodes = await api.GetExistingGsCodesAsync(ct);
                existingNaverGsCodes = existingCodes
                    .Select(item => item.GsCode)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                Log($"네이버 API 중복 인덱스 로드: {existingNaverGsCodes.Count}개");
            }
            catch (Exception ex)
            {
                Log($"네이버 API 중복 확인 실패 (업로드 계속): {ShortError(ex.Message)}");
            }
        }
        var cafe24TokenPath = string.IsNullOrWhiteSpace(options.Cafe24TokenPath)
            ? ResolveDefaultHomeCafe24TokenPath()
            : options.Cafe24TokenPath;
        var cafe24MarketData = options.DryRun
            ? null
            : await Cafe24MarketDataService.TryCreateAsync(sourcePath, Log, ct, cafe24TokenPath);

        for (int idx = 0; idx < targetRows.Count; idx++)
        {
            ct.ThrowIfCancellationRequested();
            var row = targetRows[idx];
            var rowNum = (int)row["_row_num"]!;
            var gsCode = ExtractGsCode(row);
            var productName = GetStr(row, "홈런_네이버상품명")
                .OrIfEmpty(GetStr(row, "네이버상품명"))
                .OrIfEmpty(GetStr(row, "상품명"))
                .OrIfEmpty(GetStr(row, "최종키워드2차"))
                .OrIfEmpty(GetStr(row, "1차키워드"));
            productName = CleanMarketProductName(productName);
            var shortName = productName.Length > 30 ? productName[..30] : productName;

            Log($"[{idx + 1}/{targetRows.Count}] {shortName}...");

            if (!options.DryRun
                && !string.IsNullOrWhiteSpace(gsCode)
                && existingNaverGsCodes.Contains(gsCode))
            {
                results.Add(new NaverUploadResultItem(rowNum, shortName, "SKIP_DUP_API", "", "네이버 API 중복 확인됨"));
                Log($"  스킵: 네이버 API에 이미 등록된 상품코드 {gsCode}");
                continue;
            }

            if (!HasDirectMarketProductName(row, "홈런_네이버상품명", "네이버상품명"))
            {
                results.Add(new NaverUploadResultItem(
                    rowNum,
                    shortName,
                    "NAME_FAIL",
                    "",
                    "네이버 직접등록용 상품명 컬럼이 비어 있습니다. V5 최종 llm_v5_cli 엑셀을 사용해야 합니다."));
                continue;
            }

            // 카테고리 결정
            string categoryId;
            string catName;

            var presetCat = GetStr(row, "네이버카테고리코드").OrIfEmpty(GetStr(row, "네이버카테고리"));
            if (!string.IsNullOrEmpty(presetCat))
            {
                categoryId = ((long)double.Parse(presetCat)).ToString();
                catName = $"엑셀지정({categoryId})";
                Log($"  → 엑셀 카테고리 사용: {categoryId}");
            }
            else
            {
                try
                {
                    using var catDoc = await api.PredictCategoryAsync(productName, ct);
                    var root = catDoc.RootElement;

                    if (root.TryGetProperty("_error", out _))
                    {
                        var msg = root.TryGetProperty("_msg", out var mp) ? mp.ToString() : "카테고리 추천 실패";
                        results.Add(new NaverUploadResultItem(rowNum, shortName, "CATEGORY_FAIL", "", msg.Length > 200 ? msg[..200] : msg));
                        continue;
                    }

                    if (root.TryGetProperty("contents", out var contents) && contents.GetArrayLength() > 0)
                    {
                        var top = contents[0];
                        categoryId = top.GetProperty("categoryId").ToString();
                        var wholeName = top.TryGetProperty("wholeCategoryName", out var wn) ? wn.GetString() ?? "" : "";
                        catName = wholeName;
                    }
                    else
                    {
                        if (!TryResolveFallbackNaverCategory(productName, out categoryId, out catName))
                        {
                            results.Add(new NaverUploadResultItem(rowNum, shortName, "CATEGORY_FAIL", "", "유사 상품 없음"));
                            continue;
                        }
                        Log($"  → fallback 카테고리 사용: {catName} ({categoryId})");
                    }
                }
                catch (Exception ex)
                {
                    if (!TryResolveFallbackNaverCategory(productName, out categoryId, out catName))
                    {
                        results.Add(new NaverUploadResultItem(rowNum, shortName, "CATEGORY_FAIL", "", ex.Message.Length > 200 ? ex.Message[..200] : ex.Message));
                        continue;
                    }
                    Log($"  → fallback 카테고리 사용: {catName} ({categoryId})");
                }
            }

            if (options.DryRun)
            {
                Log($"  → {catName}");
                results.Add(new NaverUploadResultItem(rowNum, shortName, "DRY_RUN_OK", $"{catName} ({categoryId})", ""));
                continue;
            }

            // 실제 등록
            try
            {
                if (cafe24MarketData is not null)
                    await cafe24MarketData.TryApplyAsync(row, ct);

                // 이미지: 화면/엑셀에서 넘어온 대표/추가 선택을 1순위로 쓰고, 없을 때만 Cafe24/가공이미지로 보충한다.
                var cafe24Images = GetCafe24ImageUrls(row);
                var exportRoot = GetStr(row, "_export_root");
                var sku = gsCode.OrIfEmpty(GetStr(row, "자체 상품코드"));
                var listingImages = !string.IsNullOrEmpty(exportRoot) && !string.IsNullOrEmpty(sku)
                    ? FindListingImages(exportRoot, sku)
                    : new List<string>();

                var selectedImages = CollectListingImageUrls(row, exportRoot);
                var fallbackImages = CollectImageUrls(row);
                var imageUrls = MergeImages(selectedImages, cafe24Images, listingImages, fallbackImages);
                var imageSource = selectedImages.Count > 0
                        ? "엑셀 선택 이미지 + fallback"
                    : cafe24Images.Count > 0
                        ? "Cafe24 + fallback"
                    : listingImages.Count > 0
                        ? "listing_images + fallback"
                        : "엑셀 fallback";
                JsonObject? imagesNode = null;
                var finalUploadedImageUrls = new List<string>();

                Log($"  이미지 소스: {imageSource} ({imageUrls.Count}장)");

                if (imageUrls.Count > 0)
                {
                    var uploadedUrls = new List<string>();
                    foreach (var imgUrl in imageUrls.Take(9))
                    {
                        try
                        {
                            var uploaded = await UploadImageWithRetryAsync(api, imgUrl, Log, ct);
                            uploadedUrls.Add(uploaded);
                            await Task.Delay(300, ct);
                        }
                        catch (Exception imageEx)
                        {
                            Log($"    이미지 업로드 실패: {ShortImageLabel(imgUrl)} | {ShortError(imageEx.Message)}");
                        }
                    }

                    if (uploadedUrls.Count > 0)
                    {
                        imagesNode = new JsonObject
                        {
                            ["representativeImage"] = new JsonObject { ["url"] = uploadedUrls[0] },
                        };
                        finalUploadedImageUrls = uploadedUrls.ToList();
                        if (uploadedUrls.Count > 1)
                        {
                            var optImages = new JsonArray();
                            foreach (var u in uploadedUrls.Skip(1))
                                optImages.Add(new JsonObject { ["url"] = u });
                            imagesNode["optionalImages"] = optImages;
                        }

                        Log($"  대표+추가 이미지 반영: {uploadedUrls.Count}장");
                    }
                }

                var optionCount = ParseOptions(GetStr(row, "옵션입력"), GetStr(row, "옵션추가금")).Count;
                if (optionCount == 1)
                    Log("  단일 옵션 1개만 감지됨. 네이버 상세에서는 선택형 옵션 UI가 보이지 않을 수 있습니다.");
                else if (optionCount > 1)
                    Log($"  옵션 조합 {optionCount}개 감지");

                var productJson = BuildNaverProduct(row, categoryId, imagesNode, referenceDeliveryInfo);
                var logName = SafeFileName(!string.IsNullOrWhiteSpace(gsCode) ? gsCode : $"row{rowNum}");
                await File.WriteAllTextAsync(
                    Path.Combine(logDir, "requests", $"{logName}.json"),
                    PrettyJson(productJson),
                    Encoding.UTF8,
                    ct);
                var productElement = JsonSerializer.Deserialize<JsonElement>(productJson.ToJsonString());
                using var resp = await api.CreateProductAsync(productElement, ct);
                var respRoot = resp.RootElement;
                await File.WriteAllTextAsync(
                    Path.Combine(logDir, "responses", $"{logName}.json"),
                    PrettyJson(respRoot),
                    Encoding.UTF8,
                    ct);

                // 등록불가 태그 처리 후 재시도
                if (respRoot.TryGetProperty("_error", out _))
                {
                    var errMsg = respRoot.TryGetProperty("_msg", out var mp) ? mp.ToString() : "";
                    var restrictedTags = ExtractRestrictedTags(errMsg);
                    if (restrictedTags.Count > 0)
                    {
                        // 태그 제거 후 재시도
                        RemoveRestrictedTags(productJson, restrictedTags);
                        await File.WriteAllTextAsync(
                            Path.Combine(logDir, "requests", $"{logName}_retry.json"),
                            PrettyJson(productJson),
                            Encoding.UTF8,
                            ct);
                        var retryElement = JsonSerializer.Deserialize<JsonElement>(productJson.ToJsonString());
                        using var resp2 = await api.CreateProductAsync(retryElement, ct);
                        var resp2Root = resp2.RootElement;
                        await File.WriteAllTextAsync(
                            Path.Combine(logDir, "responses", $"{logName}_retry.json"),
                            PrettyJson(resp2Root),
                            Encoding.UTF8,
                            ct);
                        var retryResult = ParseCreateProductResult(resp2Root);
                        if (!retryResult.Ok)
                        {
                            results.Add(new NaverUploadResultItem(rowNum, shortName, "FAIL", "", retryResult.Error));
                            MarketUploadStateStore.Upsert(gsCode, shortName, "네이버", "FAIL", "", finalUploadedImageUrls, retryResult.Error);
                        }
                        else
                        {
                            results.Add(new NaverUploadResultItem(rowNum, shortName, "OK", retryResult.ProductId, ""));
                            MarketUploadStateStore.Upsert(gsCode, shortName, "네이버", "OK", retryResult.ProductId, finalUploadedImageUrls);
                        }
                        continue;
                    }

                    if (errMsg.Length > 200) errMsg = errMsg[..200];
                    results.Add(new NaverUploadResultItem(rowNum, shortName, "FAIL", "", errMsg));
                    MarketUploadStateStore.Upsert(gsCode, shortName, "네이버", "FAIL", "", finalUploadedImageUrls, errMsg);
                }
                else
                {
                    var createResult = ParseCreateProductResult(respRoot);
                    if (createResult.Ok)
                    {
                        results.Add(new NaverUploadResultItem(rowNum, shortName, "OK", createResult.ProductId, ""));
                        MarketUploadStateStore.Upsert(gsCode, shortName, "네이버", "OK", createResult.ProductId, finalUploadedImageUrls);
                    }
                    else
                    {
                        results.Add(new NaverUploadResultItem(rowNum, shortName, "FAIL", "", createResult.Error));
                        MarketUploadStateStore.Upsert(gsCode, shortName, "네이버", "FAIL", "", finalUploadedImageUrls, createResult.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                var msg = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
                results.Add(new NaverUploadResultItem(rowNum, shortName, "FAIL", "", msg));
            }

            // 속도 제한
            if ((idx + 1) % 5 == 0)
                await Task.Delay(1000, ct);
        }

        var successCount = results.Count(r => r.Status is "OK" or "DRY_RUN_OK");
        var failCount = results.Count - successCount;
        await WriteSummaryAsync(logDir, results, ct);
        return new NaverUploadResult(results, successCount, failCount, results.Count, logDir);
    }

    // ── 상품 JSON 빌드 ─────────────────────────────

    private static JsonObject BuildNaverProduct(
        Dictionary<string, object?> row, string categoryId, JsonObject? images, JsonObject? referenceDeliveryInfo)
    {
        var productName = GetStr(row, "홈런_네이버상품명")
            .OrIfEmpty(GetStr(row, "네이버상품명"))
            .OrIfEmpty(GetStr(row, "상품명"))
            .OrIfEmpty(GetStr(row, "최종키워드2차"))
            .OrIfEmpty(GetStr(row, "1차키워드"));
        productName = CleanMarketProductName(productName);
        if (productName.Length > 100) productName = productName[..100];

        var salePrice = ResolveSalePrice(row);
        var detailHtml = MarketImageUrlGuard.RemoveUnsafeImageTags(
            GetStr(row, "상품 상세설명").OrIfEmpty(GetStr(row, "상세설명")));
        var sellerCode = ExtractGsCode(row)
            .OrIfEmpty(GetStr(row, "판매자내부상품번호"))
            .OrIfEmpty(GetStr(row, "자체 상품코드"));

        // 검색 태그
        var rawTags = GetStr(row, "홈런_네이버태그")
            .OrIfEmpty(GetStr(row, "네이버태그"))
            .OrIfEmpty(GetStr(row, "홈런_공통마켓검색키워드"))
            .OrIfEmpty(GetStr(row, "검색키워드"))
            .OrIfEmpty(GetStr(row, "검색어설정"))
            .OrIfEmpty(productName);
        string[] tagParts;
        if (rawTags.Contains('|') || rawTags.Contains(',') || rawTags.Contains('\n'))
            tagParts = Regex.Split(rawTags, @"[|,\n]+");
        else
            tagParts = rawTags.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var tagList = new JsonArray();
        var seenTags = new HashSet<string>();
        foreach (var raw in tagParts)
        {
            var tag = SanitizeTag(raw);
            if (!string.IsNullOrEmpty(tag) && seenTags.Add(tag))
            {
                tagList.Add(new JsonObject { ["text"] = tag });
                if (tagList.Count >= 10) break;
            }
        }

        // 옵션
        var options = NormalizeOptionPrices(ParseOptions(GetStr(row, "옵션입력"), GetStr(row, "옵션추가금")), ref salePrice);

        var originProduct = new JsonObject
        {
            ["statusType"] = "SALE",
            ["saleType"] = "NEW",
            ["leafCategoryId"] = categoryId,
            ["name"] = productName,
            ["detailContent"] = detailHtml,
            ["salePrice"] = salePrice,
            ["stockQuantity"] = 999,
            ["deliveryInfo"] = CloneJsonObject(referenceDeliveryInfo) ?? BuildDefaultDeliveryInfo(),
            ["detailAttribute"] = new JsonObject
            {
                ["sellerCodeInfo"] = new JsonObject
                {
                    ["sellerManagementCode"] = sellerCode,
                },
                ["naverShoppingSearchInfo"] = new JsonObject
                {
                    ["manufacturerName"] = "상세페이지 참조",
                    ["brandName"] = "",
                },
                ["afterServiceInfo"] = new JsonObject
                {
                    ["afterServiceTelephoneNumber"] = "010-2324-8352",
                    ["afterServiceGuideContent"] = "전화 문의",
                },
                ["originAreaInfo"] = new JsonObject
                {
                    ["originAreaCode"] = "0200037",
                    ["importer"] = "상세페이지 참조",
                    ["content"] = "상세설명 참조",
                    ["plural"] = false,
                },
                ["productInfoProvidedNotice"] = BuildProductInfoProvidedNotice(row, productName, sellerCode),
                ["unitCapacity"] = new JsonObject
                {
                    ["unitPriceYn"] = false,
                },
                ["certificationTargetExcludeContent"] = new JsonObject
                {
                    ["childCertifiedProductExclusionYn"] = true,
                    ["kcCertifiedProductExclusionYn"] = "TRUE",
                    ["greenCertifiedProductExclusionYn"] = true,
                },
                ["minorPurchasable"] = true,
                ["seoInfo"] = new JsonObject
                {
                    ["sellerTags"] = tagList,
                },
            },
        };

        if (images is not null)
            originProduct["images"] = images;

        if (originProduct["deliveryInfo"] is JsonObject deliveryInfo)
            deliveryInfo["deliveryCompany"] = "CJGLS";

        // 옵션 설정
        if (options.Count > 0 && originProduct["detailAttribute"] is JsonObject detailAttribute)
        {
            var optionCombinations = new JsonArray();
            foreach (var opt in options)
            {
                optionCombinations.Add(new JsonObject
                {
                    ["optionName1"] = opt.Name,
                    ["stockQuantity"] = 999,
                    ["price"] = opt.Price,
                    ["usable"] = true,
                });
            }
            detailAttribute["optionInfo"] = new JsonObject
            {
                ["optionCombinationSortType"] = "CREATE",
                ["optionCombinationGroupNames"] = new JsonObject
                {
                    ["optionGroupName1"] = "옵션",
                },
                ["optionCombinations"] = optionCombinations,
                ["useStockManagement"] = true,
            };
        }

        return new JsonObject
        {
            ["originProduct"] = originProduct,
            ["smartstoreChannelProduct"] = new JsonObject
            {
                ["channelProductDisplayStatusType"] = "ON",
                ["storeKeepExclusiveProduct"] = false,
                ["naverShoppingRegistration"] = true,
            },
        };
    }

    private static JsonObject BuildProductInfoProvidedNotice(
        IReadOnlyDictionary<string, object?> row,
        string productName,
        string sellerCode)
    {
        var raw = GetStr(row, "네이버상품정보고시")
            .OrIfEmpty(GetStr(row, "상품정보제공고시"))
            .OrIfEmpty(GetStr(row, "naverProvidedNotice"));

        if (TryParseProvidedNotice(raw, productName, out var providedNotice))
            return providedNotice;

        return BuildDefaultProvidedNotice(productName, sellerCode);
    }

    private static bool TryParseProvidedNotice(string raw, string productName, out JsonObject providedNotice)
    {
        providedNotice = new JsonObject();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        try
        {
            var node = JsonNode.Parse(raw);
            var obj = node as JsonObject;
            if (obj is null)
                return false;

            if (obj["productInfoProvidedNotice"] is JsonObject nested)
                obj = nested;

            var type = obj["productInfoProvidedNoticeType"]?.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(type))
                return false;

            var objectKey = ProvidedNoticeObjectKey(type);
            if (string.IsNullOrWhiteSpace(objectKey) || obj[objectKey] is not JsonObject)
                return false;

            if (string.Equals(type, "WEAR", StringComparison.OrdinalIgnoreCase)
                && ShouldUseSportsEquipmentNotice(productName))
            {
                providedNotice = BuildDefaultProvidedNotice(productName, "");
                return true;
            }

            providedNotice = NormalizeProvidedNotice(obj, objectKey);
            return true;
        }
        catch
        {
            providedNotice = new JsonObject();
            return false;
        }
    }

    private static JsonObject BuildDefaultProvidedNotice(string productName, string sellerCode)
    {
        var itemName = string.IsNullOrWhiteSpace(productName) ? "상품상세 참조" : productName;
        var modelName = string.IsNullOrWhiteSpace(sellerCode) ? itemName : sellerCode;
        var noticeType = InferDefaultProvidedNoticeType(productName);
        if (noticeType is "SHOES")
        {
            return new JsonObject
            {
                ["productInfoProvidedNoticeType"] = "SHOES",
                ["shoes"] = new JsonObject
                {
                    ["material"] = "상품상세 참조",
                    ["color"] = "상품상세 참조",
                    ["size"] = "상품상세 참조",
                    ["height"] = "해당사항 없음",
                    ["manufacturer"] = "상품상세 참조",
                    ["caution"] = "상품상세 참조",
                    ["warrantyPolicy"] = "관련 법 및 소비자분쟁해결기준에 따름",
                    ["afterServiceDirector"] = "010-2324-8352",
                },
            };
        }
        if (noticeType is "WEAR")
        {
            return new JsonObject
            {
                ["productInfoProvidedNoticeType"] = "WEAR",
                ["wear"] = new JsonObject
                {
                    ["material"] = "상품상세 참조",
                    ["color"] = "상품상세 참조",
                    ["size"] = "상품상세 참조",
                    ["manufacturer"] = "상품상세 참조",
                    ["caution"] = "상품상세 참조",
                    ["warrantyPolicy"] = "관련 법 및 소비자분쟁해결기준에 따름",
                    ["afterServiceDirector"] = "010-2324-8352",
                },
            };
        }
        if (noticeType is "BAG")
        {
            return new JsonObject
            {
                ["productInfoProvidedNoticeType"] = "BAG",
                ["bag"] = new JsonObject
                {
                    ["type"] = itemName,
                    ["material"] = "상품상세 참조",
                    ["color"] = "상품상세 참조",
                    ["size"] = "상품상세 참조",
                    ["manufacturer"] = "상품상세 참조",
                    ["caution"] = "상품상세 참조",
                    ["warrantyPolicy"] = "관련 법 및 소비자분쟁해결기준에 따름",
                    ["afterServiceDirector"] = "010-2324-8352",
                },
            };
        }
        if (noticeType is "SPORTS_EQUIPMENT")
        {
            return new JsonObject
            {
                ["productInfoProvidedNoticeType"] = "SPORTS_EQUIPMENT",
                ["sportsEquipment"] = new JsonObject
                {
                    ["itemName"] = itemName,
                    ["modelName"] = modelName,
                    ["certificationType"] = "해당 없음",
                    ["size"] = "상품상세 참조",
                    ["weight"] = "상품상세 참조",
                    ["color"] = "상품상세 참조",
                    ["material"] = "상품상세 참조",
                    ["components"] = "본품",
                    ["releaseDateText"] = "상품상세 참조",
                    ["manufacturer"] = "상품상세 참조",
                    ["detailContent"] = "상품상세 참조",
                    ["warrantyPolicy"] = "관련 법 및 소비자분쟁해결기준에 따름",
                    ["afterServiceDirector"] = "010-2324-8352",
                },
            };
        }
        if (noticeType is "FASHION_ITEMS")
        {
            return new JsonObject
            {
                ["productInfoProvidedNoticeType"] = "FASHION_ITEMS",
                ["fashionItems"] = new JsonObject
                {
                    ["type"] = itemName,
                    ["material"] = "상품상세 참조",
                    ["size"] = "상품상세 참조",
                    ["manufacturer"] = "상품상세 참조",
                    ["caution"] = "상품상세 참조",
                    ["warrantyPolicy"] = "관련 법 및 소비자분쟁해결기준에 따름",
                    ["afterServiceDirector"] = "010-2324-8352",
                },
            };
        }
        if (noticeType is "KITCHEN_UTENSILS")
        {
            return new JsonObject
            {
                ["productInfoProvidedNoticeType"] = "KITCHEN_UTENSILS",
                ["kitchenUtensils"] = new JsonObject
                {
                    ["itemName"] = itemName,
                    ["modelName"] = modelName,
                    ["material"] = "상품상세 참조",
                    ["component"] = "본품",
                    ["size"] = "상품상세 참조",
                    ["releaseDateText"] = "상품상세 참조",
                    ["manufacturer"] = "상품상세 참조",
                    ["producer"] = "상품상세 참조",
                    ["importDeclaration"] = "해당 없음",
                    ["warrantyPolicy"] = "관련 법 및 소비자분쟁해결기준에 따름",
                    ["afterServiceDirector"] = "010-2324-8352",
                },
            };
        }
        if (noticeType is "CAR_ARTICLES")
        {
            return new JsonObject
            {
                ["productInfoProvidedNoticeType"] = "CAR_ARTICLES",
                ["carArticles"] = new JsonObject
                {
                    ["itemName"] = itemName,
                    ["modelName"] = modelName,
                    ["releaseDateText"] = "상품상세 참조",
                    ["certificationType"] = "해당 없음",
                    ["caution"] = "상품상세 참조",
                    ["manufacturer"] = "상품상세 참조",
                    ["size"] = "상품상세 참조",
                    ["applyModel"] = "상품상세 참조",
                    ["warrantyPolicy"] = "관련 법 및 소비자분쟁해결기준에 따름",
                    ["roadWorthyCertification"] = "해당 없음",
                    ["afterServiceDirector"] = "010-2324-8352",
                },
            };
        }
        if (noticeType is "FURNITURE")
        {
            return new JsonObject
            {
                ["productInfoProvidedNoticeType"] = "FURNITURE",
                ["furniture"] = new JsonObject
                {
                    ["itemName"] = itemName,
                    ["certificationType"] = "해당 없음",
                    ["color"] = "상품상세 참조",
                    ["components"] = "본품",
                    ["material"] = "상품상세 참조",
                    ["manufacturer"] = "상품상세 참조",
                    ["importer"] = "상품상세 참조",
                    ["producer"] = "상품상세 참조",
                    ["size"] = "상품상세 참조",
                    ["installedCharge"] = "상품상세 참조",
                    ["warrantyPolicy"] = "관련 법 및 소비자분쟁해결기준에 따름",
                    ["refurb"] = "해당 없음",
                    ["afterServiceDirector"] = "010-2324-8352",
                },
            };
        }

        return new JsonObject
        {
            ["productInfoProvidedNoticeType"] = "ETC",
            ["etc"] = new JsonObject
            {
                ["itemName"] = itemName,
                ["modelName"] = modelName,
                ["certificateDetails"] = "해당 없음",
                ["manufacturer"] = "상품상세 참조",
                ["customerServicePhoneNumber"] = "010-2324-8352",
            },
        };
    }

    private static JsonObject NormalizeProvidedNotice(JsonObject obj, string objectKey)
    {
        var type = obj["productInfoProvidedNoticeType"]?.GetValue<string>()?.Trim() ?? "";
        if (!string.Equals(type, "ETC", StringComparison.OrdinalIgnoreCase)
            || obj[objectKey] is not JsonObject etc)
        {
            var normalized = JsonNode.Parse(obj.ToJsonString())!.AsObject();
            return normalized;
        }

        string Field(string name, string fallback = "상품상세 참조")
            => etc[name]?.GetValue<string>()?.Trim().OrIfEmpty(fallback) ?? fallback;

        return new JsonObject
        {
            ["productInfoProvidedNoticeType"] = "ETC",
            ["etc"] = new JsonObject
            {
                ["itemName"] = Field("itemName"),
                ["modelName"] = Field("modelName"),
                ["certificateDetails"] = Field("certificateDetails", "해당 없음"),
                ["manufacturer"] = Field("manufacturer"),
                ["customerServicePhoneNumber"] = Field("customerServicePhoneNumber", "010-2324-8352"),
            },
        };
    }

    private static string InferDefaultProvidedNoticeType(string productName)
    {
        var text = productName ?? string.Empty;
        if (Regex.IsMatch(text, "깔창|인솔|신발|운동화|구두|슬리퍼|부츠"))
            return "SHOES";
        if (Regex.IsMatch(text, "가방|백팩|파우치|숄더백|토트백"))
            return "BAG";
        if (Regex.IsMatch(text, "모자|벨트|액세서리|악세사리|키링|브로치|헤어|머리|집게|핀|고리"))
            return "FASHION_ITEMS";
        if (ShouldUseSportsEquipmentNotice(text))
            return "SPORTS_EQUIPMENT";
        if (Regex.IsMatch(text, "의류|티셔츠|셔츠|바지|자켓|재킷|점퍼|원피스|스커트"))
            return "WEAR";
        if (Regex.IsMatch(text, "주방|키친|냄비|프라이팬|후라이팬|식기|컵|접시|조리|칼|도마|수저|주걱"))
            return "KITCHEN_UTENSILS";
        if (Regex.IsMatch(text, "자동차|차량|차종|세차|와이퍼|타이어|핸들|대시보드|카매트|오토바이"))
            return "CAR_ARTICLES";
        if (Regex.IsMatch(text, "가구|의자|책상|테이블|선반|수납장|침대|소파|브라켓|행거"))
            return "FURNITURE";
        return "ETC";
    }

    private static bool ShouldUseSportsEquipmentNotice(string text)
        => Regex.IsMatch(text ?? string.Empty, "스포츠|운동|헬스|요가|필라테스|테이핑|보호대|밴드|스트랩|고정밴드|발목밴드|공|라켓|골프|등산|자전거");

    private static string ProvidedNoticeObjectKey(string type)
    {
        return type.Trim().ToUpperInvariant() switch
        {
            "WEAR" => "wear",
            "SHOES" => "shoes",
            "BAG" => "bag",
            "FASHION_ITEMS" => "fashionItems",
            "SLEEPING_GEAR" => "sleepingGear",
            "FURNITURE" => "furniture",
            "IMAGE_APPLIANCES" => "imageAppliances",
            "HOME_APPLIANCES" => "homeAppliances",
            "SEASON_APPLIANCES" => "seasonAppliances",
            "OFFICE_APPLIANCES" => "officeAppliances",
            "OPTICS_APPLIANCES" => "opticsAppliances",
            "MICROELECTRONICS" => "microElectronics",
            "CELLPHONE" => "cellPhone",
            "NAVIGATION" => "navigation",
            "CAR_ARTICLES" => "carArticles",
            "MEDICAL_APPLIANCES" => "medicalAppliances",
            "KITCHEN_UTENSILS" => "kitchenUtensils",
            "COSMETIC" => "cosmetic",
            "JEWELLERY" => "jewellery",
            "FOOD" => "food",
            "GENERAL_FOOD" => "generalFood",
            "DIET_FOOD" => "dietFood",
            "KIDS" => "kids",
            "MUSICAL_INSTRUMENT" => "musicalInstrument",
            "SPORTS_EQUIPMENT" => "sportsEquipment",
            "BOOKS" => "books",
            "RENTAL_ETC" => "rentalEtc",
            "RENTAL_HA" => "rentalHa",
            "DIGITAL_CONTENTS" => "digitalContents",
            "GIFT_CARD" => "giftCard",
            "MOBILE_COUPON" => "mobileCoupon",
            "MOVIE_SHOW" => "movieShow",
            "ETC_SERVICE" => "etcService",
            "BIOCHEMISTRY" => "biochemistry",
            "BIOCIDAL" => "biocidal",
            "ETC" => "etc",
            _ => "",
        };
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

    // ── 헬퍼 ───────────────────────────────────────

    private record OptionItem(string Name, int Price);

    private static List<OptionItem> ParseOptions(string? optionStr, string? extraPriceStr)
    {
        if (string.IsNullOrEmpty(optionStr)) return new();

        var matches = Regex.Matches(optionStr, @"([A-Z])\s+([^,}|]+)", RegexOptions.IgnoreCase);
        var prices = new List<int>();
        if (!string.IsNullOrEmpty(extraPriceStr))
        {
            foreach (var p in Regex.Split(extraPriceStr, @"[,|]"))
            {
                var trimmed = p.Trim();
                if (!string.IsNullOrEmpty(trimmed) && double.TryParse(trimmed, out var v))
                    prices.Add((int)v);
            }
        }

        var options = new List<OptionItem>();
        for (int i = 0; i < matches.Count; i++)
        {
            var name = EnsureOptionPrefix(
                $"{matches[i].Groups[1].Value.ToUpperInvariant()} {matches[i].Groups[2].Value.Trim()}",
                i);
            var price = i < prices.Count ? prices[i] : 0;
            options.Add(new OptionItem(name, price));
        }

        if (options.Count == 0)
        {
            var body = optionStr ?? "";
            var brace = Regex.Match(body, @"\{(.+?)\}");
            if (brace.Success) body = brace.Groups[1].Value;
            var values = body.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 0; i < values.Length; i++)
            {
                var price = i < prices.Count ? prices[i] : 0;
                options.Add(new OptionItem(EnsureOptionPrefix(values[i], i), price));
            }
        }
        return options;
    }

    private static List<string> GetCafe24ImageUrls(Dictionary<string, object?> row)
    {
        if (!row.TryGetValue("_cafe24_image_urls", out var value) || value is not IEnumerable<string> urls)
            return new List<string>();

        return urls
            .Select(u => u?.Trim() ?? string.Empty)
            .Where(MarketImageUrlGuard.IsAllowedUploadUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(9)
            .ToList();
    }

    private static List<string> CollectImageUrls(Dictionary<string, object?> row)
    {
        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in new[] { "이미지등록(목록)", "이미지등록(추가)", "이미지등록(상세)" })
        {
            foreach (var raw in Regex.Split(GetStr(row, column), @"[|\n]"))
                AddUrl(raw);
        }

        foreach (Match match in Regex.Matches(
                     GetStr(row, "상품 상세설명").OrIfEmpty(GetStr(row, "상세설명")),
                     "<img[^>]+src=[\"']([^\"']+)[\"']",
                     RegexOptions.IgnoreCase))
        {
            AddUrl(match.Groups[1].Value);
        }

        return urls.Take(9).ToList();

        void AddUrl(string? raw)
        {
            var url = (raw ?? "").Trim();
            if (!MarketImageUrlGuard.IsAllowedUploadUrl(url) && !File.Exists(url))
                return;
            if (seen.Add(url))
                urls.Add(url);
        }
    }

    private static List<string> CollectListingImageUrls(Dictionary<string, object?> row, string exportRoot)
    {
        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in new[] { "이미지등록(목록)", "이미지등록(추가)" })
        {
            foreach (var raw in Regex.Split(GetStr(row, column), @"[|\n]"))
                AddUrl(raw);
        }

        return urls.Take(9).ToList();

        void AddUrl(string? raw)
        {
            var url = ResolveLocalDataPath((raw ?? "").Trim(), exportRoot);
            if (!MarketImageUrlGuard.IsAllowedUploadUrl(url) && !File.Exists(url))
                return;
            if (seen.Add(url))
                urls.Add(url);
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

    private static List<string> MergeImages(params IEnumerable<string>[] imageGroups)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in imageGroups)
        {
            foreach (var image in group)
            {
                var clean = (image ?? "").Trim();
                if (!MarketImageUrlGuard.IsAllowedUploadUrl(clean) && !File.Exists(clean))
                    continue;
                if (seen.Add(clean))
                    result.Add(clean);
                if (result.Count >= 9)
                    return result;
            }
        }
        return result;
    }

    private static string EnsureOptionPrefix(string value, int index)
    {
        var trimmed = Regex.Replace(value ?? "", @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            trimmed = $"옵션{index + 1}";
        if (Regex.IsMatch(trimmed, @"^[A-Z]\s+", RegexOptions.IgnoreCase))
            return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];

        var label = index < 26 ? ((char)('A' + index)).ToString() : $"OPT{index + 1}";
        return $"{label} {trimmed}";
    }

    private static string ShortImageLabel(string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return "(empty)";

        if (System.IO.File.Exists(imageUrl))
            return System.IO.Path.GetFileName(imageUrl);

        return imageUrl.Length > 80 ? imageUrl[..80] + "..." : imageUrl;
    }

    private static string ShortError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "알 수 없는 오류";

        return message.Length > 140 ? message[..140] + "..." : message;
    }

    private static async Task<JsonObject?> LoadReferenceDeliveryInfoAsync(
        NaverCommerceApiClient api,
        Action<string> log,
        CancellationToken ct)
    {
        if (api.ReferenceChannelProductNo is long channelProductNo)
        {
            try
            {
                using var doc = await api.GetChannelProductAsync(channelProductNo, ct);
                var deliveryInfo = ExtractReferenceDeliveryInfo(doc.RootElement);
                if (deliveryInfo is not null)
                    log($"네이버 배송 기준 채널상품 로드: {channelProductNo}");
                else
                    log($"네이버 배송 기준 채널상품 로드 실패: {channelProductNo} (deliveryInfo 없음)");
                return deliveryInfo;
            }
            catch (Exception ex)
            {
                log($"네이버 배송 기준 채널상품 로드 실패: {channelProductNo} | {ShortError(ex.Message)}");
                return null;
            }
        }

        if (api.ReferenceOriginProductNo is not long originProductNo)
            return null;

        try
        {
            using var doc = await api.GetOriginProductAsync(originProductNo, ct);
            var deliveryInfo = ExtractReferenceDeliveryInfo(doc.RootElement);
            if (deliveryInfo is not null)
                log($"네이버 배송 기준 원상품 로드: {originProductNo}");
            else
                log($"네이버 배송 기준 원상품 로드 실패: {originProductNo} (deliveryInfo 없음)");
            return deliveryInfo;
        }
        catch (Exception ex)
        {
            log($"네이버 배송 기준 원상품 로드 실패: {originProductNo} | {ShortError(ex.Message)}");
            return null;
        }
    }

    private static JsonObject? ExtractReferenceDeliveryInfo(JsonElement root)
    {
        if (TryParseDeliveryInfo(root, out var direct))
            return direct;

        if (root.TryGetProperty("originProduct", out var originProduct) && TryParseDeliveryInfo(originProduct, out var nested))
            return nested;

        if (root.TryGetProperty("data", out var data) && TryParseDeliveryInfo(data, out var dataNode))
            return dataNode;

        if (root.TryGetProperty("data", out data)
            && data.TryGetProperty("originProduct", out var dataOrigin)
            && TryParseDeliveryInfo(dataOrigin, out var dataNested))
            return dataNested;

        return null;
    }

    private static bool TryParseDeliveryInfo(JsonElement element, out JsonObject? deliveryInfo)
    {
        if (element.TryGetProperty("deliveryInfo", out var deliveryElement)
            && deliveryElement.ValueKind == JsonValueKind.Object)
        {
            deliveryInfo = JsonNode.Parse(deliveryElement.GetRawText()) as JsonObject;
            return deliveryInfo is not null;
        }

        deliveryInfo = null;
        return false;
    }

    private static JsonObject BuildDefaultDeliveryInfo()
    {
        return new JsonObject
        {
            ["deliveryType"] = "DELIVERY",
            ["deliveryAttributeType"] = "NORMAL",
            ["deliveryCompany"] = "CJGLS",
            ["deliveryFee"] = new JsonObject
            {
                ["deliveryFeeType"] = "FREE",
                ["baseFee"] = 0,
            },
            ["claimDeliveryInfo"] = new JsonObject
            {
                ["returnDeliveryFee"] = 3000,
                ["exchangeDeliveryFee"] = 3000,
            },
        };
    }

    private static JsonObject? CloneJsonObject(JsonObject? source)
    {
        return source is null ? null : JsonNode.Parse(source.ToJsonString()) as JsonObject;
    }

    private static int ResolveSalePrice(IReadOnlyDictionary<string, object?> row)
    {
        var salePrice = GetInt(row, "판매가");
        if (salePrice > 0) return CeilPriceToTen(salePrice);
        salePrice = GetInt(row, "상품가");
        if (salePrice > 0) return CeilPriceToTen(salePrice);
        return 100;
    }

    private static int CeilPriceToTen(int value)
        => value <= 0 ? 0 : (int)(Math.Ceiling(value / 10m) * 10m);

    private static List<OptionItem> NormalizeOptionPrices(List<OptionItem> options, ref int salePrice)
    {
        if (options.Count == 0)
            return options;

        var minPrice = options.Min(option => option.Price);
        if (minPrice > 0)
        {
            salePrice += minPrice;
            return options.Select(option => new OptionItem(option.Name, option.Price - minPrice)).ToList();
        }

        return options.Select(option => new OptionItem(option.Name, option.Price)).ToList();
    }

    private static bool TryResolveFallbackNaverCategory(string productName, out string categoryId, out string categoryName)
    {
        var compact = Regex.Replace(productName ?? "", @"\s+", "");

        (string Pattern, string Code, string Name)[] rules =
        {
            ("수도|수전|가스켓|패킹|와셔", "50020199", "수도용품"),
            ("깔때기", "50004788", "깔때기"),
            ("연관솔|브러쉬|브러시|청소솔", "50001848", "솔"),
            ("컵홀더|차량용", "50003921", "차량용공구"),
            ("USB|보호캡|컬러캡|더스트커버", "50002922", "기타USB액세서리"),
            ("노브|손잡이|핸들", "50001062", "손잡이"),
            ("큐방|빨판|흡착", "50003314", "기타가구부속품"),
            ("손목밴드|식별밴드|팔찌", "50001780", "생활선물세트"),
            ("배관|파이프|새들|클램프|홀캡|홀커버|구멍마개", "50003288", "배관용품"),
            ("캐리어|여행가방", "50005464", "캐리어소품"),
            ("마이크|스폰지|윈드스크린", "50002328", "마이크주변기기"),
            ("액자|프레임|상장", "50003346", "탁상용액자"),
            ("후드끈|스트링|철팁|끈", "50003311", "로프/철망"),
            ("비오|압정|장식핀|고정핀|나사|볼트|못", "50003466", "나사"),
            ("부싱|전선|배선|케이블", "50016380", "전선정리용품"),
            ("휴지|걸이봉|화장지", "50002490", "화장지케이스"),
            ("가구|벽고정|지지대|브라켓", "50003314", "기타가구부속품"),
        };

        foreach (var (pattern, code, name) in rules)
        {
            if (Regex.IsMatch(compact, pattern, RegexOptions.IgnoreCase))
            {
                categoryId = code;
                categoryName = name;
                return true;
            }
        }

        categoryId = "";
        categoryName = "";
        return false;
    }

    private static string SanitizeTag(string tag)
    {
        var cleaned = Regex.Replace(tag, @"[^0-9A-Za-z가-힣\s]", "");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned;
    }

    private static List<string> ExtractRestrictedTags(string errorText)
    {
        var restricted = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(errorText);
            if (doc.RootElement.TryGetProperty("invalidInputs", out var inputs))
            {
                foreach (var item in inputs.EnumerateArray())
                {
                    var msg = item.TryGetProperty("message", out var mp) ? mp.GetString() ?? "" : "";
                    var matches = Regex.Matches(msg, @"등록불가인 단어\(([^)]+)\)");
                    foreach (Match m in matches)
                        restricted.Add(m.Groups[1].Value.Trim());
                }
            }
        }
        catch { }

        foreach (Match match in Regex.Matches(errorText ?? "", @"등록불가인\s*단어\(([^)]+)\)"))
        {
            var value = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(value) && !restricted.Contains(value))
                restricted.Add(value);
        }
        return restricted;
    }

    private static void RemoveRestrictedTags(JsonObject productJson, List<string> restricted)
    {
        var origin = productJson["originProduct"]?.AsObject();
        var detail = origin?["detailAttribute"]?.AsObject();
        var seo = detail?["seoInfo"]?.AsObject();
        var tags = seo?["sellerTags"]?.AsArray();
        if (tags is null) return;

        var toRemove = new List<JsonNode>();
        foreach (var tag in tags)
        {
            var text = tag?["text"]?.GetValue<string>() ?? "";
            if (restricted.Any(word =>
                    text.Contains(word, StringComparison.OrdinalIgnoreCase)
                    || word.Contains(text, StringComparison.OrdinalIgnoreCase)))
            {
                toRemove.Add(tag!);
            }
        }
        foreach (var node in toRemove) tags.Remove(node);
    }

    private static (bool Ok, string ProductId, string Error) ParseCreateProductResult(JsonElement root)
    {
        if (root.TryGetProperty("_error", out _))
        {
            var msg = root.TryGetProperty("_msg", out var mp) ? mp.ToString() : "등록 실패";
            return (false, "", ShortError(msg));
        }

        var productId = ExtractProductId(root);
        if (!string.IsNullOrWhiteSpace(productId))
            return (true, productId, "");

        var message = ExtractResponseMessage(root);
        if (!string.IsNullOrWhiteSpace(message))
            return (false, "", ShortError(message));

        return (false, "", $"상품번호 없는 응답: {ShortError(root.GetRawText())}");
    }

    private static string ExtractProductId(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var nested = ExtractProductId(item);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
            return "";
        }

        if (root.ValueKind != JsonValueKind.Object)
            return "";

        foreach (var key in new[]
                 {
                     "smartstoreChannelProductNo",
                     "channelProductNo",
                     "originProductNo",
                     "productNo",
                     "id",
                 })
        {
            if (root.TryGetProperty(key, out var value))
            {
                var parsed = ReadJsonScalar(value);
                if (!string.IsNullOrWhiteSpace(parsed) && parsed != "0")
                    return parsed;
            }
        }

        foreach (var key in new[]
                 {
                     "data",
                     "content",
                     "contents",
                     "result",
                     "originProduct",
                     "smartstoreChannelProduct",
                     "channelProduct",
                 })
        {
            if (root.TryGetProperty(key, out var child))
            {
                var nested = ExtractProductId(child);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        return "";
    }

    private static string ExtractResponseMessage(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "message", "msg", "errorMessage", "detail", "title" })
            {
                if (root.TryGetProperty(key, out var value))
                {
                    var parsed = ReadJsonScalar(value);
                    if (!string.IsNullOrWhiteSpace(parsed))
                        return parsed;
                }
            }

            if (root.TryGetProperty("invalidInputs", out var invalidInputs))
                return invalidInputs.GetRawText();

            foreach (var key in new[] { "data", "content", "result", "error", "errors" })
            {
                if (root.TryGetProperty(key, out var child))
                {
                    var nested = ExtractResponseMessage(child);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var nested = ExtractResponseMessage(item);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        return "";
    }

    private static string ReadJsonScalar(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };

    private static string? ResolveDefaultHomeCafe24TokenPath()
    {
        var path = DesktopKeyStore.GetPath("cafe24_token_rkghrud1.json");
        return File.Exists(path) ? path : null;
    }

    private static string CreateLogDirectory(string sourcePath)
    {
        var root = ResolveExportRoot(sourcePath);
        var dir = Path.Combine(root, "logs", "naver_upload", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string PrettyJson(JsonObject payload)
        => payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static string PrettyJson(JsonElement payload)
        => JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

    private static string SafeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
            name = name.Replace(ch, '_');
        return name;
    }

    private static List<Dictionary<string, object?>> ReadSourceFile(string filePath)
    {
        // A마켓 시트 또는 기본 시트 사용
        using var wb = new XLWorkbook(filePath);
        IXLWorksheet ws;
        if (wb.TryGetWorksheet("A마켓", out var aSheet))
            ws = aSheet;
        else
            ws = wb.Worksheets.First();

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;

        var headers = new Dictionary<int, string>();
        for (int c = 1; c <= lastCol; c++)
        {
            var val = ws.Cell(1, c).GetString().Trim();
            if (!string.IsNullOrEmpty(val)) headers[c] = val;
        }

        var rows = new List<Dictionary<string, object?>>();
        for (int r = 2; r <= lastRow; r++)
        {
            var row = new Dictionary<string, object?>();
            foreach (var (col, name) in headers)
            {
                var cell = ws.Cell(r, col);
                row[name] = cell.IsEmpty() ? null : cell.Value.IsNumber ? cell.Value.GetNumber() : cell.GetString();
            }
            row["_row_num"] = r;
            row["_source_file_path"] = filePath;
            row["_export_root"] = ResolveExportRoot(filePath);
            rows.Add(row);
        }
        ApplyCategoryMatchIfAvailable(filePath, rows);
        return rows;
    }

    private static void ApplyCategoryMatchIfAvailable(string sourcePath, List<Dictionary<string, object?>> rows)
    {
        var dir = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir) || rows.Count == 0)
            return;

        var categoryFiles = FindCategoryMatchFiles(sourcePath)
            .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (categoryFiles.Count == 0)
            return;

        var categoryByGs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var categoryFile in categoryFiles)
        {
            using var wb = new XLWorkbook(categoryFile);
            var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;
            var headers = new Dictionary<int, string>();
            for (var c = 1; c <= lastCol; c++)
            {
                var value = ws.Cell(1, c).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    headers[c] = value;
            }

            for (var r = 2; r <= lastRow; r++)
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (col, header) in headers)
                {
                    var value = ws.Cell(r, col).GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        values[header] = value;
                }

                var gsCode = FirstNonEmpty(values, "상품코드", "자체 상품코드", "GS코드");
                var naverCategory = FirstNonEmpty(values, "네이버카테고리코드", "네이버카테고리", "네이버카테고리코드/경로");
                var code = ExtractCategoryCode(naverCategory);
                var normalizedGs = NormalizeGsCode(gsCode);
                if (!string.IsNullOrWhiteSpace(normalizedGs) && !string.IsNullOrWhiteSpace(code) && !categoryByGs.ContainsKey(normalizedGs))
                    categoryByGs[normalizedGs] = code;
            }
        }

        foreach (var row in rows)
        {
            var gsCode = NormalizeGsCode(ExtractGsCode(row));
            if (categoryByGs.TryGetValue(gsCode, out var code))
                row["네이버카테고리코드"] = code;
        }
    }

    private static string ExtractCategoryCode(string value)
    {
        var match = Regex.Match(value ?? "", @"[A-Z]{2}\d{6,}|\d{5,}", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : "";
    }

    private static IEnumerable<string> FindCategoryMatchFiles(string sourcePath)
    {
        var dir = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            yield break;

        var sourcePrefix = GetCategoryMatchSourcePrefix(Path.GetFileNameWithoutExtension(sourcePath));
        if (!string.IsNullOrWhiteSpace(sourcePrefix))
        {
            foreach (var file in Directory.GetFiles(dir, $"{sourcePrefix}*category_match*.xlsx", SearchOption.TopDirectoryOnly)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                yield return file;
            }
        }

        foreach (var file in Directory.GetFiles(dir, "*category_match*.xlsx", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            yield return file;
        }
    }

    private static string GetCategoryMatchSourcePrefix(string sourceStem)
    {
        var stem = sourceStem?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(stem))
            return "";

        foreach (var suffix in new[] { "_llm_v5_cli", "_llm_v4_cli", "_llm_v3_cli" })
        {
            if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return stem[..^suffix.Length];
        }

        return stem;
    }

    private static string NormalizeGsCode(string value)
    {
        var match = Regex.Match(value ?? "", @"GS\d{7}[A-Z0-9]*", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : (value ?? "").Trim().ToUpperInvariant();
    }

    private static string FirstNonEmpty(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return "";
    }

    private static async Task WriteSummaryAsync(string logDir, IReadOnlyList<NaverUploadResultItem> results, CancellationToken ct)
    {
        var csv = new StringBuilder();
        csv.AppendLine("row,name,status,productId,error");
        foreach (var item in results)
            csv.AppendLine(string.Join(",", Csv(item.Row.ToString()), Csv(item.Name), Csv(item.Status), Csv(item.ProductId), Csv(item.Error)));

        await File.WriteAllTextAsync(Path.Combine(logDir, "summary.csv"), csv.ToString(), Encoding.UTF8, ct);
        await File.WriteAllTextAsync(
            Path.Combine(logDir, "summary.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8,
            ct);
    }

    private static string Csv(string value)
        => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    private static string ResolveExportRoot(string sourceFilePath)
    {
        var path = System.IO.Path.GetFullPath(sourceFilePath);
        var parent = System.IO.Path.GetDirectoryName(path) ?? "";
        var parentName = System.IO.Path.GetFileName(parent).ToLower();
        var grandParent = System.IO.Path.GetDirectoryName(parent) ?? "";
        var grandName = System.IO.Path.GetFileName(grandParent).ToLower();

        if (parentName.StartsWith("llm_result", StringComparison.OrdinalIgnoreCase) && grandName == "llm_chunks")
            return System.IO.Path.GetDirectoryName(grandParent) ?? grandParent;
        if (parentName.StartsWith("llm_result", StringComparison.OrdinalIgnoreCase))
            return grandParent;
        return parent;
    }

    /// <summary>listing_images 폴더에서 GS코드 가공이미지 파일 찾기 (이미지 선택 반영)</summary>
    private static List<string> FindListingImages(string exportRoot, string gsCode)
    {
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

            return allFiles;
        }

        return new List<string>();
    }

    private static string GetStr(IReadOnlyDictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var v) && v is not null ? v.ToString()?.Trim() ?? "" : "";

    private static string ExtractGsCode(Dictionary<string, object?> row)
    {
        foreach (var key in new[] { "자체 상품코드", "GS코드", "상품코드" })
        {
            var value = GetStr(row, key);
            var match = Regex.Match(value, @"GS\d{7}[A-Z0-9]*", RegexOptions.IgnoreCase);
            if (match.Success) return match.Value.ToUpperInvariant();
        }

        foreach (var value in row.Values)
        {
            var text = value?.ToString() ?? "";
            var match = Regex.Match(text, @"GS\d{7}[A-Z0-9]*", RegexOptions.IgnoreCase);
            if (match.Success) return match.Value.ToUpperInvariant();
        }

        return "";
    }

    private static string CleanMarketProductName(string value)
    {
        var original = (value ?? "").Trim();
        var cleaned = Regex.Replace(original, @"\bGS\d{7}[A-Z0-9]*\b", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '-', '_', '/', '|');
        return string.IsNullOrWhiteSpace(cleaned) ? original : cleaned;
    }

    private static bool HasDirectMarketProductName(Dictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = CleanMarketProductName(GetStr(row, key));
            if (!string.IsNullOrWhiteSpace(value)
                && value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 3)
            {
                return true;
            }
        }

        return false;
    }

    private static int GetInt(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v is null) return 0;
        if (v is double d) return (int)d;
        if (int.TryParse(v.ToString(), out var i)) return i;
        if (double.TryParse(v.ToString(), out var d2)) return (int)d2;
        return 0;
    }
}
