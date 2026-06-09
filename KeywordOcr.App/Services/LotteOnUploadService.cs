using System;
using System.Collections.Generic;
using System.Globalization;
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

public sealed record LotteOnUploadOptions
{
    public int RowStart { get; set; }
    public int RowEnd { get; set; }
    public bool DryRun { get; set; } = true;
    public bool Force { get; set; }
    public IReadOnlySet<string>? AllowedGsCodes { get; set; }
    public string? Cafe24TokenPath { get; set; }
}

public sealed record LotteOnUploadResultItem(
    int Row,
    string Name,
    string Status,
    string SpdNo,
    string Error);

public sealed record LotteOnUploadResult(
    IReadOnlyList<LotteOnUploadResultItem> Items,
    int SuccessCount,
    int FailCount,
    int TotalCount,
    string LogDirectory);

public sealed class LotteOnUploadService
{
    private const string TrGrpCd = "SR";
    private const string TrNo = "LO10064395";
    private const string OwhpNo = "PLO2589408";
    private const string RtrpNo = "PLO2589408";
    private const string DvCstPolNo = "3808494";
    private const string AdtnDvCstPolNo = "2058331";
    private const string HdcCd = "0006";
    private const int DefaultStock = 9999;

    private sealed record LotteOnUploadDefaults(
        string TrGrpCd,
        string TrNo,
        string OwhpNo,
        string RtrpNo,
        string DvCstPolNo,
        string AdtnDvCstPolNo,
        string HdcCd);

    private static LotteOnUploadDefaults LoadUploadDefaults()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ShouldUsePersistedDefaults())
            LoadPersistedDefaults(values);

        var path = DesktopKeyStore.GetPath("lotteon_api.txt");
        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;
                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx <= 0) continue;
                values[trimmed[..eqIdx].Trim()] = trimmed[(eqIdx + 1)..].Trim();
            }
        }

        string Pick(string fallback, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return fallback;
        }

        return new LotteOnUploadDefaults(
            Pick(TrGrpCd, "trGrpCd", "IDENTITY_TR_GRP_CD"),
            Pick(TrNo, "trNo", "IDENTITY_TR_NO", "VENDOR_NO", "SELLER_NO"),
            Pick(OwhpNo, "owhpNo", "PICKUP_PLACE_NO", "PICKUP_PLACE", "owhp_no"),
            Pick(RtrpNo, "rtrpNo", "RETURN_PLACE_NO", "rtrp_no"),
            Pick(DvCstPolNo, "dvCstPolNo", "DELIVERY_COST_POLICY_NO", "delivery_cost_policy_no"),
            Pick(AdtnDvCstPolNo, "adtnDvCstPolNo", "ADDITIONAL_DELIVERY_COST_POLICY_NO", "additional_delivery_cost_policy_no"),
            Pick(HdcCd, "hdcCd", "rtngHdcCd", "DELIVERY_COMPANY_CODE"));
    }

    private static string PersistentKeyDirectory =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBOCR_ORIGINAL_KEY_ROOT"))
            ? Environment.GetEnvironmentVariable("WEBOCR_ORIGINAL_KEY_ROOT")!
            : DesktopKeyStore.DirectoryPath;

    private static bool ShouldUsePersistedDefaults()
    {
        try
        {
            var keyRoot = Path.GetFullPath(DesktopKeyStore.DirectoryPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var persistentRoot = Path.GetFullPath(PersistentKeyDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(keyRoot, persistentRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static void LoadPersistedDefaults(Dictionary<string, string> values)
    {
        var path = Path.Combine(PersistentKeyDirectory, "lotteon_upload_defaults.json");
        if (!File.Exists(path)) return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    values[property.Name] = value.Trim();
            }
        }
        catch
        {
            // 저장된 정책 캐시가 깨져도 원본 키 파일 값으로 계속 진행합니다.
        }
    }

    private static void SaveUploadDefaultsSnapshot(LotteOnUploadDefaults defaults)
    {
        if (!ShouldUsePersistedDefaults())
            return;

        try
        {
            Directory.CreateDirectory(PersistentKeyDirectory);
            var payload = new JsonObject
            {
                ["trGrpCd"] = defaults.TrGrpCd,
                ["trNo"] = defaults.TrNo,
                ["owhpNo"] = defaults.OwhpNo,
                ["rtrpNo"] = defaults.RtrpNo,
                ["dvCstPolNo"] = defaults.DvCstPolNo,
                ["adtnDvCstPolNo"] = defaults.AdtnDvCstPolNo,
                ["hdcCd"] = defaults.HdcCd,
                ["savedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            };
            File.WriteAllText(
                Path.Combine(PersistentKeyDirectory, "lotteon_upload_defaults.json"),
                payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                Encoding.UTF8);
        }
        catch
        {
        }
    }

    public async Task<LotteOnUploadResult> UploadAsync(
        string sourcePath,
        LotteOnUploadOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        void Log(string msg) => progress?.Report(msg);

        var allRows = ReadSourceFile(sourcePath);
        var targetRows = FilterRows(allRows, options);
        var logDir = CreateLogDirectory(sourcePath);
        Directory.CreateDirectory(Path.Combine(logDir, "requests"));
        Directory.CreateDirectory(Path.Combine(logDir, "responses"));

        Log($"[롯데ON] 가공파일 로드: {allRows.Count}개 / 처리 대상 {targetRows.Count}개");
        Log($"[롯데ON] 로그 폴더: {logDir}");

        var history = LoadHistory();
        var results = new List<LotteOnUploadResultItem>();
        var cafe24TokenPath = string.IsNullOrWhiteSpace(options.Cafe24TokenPath)
            ? ResolveDefaultHomeCafe24TokenPath()
            : options.Cafe24TokenPath;
        var cafe24MarketData = options.DryRun
            ? null
            : await Cafe24MarketDataService.TryCreateAsync(sourcePath, Log, ct, cafe24TokenPath);

        using var api = LotteOnApiClient.FromKeyFile();
        if (!options.DryRun)
        {
            await ValidateIdentityAsync(api, logDir, Log, ct);
        }

        for (var i = 0; i < targetRows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = targetRows[i];
            var rowNum = (int)row["_row_num"]!;
            var gsCode = ExtractGsCode(row);
            var productName = GetProductName(row);
            var shortName = productName.Length > 30 ? productName[..30] : productName;

            Log($"[롯데ON] [{i + 1}/{targetRows.Count}] {gsCode} {shortName}");

            if (string.IsNullOrWhiteSpace(gsCode))
            {
                results.Add(new LotteOnUploadResultItem(rowNum, shortName, "SKIP_NO_GS", "", "GS코드 없음"));
                MarketUploadStateStore.Upsert(gsCode, shortName, "롯데ON", "SKIP_NO_GS", "", new List<string>(), "GS코드 없음");
                continue;
            }

            if (!options.Force && history.TryGetValue(gsCode, out var previousSpdNo) && !string.IsNullOrWhiteSpace(previousSpdNo))
            {
                results.Add(new LotteOnUploadResultItem(rowNum, shortName, "SKIP_DUP", previousSpdNo, "성공 이력 있음"));
                MarketUploadStateStore.Upsert(gsCode, shortName, "롯데ON", "SKIP_DUP", previousSpdNo, new List<string>(), "성공 이력 있음");
                Log($"[롯데ON] {gsCode} 스킵: 기존 성공 이력 {previousSpdNo}");
                continue;
            }

            if (!HasDirectMarketProductName(row, "홈런_롯데ON상품명", "롯데ON상품명"))
            {
                results.Add(new LotteOnUploadResultItem(
                    rowNum,
                    shortName,
                    "NAME_FAIL",
                    "",
                    "롯데ON 직접등록용 상품명 컬럼이 비어 있습니다. V5 최종 llm_v5_cli 엑셀을 사용해야 합니다."));
                MarketUploadStateStore.Upsert(gsCode, shortName, "롯데ON", "NAME_FAIL", "", new List<string>(), "롯데ON 직접등록용 상품명 컬럼이 비어 있습니다.");
                continue;
            }

            try
            {
                if (cafe24MarketData is not null && !options.DryRun)
                    await cafe24MarketData.TryApplyAsync(row, ct);

                if (!options.DryRun)
                    await SupplementImagesViaNaverCdnAsync(row, Log, ct);

                var stateImageUrls = CollectImageUrls(row);
                var payload = BuildRegistrationPayload(row);
                var requestPath = Path.Combine(logDir, "requests", $"{SafeFileName(gsCode)}.json");
                await File.WriteAllTextAsync(requestPath, PrettyJson(payload), Encoding.UTF8, ct);

                if (options.DryRun)
                {
                    results.Add(new LotteOnUploadResultItem(rowNum, shortName, "DRY_RUN_OK", "", ""));
                    continue;
                }

                using var response = await api.RegisterProductAsync(payload, ct);
                var responsePath = Path.Combine(logDir, "responses", $"{SafeFileName(gsCode)}.json");
                await File.WriteAllTextAsync(responsePath, PrettyJson(response.RootElement), Encoding.UTF8, ct);

                var (ok, spdNo, error) = ParseRegistrationResult(response.RootElement);
                if (ok)
                {
                    history[gsCode] = spdNo;
                    SaveHistory(history);
                    results.Add(new LotteOnUploadResultItem(rowNum, shortName, "OK", spdNo, ""));
                    MarketUploadStateStore.Upsert(gsCode, shortName, "롯데ON", "OK", spdNo, stateImageUrls);
                    Log($"[롯데ON] {gsCode} 등록 완료: {spdNo}");
                }
                else
                {
                    results.Add(new LotteOnUploadResultItem(rowNum, shortName, "FAIL", spdNo, error));
                    MarketUploadStateStore.Upsert(gsCode, shortName, "롯데ON", "FAIL", spdNo, stateImageUrls, error);
                    Log($"[롯데ON] {gsCode} 등록 실패: {error}");
                }
            }
            catch (Exception ex)
            {
                var message = ShortError(ex.Message);
                results.Add(new LotteOnUploadResultItem(rowNum, shortName, "FAIL", "", message));
                MarketUploadStateStore.Upsert(gsCode, shortName, "롯데ON", "FAIL", "", new List<string>(), message);
                Log($"[롯데ON] {gsCode} 오류: {message}");
            }

            await Task.Delay(500, ct);
        }

        await WriteSummaryAsync(logDir, results, ct);

        var successCount = results.Count(r => r.Status is "OK" or "DRY_RUN_OK" or "SKIP_DUP");
        var failCount = results.Count - successCount;
        return new LotteOnUploadResult(results, successCount, failCount, results.Count, logDir);
    }

    private static async Task ValidateIdentityAsync(
        LotteOnApiClient api,
        string logDir,
        Action<string> log,
        CancellationToken ct)
    {
        using var identity = await api.GetIdentityAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(logDir, "preflight_identity.json"), PrettyJson(identity.RootElement), Encoding.UTF8, ct);
        var root = identity.RootElement;
        var returnCode = root.TryGetProperty("returnCode", out var rc) ? rc.GetString() ?? "" : "";
        var data = root.TryGetProperty("data", out var d) ? d : default;
        var trGrpCd = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("trGrpCd", out var tg) ? tg.GetString() ?? "" : "";
        var trNo = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("trNo", out var tn) ? tn.GetString() ?? "" : "";
        var defaults = LoadUploadDefaults();

        if (returnCode != "0000" || !string.Equals(trGrpCd, defaults.TrGrpCd, StringComparison.OrdinalIgnoreCase) || trNo != defaults.TrNo)
            throw new InvalidOperationException($"롯데ON identity 불일치: returnCode={returnCode}, trGrpCd={trGrpCd}, trNo={trNo}");

        SaveUploadDefaultsSnapshot(defaults);
        log($"[롯데ON] identity 확인: {trGrpCd}/{trNo}");
    }

    private static JsonObject BuildRegistrationPayload(Dictionary<string, object?> row)
    {
        var product = BuildProductPayload(row);
        return new JsonObject { ["spdLst"] = new JsonArray(product) };
    }

    private static JsonObject BuildProductPayload(Dictionary<string, object?> row)
    {
        var defaults = LoadUploadDefaults();
        var gsCode = ExtractGsCode(row);
        var productName = Clamp(CleanMarketProductName(GetProductName(row)), 100);
        var supplyName = GetStr(row, "공급사 상품명").OrIfEmpty(productName);
        var salePrice = ResolveSalePrice(row);
        var category = ResolveCategory(row, productName);
        var detailHtml = MarketImageUrlGuard.RemoveUnsafeImageTags(
            GetStr(row, "상품 상세설명").OrIfEmpty(GetStr(row, "상세설명")));
        var images = CollectImageUrls(row);
        if (string.IsNullOrWhiteSpace(detailHtml) && images.Count > 0)
            detailHtml = "<center>" + string.Concat(images.Select(url => $"<img src=\"{EscapeHtml(url)}\">")) + "</center>";
        if (string.IsNullOrWhiteSpace(detailHtml))
            detailHtml = "상세페이지 참조";

        var keywords = BuildKeywords(row, productName, supplyName)
            .Take(5)
            .ToList();

        var options = ParseOptions(GetStr(row, "옵션입력"), GetStr(row, "옵션추가금"));
        if (options.Count == 0)
            options.Add(new OptionItem("기본", 0));

        var mainImage = images.FirstOrDefault()
            ?? ExtractFirstImageFromHtml(detailHtml)
            ?? "";

        var optValues = new JsonArray();
        var itemList = new JsonArray();
        for (var i = 0; i < options.Count; i++)
        {
            var opt = options[i];
            var optVal = string.IsNullOrWhiteSpace(opt.Name) ? $"옵션{i + 1}" : Clamp(EnsureOptionPrefix(opt.Name, i), 50);
            optValues.Add(new JsonObject
            {
                ["optValSeq"] = i + 1,
                ["optVal"] = optVal,
            });

            var item = new JsonObject
            {
                ["eitmNo"] = $"{gsCode}_{i + 1}",
                ["rprtSitmYn"] = i == 0 ? "Y" : "N",
                ["dpYn"] = "Y",
                ["sortSeq"] = i + 1,
                ["itmOptLst"] = new JsonArray(new JsonObject
                {
                    ["optNm"] = "옵션",
                    ["optVal"] = optVal,
                }),
                ["slPrc"] = salePrice + opt.AdditionalPrice,
                ["stkQty"] = DefaultStock,
            };

            if (images.Count > 0)
            {
                item["itmImgLst"] = BuildItemImages(images);
            }

            itemList.Add(item);
        }

        return new JsonObject
        {
            ["trGrpCd"] = defaults.TrGrpCd,
            ["trNo"] = defaults.TrNo,
            ["lrtrNo"] = "",
            ["purTrNo"] = "",
            ["scatNo"] = category.StandardCategoryNo,
            ["dcatLst"] = new JsonArray(new JsonObject
            {
                ["mallCd"] = "LTON",
                ["lfDcatNo"] = category.DisplayCategoryNo,
            }),
            ["epdNo"] = gsCode,
            ["slTypCd"] = "GNRL",
            ["pdTypCd"] = "GNRL_GNRL",
            ["spdNm"] = productName,
            ["mfcrNm"] = "상세페이지 참조",
            ["oplcCd"] = "CN",
            ["mdlNo"] = gsCode,
            ["tdfDvsCd"] = "01",
            ["slStrtDttm"] = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            ["slEndDttm"] = "99991231235959",
            ["pdItmsInfo"] = BuildNotice(category.ProductItemCode, productName, supplyName, gsCode),
            ["impPrxCd"] = "NONE",
            ["purPsbQtyInfo"] = new JsonObject
            {
                ["itmByMinPurYn"] = "N",
                ["itmByMaxPurPsbQtyYn"] = "N",
                ["maxPurLmtTypCd"] = "PERIOD",
            },
            ["ageLmtCd"] = "0",
            ["prstPsbYn"] = "N",
            ["prstPckPsbYn"] = "N",
            ["prstMsgPsbYn"] = "N",
            ["prcCmprEpsrYn"] = "Y",
            ["bookCultCstDdctYn"] = "N",
            ["impDvsCd"] = "NONE",
            ["cshbltyPdYn"] = "N",
            ["ctrtTypCd"] = "A",
            ["pdStatCd"] = "NEW",
            ["dpYn"] = "Y",
            ["ltonDpYn"] = "Y",
            ["scKwdLst"] = new JsonArray(keywords.Select(k => JsonValue.Create(k)).ToArray<JsonNode?>()),
            ["epnLst"] = new JsonArray(new JsonObject
            {
                ["pdEpnTypCd"] = "DSCRP",
                ["cnts"] = detailHtml,
            }),
            ["cnclPsbYn"] = "Y",
            ["dmstOvsDvDvsCd"] = "DMST",
            ["pstkYn"] = "N",
            ["dvProcTypCd"] = "LO_ENTP",
            ["dvPdTypCd"] = "GNRL",
            ["sndBgtNday"] = 3,
            ["dvRgsprGrpCd"] = "GN101",
            ["dvMnsCd"] = "DPCL",
            ["owhpNo"] = defaults.OwhpNo,
            ["hdcCd"] = defaults.HdcCd,
            ["dvCstPolNo"] = defaults.DvCstPolNo,
            ["adtnDvCstPolNo"] = defaults.AdtnDvCstPolNo,
            ["cmbnDvPsbYn"] = "Y",
            ["dvCstStdQty"] = 0,
            ["qckDvUseYn"] = "N",
            ["crdayDvPsbYn"] = "N",
            ["hpDdDvPsbYn"] = "N",
            ["saveTypCd"] = "NONE",
            ["shopCnvMsgPsbYn"] = "N",
            ["rgnLmtPdYn"] = "N",
            ["fprdDvPsbYn"] = "N",
            ["spcfSqncPdYn"] = "N",
            ["rtngPsbYn"] = "Y",
            ["xchgPsbYn"] = "Y",
            ["cmbnRtngPsbYn"] = "Y",
            ["rtngHdcCd"] = defaults.HdcCd,
            ["rtngRtrvPsbYn"] = "Y",
            ["rtrvTypCd"] = "ENTP_RTRV",
            ["rtrpNo"] = defaults.RtrpNo,
            ["stkMgtYn"] = "Y",
            ["sitmYn"] = "Y",
            ["optSrtLst"] = new JsonArray(new JsonObject
            {
                ["optSeq"] = 1,
                ["optNm"] = "옵션",
                ["optValSrtLst"] = optValues,
            }),
            ["itmLst"] = itemList,
            ["adtnPdYn"] = "N",
        };
    }

    private static JsonObject BuildNotice(string pdItmsCd, string productName, string supplyName, string gsCode)
    {
        var asInfo = Environment.GetEnvironmentVariable("LOTTEON_AS_INFO");
        if (string.IsNullOrWhiteSpace(asInfo))
            asInfo = "홈런마켓 010-2324-8352";

        JsonObject Article(string code, string value) => new()
        {
            ["pdArtlCd"] = code,
            ["pdArtlCnts"] = value,
        };

        var articles = new JsonArray();
        if (pdItmsCd == "04")
        {
            articles.Add(Article("0130", "장갑"));
            articles.Add(Article("0120", "폴리에스터"));
            articles.Add(Article("0030", "상세페이지 참조"));
            articles.Add(Article("0070", "제조자 상세페이지 참조 / 수입자 홈런마켓"));
            articles.Add(Article("0060", "중국"));
            articles.Add(Article("0110", "화기 및 날카로운 물체 주의, 용도 외 사용 금지"));
            articles.Add(Article("0080", "관련 법 및 소비자분쟁해결기준에 따름"));
            articles.Add(Article("0090", asInfo));
        }
        else
        {
            articles.Add(Article("0210", $"{productName} / {supplyName.OrIfEmpty(gsCode)}"));
            articles.Add(Article("1400", "해당 없음"));
            articles.Add(Article("1420", "중국"));
            articles.Add(Article("0070", "제조자 상세페이지 참조 / 수입자 홈런마켓"));
            articles.Add(Article("1440", asInfo));
        }

        return new JsonObject
        {
            ["pdItmsCd"] = pdItmsCd,
            ["pdItmsArtlLst"] = articles,
        };
    }

    private static LotteOnCategory ResolveCategory(IReadOnlyDictionary<string, object?> row, string productName)
    {
        var standard = FirstNonEmpty(row,
            "롯데ON표준카테고리코드",
            "롯데ON표준카테고리",
            "표준카테고리코드");
        var display = FirstNonEmpty(row,
            "롯데ON전시카테고리코드",
            "롯데ON전시카테고리",
            "전시카테고리코드");
        var itemCode = FirstNonEmpty(row,
            "롯데ON상품품목코드",
            "롯데ON품목코드",
            "pdItmsCd",
            "상품품목코드");

        var explicitCategory = TryCompleteCategory(
            CleanCategoryCode(standard),
            CleanCategoryCode(display),
            CleanCategoryCode(itemCode),
            productName,
            allowUnverifiedPair: false);
        if (explicitCategory is not null)
            return explicitCategory;

        var compact = Regex.Replace(productName, @"\s+", "");
        if (Regex.IsMatch(compact, "요가양말|필라테스양말|토삭스|발가락양말|논슬립양말|운동양말", RegexOptions.IgnoreCase))
            return new LotteOnCategory("BC41081502", "FC08120400", "38");
        if (Regex.IsMatch(compact, "강아지발도장|반려견발도장|발도장키트|발자국키트|펫발도장|펫기념", RegexOptions.IgnoreCase))
            return new LotteOnCategory("BC10080200", "FC19040401", "38");
        if (Regex.IsMatch(compact, "강아지발캡|반려견발캡|발바닥커버|펫발커버|산책용.*발", RegexOptions.IgnoreCase))
            return new LotteOnCategory("BC10080200", "FC19040401", "38");
        if (Regex.IsMatch(compact, "물총|워터건|물놀이장난감", RegexOptions.IgnoreCase))
            return new LotteOnCategory("BC55040500", "FC03040405", "38");
        if (Regex.IsMatch(compact, "안경클립조명|LED.*안경|돋보기.*조명|정밀작업.*라이트", RegexOptions.IgnoreCase))
            return new LotteOnCategory("BC66120200", "FC08071202", "38");
        if (Regex.IsMatch(compact, "책상후크|테이블후크|가방걸이|정리행거", RegexOptions.IgnoreCase))
            return new LotteOnCategory("BC37101000", "FC02061010", "38");
        if (Regex.IsMatch(compact, "손목폰가방|휴대폰파우치|러닝.*포켓", RegexOptions.IgnoreCase))
            return new LotteOnCategory("BC04071100", "FC17101800", "38");
        if (Regex.IsMatch(compact, "장갑|글러브|반장갑|작업장갑|코팅장갑", RegexOptions.IgnoreCase))
            return new LotteOnCategory("BC10080200", "FC19040401", "38");
        if (Regex.IsMatch(compact, "너트|아이너트|육각너트|와셔너트", RegexOptions.IgnoreCase))
            return new LotteOnCategory("BC10080200", "FC19040401", "38");
        if (Regex.IsMatch(compact, "배관|파이프|홀캡|홀커버|구멍마개|새들|클램프|수도|수전|가스켓|패킹|와셔", RegexOptions.IgnoreCase))
            return new LotteOnCategory("BC10080200", "FC19040401", "38");
        if (Regex.IsMatch(compact, "부싱|전선|배선|케이블", RegexOptions.IgnoreCase))
            return new LotteOnCategory("BC10080200", "FC19040401", "38");
        if (Regex.IsMatch(compact, "택끈|라벨끈|상품택|포장끈|리본|끈", RegexOptions.IgnoreCase))
            return new LotteOnCategory("BC10080200", "FC19040401", "38");
        if (Regex.IsMatch(compact, "나사|스크류|드릴링|볼트|피스|앙카|고정핀|스위치고정핀|철물|브라켓|클립|후크|홀더|스토퍼|고정부속|부속|부품", RegexOptions.IgnoreCase))
            return new LotteOnCategory("BC10080200", "FC19040401", "38");
        if (Regex.IsMatch(compact, "깔때기|공병|컵홀더|빨판|큐방|손목밴드|식별밴드|휴지|걸이봉|USB|보호캡|컬러캡|노브|손잡이|캐리어|마이크|스폰지|액자|프레임|후드끈|스트링|비오|압정|장식핀|벽고정|지지대", RegexOptions.IgnoreCase))
            return new LotteOnCategory("BC10080200", "FC19040401", "38");

        throw new InvalidOperationException($"롯데ON 카테고리 매칭 실패: {productName}. 장갑 fallback 업로드를 중단했습니다.");
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

    private static LotteOnCategory? TryCompleteCategory(
        string standard,
        string display,
        string itemCode,
        string productName,
        bool allowUnverifiedPair)
    {
        if (string.IsNullOrWhiteSpace(standard) && string.IsNullOrWhiteSpace(display))
            return null;

        var mapped = TryGetVerifiedCategoryPair(standard, display);
        if (mapped is not null)
        {
            standard = mapped.StandardCategoryNo;
            display = mapped.DisplayCategoryNo;
        }
        else if (!allowUnverifiedPair)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(standard))
        {
            standard = display switch
            {
                "FC19040401" => "BC10080200",
                "FC08120400" => "BC41081502",
                "FC03040405" => "BC55040500",
                "FC08071202" => "BC66120200",
                "FC02061010" => "BC37101000",
                "FC17101800" => "BC04071100",
                _ => "",
            };
        }

        if (string.IsNullOrWhiteSpace(display))
        {
            display = standard switch
            {
                "BC10080200" => "FC19040401",
                "BC41081502" => "FC08120400",
                "BC55040500" => "FC03040405",
                "BC66120200" => "FC08071202",
                "BC37101000" => "FC02061010",
                "BC04071100" => "FC17101800",
                _ => "",
            };
        }

        if (string.IsNullOrWhiteSpace(standard) || string.IsNullOrWhiteSpace(display))
            return null;

        if (string.IsNullOrWhiteSpace(itemCode))
            itemCode = IsGloveProduct(productName, standard, display) ? "04" : "38";

        return new LotteOnCategory(standard, display, itemCode);
    }

    private static LotteOnCategory? TryGetVerifiedCategoryPair(string standard, string display)
    {
        var verified = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BC10080200"] = "FC19040401",
            ["BC04071100"] = "FC17101800",
            ["BC37101000"] = "FC02061010",
            ["BC41081502"] = "FC08120400",
            ["BC55040500"] = "FC03040405",
            ["BC66120200"] = "FC08071202",
        };

        foreach (var pair in LoadVerifiedCategoryMap())
            verified[pair.Key] = pair.Value;

        if (!string.IsNullOrWhiteSpace(standard) && verified.TryGetValue(standard, out var verifiedDisplay))
            return new LotteOnCategory(standard, verifiedDisplay, "38");

        if (!string.IsNullOrWhiteSpace(display))
        {
            foreach (var pair in verified)
            {
                if (string.Equals(pair.Value, display, StringComparison.OrdinalIgnoreCase))
                    return new LotteOnCategory(pair.Key, pair.Value, "38");
            }
        }

        return null;
    }

    private static Dictionary<string, string> LoadVerifiedCategoryMap()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var paths = new[]
        {
            DesktopKeyStore.GetPath("lotteon_category_map.json"),
            Path.Combine(AppContext.BaseDirectory, "lotteon_category_map.json"),
        };

        foreach (var path in paths)
        {
            if (!File.Exists(path))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in root.EnumerateObject())
                    {
                        var display = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
                        if (IsLotteStandardCode(prop.Name) && IsLotteDisplayCode(display))
                            result[prop.Name.Trim()] = display!.Trim();
                    }
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        var s = item.TryGetProperty("standard", out var st) ? st.GetString()
                            : item.TryGetProperty("scatNo", out var scat) ? scat.GetString()
                            : "";
                        var d = item.TryGetProperty("display", out var di) ? di.GetString()
                            : item.TryGetProperty("lfDcatNo", out var lf) ? lf.GetString()
                            : "";
                        if (IsLotteStandardCode(s) && IsLotteDisplayCode(d))
                            result[s!.Trim()] = d!.Trim();
                    }
                }
            }
            catch
            {
                // Optional cache only. Built-in verified pairs remain available.
            }
        }

        return result;
    }

    private static bool IsLotteStandardCode(string? value)
        => Regex.IsMatch(value ?? "", @"^BC\d{8}$", RegexOptions.IgnoreCase);

    private static bool IsLotteDisplayCode(string? value)
        => Regex.IsMatch(value ?? "", @"^[EF]C\d{8}$", RegexOptions.IgnoreCase);

    private static bool IsGloveProduct(string productName, string standard, string display)
        => standard == "BC99110100"
           || display == "FC18110600"
           || Regex.IsMatch(productName ?? "", "장갑|글러브|반장갑|작업장갑|코팅장갑", RegexOptions.IgnoreCase);

    private static (bool Ok, string SpdNo, string Error) ParseRegistrationResult(JsonElement root)
    {
        var returnCode = root.TryGetProperty("returnCode", out var rc) ? rc.GetString() ?? "" : "";
        if (returnCode != "0000")
        {
            var message = root.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "";
            return (false, "", $"returnCode={returnCode} {message}");
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
        {
            var item = data[0];
            var resultCode = item.TryGetProperty("resultCode", out var r) ? r.GetString() ?? "" : "";
            var spdNo = item.TryGetProperty("spdNo", out var s) ? s.GetString() ?? "" : "";
            if (resultCode == "0000") return (true, spdNo, "");

            var resultMessage = item.TryGetProperty("resultMessage", out var rm) ? rm.GetString() ?? "" : "";
            return (false, spdNo, $"resultCode={resultCode} {resultMessage}");
        }

        return (false, "", "등록 응답 data 없음");
    }

    private static List<Dictionary<string, object?>> FilterRows(List<Dictionary<string, object?>> rows, LotteOnUploadOptions options)
    {
        IEnumerable<Dictionary<string, object?>> query = rows;
        if (options.RowStart > 0)
        {
            var end = options.RowEnd > 0 ? options.RowEnd : options.RowStart;
            query = query.Where(row =>
            {
                var rowNum = (int)row["_row_num"]! - 1;
                return rowNum >= options.RowStart && rowNum <= end;
            });
        }

        if (options.AllowedGsCodes is { Count: > 0 } allowed)
        {
            query = query.Where(row =>
            {
                var gsCode = ExtractGsCode(row);
                return !string.IsNullOrWhiteSpace(gsCode) && allowed.Contains(gsCode);
            });
        }

        return query.ToList();
    }

    private static List<Dictionary<string, object?>> ReadSourceFile(string filePath)
    {
        var rows = ExcelSourceReader.ReadSourceRows(filePath, "분리추출후", "A마켓");
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

        var categoryByGs = new Dictionary<string, (string Standard, string Display)>(StringComparer.OrdinalIgnoreCase);
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

                var gsCode = FirstNonEmptyString(values, "상품코드", "자체 상품코드", "GS코드");
                var standard = ExtractCategoryCode(FirstNonEmptyString(values, "롯데ON표준카테고리코드", "롯데ON표준카테고리", "롯데ON표준카테고리코드/경로"));
                var display = ExtractCategoryCode(FirstNonEmptyString(values, "롯데ON전시카테고리코드", "롯데ON전시카테고리", "롯데ON전시카테고리코드/경로"));
                var normalizedGs = NormalizeGsCode(gsCode);
                if (!string.IsNullOrWhiteSpace(normalizedGs)
                    && (!string.IsNullOrWhiteSpace(standard) || !string.IsNullOrWhiteSpace(display))
                    && !categoryByGs.ContainsKey(normalizedGs))
                {
                    categoryByGs[normalizedGs] = (standard, display);
                }
            }
        }

        foreach (var row in rows)
        {
            var gsCode = NormalizeGsCode(ExtractGsCode(row));
            if (!categoryByGs.TryGetValue(gsCode, out var category))
                continue;

            if (!string.IsNullOrWhiteSpace(category.Standard))
                row["롯데ON표준카테고리코드"] = category.Standard;
            if (!string.IsNullOrWhiteSpace(category.Display))
                row["롯데ON전시카테고리코드"] = category.Display;
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
        => ExcelSourceReader.NormalizeGsCode(value);

    private static string FirstNonEmptyString(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return "";
    }

    private static string ResolveExportRoot(string sourceFilePath)
    {
        return ExcelSourceReader.ResolveExportRoot(sourceFilePath);
    }

    private static string? ResolveDefaultHomeCafe24TokenPath()
    {
        var path = DesktopKeyStore.GetPath("cafe24_token_rkghrud1.json");
        return File.Exists(path) ? path : null;
    }

    private static string CreateLogDirectory(string sourcePath)
    {
        var root = ResolveExportRoot(sourcePath);
        var dir = Path.Combine(root, "logs", "lotteon_upload", DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task WriteSummaryAsync(string logDir, IReadOnlyList<LotteOnUploadResultItem> results, CancellationToken ct)
    {
        var csv = new StringBuilder();
        csv.AppendLine("row,name,status,spdNo,error");
        foreach (var item in results)
        {
            csv.AppendLine(string.Join(",", Csv(item.Row.ToString(CultureInfo.InvariantCulture)), Csv(item.Name), Csv(item.Status), Csv(item.SpdNo), Csv(item.Error)));
        }
        await File.WriteAllTextAsync(Path.Combine(logDir, "summary.csv"), csv.ToString(), Encoding.UTF8, ct);
        await File.WriteAllTextAsync(Path.Combine(logDir, "summary.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, ct);
    }

    private static Dictionary<string, string> LoadHistory()
    {
        var path = DesktopKeyStore.GetPath("lotteon_upload_history.json");
        if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8))
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveHistory(Dictionary<string, string> history)
    {
        Directory.CreateDirectory(DesktopKeyStore.DirectoryPath);
        File.WriteAllText(DesktopKeyStore.GetPath("lotteon_upload_history.json"),
            JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    private static async Task SupplementImagesViaNaverCdnAsync(
        Dictionary<string, object?> row,
        Action<string> log,
        CancellationToken ct)
    {
        var exportRoot = GetStr(row, "_export_root");
        var gsCode = ExtractGsCode(row);
        var (sourceImages, sourceLabel, alreadyUploadReady) = ResolveImageSources(row, gsCode, exportRoot);
        if (alreadyUploadReady)
        {
            row["_cafe24_image_urls"] = sourceImages;
            log($"[롯데ON] {gsCode} {sourceLabel} 이미지 URL 반영: {sourceImages.Count}장");
            return;
        }
        if (sourceImages.Count == 0)
            return;

        try
        {
            using var naverApi = NaverCommerceApiClient.FromKeyFile();
            var uploaded = new List<string>();
            foreach (var imagePath in sourceImages.Take(9))
            {
                try
                {
                    uploaded.Add(await naverApi.UploadImageAsync(imagePath, ct));
                    await Task.Delay(250, ct);
                }
                catch (Exception ex)
                {
                    log($"[롯데ON] {gsCode} 가공이미지 업로드 실패: {ShortImageLabel(imagePath)} | {ShortError(ex.Message)}");
                }
            }

            if (uploaded.Count == 0)
                return;

            var fallback = CollectImageUrls(row);
            row["_cafe24_image_urls"] = MergeImages(uploaded, fallback);
            log($"[롯데ON] {gsCode} {sourceLabel} URL 반영: 보충 {uploaded.Count}장");
        }
        catch (Exception ex)
        {
            log($"[롯데ON] {gsCode} 가공이미지 URL 변환 생략: {ShortError(ex.Message)}");
        }
    }

    private static (List<string> SourceImages, string SourceLabel, bool AlreadyUploadReady) ResolveImageSources(
        Dictionary<string, object?> row,
        string gsCode,
        string exportRoot)
    {
        var selectedImages = CollectSelectedImageSources(row, exportRoot);
        if (selectedImages.Count > 0)
            return (selectedImages, "selected_images", false);

        if (!string.IsNullOrWhiteSpace(exportRoot) && !string.IsNullOrWhiteSpace(gsCode))
        {
            var listingImages = FindListingImages(exportRoot, gsCode).Take(9).ToList();
            if (listingImages.Count > 0)
                return (listingImages, "listing_images", false);
        }

        var cafe24Images = GetCafe24ImageUrls(row);
        if (cafe24Images.Count > 0)
            return (cafe24Images, "Cafe24", true);

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

    private static List<string> FindListingImages(string exportRoot, string gsCode)
    {
        var gsBase = Regex.Replace(gsCode.Trim(), @"[A-Z]$", "", RegexOptions.IgnoreCase);
        ImageSelection? selection = null;
        var selectionsPath = Path.Combine(exportRoot, "image_selections.json");
        if (File.Exists(selectionsPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(selectionsPath));
                var gs9 = gsBase.Length >= 9 ? gsBase[..9] : gsBase;
                if (doc.RootElement.TryGetProperty(gs9, out var sel))
                {
                    int? mainIdx = sel.TryGetProperty("main", out var main) && main.ValueKind == JsonValueKind.Number
                        ? main.GetInt32()
                        : null;
                    int? mainIdxB = sel.TryGetProperty("mainB", out var mainB) && mainB.ValueKind == JsonValueKind.Number
                        ? mainB.GetInt32()
                        : null;
                    var addIndices = new List<int>();
                    if (sel.TryGetProperty("additional", out var addArr) && addArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var add in addArr.EnumerateArray())
                        {
                            if (add.ValueKind == JsonValueKind.Number)
                                addIndices.Add(add.GetInt32());
                        }
                    }
                    selection = new ImageSelection(mainIdx, addIndices, mainIdxB);
                }
            }
            catch
            {
                selection = null;
            }
        }

        var listingRoot = Path.Combine(exportRoot, "listing_images");
        if (!Directory.Exists(listingRoot))
            return new List<string>();

        var searchDirs = new List<string> { listingRoot };
        try
        {
            searchDirs.AddRange(Directory.GetDirectories(listingRoot).OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase));
        }
        catch { }

        foreach (var dir in searchDirs)
        {
            var gsFolder = Path.Combine(dir, gsBase);
            if (!Directory.Exists(gsFolder))
                gsFolder = Path.Combine(dir, gsCode);
            if (!Directory.Exists(gsFolder))
                continue;

            var allFiles = Directory.GetFiles(gsFolder)
                .Where(f => Regex.IsMatch(f, @"\.(jpg|jpeg|png|bmp|webp)$", RegexOptions.IgnoreCase))
                .OrderBy(f => f)
                .ToList();
            if (allFiles.Count == 0)
                continue;

            if (selection?.MainIndex is not null)
            {
                var (mainPath, addPaths) = Cafe24UploadSupport.PickImagesBySelection(gsFolder, selection);
                if (mainPath is not null)
                {
                    var result = new List<string> { mainPath };
                    result.AddRange(addPaths);
                    if (addPaths.Count == 0)
                    {
                        result.AddRange(allFiles.Where(path => !string.Equals(path, mainPath, StringComparison.OrdinalIgnoreCase)));
                    }
                    return result;
                }
            }

            return allFiles;
        }

        return new List<string>();
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
                if (!MarketImageUrlGuard.IsAllowedUploadUrl(clean))
                    continue;
                if (seen.Add(clean))
                    result.Add(clean);
                if (result.Count >= 9)
                    return result;
            }
        }
        return result;
    }

    private static List<string> GetCafe24ImageUrls(Dictionary<string, object?> row)
    {
        if (!row.TryGetValue("_cafe24_image_urls", out var value) || value is not IEnumerable<string> urls)
            return new List<string>();

        return urls
            .Select(url => url?.Trim() ?? string.Empty)
            .Where(MarketImageUrlGuard.IsAllowedUploadUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(9)
            .ToList();
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

    private static string ShortImageLabel(string imageSource)
    {
        if (string.IsNullOrWhiteSpace(imageSource))
            return "(빈 이미지)";
        if (File.Exists(imageSource))
            return Path.GetFileName(imageSource);
        return imageSource.Length > 80 ? imageSource[..80] + "..." : imageSource;
    }

    private static List<OptionItem> ParseOptions(string optionStr, string extraPriceStr)
    {
        var prices = new List<int>();
        if (!string.IsNullOrWhiteSpace(extraPriceStr))
        {
            foreach (var raw in Regex.Split(extraPriceStr, @"[,|]"))
            {
                if (double.TryParse(raw.Trim(), out var parsed))
                    prices.Add((int)parsed);
            }
        }

        var values = new List<string>();
        foreach (Match match in Regex.Matches(optionStr ?? "", @"([A-Z])\s+([^,}|]+)", RegexOptions.IgnoreCase))
            values.Add(EnsureOptionPrefix($"{match.Groups[1].Value.ToUpperInvariant()} {match.Groups[2].Value.Trim()}", values.Count));

        if (values.Count == 0)
        {
            var body = optionStr ?? "";
            var brace = Regex.Match(body, @"\{(.+?)\}");
            if (brace.Success) body = brace.Groups[1].Value;
            foreach (var part in body.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var value = EnsureOptionPrefix(part, values.Count);
                if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
            }
        }

        return values
            .Select((name, index) => new OptionItem(EnsureOptionPrefix(name, index), index < prices.Count ? prices[index] : 0))
            .ToList();
    }

    private static JsonArray BuildItemImages(IReadOnlyList<string> images)
    {
        var imageNodes = new JsonArray();
        for (var index = 0; index < images.Count && index < 9; index++)
        {
            imageNodes.Add(new JsonObject
            {
                ["epsrTypCd"] = "IMG",
                ["epsrTypDtlCd"] = "IMG_SQRE",
                ["origImgFileNm"] = images[index],
                ["rprtImgYn"] = index == 0 ? "Y" : "N",
            });
        }
        return imageNodes;
    }

    private static List<string> CollectImageUrls(Dictionary<string, object?> row)
    {
        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (row.TryGetValue("_cafe24_image_urls", out var value) && value is IEnumerable<string> cafe24Urls)
        {
            foreach (var url in cafe24Urls)
                AddUrl(url);
        }

        foreach (var column in new[] { "이미지등록(목록)", "이미지등록(추가)", "이미지등록(상세)" })
        {
            foreach (var raw in Regex.Split(GetStr(row, column), @"[|\n]"))
                AddUrl(raw);
        }

        foreach (Match match in Regex.Matches(GetStr(row, "상품 상세설명").OrIfEmpty(GetStr(row, "상세설명")), "<img[^>]+src=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase))
            AddUrl(match.Groups[1].Value);

        return urls.Take(9).ToList();

        void AddUrl(string? raw)
        {
            var url = (raw ?? "").Trim();
            if (!MarketImageUrlGuard.IsAllowedUploadUrl(url))
                return;

            if (seen.Add(url)) urls.Add(url);
        }
    }

    private static string? ExtractFirstImageFromHtml(string html)
    {
        foreach (Match match in Regex.Matches(html ?? "", "<img[^>]+src=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase))
        {
            var url = match.Groups[1].Value.Trim();
            if (MarketImageUrlGuard.IsAllowedUploadUrl(url))
                return url;
        }
        return null;
    }

    private static List<string> BuildKeywords(IReadOnlyDictionary<string, object?> row, string productName, string supplyName)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in new[]
                 {
                     GetStr(row, "홈런_롯데ON검색키워드"),
                     GetStr(row, "롯데ON검색키워드"),
                     GetStr(row, "홈런_공통마켓검색키워드"),
                     GetStr(row, "공통마켓검색키워드"),
                     GetStr(row, "검색키워드"),
                     GetStr(row, "검색어설정"),
                     productName,
                     supplyName,
                 })
        {
            foreach (var keyword in ParseKeywords(source))
            {
                if (seen.Add(keyword))
                    result.Add(keyword);
                if (result.Count >= 5)
                    return result;
            }
        }

        return result;
    }

    private static List<string> ParseKeywords(string raw)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleanedRaw = Regex.Replace(raw ?? "", @"GS\d{7}[A-Z0-9]*", " ", RegexOptions.IgnoreCase);
        foreach (var part in Regex.Split(cleanedRaw, @"[|,\n;/]+|\s+"))
        {
            var keyword = Regex.Replace(part.Trim(), @"[^0-9A-Za-z가-힣]+", " ");
            keyword = Regex.Replace(keyword, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(keyword)) continue;
            if (keyword.Length > 30) keyword = keyword[..30];
            if (seen.Add(keyword)) result.Add(keyword);
        }

        return result;
    }

    private static string FirstNonEmpty(IReadOnlyDictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetStr(row, key);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return "";
    }

    private static string CleanCategoryCode(string value)
    {
        value = Regex.Replace(value ?? "", @"\s+", "").Trim();
        if (value.EndsWith(".0", StringComparison.Ordinal))
            value = value[..^2];
        return value;
    }

    private static string ExtractGsCode(IReadOnlyDictionary<string, object?> row)
        => ExcelSourceReader.ExtractGsCodeFromRow(row);

    private static string GetProductName(IReadOnlyDictionary<string, object?> row)
        => GetStr(row, "홈런_롯데ON상품명")
            .OrIfEmpty(GetStr(row, "롯데ON상품명"))
            .OrIfEmpty(GetStr(row, "상품명"))
            .OrIfEmpty(GetStr(row, "최종키워드2차"))
            .OrIfEmpty(GetStr(row, "1차키워드"));

    private static string CleanMarketProductName(string value)
    {
        var original = (value ?? "").Trim();
        var cleaned = Regex.Replace(original, @"\bGS\d{7}[A-Z0-9]*\b", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '-', '_', '/', '|');
        return string.IsNullOrWhiteSpace(cleaned) ? original : cleaned;
    }

    private static bool HasDirectMarketProductName(IReadOnlyDictionary<string, object?> row, params string[] keys)
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

    private static string GetStr(IReadOnlyDictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null ? value.ToString()?.Trim() ?? "" : "";

    private static int GetInt(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null) return 0;
        if (value is double d) return (int)d;
        if (int.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var i)) return i;
        if (double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d2)) return (int)d2;
        return 0;
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

    private static string Csv(string value)
        => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    private static string EscapeHtml(string value)
        => (value ?? "").Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Clamp(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static string ShortError(string message)
        => string.IsNullOrWhiteSpace(message) ? "알 수 없는 오류" : message.Length > 200 ? message[..200] : message;

    private sealed record LotteOnCategory(string StandardCategoryNo, string DisplayCategoryNo, string ProductItemCode);

    private sealed record OptionItem(string Name, int AdditionalPrice);
}
