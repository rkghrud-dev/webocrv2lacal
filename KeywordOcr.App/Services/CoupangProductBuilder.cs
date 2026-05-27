using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace KeywordOcr.App.Services;

/// <summary>
/// 엑셀 데이터를 쿠팡 상품 등록 JSON으로 변환
/// </summary>
public static class CoupangProductBuilder
{
    public const int OutboundCode = 23273329;
    public const int ReturnCenterCode = 1002256451;
    private const int DefaultDeliveryCharge = 3000;
    private const int DefaultFreeShipOverAmount = 50000;

    // ── 엑셀 읽기 ──────────────────────────────────

    public static List<Dictionary<string, object?>> ReadSourceFile(string filePath)
    {
        using var wb = new XLWorkbook(filePath);
        IXLWorksheet ws;
        if (wb.TryGetWorksheet("분리추출후", out var splitSheet))
            ws = splitSheet;
        else if (wb.TryGetWorksheet("B마켓", out var bSheet))
            ws = bSheet;
        else
            ws = wb.Worksheets.First();

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;

        // 헤더
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

        return rows;
    }

    // ── 상품 JSON 빌드 ─────────────────────────────

    public static JsonObject BuildProduct(
        Dictionary<string, object?> row,
        long categoryCode,
        JsonElement categoryMeta,
        string vendorId,
        JsonObject? deliveryTemplate = null)
    {
        return BuildProduct(row, categoryCode, categoryMeta, vendorId, "rkghrud", deliveryTemplate);
    }

    public static JsonObject BuildProduct(
        Dictionary<string, object?> row,
        long categoryCode,
        JsonElement categoryMeta,
        string vendorId,
        string vendorUserId,
        JsonObject? deliveryTemplate = null,
        long? outboundShippingPlaceCode = null,
        long? returnCenterCode = null)
    {
        var productName = GetStr(row, "홈런_쿠팡상품명")
            .OrIfEmpty(GetStr(row, "쿠팡상품명"))
            .OrIfEmpty(GetStr(row, "홈런_공통마켓상품명"))
            .OrIfEmpty(GetStr(row, "상품명"))
            .OrIfEmpty(GetStr(row, "최종키워드2차"))
            .OrIfEmpty(GetStr(row, "1차키워드"));
        var displayName = productName.Length > 100 ? productName[..100] : productName;

        // ── 브랜드 표준화: 공백/특수문자 제거 (쿠팡 권장) ──
        var rawBrand = NormalizeBrand(GetStr(row, "브랜드"));
        var brand = Regex.Replace(rawBrand, @"[^0-9A-Za-z가-힣]", "");
        if (string.IsNullOrEmpty(brand)) brand = "샤플라이";

        // ── generalProductName: 옵션 정보 제외한 순수 제품명 ──
        var generalName = Regex.Replace(displayName, @"\d+(mm|cm|m|g|kg|ml|L|개|매|장|ea)\b", "",
            RegexOptions.IgnoreCase).Trim();
        generalName = Regex.Replace(generalName, @"\s{2,}", " ").Trim();
        if (generalName.Length > 100) generalName = generalName[..100];

        // ── sellerProductName: 내부 관리용 (발주서용) ──
        var extSku = GetStr(row, "자체 상품코드");
        var sellerProductName = string.IsNullOrEmpty(extSku)
            ? displayName
            : $"{extSku}_{displayName}";
        if (sellerProductName.Length > 100) sellerProductName = sellerProductName[..100];

        var salePrice = GetInt(row, "판매가");
        var originalPrice = GetInt(row, "소비자가");
        if (originalPrice < salePrice) originalPrice = salePrice;

        // 이미지
        var detailHtml = MarketImageUrlGuard.RemoveUnsafeImageTags(
            GetStr(row, "상품 상세설명").OrIfEmpty(GetStr(row, "상세설명")));
        var detailImageUrls = BuildDetailImageUrls(row);

        // Cafe24 기본마켓 가공이미지 URL 우선 + 부족하면 상세HTML 이미지로 보충
        List<string> listingImageUrls;
        if (row.TryGetValue("_cafe24_image_urls", out var cafe24Imgs) && cafe24Imgs is List<string> cafe24List && cafe24List.Count > 0)
        {
            listingImageUrls = cafe24List
                .Where(MarketImageUrlGuard.IsAllowedUploadUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            listingImageUrls = BuildImageUrls(row);
        }

        // 대표이미지만 있고 추가이미지가 없으면 상세HTML 이미지로 보충
        if (listingImageUrls.Count <= 1 && detailImageUrls.Count > 0)
        {
            var seen = new HashSet<string>(listingImageUrls, StringComparer.OrdinalIgnoreCase);
            foreach (var imgUrl in detailImageUrls)
            {
                if (seen.Add(imgUrl))
                    listingImageUrls.Add(imgUrl);
                if (listingImageUrls.Count >= 10) break;
            }
        }

        var images = new JsonArray();
        for (int i = 0; i < Math.Min(listingImageUrls.Count, 10); i++)
        {
            images.Add(new JsonObject
            {
                ["imageOrder"] = i,
                ["imageType"] = i == 0 ? "REPRESENTATION" : "DETAIL",
                ["vendorPath"] = listingImageUrls[i],
            });
        }

        // 옵션
        var options = ParseOptions(GetStr(row, "옵션입력"), GetStr(row, "옵션추가금"));

        // 고시정보 / 속성
        var optionAttrName = FindExposedAttributeName(categoryMeta);
        var noticeContent = BuildNoticeContent(categoryMeta, row, displayName, options);
        var baseAttributes = BuildAttributes(categoryMeta, optionAttrName);

        // 검색태그 (중복 제거)
        var searchTags = GetStr(row, "홈런_쿠팡검색태그")
            .OrIfEmpty(GetStr(row, "쿠팡검색태그"))
            .OrIfEmpty(GetStr(row, "홈런_공통마켓검색키워드"))
            .OrIfEmpty(GetStr(row, "공통마켓검색키워드"))
            .OrIfEmpty(GetStr(row, "검색어설정"))
            .OrIfEmpty(GetStr(row, "검색키워드"));
        var tagList = new JsonArray();
        foreach (var tag in ParseSearchTags(searchTags, maxTags: 20))
            tagList.Add(tag);

        // items
        var items = new JsonArray();
        if (options.Count > 0)
        {
            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                // 옵션별 고유 속성 추가
                var itemAttrs = baseAttributes.DeepClone().AsArray();
                itemAttrs.Add(new JsonObject
                {
                    ["attributeTypeName"] = optionAttrName,
                    ["attributeValueName"] = opt.AttributeValue,
                });
                // SKU: 불변키+옵션 형식 (문서 권장)
                var optSku = string.IsNullOrEmpty(extSku)
                    ? $"OPT{i + 1}"
                    : $"{extSku}_{Regex.Replace(opt.Name, @"[^0-9A-Za-z가-힣]", "")}";
                items.Add(MakeItem(
                    opt.Name, salePrice + opt.Price, originalPrice + opt.Price,
                    optSku,
                    noticeContent, itemAttrs, images, tagList, detailImageUrls));
            }
        }
        else
        {
            items.Add(MakeItem(
                displayName, salePrice, originalPrice, extSku,
                noticeContent, baseAttributes, images, tagList, detailImageUrls));
        }

        var product = new JsonObject
        {
            ["displayCategoryCode"] = categoryCode,
            ["sellerProductName"] = sellerProductName,
            ["vendorId"] = vendorId,
            ["saleStartedAt"] = "2020-01-01T00:00:00",
            ["saleEndedAt"] = "2099-12-31T00:00:00",
            ["displayProductName"] = displayName,
            ["brand"] = brand,
            ["generalProductName"] = generalName,
            ["productGroup"] = "",
            ["requested"] = true,
            ["items"] = items,
            ["requiredDocuments"] = new JsonArray(),
            ["extraInfoMessage"] = "",
            ["manufacture"] = "",
        };

        ApplyDeliveryTemplate(product, vendorUserId, deliveryTemplate, outboundShippingPlaceCode, returnCenterCode);
        return product;
    }

    private static void ApplyDeliveryTemplate(
        JsonObject product,
        string vendorUserId,
        JsonObject? deliveryTemplate,
        long? outboundShippingPlaceCode,
        long? returnCenterCode)
    {
        var source = deliveryTemplate ?? BuildDefaultDeliveryTemplate(vendorUserId);
        foreach (var kvp in source)
            product[kvp.Key] = kvp.Value?.DeepClone();

        product["deliveryChargeType"] = "CONDITIONAL_FREE";
        product["deliveryCharge"] = DefaultDeliveryCharge;
        product["freeShipOverAmount"] = DefaultFreeShipOverAmount;
        product["deliveryChargeOnReturn"] = DefaultDeliveryCharge;
        product["returnCharge"] = DefaultDeliveryCharge;
        product["deliveryCompanyCode"] = "CJGLS";
        product["remoteAreaDeliverable"] = "Y";
        product["vendorUserId"] = string.IsNullOrWhiteSpace(vendorUserId) ? "rkghrud" : vendorUserId;
        product["outboundShippingPlaceCode"] = outboundShippingPlaceCode ?? OutboundCode;
        product["returnCenterCode"] = returnCenterCode ?? ReturnCenterCode;
    }

    private static JsonObject BuildDefaultDeliveryTemplate(string vendorUserId)
    {
        return new JsonObject
        {
            ["deliveryMethod"] = "SEQUENCIAL",
            ["deliveryCompanyCode"] = "CJGLS",
            ["deliveryChargeType"] = "CONDITIONAL_FREE",
            ["deliveryCharge"] = DefaultDeliveryCharge,
            ["freeShipOverAmount"] = DefaultFreeShipOverAmount,
            ["deliveryChargeOnReturn"] = DefaultDeliveryCharge,
            ["returnCharge"] = DefaultDeliveryCharge,
            ["outboundShippingPlaceCode"] = OutboundCode,
            ["returnCenterCode"] = ReturnCenterCode,
            ["returnChargeName"] = "명일우진반품",
            ["companyContactNumber"] = "010-2324-8352",
            ["returnZipCode"] = "05287",
            ["returnAddress"] = "서울특별시 강동구 상일로 74",
            ["returnAddressDetail"] = "고덕리엔파크3단지아파트 고덕리엔파크 321동 CJ대한통운 명일우진대리점",
            ["remoteAreaDeliverable"] = "Y",
            ["unionDeliveryType"] = "UNION_DELIVERY",
            ["vendorUserId"] = string.IsNullOrWhiteSpace(vendorUserId) ? "rkghrud" : vendorUserId,
            ["afterServiceInformation"] = "010-2324-8352",
            ["afterServiceContactNumber"] = "010-2324-8352",
        };
    }
    // ── 내부 헬퍼 ──────────────────────────────────

    private static JsonObject MakeItem(
        string itemName, int salePrice, int originalPrice, string sku,
        JsonArray noticeContent, JsonNode attributes,
        JsonArray images, JsonArray searchTags, List<string> detailImageUrls)
    {
        // 상세이미지 → contents (IMAGE_NO_SPACE)
        var contents = new JsonArray();
        if (detailImageUrls.Count > 0)
        {
            var contentDetails = new JsonArray();
            foreach (var imgUrl in detailImageUrls)
            {
                contentDetails.Add(new JsonObject
                {
                    ["content"] = imgUrl,
                    ["detailType"] = "IMAGE",
                });
            }
            contents.Add(new JsonObject
            {
                ["contentsType"] = "IMAGE_NO_SPACE",
                ["contentDetails"] = contentDetails,
            });
        }

        return new JsonObject
        {
            ["itemName"] = itemName,
            ["originalPrice"] = originalPrice,
            ["salePrice"] = salePrice,
            ["maximumBuyCount"] = 9999,
            ["maximumBuyForPerson"] = 9999,
            ["outboundShippingTimeDay"] = 2,
            ["maximumBuyForPersonPeriod"] = 1,
            ["unitCount"] = 1,
            ["adultOnly"] = "EVERYONE",
            ["taxType"] = "TAX",
            ["parallelImported"] = "NOT_PARALLEL_IMPORTED",
            ["overseasPurchased"] = "NOT_OVERSEAS_PURCHASED",
            ["pccNeeded"] = false,
            ["externalVendorSku"] = sku,
            ["barcode"] = "",
            ["emptyBarcode"] = true,
            ["emptyBarcodeReason"] = "",
            ["notices"] = noticeContent.DeepClone(),
            ["attributes"] = attributes.DeepClone(),
            ["contents"] = contents,
            ["images"] = images.DeepClone(),
            ["searchTags"] = searchTags.DeepClone(),
        };
    }

    private static List<string> ParseSearchTags(string raw, int maxTags)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var source = raw ?? "";
        var hasExplicitDelimiter = Regex.IsMatch(source, @"[,|\n;/]+");
        var parts = hasExplicitDelimiter
            ? Regex.Split(source, @"[,|\n;/]+")
            : new[] { source };

        foreach (var part in parts)
        {
            foreach (var tag in NormalizeSearchTagCandidates(part, splitLongPhrase: !hasExplicitDelimiter))
            {
                if (seen.Add(tag))
                    result.Add(tag);
                if (result.Count >= maxTags)
                    return result;
            }
        }

        return result;
    }

    private static IEnumerable<string> NormalizeSearchTagCandidates(string raw, bool splitLongPhrase)
    {
        var cleaned = Regex.Replace(raw ?? "", @"[^0-9A-Za-z가-힣\s]", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            yield break;

        if (cleaned.Length <= 20)
        {
            yield return cleaned;
            yield break;
        }

        if (!splitLongPhrase)
            yield break;

        foreach (var token in cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length is >= 2 and <= 20)
                yield return token;
        }
    }

    /// <summary>상세페이지용 이미지 추출 (상품 상세설명 HTML의 img 태그에서 추출)</summary>
    private static List<string> BuildDetailImageUrls(Dictionary<string, object?> row)
    {
        var urls = new List<string>();
        var seen = new HashSet<string>();

        // "상품 상세설명" HTML에서 <img src="..."> 추출 — 이것이 진짜 상세이미지
        var detailHtml = GetStr(row, "상품 상세설명").OrIfEmpty(GetStr(row, "상세설명"));
        if (!string.IsNullOrEmpty(detailHtml))
        {
            var imgMatches = Regex.Matches(detailHtml, @"<img[^>]+src=[""']([^""']+)", RegexOptions.IgnoreCase);
            foreach (Match m in imgMatches)
            {
                var imgUrl = m.Groups[1].Value.Trim();
                if (MarketImageUrlGuard.IsAllowedUploadUrl(imgUrl) && seen.Add(imgUrl))
                    urls.Add(imgUrl);
            }
        }

        return urls;
    }

    /// <summary>목록용 이미지만 추출 (대표이미지 + 추가이미지)</summary>
    private static List<string> BuildImageUrls(Dictionary<string, object?> row)
    {
        var urls = new List<string>();
        var seen = new HashSet<string>();

        foreach (var column in new[] { "이미지등록(목록)", "이미지등록(추가)" })
        {
            var val = GetStr(row, column);
            if (string.IsNullOrEmpty(val))
                continue;

            foreach (var u in Regex.Split(val, @"[|\n]+"))
            {
                var trimmed = u.Trim();
                    if (MarketImageUrlGuard.IsAllowedUploadUrl(trimmed) && seen.Add(trimmed))
                        urls.Add(trimmed);
            }
        }

        // 목록/추가 이미지가 없으면 상세이미지 첫 1장을 대표이미지로 fallback
        // 목록 이미지가 없으면 상세이미지 첫 1장을 대표이미지로 fallback
        if (urls.Count == 0)
        {
            var detailVal = GetStr(row, "이미지등록(상세)");
            if (!string.IsNullOrEmpty(detailVal))
            {
                foreach (var u in Regex.Split(detailVal, @"[|\n]"))
                {
                    var trimmed = u.Trim();
                    if (MarketImageUrlGuard.IsAllowedUploadUrl(trimmed) && seen.Add(trimmed))
                    {
                        urls.Add(trimmed);
                        break; // 대표이미지 1장만
                    }
                }
            }
        }

        return urls.Take(10).ToList();
    }

    private record OptionItem(string Name, string AttributeValue, int Price);

    private static List<OptionItem> ParseOptions(string? optionStr, string? extraPriceStr)
    {
        if (string.IsNullOrEmpty(optionStr)) return new();

        var matches = Regex.Matches(optionStr, @"([A-Z])\s+([^,}|]+)");
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
            var label = matches[i].Groups[1].Value.Trim();
            var value = matches[i].Groups[2].Value.Trim();
            var name = EnsureOptionPrefix($"{label} {value}".Trim(), i);
            var price = i < prices.Count ? prices[i] : 0;
            options.Add(new OptionItem(name, value, price));
        }

        if (options.Count == 0)
        {
            var body = optionStr ?? "";
            var brace = Regex.Match(body, @"\{(.+?)\}");
            if (brace.Success)
                body = brace.Groups[1].Value;

            var values = body
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            for (var i = 0; i < values.Count; i++)
            {
                var name = EnsureOptionPrefix(values[i], i);
                var price = i < prices.Count ? prices[i] : 0;
                options.Add(new OptionItem(name, StripOptionPrefix(name), price));
            }
        }
        return options;
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

    private static string StripOptionPrefix(string value)
        => Regex.Replace(value ?? "", @"^[A-Z]\s+", "", RegexOptions.IgnoreCase).Trim();

    private static JsonArray BuildNoticeContent(
        JsonElement categoryMeta,
        Dictionary<string, object?> row,
        string productName,
        IReadOnlyList<OptionItem> options)
    {
        var arr = new JsonArray();
        if (!categoryMeta.TryGetProperty("data", out var data)) return arr;
        if (!data.TryGetProperty("noticeCategories", out var notices)) return arr;
        if (notices.GetArrayLength() == 0) return arr;

        var notice = SelectNoticeCategory(notices, row, productName);
        var noticeName = notice.GetProperty("noticeCategoryName").GetString() ?? "";
        var noticeFields = BuildNoticeFieldContext(row, productName, options);

        if (notice.TryGetProperty("noticeCategoryDetailNames", out var details))
        {
            foreach (var d in details.EnumerateArray())
            {
                var detailName = d.GetProperty("noticeCategoryDetailName").GetString() ?? "";
                arr.Add(new JsonObject
                {
                    ["noticeCategoryName"] = noticeName,
                    ["noticeCategoryDetailName"] = detailName,
                    ["content"] = BuildNoticeDetailContent(detailName, noticeFields),
                });
            }
        }
        return arr;
    }

    private sealed record NoticeFieldContext(
        string ProductName,
        string ModelName,
        string Material,
        string Color,
        string Size,
        string Quantity,
        string Manufacturer,
        string Importer,
        string Origin,
        string CustomerService,
        string Components,
        string SourceText);

    private static JsonElement SelectNoticeCategory(
        JsonElement notices,
        Dictionary<string, object?> row,
        string productName)
    {
        var sourceText = string.Join(" ", productName, GetStr(row, "OCR요약"), GetStr(row, "상품 상세설명"), GetStr(row, "상세설명"));
        var best = notices[0];
        var bestScore = int.MinValue;

        foreach (var notice in notices.EnumerateArray())
        {
            var noticeName = notice.TryGetProperty("noticeCategoryName", out var nameProp) ? nameProp.GetString() ?? "" : "";
            var detailsText = "";
            if (notice.TryGetProperty("noticeCategoryDetailNames", out var details))
            {
                detailsText = string.Join(" ", details.EnumerateArray()
                    .Select(detail => detail.TryGetProperty("noticeCategoryDetailName", out var detailName) ? detailName.GetString() ?? "" : ""));
            }

            var score = 0;
            if (Regex.IsMatch(sourceText, "깔창|인솔|신발|구두|운동화|슬리퍼|부츠") && Regex.IsMatch(noticeName, "신발|구두"))
                score += 80;
            if (Regex.IsMatch(sourceText, "가방|백팩|파우치|숄더백|토트백") && noticeName.Contains("가방"))
                score += 80;
            if (Regex.IsMatch(sourceText, "의류|티셔츠|셔츠|바지|자켓|재킷|점퍼|원피스|스커트") && noticeName.Contains("의류"))
                score += 80;
            if (noticeName.Contains("기타"))
                score -= 10;
            if (detailsText.Contains("재질") || detailsText.Contains("색상") || detailsText.Contains("크기"))
                score += 5;

            if (score > bestScore)
            {
                bestScore = score;
                best = notice;
            }
        }

        return best;
    }

    private static NoticeFieldContext BuildNoticeFieldContext(
        Dictionary<string, object?> row,
        string productName,
        IReadOnlyList<OptionItem> options)
    {
        var sourceText = string.Join(" ",
            productName,
            GetStr(row, "OCR요약"),
            GetStr(row, "상품 상세설명"),
            GetStr(row, "상세설명"));

        var jsonFields = ExtractNoticeJsonFields(GetStr(row, "네이버상품정보고시")
            .OrIfEmpty(GetStr(row, "상품정보제공고시"))
            .OrIfEmpty(GetStr(row, "naverProvidedNotice")));

        var optionValues = ExtractOptionValues(options);
        var material = FirstNonEmpty(
            jsonFields.GetValueOrDefault("material"),
            ExtractLabeledNoticeValue(sourceText, "소재", "재질", "재료", "원단"),
            ExtractMaterial(sourceText));
        var color = FirstNonEmpty(
            jsonFields.GetValueOrDefault("color"),
            ExtractLabeledNoticeValue(sourceText, "색상", "컬러", "색깔"),
            optionValues);
        var size = FirstNonEmpty(
            jsonFields.GetValueOrDefault("size"),
            ExtractLabeledNoticeValue(sourceText, "사이즈", "크기", "규격", "치수", "중량"),
            ExtractSize(sourceText));
        var manufacturer = FirstNonEmpty(
            jsonFields.GetValueOrDefault("manufacturer"),
            ExtractLabeledNoticeValue(sourceText, "제조사", "제조자", "제조원"));
        var importer = FirstNonEmpty(
            jsonFields.GetValueOrDefault("importer"),
            ExtractLabeledNoticeValue(sourceText, "수입사", "수입원", "수입자"));
        var origin = FirstNonEmpty(
            jsonFields.GetValueOrDefault("origin"),
            ExtractLabeledNoticeValue(sourceText, "제조국", "원산지"),
            "중국");
        var customerService = FirstNonEmpty(
            jsonFields.GetValueOrDefault("customerServicePhoneNumber"),
            ExtractLabeledNoticeValue(sourceText, "A/S", "AS", "고객센터", "소비자상담"),
            "홈런마켓 / 010-2324-8352");

        return new NoticeFieldContext(
            productName,
            FirstNonEmpty(jsonFields.GetValueOrDefault("modelName"), ExtractGsCodeFromText(sourceText), productName),
            material,
            color,
            size,
            FirstNonEmpty(jsonFields.GetValueOrDefault("quantity"), ExtractLabeledNoticeValue(sourceText, "수량", "구성수량"), "1개"),
            manufacturer,
            importer,
            origin,
            customerService,
            FirstNonEmpty(ExtractLabeledNoticeValue(sourceText, "제품 구성", "제품구성", "구성품", "구성"), "본품 1개"),
            sourceText);
    }

    private static Dictionary<string, string> ExtractNoticeJsonFields(string raw)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
            return fields;

        try
        {
            var node = JsonNode.Parse(raw);
            var obj = node as JsonObject;
            if (obj is null)
                return fields;
            if (obj["productInfoProvidedNotice"] is JsonObject nested)
                obj = nested;

            foreach (var (_, valueNode) in obj)
            {
                if (valueNode is not JsonObject detail)
                    continue;
                foreach (var (key, value) in detail)
                {
                    var text = CleanNoticeValue(value?.GetValue<object>()?.ToString() ?? "");
                    if (!string.IsNullOrWhiteSpace(text) && text != "상품상세 참조" && text != "상세페이지 참조")
                        fields[key] = text;
                }
            }
        }
        catch { }

        return fields;
    }

    private static string BuildNoticeDetailContent(
        string detailName,
        NoticeFieldContext fields)
    {
        if (detailName.Contains("품명") || detailName.Contains("모델명"))
            return string.IsNullOrWhiteSpace(fields.ModelName)
                ? fields.ProductName
                : $"{fields.ProductName} / {fields.ModelName}";
        if (detailName.Contains("KC") || detailName.Contains("인증") || detailName.Contains("허가"))
            return "해당없음";
        if (detailName.Contains("크기") || detailName.Contains("중량"))
            return string.IsNullOrEmpty(fields.Size) ? "상세페이지 참조" : fields.Size;
        if (detailName.Contains("색상"))
            return string.IsNullOrEmpty(fields.Color) ? "상세페이지 참조" : fields.Color;
        if (detailName.Contains("재질") || detailName.Contains("소재") || detailName.Contains("원단"))
            return string.IsNullOrEmpty(fields.Material) ? "상세페이지 참조" : fields.Material;
        if (detailName.Contains("제품 구성") || detailName.Contains("제품구성"))
            return fields.Components;
        if (detailName.Contains("수량"))
            return fields.Quantity;
        if (detailName.Contains("출시"))
            return "상세페이지 참조";
        if (detailName.Contains("제조자") || detailName.Contains("수입자"))
            return BuildManufacturerImporter(fields.Manufacturer, fields.Importer);
        if (detailName.Contains("제조국") || detailName.Contains("원산지"))
            return string.IsNullOrEmpty(fields.Origin) ? "중국" : fields.Origin;
        if (detailName.Contains("세부 사양"))
            return "상세페이지 참조";
        if (detailName.Contains("품질보증"))
            return "소비자분쟁해결기준에 따름";
        if (detailName.Contains("A/S") || detailName.Contains("소비자상담"))
            return fields.CustomerService;
        return "상세페이지 참조";
    }

    private static string BuildManufacturerImporter(string manufacturer, string importer)
    {
        var maker = string.IsNullOrWhiteSpace(manufacturer) ? "상세페이지 참조" : manufacturer;
        var import = string.IsNullOrWhiteSpace(importer) ? "홈런마켓" : importer;
        return $"제조자: {maker} / 수입자: {import}";
    }

    private static string ExtractLabeledNoticeValue(string sourceText, params string[] labels)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || labels.Length == 0)
            return "";

        var labelPattern = string.Join("|", labels.Select(Regex.Escape));
        var stopPattern = "소재|재질|재료|원단|수량|구성수량|색상|컬러|색깔|사이즈|크기|규격|치수|중량|제조사|제조자|제조원|수입사|수입원|수입자|제조국|원산지|A/S|AS|고객센터|소비자상담|제품 구성|제품구성|구성품|구성";
        var joined = Regex.Replace(sourceText, @"<[^>]+>", " ");
        joined = Regex.Replace(joined, @"\s+", " ").Trim();

        var match = Regex.Match(
            joined,
            $@"(?:^|\s)(?:{labelPattern})\s*[:：]?\s*(.+?)(?=\s+(?:{stopPattern})\s*[:：]?|\s+홈런마켓\b|\s+급배송\b|$)",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            return "";

        var value = CleanNoticeValue(match.Groups[1].Value);
        return IsNoticeNoise(value) ? "" : value;
    }

    private static string CleanNoticeValue(string value)
    {
        var text = Regex.Replace(value ?? "", @"[{}""`]+", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim(" ,./|·:-".ToCharArray());
        text = Regex.Split(text, @"\s+(?:홈런마켓|급배송|평일|택배사|구매대행|국내|모든\s*제품|상품/대량구매|대량구매|퀵서비스|방문수령)\b", RegexOptions.IgnoreCase)[0];
        return text.Trim(" ,./|·:-".ToCharArray());
    }

    private static bool IsNoticeNoise(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;
        return Regex.IsMatch(value, @"^(?:high|bullet|advantage|product\s*profile|size|in/mm|on|off|zero|\d{1,4}|\d{1,4}\s*mm)$", RegexOptions.IgnoreCase);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var cleaned = CleanNoticeValue(value ?? "");
            if (!string.IsNullOrWhiteSpace(cleaned) && !IsNoticeNoise(cleaned))
                return cleaned;
        }
        return "";
    }

    private static string ExtractGsCodeFromText(string value)
    {
        var match = Regex.Match(value ?? "", @"GS\d{7}[A-Z0-9]*", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : "";
    }

    private static string ExtractOptionValues(IReadOnlyList<OptionItem> options)
    {
        var values = options
            .Select(option => option.AttributeValue.Trim())
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        return string.Join(", ", values);
    }

    private static string ExtractMaterial(string sourceText)
    {
        var found = new List<string>();
        foreach (var (label, pattern) in new[]
                 {
                     ("ABS", @"\bABS\b"),
                     ("스테인리스", "스테인리스|스텐"),
                     ("플라스틱", "플라스틱|PP|PVC|PE"),
                     ("아크릴", "아크릴"),
                     ("실리콘", "실리콘"),
                     ("알루미늄", "알루미늄"),
                     ("철제", "철제|스틸"),
                 })
        {
            if (Regex.IsMatch(sourceText ?? "", pattern, RegexOptions.IgnoreCase)
                && !found.Contains(label, StringComparer.OrdinalIgnoreCase))
            {
                found.Add(label);
            }
        }

        return string.Join(", ", found.Take(3));
    }

    private static string ExtractSize(string sourceText)
    {
        var matches = Regex.Matches(sourceText ?? "", @"\d+(?:\.\d+)?\s*(?:mm|cm|m|g|kg|ml|L|리터|개|매|장)", RegexOptions.IgnoreCase)
            .Select(match => Regex.Replace(match.Value, @"\s+", ""))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
        return string.Join(", ", matches);
    }

    private static JsonArray BuildAttributes(JsonElement categoryMeta, string optionAttrName)
    {
        var arr = new JsonArray();
        if (!categoryMeta.TryGetProperty("data", out var data)) return arr;
        if (!data.TryGetProperty("attributes", out var attrs)) return arr;

        foreach (var a in attrs.EnumerateArray())
        {
            var required = a.GetProperty("required").GetString();
            if (required != "MANDATORY") continue;

            var attrName = a.GetProperty("attributeTypeName").GetString() ?? "";
            var isSelectedOptionAttr = string.Equals(attrName, optionAttrName, StringComparison.Ordinal);

            if (a.TryGetProperty("exposed", out var exposed) && exposed.GetString() == "EXPOSED")
            {
                if (isSelectedOptionAttr)
                    continue;

                if (!IsQuantityAttribute(attrName))
                    continue;
            }

            string val;

            var inputType = a.GetProperty("inputType").GetString();
            if (inputType == "SELECT" && a.TryGetProperty("inputValues", out var inputValues)
                && inputValues.GetArrayLength() > 0)
            {
                var first = inputValues[0];
                val = first.ValueKind == JsonValueKind.Object
                    ? first.GetProperty("inputValueName").GetString() ?? first.ToString()
                    : first.ToString();
            }
            else if (IsQuantityAttribute(attrName))
                val = "1개";
            else if (attrName == "색상")
                val = "기타";
            else if (a.GetProperty("dataType").GetString() == "NUMBER")
                val = "1";
            else
                val = "상세페이지 참조";

            var entry = new JsonObject
            {
                ["attributeTypeName"] = attrName,
                ["attributeValueName"] = val,
            };

            if (a.TryGetProperty("basicUnit", out var unit))
            {
                var unitStr = unit.GetString();
                if (!IsQuantityAttribute(attrName) && !string.IsNullOrEmpty(unitStr) && unitStr != "없음")
                    entry["unitCodeName"] = unitStr;
            }

            arr.Add(entry);
        }
        return arr;
    }

    /// <summary>카테고리 메타에서 EXPOSED 속성명 찾기 (옵션 구분용)</summary>
    private static string FindExposedAttributeName(JsonElement categoryMeta)
    {
        if (!categoryMeta.TryGetProperty("data", out var data)) return "옵션";
        if (!data.TryGetProperty("attributes", out var attrs)) return "옵션";

        var exposedNames = new List<string>();
        foreach (var a in attrs.EnumerateArray())
        {
            var name = a.TryGetProperty("attributeTypeName", out var nameProp)
                ? nameProp.GetString()
                : null;
            if (string.Equals(name, "옵션", StringComparison.OrdinalIgnoreCase))
                return "옵션";

            if (a.TryGetProperty("exposed", out var exposed) && exposed.GetString() == "EXPOSED"
                && !string.IsNullOrEmpty(name))
                exposedNames.Add(name);
        }

        foreach (var preferred in new[] { "사이즈", "색상", "색상/사이즈", "규격", "타입", "종류" })
        {
            var matched = exposedNames.FirstOrDefault(name => string.Equals(name, preferred, StringComparison.Ordinal));
            if (!string.IsNullOrEmpty(matched))
                return matched;
        }

        var nonQuantity = exposedNames.FirstOrDefault(name => !IsQuantityAttribute(name));
        if (!string.IsNullOrEmpty(nonQuantity))
            return nonQuantity;

        return exposedNames.FirstOrDefault() ?? "옵션";
    }

    private static bool IsQuantityAttribute(string attrName)
        => attrName is "수량" or "총 수량";

    private static string ResolveExportRoot(string sourceFilePath)
    {
        var path = Path.GetFullPath(sourceFilePath);
        var parent = Path.GetDirectoryName(path) ?? "";
        var parentName = Path.GetFileName(parent).ToLower();
        var grandParent = Path.GetDirectoryName(parent) ?? "";
        var grandName = Path.GetFileName(grandParent).ToLower();

        if (parentName == "llm_result" && grandName == "llm_chunks")
            return Path.GetDirectoryName(grandParent) ?? grandParent;
        if (parentName is "llm_result" or "llm_result_v5_cli" or "llm_result_v4_cli")
            return grandParent;
        return parent;
    }

    // ── 유틸 ───────────────────────────────────────

    private static string GetStr(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var v) && v is not null ? v.ToString()?.Trim() ?? "" : "";

    private static int GetInt(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v is null) return 0;
        if (v is double d) return (int)d;
        if (int.TryParse(v.ToString(), out var i)) return i;
        if (double.TryParse(v.ToString(), out var d2)) return (int)d2;
        return 0;
    }

    private static string NormalizeBrand(string value)
    {
        var brand = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(brand)
            || string.Equals(brand, "B0000000", StringComparison.OrdinalIgnoreCase)
            || brand.Contains("브랜드없음", StringComparison.OrdinalIgnoreCase)
            || brand.Contains("자체브랜드", StringComparison.OrdinalIgnoreCase)
            || string.Equals(brand, "없음", StringComparison.OrdinalIgnoreCase))
        {
            return "샤플라이";
        }

        return brand;
    }
}

internal static class StringExt
{
    public static string OrIfEmpty(this string s, string fallback)
        => string.IsNullOrWhiteSpace(s) ? fallback : s;
}
