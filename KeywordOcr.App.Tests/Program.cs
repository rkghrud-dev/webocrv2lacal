using System.Reflection;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KeywordOcr.App.Services;

if (args.Any(arg => string.Equals(arg, "--validate-market-excel", StringComparison.OrdinalIgnoreCase)))
{
    RunMarketExcelValidation(args);
    return;
}

if (args.Any(arg => string.Equals(arg, "--export-market-excel", StringComparison.OrdinalIgnoreCase)))
{
    RunMarketExcelExport(args);
    return;
}

if (args.Any(arg => string.Equals(arg, "--direct-market-upload", StringComparison.OrdinalIgnoreCase)))
{
    await RunDirectMarketUploadAsync(args);
    return;
}

var packageOnly = args.Any(arg => string.Equals(arg, "--workspace-package", StringComparison.OrdinalIgnoreCase));
if (!packageOnly)
{
    TestPrefersSizeAttributeOverQuantity();
    TestQuantityOptionUsesNumericValueAndUnit();
    TestCoupangSpecificProductNameWins();
    TestCoupangSearchTagsKeepSpaces();
    TestCoupangOptionItemNamesKeepAlphabetPrefix();
    TestMarketImageGuardNormalizesMalformedImgTags();
    TestNaverFallbackImagesIncludeAdditionalColumn();
    TestNaverSelectedDataImagePathIsResolved();
    TestNaverLatestListingImageFolderWins();
    TestCoupangListingImagesWinOverCafe24Urls();
    TestCoupangSelectedImageColumnsWinOverListingFallback();
    TestCoupangLatestListingImageFolderWins();
    TestLotteOnListingImagesWinOverCafe24Urls();
    TestLotteOnSelectedImageColumnsWinOverListingFallback();
    TestLotteOnLatestListingImageFolderWins();
}
TestWorkspacePackageRoundTrip();
TestWorkspacePackageRejectsUnsafeEntryPath();
TestWorkspaceWorkbookEditRoundTrip();
Console.WriteLine("PASS");

static async Task RunDirectMarketUploadAsync(string[] args)
{
    var file = GetArgValue(args, "--file");
    var gs = GetArgValue(args, "--gs");
    var rowArg = GetArgValue(args, "--row");
    var search = GetArgValue(args, "--search");
    var runNaver = args.Any(arg => string.Equals(arg, "--naver", StringComparison.OrdinalIgnoreCase));
    var runLotteOn = args.Any(arg => string.Equals(arg, "--lotteon", StringComparison.OrdinalIgnoreCase));
    var runCoupang = args.Any(arg => string.Equals(arg, "--coupang", StringComparison.OrdinalIgnoreCase));
    var dryRun = args.Any(arg => string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase));
    var force = args.Any(arg => string.Equals(arg, "--force", StringComparison.OrdinalIgnoreCase));

    if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        throw new FileNotFoundException("업로드 엑셀을 찾지 못했습니다.", file);
    if (!runCoupang && string.IsNullOrWhiteSpace(gs))
        throw new ArgumentException("--gs 값이 필요합니다.");
    if (!runNaver && !runLotteOn && !runCoupang)
        throw new ArgumentException("--naver, --lotteon, --coupang 중 하나 이상이 필요합니다.");

    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { gs.Trim() };
    var progress = new Progress<string>(Console.WriteLine);

    if (runNaver)
    {
        Console.WriteLine($"[네이버] {(dryRun ? "dry-run" : "실제 업로드")} 시작: {Path.GetFileName(file)} / {gs}");
        var result = await new NaverUploadService().UploadAsync(file, new NaverUploadOptions
        {
            DryRun = dryRun,
            AllowedGsCodes = allowed,
        }, progress, CancellationToken.None);
        Console.WriteLine($"[네이버] 결과: 성공 {result.SuccessCount} / 실패 {result.FailCount} / 전체 {result.TotalCount}");
        Console.WriteLine($"[네이버] 로그: {result.LogDirectory}");
        foreach (var item in result.Items)
            Console.WriteLine($"[네이버] row={item.Row} status={item.Status} id={item.ProductId} error={item.Error}");
    }

    if (runLotteOn)
    {
        Console.WriteLine($"[롯데ON] {(dryRun ? "dry-run" : "실제 업로드")} 시작: {Path.GetFileName(file)} / {gs}");
        var result = await new LotteOnUploadService().UploadAsync(file, new LotteOnUploadOptions
        {
            DryRun = dryRun,
            Force = force,
            AllowedGsCodes = allowed,
        }, progress, CancellationToken.None);
        Console.WriteLine($"[롯데ON] 결과: 성공 {result.SuccessCount} / 실패 {result.FailCount} / 전체 {result.TotalCount}");
        Console.WriteLine($"[롯데ON] 로그: {result.LogDirectory}");
        foreach (var item in result.Items)
            Console.WriteLine($"[롯데ON] row={item.Row} status={item.Status} spdNo={item.SpdNo} error={item.Error}");
    }

    if (runCoupang)
    {
        var allowedRows = ResolveCoupangRows(file, rowArg, gs, search);
        Console.WriteLine($"[쿠팡] {(dryRun ? "dry-run" : "실제 업로드")} 시작: {Path.GetFileName(file)} / rows={string.Join(",", allowedRows)}");
        var result = await new CoupangUploadService().UploadAsync(file, new CoupangUploadOptions
        {
            DryRun = dryRun,
        }, progress, CancellationToken.None, allowedRows);
        Console.WriteLine($"[쿠팡] 결과: 성공 {result.SuccessCount} / 실패 {result.FailCount} / 전체 {result.TotalCount}");
        foreach (var item in result.Items)
            Console.WriteLine($"[쿠팡] row={item.Row} status={item.Status} id={item.SellerProductId} category={item.Category} error={item.Error}");
    }
}

static void RunMarketExcelValidation(string[] args)
{
    var source = GetArgValue(args, "--source");
    var gs = GetArgValue(args, "--gs");
    var loopsArg = GetArgValue(args, "--loops");
    var projectRoot = GetArgValue(args, "--project-root");
    if (string.IsNullOrWhiteSpace(projectRoot))
        projectRoot = Directory.GetCurrentDirectory();

    if (!File.Exists(source))
        throw new FileNotFoundException("검증할 V5 업로드 엑셀을 찾지 못했습니다.", source);
    if (string.IsNullOrWhiteSpace(gs))
        throw new ArgumentException("--gs 값이 필요합니다.");

    var loops = int.TryParse(loopsArg, out var parsedLoops) && parsedLoops > 0 ? parsedLoops : 10;
    var service = new MarketExcelExportService(projectRoot);
    var progress = new Progress<string>(msg => Console.WriteLine("[검증] " + msg));
    MarketExcelExportResult? last = null;

    for (var i = 1; i <= loops; i++)
    {
        Console.WriteLine($"[검증] {i}/{loops} 마켓 엑셀 생성");
        last = service.Export(source, new[] { gs }, exportElevenst: true, exportEsm: true, progress);
        ValidateMarketExcelResult(last, gs);
    }

    Console.WriteLine($"MARKET_EXCEL_VALIDATION_PASS loops={loops} output={last?.OutputDirectory}");
}

static void RunMarketExcelExport(string[] args)
{
    var source = GetArgValue(args, "--source");
    var gsArg = GetArgValue(args, "--gs");
    var projectRoot = GetArgValue(args, "--project-root");
    if (string.IsNullOrWhiteSpace(projectRoot))
        projectRoot = Directory.GetCurrentDirectory();
    if (!File.Exists(source))
        throw new FileNotFoundException("마켓 엑셀 소스 파일을 찾지 못했습니다.", source);

    var selectedGs = string.Equals(gsArg, "all", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(gsArg)
        ? ReadGsCodesFromSource(source)
        : gsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    if (selectedGs.Count == 0)
        throw new InvalidOperationException("마켓 엑셀로 만들 GS코드를 찾지 못했습니다.");

    var service = new MarketExcelExportService(projectRoot);
    var progress = new Progress<string>(msg => Console.WriteLine("[생성] " + msg));
    var result = service.Export(source, selectedGs, exportElevenst: true, exportEsm: true, progress);
    foreach (var file in result.Files)
        Console.WriteLine($"MARKET_EXCEL_FILE {file.Market} {file.Path}");
    Console.WriteLine($"MARKET_EXCEL_EXPORT_DONE products={result.ProductCount} warnings={result.WarningCount} output={result.OutputDirectory}");
}

static List<string> ReadGsCodesFromSource(string source)
{
    using var workbook = new XLWorkbook(source);
    var sheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name.Trim() == "분리추출후")
                ?? workbook.Worksheets.First();
    var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var lastColumn = sheet.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0;
    for (var col = 1; col <= lastColumn; col++)
    {
        var header = sheet.Cell(1, col).GetFormattedString().Trim();
        if (!string.IsNullOrWhiteSpace(header) && !headers.ContainsKey(header))
            headers[header] = col;
    }

    var codeColumns = new[] { "자체 상품코드", "상품코드", "상품명" }
        .Where(headers.ContainsKey)
        .Select(name => headers[name])
        .ToList();
    var result = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
    for (var row = 2; row <= lastRow; row++)
    {
        foreach (var col in codeColumns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                sheet.Cell(row, col).GetFormattedString(),
                @"GS\d{7}[A-Z0-9]*",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success)
                continue;
            var code = match.Value.ToUpperInvariant();
            if (seen.Add(code))
                result.Add(code);
            break;
        }
    }

    return result;
}

static void ValidateMarketExcelResult(MarketExcelExportResult result, string gs)
{
    var elevenst = result.Files.FirstOrDefault(file => file.Market == "11번가").Path
        ?? throw new InvalidOperationException("11번가 엑셀 생성 파일이 없습니다.");
    var esm = result.Files.FirstOrDefault(file => file.Market == "ESM").Path
        ?? throw new InvalidOperationException("ESM 엑셀 생성 파일이 없습니다.");

    var elevenMain = ReadExcelTextViaCom(elevenst, 1, 4, 8);
    var elevenAdditional1 = ReadExcelTextViaCom(elevenst, 1, 4, 9);
    var elevenAdditional2 = ReadExcelTextViaCom(elevenst, 1, 4, 10);
    var elevenAdditional3 = ReadExcelTextViaCom(elevenst, 1, 4, 11);
    var elevenStore = ReadExcelTextViaCom(elevenst, 1, 4, 42);
    if (elevenMain.StartsWith("http", StringComparison.OrdinalIgnoreCase))
    {
        AssertContains("rkghrud1.cafe24.com/web/product/big/", elevenMain, "11번가 대표이미지 Cafe24 홈런마켓 URL");
        AssertContains("rkghrud1.cafe24.com/web/product/extra/big/", elevenAdditional1, "11번가 추가이미지1 Cafe24 홈런마켓 URL");
    }
    else
    {
        var imageZip = result.Files.FirstOrDefault(file => file.Market == "11번가 이미지ZIP").Path
            ?? throw new InvalidOperationException("11번가 이미지 ZIP 생성 파일이 없습니다.");
        AssertTrue(File.Exists(imageZip), "11번가 이미지 ZIP 파일 존재");
        AssertZipImageAtLeast(imageZip, elevenMain, 600, "11번가 대표이미지 ZIP 600px 이상");
        AssertZipImageAtLeast(imageZip, elevenAdditional1, 600, "11번가 추가이미지1 ZIP 600px 이상");
        AssertZipImageAtLeast(imageZip, elevenAdditional2, 600, "11번가 추가이미지2 ZIP 600px 이상");
        AssertZipImageAtLeast(imageZip, elevenAdditional3, 600, "11번가 추가이미지3 ZIP 600px 이상");
    }
    AssertDoesNotContain("gi.esmplus.com", elevenMain, "11번가 대표이미지 ESM fallback URL");
    AssertDoesNotContain("heypoppy10", elevenMain, "11번가 대표이미지 준비몰 URL");
    AssertDoesNotContain("상세", elevenMain, "11번가 대표이미지 한글 파일명");
    AssertDoesNotContain("gi.esmplus.com", elevenAdditional1, "11번가 추가이미지1 ESM fallback URL");
    AssertDoesNotContain("heypoppy10", elevenAdditional1, "11번가 추가이미지1 준비몰 URL");
    AssertEqual("홈런market", elevenStore, "11번가 스토어명");

    using var workbook = new XLWorkbook(esm);
    var sheet = workbook.Worksheet("NEW 일반상품");
    var esmMain = sheet.Cell(8, 26).GetString();
    var esmAdditional = sheet.Cell(8, 27).GetString();
    var esmStock = sheet.Cell(8, 21).GetFormattedString().Replace(",", "");
    var esmOptionStock = sheet.Cell(8, 22).GetFormattedString().Replace(",", "");
    var esmCategoryCode = sheet.Cell(8, 11).GetString();
    var esmAuctionCode = sheet.Cell(8, 12).GetString();
    var esmGmarketCode = sheet.Cell(8, 13).GetString();
    AssertContains("rkghrud1.cafe24.com/web/product/big/", esmMain, "ESM 기본이미지 Cafe24 홈런마켓 URL");
    AssertDoesNotContain("gi.esmplus.com", esmMain, "ESM 기본이미지 ESM fallback URL");
    AssertDoesNotContain("heypoppy10", esmMain, "ESM 기본이미지 준비몰 URL");
    AssertDoesNotContain("상세", esmMain, "ESM 기본이미지 한글 파일명");
    AssertDoesNotContain("상세", esmAdditional, "ESM 추가이미지 한글 파일명");
    AssertContains("rkghrud1.cafe24.com/web/product/extra/big/", esmAdditional, "ESM 추가이미지 Cafe24 홈런마켓 URL");
    AssertDoesNotContain("gi.esmplus.com", esmAdditional, "ESM 추가이미지 ESM fallback URL");
    AssertDoesNotContain("heypoppy10", esmAdditional, "ESM 추가이미지 준비몰 URL");
    AssertTrue(!string.IsNullOrWhiteSpace(esmCategoryCode), "ESM 카테고리 코드");
    AssertTrue(!string.IsNullOrWhiteSpace(esmAuctionCode), "ESM A 노출코드");
    AssertTrue(!string.IsNullOrWhiteSpace(esmGmarketCode), "ESM G마켓 노출코드");
    AssertEqual("99999", esmStock, "ESM 재고수량");
    AssertEqual("99999", esmOptionStock, "ESM 옵션재고");
}

static void AssertZipImageAtLeast(string zipPath, string entryName, int minSize, string label)
{
    using var archive = ZipFile.OpenRead(zipPath);
    var entry = archive.GetEntry(entryName)
        ?? throw new InvalidOperationException($"{label}: ZIP 안에 파일이 없습니다. {entryName}");
    using var stream = entry.Open();
    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    memory.Position = 0;
    var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
        memory,
        System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile,
        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
    var frame = decoder.Frames.FirstOrDefault()
        ?? throw new InvalidOperationException($"{label}: 이미지가 아닙니다. {entryName}");
    AssertTrue(frame.PixelWidth >= minSize && frame.PixelHeight >= minSize,
        $"{label}: {frame.PixelWidth}x{frame.PixelHeight}");
}

static string ReadExcelTextViaCom(string path, object sheetNameOrIndex, int row, int column)
{
    object? excelObject = null;
    object? workbookObject = null;
    object? worksheetObject = null;
    object? cellObject = null;
    try
    {
        var excelType = Type.GetTypeFromProgID("Excel.Application")
                        ?? throw new InvalidOperationException("Excel COM을 사용할 수 없습니다.");
        excelObject = Activator.CreateInstance(excelType);
        dynamic excel = excelObject!;
        excel.Visible = false;
        excel.DisplayAlerts = false;
        dynamic workbook = excel.Workbooks.Open(path, 0, true);
        workbookObject = workbook;
        dynamic worksheet = workbook.Worksheets[sheetNameOrIndex];
        worksheetObject = worksheet;
        dynamic cell = worksheet.Cells[row, column];
        cellObject = cell;
        return (cell.Text?.ToString() ?? "").Trim();
    }
    finally
    {
        if (workbookObject is not null)
        {
            try { ((dynamic)workbookObject).Close(false); } catch { }
        }
        if (excelObject is not null)
        {
            try { ((dynamic)excelObject).Quit(); } catch { }
        }
        ReleaseComObject(cellObject);
        ReleaseComObject(worksheetObject);
        ReleaseComObject(workbookObject);
        ReleaseComObject(excelObject);
    }
}

static void ReleaseComObject(object? value)
{
    if (value is not null && Marshal.IsComObject(value))
    {
        try { Marshal.FinalReleaseComObject(value); } catch { }
    }
}

static HashSet<int> ResolveCoupangRows(string file, string rowArg, string gs, string search)
{
    if (int.TryParse(rowArg, out var row) && row > 1)
        return [row];

    var term = FirstNonEmpty(gs, search);
    if (string.IsNullOrWhiteSpace(term))
        throw new ArgumentException("쿠팡 1건 업로드에는 --row, --gs, --search 중 하나가 필요합니다.");

    var rows = CoupangProductBuilder.ReadSourceFile(file);
    var matches = rows
        .Where(r => RowContains(r, term))
        .Select(r => (int)r["_row_num"]!)
        .Distinct()
        .Take(2)
        .ToList();

    if (matches.Count == 0)
        throw new InvalidOperationException($"쿠팡 대상 행을 찾지 못했습니다: {term}");
    if (matches.Count > 1)
        throw new InvalidOperationException($"쿠팡 대상이 2개 이상입니다. --row로 하나만 지정하세요: {string.Join(",", matches)}");

    return [matches[0]];
}

static bool RowContains(Dictionary<string, object?> row, string term)
{
    foreach (var key in new[] { "자체 상품코드", "상품명", "홈런_쿠팡상품명", "쿠팡상품명", "홈런_공통마켓상품명", "최종키워드2차", "1차키워드" })
    {
        if (row.TryGetValue(key, out var value)
            && (value?.ToString() ?? "").Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static string FirstNonEmpty(params string[] values)
{
    foreach (var value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();
    }

    return "";
}

static string GetArgValue(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }
    return "";
}

static void TestPrefersSizeAttributeOverQuantity()
{
    var row = BuildRow("A 50mm, B 110mm");
    using var metaDoc = JsonDocument.Parse("""
    {
      "data": {
        "noticeCategories": [],
        "attributes": [
          {
            "attributeTypeName": "수량",
            "dataType": "NUMBER",
            "inputType": "INPUT",
            "inputValues": [],
            "basicUnit": "개",
            "usableUnits": ["개", "박스", "세트"],
            "required": "MANDATORY",
            "groupNumber": "NONE",
            "exposed": "EXPOSED"
          },
          {
            "attributeTypeName": "사이즈",
            "dataType": "STRING",
            "inputType": "INPUT",
            "inputValues": [],
            "basicUnit": "없음",
            "usableUnits": [],
            "required": "MANDATORY",
            "groupNumber": "NONE",
            "exposed": "EXPOSED"
          }
        ]
      }
    }
    """);

    var product = CoupangProductBuilder.BuildProduct(row, 64367, metaDoc.RootElement, "VENDOR");
    var sizeAttr = GetAttribute(product, 0, "사이즈");
    var quantityAttr = GetAttribute(product, 0, "수량");

    AssertEqual("50mm", sizeAttr["attributeValueName"]?.GetValue<string>(), "size attribute value");
    AssertNull(sizeAttr["unitCodeName"], "size attribute unit");
    AssertEqual("1개", quantityAttr["attributeValueName"]?.GetValue<string>(), "fixed quantity value");
    AssertNull(quantityAttr["unitCodeName"], "fixed quantity unit");
}

static void TestQuantityOptionUsesNumericValueAndUnit()
{
    var row = BuildRow("A 2개, B 3개");
    using var metaDoc = JsonDocument.Parse("""
    {
      "data": {
        "noticeCategories": [],
        "attributes": [
          {
            "attributeTypeName": "수량",
            "dataType": "NUMBER",
            "inputType": "INPUT",
            "inputValues": [],
            "basicUnit": "개",
            "usableUnits": ["개", "박스", "세트"],
            "required": "MANDATORY",
            "groupNumber": "NONE",
            "exposed": "EXPOSED"
          }
        ]
      }
    }
    """);

    var product = CoupangProductBuilder.BuildProduct(row, 64367, metaDoc.RootElement, "VENDOR");
    var quantityAttr = GetAttribute(product, 0, "수량");

    AssertEqual("2개", quantityAttr["attributeValueName"]?.GetValue<string>(), "quantity attribute value");
    AssertNull(quantityAttr["unitCodeName"], "quantity attribute unit");
}

static void TestCoupangSpecificProductNameWins()
{
    var row = BuildRow("A 기본");
    row["홈런_공통마켓상품명"] = "공통 상품명";
    row["홈런_쿠팡상품명"] = "쿠팡 전용 상품명";

    using var metaDoc = JsonDocument.Parse("""{"data":{"noticeCategories":[],"attributes":[]}}""");
    var product = CoupangProductBuilder.BuildProduct(row, 64367, metaDoc.RootElement, "VENDOR");

    AssertEqual("쿠팡 전용 상품명", product["displayProductName"]?.GetValue<string>(), "coupang product name priority");
}

static void TestCoupangSearchTagsKeepSpaces()
{
    var row = BuildRow("A 기본");
    row["홈런_공통마켓검색키워드"] = "공통키워드";
    row["홈런_쿠팡검색태그"] = "음료 디스펜서, 워터 저그|카페 물통";

    using var metaDoc = JsonDocument.Parse("""{"data":{"noticeCategories":[],"attributes":[]}}""");
    var product = CoupangProductBuilder.BuildProduct(row, 64367, metaDoc.RootElement, "VENDOR");
    var tags = GetSearchTags(product, 0);

    AssertEqualInt(3, tags.Count, "coupang search tag count");
    AssertEqual("음료 디스펜서", tags[0], "coupang search tag preserves first phrase");
    AssertEqual("워터 저그", tags[1], "coupang search tag preserves second phrase");
    AssertEqual("카페 물통", tags[2], "coupang search tag preserves third phrase");
}

static void TestCoupangOptionItemNamesKeepAlphabetPrefix()
{
    var row = BuildRow("랜덤|블랙|카키");
    using var metaDoc = JsonDocument.Parse("""{"data":{"noticeCategories":[],"attributes":[]}}""");
    var product = CoupangProductBuilder.BuildProduct(row, 64367, metaDoc.RootElement, "VENDOR");
    var items = product["items"]?.AsArray() ?? throw new Exception("items missing");

    AssertEqual("A 랜덤", items[0]?["itemName"]?.GetValue<string>(), "coupang option prefix A");
    AssertEqual("B 블랙", items[1]?["itemName"]?.GetValue<string>(), "coupang option prefix B");
    AssertEqual("C 카키", items[2]?["itemName"]?.GetValue<string>(), "coupang option prefix C");
}

static void TestNaverFallbackImagesIncludeAdditionalColumn()
{
    var row = new Dictionary<string, object?>
    {
        ["이미지등록(목록)"] = "https://example.com/main.jpg",
        ["이미지등록(추가)"] = "https://example.com/add-1.jpg|https://example.com/add-2.jpg",
        ["이미지등록(상세)"] = "https://example.com/detail.jpg",
    };

    var method = typeof(NaverUploadService).GetMethod(
        "CollectImageUrls",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new Exception("CollectImageUrls not found");

    var result = (List<string>?)method.Invoke(null, new object?[] { row })
        ?? throw new Exception("CollectImageUrls returned null");

    AssertEqualInt(4, result.Count, "naver fallback image count");
    AssertEqual("https://example.com/main.jpg", result[0], "naver fallback main image");
    AssertEqual("https://example.com/add-1.jpg", result[1], "naver fallback additional image 1");
    AssertEqual("https://example.com/add-2.jpg", result[2], "naver fallback additional image 2");
    AssertEqual("https://example.com/detail.jpg", result[3], "naver fallback detail image");
}

static void TestMarketImageGuardNormalizesMalformedImgTags()
{
    var guardType = typeof(NaverUploadService).Assembly.GetType("KeywordOcr.App.Services.MarketImageUrlGuard")
        ?? throw new Exception("MarketImageUrlGuard not found");
    var method = guardType.GetMethod(
        "RemoveUnsafeImageTags",
        BindingFlags.Public | BindingFlags.Static)
        ?? throw new Exception("RemoveUnsafeImageTags not found");

    var malformed = "<center><img src=\"http://ai.esmplus.com/bluegraywolf/goodsellers/GS2100569A/1.jpg\"\"\"\"\"\"\"\"><img src=\"\"\"\"\"\"\"\"http://ai.esmplus.com/bluegraywolf/goodsellers/GS2100569A/2.jpg\"\"\"\"\"\"\"\"></center>";
    var result = (string?)method.Invoke(null, new object?[] { malformed }) ?? "";

    AssertTrue(!result.Contains("\"\""), "malformed image quotes are removed");
    AssertEqual("<center><img src=\"http://ai.esmplus.com/bluegraywolf/goodsellers/GS2100569A/1.jpg\"><img src=\"http://ai.esmplus.com/bluegraywolf/goodsellers/GS2100569A/2.jpg\"></center>", result, "malformed image tags normalized");
}

static void TestNaverSelectedDataImagePathIsResolved()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "keywordocr-naver-selected-images-" + Guid.NewGuid().ToString("N"));
    try
    {
        var dataRoot = Path.Combine(tempRoot, "data");
        var exportRoot = Path.Combine(dataRoot, "exports");
        var imageDir = Path.Combine(exportRoot, "listing_images", "20260518", "GS0601372");
        Directory.CreateDirectory(imageDir);
        var selected = Path.Combine(imageDir, "GS0601372_4.jpg");
        File.WriteAllText(selected, "main");

        var row = new Dictionary<string, object?>
        {
            ["이미지등록(목록)"] = "/data/exports/listing_images/20260518/GS0601372/GS0601372_4.jpg"
        };

        var method = typeof(NaverUploadService).GetMethod(
            "CollectListingImageUrls",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new Exception("CollectListingImageUrls not found");
        var result = (List<string>?)method.Invoke(null, new object?[] { row, exportRoot })
            ?? throw new Exception("CollectListingImageUrls returned null");

        AssertEqual(selected, result[0], "naver selected /data image path resolves to local file");
    }
    finally
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}

static void TestNaverLatestListingImageFolderWins()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "keywordocr-naver-latest-images-" + Guid.NewGuid().ToString("N"));
    try
    {
        var exportRoot = Path.Combine(tempRoot, "exports");
        var oldDir = Path.Combine(exportRoot, "listing_images", "20260517", "GS0601372");
        var newDir = Path.Combine(exportRoot, "listing_images", "20260518", "GS0601372");
        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(newDir);
        var oldImage = Path.Combine(oldDir, "GS0601372_1.jpg");
        var newImage = Path.Combine(newDir, "GS0601372_1.jpg");
        File.WriteAllText(oldImage, "old");
        File.WriteAllText(newImage, "new");

        var method = typeof(NaverUploadService).GetMethod(
            "FindListingImages",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new Exception("Naver FindListingImages not found");
        var result = (List<string>?)method.Invoke(null, new object[] { exportRoot, "GS0601372A" })
            ?? throw new Exception("Naver FindListingImages returned null");

        AssertEqual(newImage, result[0], "naver latest listing_images date folder wins");
    }
    finally
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}

static void TestCoupangListingImagesWinOverCafe24Urls()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "keywordocr-coupang-images-" + Guid.NewGuid().ToString("N"));
    try
    {
        var exportRoot = Path.Combine(tempRoot, "exports");
        var imageDir = Path.Combine(exportRoot, "listing_images", "GS0601373");
        Directory.CreateDirectory(imageDir);
        var img1 = Path.Combine(imageDir, "01.jpg");
        var img2 = Path.Combine(imageDir, "02.jpg");
        var img3 = Path.Combine(imageDir, "03.jpg");
        File.WriteAllText(img1, "1");
        File.WriteAllText(img2, "2");
        File.WriteAllText(img3, "3");
        File.WriteAllText(Path.Combine(exportRoot, "image_selections.json"), """
        {
          "GS0601373": {
            "main": 2,
            "additional": [0]
          }
        }
        """);

        var row = new Dictionary<string, object?>
        {
            ["_cafe24_image_urls"] = new List<string> { "https://example.com/cafe24-main.jpg" }
        };

        var method = typeof(CoupangUploadService).GetMethod(
            "ResolveImageSources",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new Exception("ResolveImageSources not found");
        var tuple = method.Invoke(null, new object[] { row, "GS0601373A", exportRoot })
            ?? throw new Exception("ResolveImageSources returned null");
        var tupleType = tuple.GetType();
        var images = (List<string>)(tupleType.GetField("Item1")?.GetValue(tuple)
            ?? throw new Exception("ResolveImageSources images missing"));
        var sourceLabel = (string)(tupleType.GetField("Item2")?.GetValue(tuple)
            ?? throw new Exception("ResolveImageSources label missing"));
        var alreadyUploadReady = (bool)(tupleType.GetField("Item3")?.GetValue(tuple)
            ?? throw new Exception("ResolveImageSources ready flag missing"));

        AssertEqual("listing_images", sourceLabel, "coupang selected image source label");
        AssertTrue(!alreadyUploadReady, "coupang selected images should be uploaded to CDN");
        AssertEqual(img3, images[0], "coupang selected representative image wins");
        AssertEqual(img1, images[1], "coupang selected additional image follows");
    }
    finally
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}

static void TestLotteOnListingImagesWinOverCafe24Urls()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "keywordocr-lotteon-images-" + Guid.NewGuid().ToString("N"));
    try
    {
        var exportRoot = Path.Combine(tempRoot, "exports");
        var imageDir = Path.Combine(exportRoot, "listing_images", "GS0601373");
        Directory.CreateDirectory(imageDir);
        var img1 = Path.Combine(imageDir, "01.jpg");
        var img2 = Path.Combine(imageDir, "02.jpg");
        var img3 = Path.Combine(imageDir, "03.jpg");
        File.WriteAllText(img1, "1");
        File.WriteAllText(img2, "2");
        File.WriteAllText(img3, "3");
        File.WriteAllText(Path.Combine(exportRoot, "image_selections.json"), """
        {
          "GS0601373": {
            "main": 2,
            "additional": [0]
          }
        }
        """);

        var row = new Dictionary<string, object?>
        {
            ["_cafe24_image_urls"] = new List<string> { "https://example.com/cafe24-main.jpg" }
        };

        var method = typeof(LotteOnUploadService).GetMethod(
            "ResolveImageSources",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new Exception("LotteOn ResolveImageSources not found");
        var tuple = method.Invoke(null, new object[] { row, "GS0601373A", exportRoot })
            ?? throw new Exception("LotteOn ResolveImageSources returned null");
        var tupleType = tuple.GetType();
        var images = (List<string>)(tupleType.GetField("Item1")?.GetValue(tuple)
            ?? throw new Exception("LotteOn ResolveImageSources images missing"));
        var sourceLabel = (string)(tupleType.GetField("Item2")?.GetValue(tuple)
            ?? throw new Exception("LotteOn ResolveImageSources label missing"));
        var alreadyUploadReady = (bool)(tupleType.GetField("Item3")?.GetValue(tuple)
            ?? throw new Exception("LotteOn ResolveImageSources ready flag missing"));

        AssertEqual("listing_images", sourceLabel, "lotteon selected image source label");
        AssertTrue(!alreadyUploadReady, "lotteon selected images should be uploaded to CDN");
        AssertEqual(img3, images[0], "lotteon selected representative image wins");
        AssertEqual(img1, images[1], "lotteon selected additional image follows");
    }
    finally
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}

static void TestCoupangSelectedImageColumnsWinOverListingFallback()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "keywordocr-coupang-selected-images-" + Guid.NewGuid().ToString("N"));
    try
    {
        var dataRoot = Path.Combine(tempRoot, "data");
        var exportRoot = Path.Combine(dataRoot, "exports");
        var imageDir = Path.Combine(exportRoot, "listing_images", "GS0601372");
        Directory.CreateDirectory(imageDir);
        var fallback = Path.Combine(imageDir, "GS0601372_1.jpg");
        var selectedMain = Path.Combine(imageDir, "GS0601372_4.jpg");
        var selectedAdd = Path.Combine(imageDir, "GS0601372_2.jpg");
        File.WriteAllText(fallback, "fallback");
        File.WriteAllText(selectedMain, "main");
        File.WriteAllText(selectedAdd, "add");

        var row = new Dictionary<string, object?>
        {
            ["이미지등록(목록)"] = "/data/exports/listing_images/GS0601372/GS0601372_4.jpg",
            ["이미지등록(추가)"] = "/data/exports/listing_images/GS0601372/GS0601372_2.jpg",
            ["_cafe24_image_urls"] = new List<string> { "https://example.com/cafe24-main.jpg" }
        };

        var method = typeof(CoupangUploadService).GetMethod(
            "ResolveImageSources",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new Exception("ResolveImageSources not found");
        var tuple = method.Invoke(null, new object[] { row, "GS0601372A", exportRoot })
            ?? throw new Exception("ResolveImageSources returned null");
        var tupleType = tuple.GetType();
        var images = (List<string>)(tupleType.GetField("Item1")?.GetValue(tuple)
            ?? throw new Exception("ResolveImageSources images missing"));
        var sourceLabel = (string)(tupleType.GetField("Item2")?.GetValue(tuple)
            ?? throw new Exception("ResolveImageSources label missing"));

        AssertEqual("selected_images", sourceLabel, "coupang explicit selected image source label");
        AssertEqual(selectedMain, images[0], "coupang explicit representative image wins");
        AssertEqual(selectedAdd, images[1], "coupang explicit additional image follows");
    }
    finally
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}

static void TestCoupangLatestListingImageFolderWins()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "keywordocr-coupang-latest-images-" + Guid.NewGuid().ToString("N"));
    try
    {
        var exportRoot = Path.Combine(tempRoot, "exports");
        var oldDir = Path.Combine(exportRoot, "listing_images", "20260517", "GS0601372");
        var newDir = Path.Combine(exportRoot, "listing_images", "20260518", "GS0601372");
        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(newDir);
        var oldImage = Path.Combine(oldDir, "GS0601372_1.jpg");
        var newImage = Path.Combine(newDir, "GS0601372_1.jpg");
        File.WriteAllText(oldImage, "old");
        File.WriteAllText(newImage, "new");

        var method = typeof(CoupangUploadService).GetMethod(
            "FindListingImages",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new Exception("Coupang FindListingImages not found");
        var result = (List<string>?)method.Invoke(null, new object[] { exportRoot, "GS0601372A" })
            ?? throw new Exception("Coupang FindListingImages returned null");

        AssertEqual(newImage, result[0], "coupang latest listing_images date folder wins");
    }
    finally
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}

static void TestLotteOnSelectedImageColumnsWinOverListingFallback()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "keywordocr-lotteon-selected-images-" + Guid.NewGuid().ToString("N"));
    try
    {
        var dataRoot = Path.Combine(tempRoot, "data");
        var exportRoot = Path.Combine(dataRoot, "exports");
        var imageDir = Path.Combine(exportRoot, "listing_images", "GS0601372");
        Directory.CreateDirectory(imageDir);
        var fallback = Path.Combine(imageDir, "GS0601372_1.jpg");
        var selectedMain = Path.Combine(imageDir, "GS0601372_4.jpg");
        var selectedAdd = Path.Combine(imageDir, "GS0601372_2.jpg");
        File.WriteAllText(fallback, "fallback");
        File.WriteAllText(selectedMain, "main");
        File.WriteAllText(selectedAdd, "add");

        var row = new Dictionary<string, object?>
        {
            ["이미지등록(목록)"] = "/data/exports/listing_images/GS0601372/GS0601372_4.jpg",
            ["이미지등록(추가)"] = "/data/exports/listing_images/GS0601372/GS0601372_2.jpg",
            ["_cafe24_image_urls"] = new List<string> { "https://example.com/cafe24-main.jpg" }
        };

        var method = typeof(LotteOnUploadService).GetMethod(
            "ResolveImageSources",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new Exception("LotteOn ResolveImageSources not found");
        var tuple = method.Invoke(null, new object[] { row, "GS0601372A", exportRoot })
            ?? throw new Exception("LotteOn ResolveImageSources returned null");
        var tupleType = tuple.GetType();
        var images = (List<string>)(tupleType.GetField("Item1")?.GetValue(tuple)
            ?? throw new Exception("LotteOn ResolveImageSources images missing"));
        var sourceLabel = (string)(tupleType.GetField("Item2")?.GetValue(tuple)
            ?? throw new Exception("LotteOn ResolveImageSources label missing"));

        AssertEqual("selected_images", sourceLabel, "lotteon explicit selected image source label");
        AssertEqual(selectedMain, images[0], "lotteon explicit representative image wins");
        AssertEqual(selectedAdd, images[1], "lotteon explicit additional image follows");
    }
    finally
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}

static void TestLotteOnLatestListingImageFolderWins()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "keywordocr-lotteon-latest-images-" + Guid.NewGuid().ToString("N"));
    try
    {
        var exportRoot = Path.Combine(tempRoot, "exports");
        var oldDir = Path.Combine(exportRoot, "listing_images", "20260517", "GS0601372");
        var newDir = Path.Combine(exportRoot, "listing_images", "20260518", "GS0601372");
        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(newDir);
        var oldImage = Path.Combine(oldDir, "GS0601372_1.jpg");
        var newImage = Path.Combine(newDir, "GS0601372_1.jpg");
        File.WriteAllText(oldImage, "old");
        File.WriteAllText(newImage, "new");

        var method = typeof(LotteOnUploadService).GetMethod(
            "FindListingImages",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new Exception("LotteOn FindListingImages not found");
        var result = (List<string>?)method.Invoke(null, new object[] { exportRoot, "GS0601372A" })
            ?? throw new Exception("LotteOn FindListingImages returned null");

        AssertEqual(newImage, result[0], "lotteon latest listing_images date folder wins");
    }
    finally
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}

static void TestWorkspacePackageRoundTrip()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "keywordocr-package-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        var workspace = Path.Combine(tempRoot, "work");
        var resultDir = Path.Combine(workspace, "llm_result_v4_cli");
        var refDir = Path.Combine(workspace, "category_reference");
        Directory.CreateDirectory(resultDir);
        Directory.CreateDirectory(refDir);

        var uploadFile = Path.Combine(workspace, "업로드용_sample.xlsx");
        var resultFile = Path.Combine(resultDir, "업로드용_sample_llm_v4_cli.xlsx");
        var selectionsFile = Path.Combine(workspace, "image_selections.json");
        File.WriteAllText(uploadFile, "upload");
        File.WriteAllText(resultFile, "result");
        File.WriteAllText(selectionsFile, "{}");
        File.WriteAllText(Path.Combine(refDir, "naver_categories.csv"), "id,name");
        File.WriteAllText(Path.Combine(workspace, "cafe24_token.json"), "secret");
        File.WriteAllText(Path.Combine(workspace, "~$업로드용_sample.xlsx"), "lock");

        File.SetLastWriteTimeUtc(uploadFile, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(resultFile, DateTime.UtcNow);

        var packagePath = Path.Combine(tempRoot, "package.zip");
        var saved = WorkspacePackageService.CreatePackage(
            workspace,
            packagePath,
            "source.csv",
            resultFile,
            productCount: 7,
            selectedCodes: new[] { "GS0601074A", "GS0601075A" });

        AssertEqualInt(4, saved.IncludedFileCount, "workspace package included file count");
        AssertEqualInt(2, saved.ExcludedFileCount, "workspace package excluded file count");

        using (var archive = ZipFile.OpenRead(packagePath))
        {
            AssertTrue(archive.GetEntry("manifest.json") is not null, "manifest entry exists");
            AssertTrue(archive.GetEntry("README.txt") is not null, "readme entry exists");
            AssertTrue(archive.GetEntry("workspace/업로드용_sample.xlsx") is not null, "upload entry exists");
            AssertTrue(archive.GetEntry("workspace/llm_result_v4_cli/업로드용_sample_llm_v4_cli.xlsx") is not null, "v4 result entry exists");
            AssertTrue(archive.GetEntry("workspace/cafe24_token.json") is null, "token entry excluded");
            AssertTrue(archive.GetEntry("workspace/~$업로드용_sample.xlsx") is null, "lock file excluded");
        }

        var restored = WorkspacePackageService.RestorePackage(packagePath, Path.Combine(tempRoot, "EXPORT"));
        AssertTrue(File.Exists(restored.UploadWorkbookPath), "restored upload workbook exists");
        AssertTrue(File.Exists(restored.LatestV4ResultPath), "restored v4 result exists");
        AssertTrue(File.Exists(restored.ImageSelectionsPath), "restored image selections exists");
        AssertTrue(!File.Exists(Path.Combine(restored.RestoredFolder, "cafe24_token.json")), "token not restored");
        AssertEqual("source.csv", restored.Manifest.SourceFileName, "manifest source file name");
        AssertEqualInt(7, restored.Manifest.ProductCount, "manifest product count");
        AssertEqualInt(2, restored.Manifest.SelectedCodes.Count, "manifest selected code count");
    }
    finally
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}

static void TestWorkspacePackageRejectsUnsafeEntryPath()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "keywordocr-package-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(tempRoot);
        var packagePath = Path.Combine(tempRoot, "unsafe.zip");
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            var manifest = new WorkspacePackageManifest { WorkspaceFolderName = "unsafe" };
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open()))
                writer.Write(JsonSerializer.Serialize(manifest));

            var unsafeEntry = archive.CreateEntry("workspace/../escape.txt");
            using var unsafeWriter = new StreamWriter(unsafeEntry.Open());
            unsafeWriter.Write("escape");
        }

        try
        {
            WorkspacePackageService.RestorePackage(packagePath, Path.Combine(tempRoot, "EXPORT"));
            throw new Exception("unsafe zip path was not rejected");
        }
        catch (InvalidDataException)
        {
            // Expected.
        }

        AssertTrue(!File.Exists(Path.Combine(tempRoot, "EXPORT", "escape.txt")), "unsafe entry not extracted");
    }
    finally
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}

static void TestWorkspaceWorkbookEditRoundTrip()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "keywordocr-workbook-edit-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(tempRoot);
        var workbookPath = Path.Combine(tempRoot, "edit.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var a = workbook.AddWorksheet("분리추출후");
            a.Cell(1, 1).Value = "자체 상품코드";
            a.Cell(1, 2).Value = "상품명";
            a.Cell(1, 3).Value = "검색어설정";
            a.Cell(1, 4).Value = "검색키워드";
            a.Cell(1, 5).Value = "판매가";
            a.Cell(2, 1).Value = "GS0000001A";
            a.Cell(2, 2).Value = "A old";
            a.Cell(2, 3).Value = "A tag";
            a.Cell(2, 4).Value = "A key";
            a.Cell(2, 5).Value = 1234;

            var b = workbook.AddWorksheet("B마켓");
            b.Cell(1, 1).Value = "자체 상품코드";
            b.Cell(1, 2).Value = "상품명";
            b.Cell(1, 3).Value = "검색어설정";
            b.Cell(1, 4).Value = "검색키워드";
            b.Cell(2, 1).Value = "GS0000001A";
            b.Cell(2, 2).Value = "B old";
            b.Cell(2, 3).Value = "B tag";
            b.Cell(2, 4).Value = "B key";

            var ocr = workbook.AddWorksheet("OCR결과");
            ocr.Cell(1, 1).Value = "GS코드";
            ocr.Cell(1, 2).Value = "후보키워드_정확형";
            ocr.Cell(1, 3).Value = "후보키워드_용도형";
            ocr.Cell(1, 4).Value = "후보키워드_확장형";
            ocr.Cell(1, 5).Value = "검수필요";
            ocr.Cell(1, 6).Value = "검수메모";
            ocr.Cell(2, 1).Value = "GS0000001A";
            ocr.Cell(2, 2).Value = "old exact";
            ocr.Cell(2, 3).Value = "old use";
            ocr.Cell(2, 4).Value = "old expand";
            ocr.Cell(2, 5).Value = "N";
            ocr.Cell(2, 6).Value = "";

            workbook.SaveAs(workbookPath);
        }

        var loaded = WorkspaceWorkbookEditService.Load(workbookPath);
        AssertEqualInt(1, loaded.Rows.Count, "workspace edit row count");
        var row = loaded.Rows[0];
        row.AProductName = "A new";
        row.ASearchTags = "A new tag";
        row.ASearchKeywords = "A new key";
        row.HomeNaverProductName = "Naver new";
        row.HomeNaverTags = "naver tag";
        row.HomeLotteOnProductName = "Lotte new";
        row.HomeLotteOnKeywords = "lotte key";
        row.HomeCommonProductName = "Common new";
        row.HomeCommonKeywords = "common key";
        row.BProductName = "B new";
        row.BSearchTags = "B new tag";
        row.BSearchKeywords = "B new key";
        row.CandidateExactKeywords = "exact one, exact two";
        row.CandidateUseKeywords = "use one, use two";
        row.CandidateExpandKeywords = "expand one, expand two";
        row.ReviewNeeded = "Y";
        row.ReviewMemo = "check";

        WorkspaceWorkbookEditService.Save(workbookPath, loaded.Rows);

        using var verify = new XLWorkbook(workbookPath);
        AssertEqual("A new", verify.Worksheet("분리추출후").Cell(2, 2).GetString(), "A product name saved");
        AssertEqual("A new tag", verify.Worksheet("분리추출후").Cell(2, 3).GetString(), "A tags saved");
        AssertEqual("A new key", verify.Worksheet("분리추출후").Cell(2, 4).GetString(), "A keywords saved");
        AssertEqual("1234", verify.Worksheet("분리추출후").Cell(2, 5).GetString(), "unshown price preserved");
        AssertEqual("홈런_네이버상품명", verify.Worksheet("분리추출후").Cell(1, 6).GetString(), "home naver name column created");
        AssertEqual("Naver new", verify.Worksheet("분리추출후").Cell(2, 6).GetString(), "home naver name saved");
        AssertEqual("naver tag", verify.Worksheet("분리추출후").Cell(2, 7).GetString(), "home naver tags saved");
        AssertEqual("Lotte new", verify.Worksheet("분리추출후").Cell(2, 8).GetString(), "home lotte name saved");
        AssertEqual("lotte key", verify.Worksheet("분리추출후").Cell(2, 9).GetString(), "home lotte keywords saved");
        AssertEqual("Common new", verify.Worksheet("분리추출후").Cell(2, 10).GetString(), "home common name saved");
        AssertEqual("common key", verify.Worksheet("분리추출후").Cell(2, 11).GetString(), "home common keywords saved");
        AssertEqual("B new", verify.Worksheet("B마켓").Cell(2, 2).GetString(), "B product name saved");
        AssertEqual("exact one, exact two", verify.Worksheet("OCR결과").Cell(2, 2).GetString(), "exact candidate saved");
        AssertEqual("use one, use two", verify.Worksheet("OCR결과").Cell(2, 3).GetString(), "use candidate saved");
        AssertEqual("expand one, expand two", verify.Worksheet("OCR결과").Cell(2, 4).GetString(), "expand candidate saved");
        AssertEqual("Y", verify.Worksheet("OCR결과").Cell(2, 5).GetString(), "review needed saved");
        AssertEqual("check", verify.Worksheet("OCR결과").Cell(2, 6).GetString(), "review memo saved");
    }
    finally
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}

static Dictionary<string, object?> BuildRow(string optionInput)
{
    return new Dictionary<string, object?>
    {
        ["상품명"] = "테스트 상품",
        ["최종키워드2차"] = "테스트 상품",
        ["1차키워드"] = "테스트",
        ["판매가"] = 1000d,
        ["소비자가"] = 1000d,
        ["옵션입력"] = optionInput,
        ["옵션추가금"] = "0,0",
        ["자체 상품코드"] = "GS0000001A",
        ["이미지등록(목록)"] = "https://example.com/a.jpg",
        ["상품 상세설명"] = "<img src='https://example.com/detail.jpg'>"
    };
}

static JsonObject GetAttribute(JsonObject product, int itemIndex, string attrName)
{
    var items = product["items"]?.AsArray() ?? throw new Exception("items missing");
    var attrs = items[itemIndex]?["attributes"]?.AsArray() ?? throw new Exception("attributes missing");
    foreach (var attr in attrs)
    {
        var obj = attr?.AsObject();
        if (obj is null) continue;
        var name = obj["attributeTypeName"]?.GetValue<string>();
        if (string.Equals(name, attrName, StringComparison.Ordinal))
            return obj;
    }

    throw new Exception($"attribute '{attrName}' missing");
}

static List<string> GetSearchTags(JsonObject product, int itemIndex)
{
    var items = product["items"]?.AsArray() ?? throw new Exception("items missing");
    var tags = items[itemIndex]?["searchTags"]?.AsArray() ?? throw new Exception("searchTags missing");
    return tags.Select(tag => tag?.GetValue<string>() ?? "").ToList();
}

static void AssertEqual(string expected, string? actual, string label)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new Exception($"{label}: expected '{expected}', got '{actual ?? "<null>"}'");
}

static void AssertContains(string expectedPart, string? actual, string label)
{
    if ((actual ?? "").IndexOf(expectedPart, StringComparison.OrdinalIgnoreCase) < 0)
        throw new Exception($"{label}: expected to contain '{expectedPart}', got '{actual ?? "<null>"}'");
}

static void AssertDoesNotContain(string blockedPart, string? actual, string label)
{
    if ((actual ?? "").IndexOf(blockedPart, StringComparison.OrdinalIgnoreCase) >= 0)
        throw new Exception($"{label}: expected not to contain '{blockedPart}', got '{actual ?? "<null>"}'");
}

static void AssertEqualInt(int expected, int actual, string label)
{
    if (expected != actual)
        throw new Exception($"{label}: expected '{expected}', got '{actual}'");
}

static void AssertTrue(bool condition, string label)
{
    if (!condition)
        throw new Exception($"{label}: expected true");
}

static void AssertNull(JsonNode? actual, string label)
{
    if (actual is not null)
        throw new Exception($"{label}: expected null, got '{actual}'");
}

