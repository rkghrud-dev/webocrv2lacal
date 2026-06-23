using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using System.Threading;
using ClosedXML.Excel;

namespace KeywordOcr.App.Services;

public sealed record MarketExcelExportFile(string Market, string Path, int ProductCount);

public sealed record MarketExcelExportResult(
    string OutputDirectory,
    IReadOnlyList<MarketExcelExportFile> Files,
    string ReportPath,
    int ProductCount,
    int WarningCount);

public sealed class MarketExcelExportService
{
    private const string ElevenstTemplatePath = @"C:\Users\rkghr\Downloads\ExcelUnitProductList-Ver2.50.xlsx";
    private const string EsmTemplatePath = @"C:\Users\rkghr\Downloads\new_basic_bulk (1).xlsx";
    private const string EsmSheetName = "NEW 일반상품";
    private const string DetailIntroImageUrl = "https://gi.esmplus.com/rkghrud/1.jpg";

    private static readonly Regex GsCodeRegex = new(@"GS\d{7}[A-Z0-9]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ImageUrlRegex = new(@"https?://[^'""\s<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OptionPrefixRegex = new(@"^[A-Z]\s+", RegexOptions.Compiled);
    private static readonly HttpClient ImageHttpClient = CreateImageHttpClient();
    private static readonly Dictionary<string, ImageDimensionInfo> ImageDimensionCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _projectRoot;
    private readonly string? _cafe24TokenPath;

    public MarketExcelExportService(string projectRoot, string? cafe24TokenPath = null)
    {
        _projectRoot = projectRoot;
        _cafe24TokenPath = string.IsNullOrWhiteSpace(cafe24TokenPath)
            ? ResolveDefaultHomeCafe24TokenPath()
            : cafe24TokenPath;
    }

    private static string? ResolveDefaultHomeCafe24TokenPath()
    {
        var path = DesktopKeyStore.GetPath("cafe24_token_rkghrud1.json");
        return File.Exists(path) ? path : null;
    }

    public MarketExcelExportResult Export(
        string sourceWorkbookPath,
        IEnumerable<string> selectedGsCodes,
        bool exportElevenst,
        bool exportEsm,
        IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(sourceWorkbookPath) || !File.Exists(sourceWorkbookPath))
            throw new FileNotFoundException("V5 결과 엑셀 파일을 찾지 못했습니다.", sourceWorkbookPath);
        if (!exportElevenst && !exportEsm)
            throw new InvalidOperationException("생성할 마켓을 하나 이상 선택하세요.");

        var selected = selectedGsCodes
            .Select(NormalizeGsCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (selected.Count == 0)
            throw new InvalidOperationException("엑셀로 만들 상품을 선택하세요.");

        var outputDirectory = ResolveMarketOutputDirectory(sourceWorkbookPath);
        Directory.CreateDirectory(outputDirectory);

        progress?.Report("소스 엑셀과 카테고리 매칭 파일을 읽는 중...");
        var categoryMap = LoadCategoryMatchMap(sourceWorkbookPath);
        var cafe24Data = LoadCafe24MarketData(sourceWorkbookPath, progress);
        var products = LoadSourceProducts(sourceWorkbookPath, selected, categoryMap, cafe24Data);
        if (products.Count == 0)
            throw new InvalidOperationException("선택된 GS코드와 일치하는 상품을 V5 결과 엑셀에서 찾지 못했습니다.");

        var reportRows = new List<MarketExcelReportRow>();
        var files = new List<MarketExcelExportFile>();
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        if (exportElevenst)
        {
            progress?.Report("11번가 업로드 XLS 생성 중...");
            var elevenst = ExportElevenst(products, outputDirectory, stamp, reportRows);
            files.Add(new MarketExcelExportFile("11번가", elevenst.ExcelPath, products.Count));
            if (!string.IsNullOrWhiteSpace(elevenst.ImageZipPath))
                files.Add(new MarketExcelExportFile("11번가 이미지ZIP", elevenst.ImageZipPath, products.Count));
        }

        if (exportEsm)
        {
            progress?.Report("ESM/옥션/G마켓 업로드 엑셀 생성 중...");
            var esmCodes = LoadEsmFullCategoryCodes(progress);
            var path = ExportEsm(products, outputDirectory, stamp, esmCodes, reportRows);
            files.Add(new MarketExcelExportFile("ESM", path, products.Count));
        }

        var reportPath = SaveReport(outputDirectory, stamp, reportRows);
        var warningCount = reportRows.Count(row => !string.Equals(row.Status, "OK", StringComparison.OrdinalIgnoreCase));
        progress?.Report($"마켓 엑셀 생성 완료: {files.Count}개 파일 / 경고 {warningCount}건");

        return new MarketExcelExportResult(outputDirectory, files, reportPath, products.Count, warningCount);
    }

    private ElevenstExportPaths ExportElevenst(
        IReadOnlyList<MarketSourceProduct> products,
        string outputDirectory,
        string stamp,
        List<MarketExcelReportRow> reportRows)
    {
        if (!File.Exists(ElevenstTemplatePath))
            throw new FileNotFoundException("11번가 공식 양식 파일을 찾지 못했습니다.", ElevenstTemplatePath);

        var workXlsx = Path.Combine(outputDirectory, $"11번가_업로드_{stamp}_work.xlsx");
        var finalXls = Path.Combine(outputDirectory, $"11번가_업로드_{stamp}.xls");
        var imageZipPath = Path.Combine(outputDirectory, $"11번가_이미지_{stamp}.zip");
        var hasImageZip = false;

        using (var imageZip = ZipFile.Open(imageZipPath, ZipArchiveMode.Create))
        using (var workbook = new XLWorkbook(ElevenstTemplatePath))
        {
            var sheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name.Trim() == "대량등록 양식")
                        ?? workbook.Worksheets.First();

            ClearRows(sheet, 6);

            var rowNumber = 6;
            foreach (var product in products)
            {
                var notes = new List<string>();
                var requiredErrors = new List<string>();
                var categoryCode = FirstNonEmpty(product.ElevenstCategoryCode, ExtractCode(product.Category?.ElevenstRaw));
                var imageCells = BuildElevenstImageCells(product, imageZip, ref hasImageZip);
                if (string.IsNullOrWhiteSpace(categoryCode))
                    notes.Add("11번가 카테고리코드 없음");
                if (imageCells.Count == 0)
                    requiredErrors.Add("대표 이미지 없음(필수)");
                if (hasImageZip && imageCells.Count > 0 && !imageCells[0].StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    notes.Add("Cafe24 600px 미만 URL 대신 1000px 이상 이미지 ZIP 사용");

                var productName = TrimAtWord(product.CommonProductName, 100, out var nameTrimmed);
                if (nameTrimmed)
                    notes.Add("상품명 100자 이하 자동 축약");

                SetText(sheet, rowNumber, 2, categoryCode);
                SetText(sheet, rowNumber, 3, product.GsCode);
                SetText(sheet, rowNumber, 4, product.GsCode);
                SetText(sheet, rowNumber, 5, productName);
                SetText(sheet, rowNumber, 8, imageCells.ElementAtOrDefault(0) ?? "");

                for (var i = 1; i < Math.Min(imageCells.Count, 4); i++)
                    SetText(sheet, rowNumber, 8 + i, imageCells[i]);

                SetText(sheet, rowNumber, 13, product.DetailHtml);
                SetText(sheet, rowNumber, 14, "Y");
                SetText(sheet, rowNumber, 15, "01");
                SetText(sheet, rowNumber, 16, "N");
                SetText(sheet, rowNumber, 17, "01");
                SetText(sheet, rowNumber, 18, "01");
                SetText(sheet, rowNumber, 19, "108");
                SetNumber(sheet, rowNumber, 29, product.Price);

                if (product.Options.Count > 0)
                {
                    SetText(sheet, rowNumber, 31, product.ElevenstOptionCode);
                    SetText(sheet, rowNumber, 32, string.Join("|", product.Options.Select(option => option.Name)));
                    SetText(sheet, rowNumber, 33, string.Join("|", product.Options.Select(option => option.AdditionalPrice.ToString(CultureInfo.InvariantCulture))));
                    SetText(sheet, rowNumber, 34, string.Join("|", product.Options.Select(_ => "999")));
                    SetNumber(sheet, rowNumber, 37, product.Options.Count * 999);
                }
                else
                {
                    SetNumber(sheet, rowNumber, 37, 999);
                }

                if (product.ConsumerPrice > 0)
                    SetNumber(sheet, rowNumber, 41, product.ConsumerPrice);
                SetText(sheet, rowNumber, 42, "홈런market");
                SetText(sheet, rowNumber, 43, "Y");
                SetText(sheet, rowNumber, 44, "01");
                SetText(sheet, rowNumber, 45, product.GsCode);
                SetText(sheet, rowNumber, 46, "02");
                SetText(sheet, rowNumber, 47, "1287");
                SetText(sheet, rowNumber, 50, "01|03\n02|03\n03|03\n04|05");
                SetText(sheet, rowNumber, 51, "01");
                SetText(sheet, rowNumber, 54, "891045");
                SetText(sheet, rowNumber, 55, "11800");
                SetText(sheet, rowNumber, 56, "상품상세설명 참조");
                SetText(sheet, rowNumber, 57, "11905");
                SetText(sheet, rowNumber, 58, "상품상세설명 참조");
                SetText(sheet, rowNumber, 59, "23760413");
                SetText(sheet, rowNumber, 60, "판매자 고객센터 문의");
                SetText(sheet, rowNumber, 61, "23759100");
                SetText(sheet, rowNumber, 62, "중국");
                SetText(sheet, rowNumber, 63, "23756033");
                SetText(sheet, rowNumber, 64, "해당사항 없음");
                SetText(sheet, rowNumber, 100, "01");
                SetText(sheet, rowNumber, 101, "01");
                SetText(sheet, rowNumber, 102, "00034");
                SetText(sheet, rowNumber, 103, "1228104");
                SetText(sheet, rowNumber, 105, "03");
                SetNumber(sheet, rowNumber, 106, 3000);
                SetNumber(sheet, rowNumber, 107, 50000);
                SetText(sheet, rowNumber, 108, "N");
                SetText(sheet, rowNumber, 109, "03");
                SetNumber(sheet, rowNumber, 111, 3000);
                SetText(sheet, rowNumber, 112, "01");
                SetNumber(sheet, rowNumber, 113, 6000);
                SetText(sheet, rowNumber, 114, "상품 상세설명을 참고해 주세요.");
                SetText(sheet, rowNumber, 115, "상품 상세설명 및 판매자 반품/교환 정책을 참고해 주세요.");

                reportRows.Add(new MarketExcelReportRow(
                    "11번가",
                    product.GsCode,
                    productName,
                    requiredErrors.Count > 0 ? "ERROR" : notes.Count == 0 ? "OK" : "WARN",
                    string.Join(" / ", requiredErrors.Concat(notes)),
                    finalXls));

                rowNumber++;
            }

            // 11번가 공식 양식은 작성 후 안내/예시 행(4~5행)을 삭제한 파일만 업로드로 받는다.
            sheet.Rows(4, 5).Delete();
            workbook.SaveAs(workXlsx);
        }

        if (!hasImageZip)
            TryDelete(imageZipPath);

        try
        {
            ConvertXlsxToXls(workXlsx, finalXls);
            File.Delete(workXlsx);
            return new ElevenstExportPaths(finalXls, hasImageZip ? imageZipPath : null);
        }
        catch
        {
            return new ElevenstExportPaths(workXlsx, hasImageZip ? imageZipPath : null);
        }
    }

    private string ExportEsm(
        IReadOnlyList<MarketSourceProduct> products,
        string outputDirectory,
        string stamp,
        EsmFullCategoryCodes esmCodes,
        List<MarketExcelReportRow> reportRows)
    {
        if (!File.Exists(EsmTemplatePath))
            throw new FileNotFoundException("ESM 공식 양식 파일을 찾지 못했습니다.", EsmTemplatePath);

        var outputPath = Path.Combine(outputDirectory, $"ESM_옥션지마켓_업로드_{stamp}.xlsx");
        using var workbook = new XLWorkbook(EsmTemplatePath);
        var sheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name.Trim() == EsmSheetName)
                    ?? workbook.Worksheets.First();

        ClearRows(sheet, 8);

        var rowNumber = 8;
        var sequence = 1;
        foreach (var product in products)
        {
            var notes = new List<string>();
            var requiredErrors = new List<string>();
            var esmPath = product.Category?.EsmPath ?? "";
            var gmarketPath = product.Category?.GmarketPath ?? "";
            var auctionPath = ExtractPath(product.Category?.AuctionRaw ?? "");
            var auctionCode = FirstNonEmpty(
                product.AuctionCategoryCode,
                ExtractCode(product.Category?.AuctionRaw),
                esmCodes.FindSiteCodeBySitePath("A옥션", auctionPath),
                esmCodes.FindSiteCodeBySimilarSitePath("A옥션", auctionPath),
                esmCodes.FindSiteCodeByEsmPath("A옥션", esmPath));
            var esmCode = FirstNonEmpty(
                product.EsmCategoryCode,
                esmCodes.FindEsmCodeBySiteCode("A옥션", auctionCode),
                esmCodes.FindEsmCodeBySiteCode("G마켓", product.GmarketCategoryCode),
                esmCodes.FindEsmCodeByEsmPath(esmPath),
                esmCodes.FindEsmCodeBySitePath("G마켓", gmarketPath),
                esmCodes.FindEsmCodeBySimilarSitePath("G마켓", gmarketPath),
                esmCodes.FindEsmCodeBySitePath("A옥션", auctionPath),
                esmCodes.FindEsmCodeBySimilarSitePath("A옥션", auctionPath));
            var gmarketCode = FirstNonEmpty(
                product.GmarketCategoryCode,
                esmCodes.FindSiteCodeBySitePath("G마켓", gmarketPath),
                esmCodes.FindSiteCodeBySimilarSitePath("G마켓", gmarketPath),
                esmCodes.FindSiteCodeByEsmPath("G마켓", esmPath),
                esmCodes.FindSiteCodeByEsmCode("G마켓", esmCode));

            if (string.IsNullOrWhiteSpace(esmCode))
                notes.Add("ESM 카테고리코드 없음");
            if (string.IsNullOrWhiteSpace(auctionCode))
                notes.Add("A 노출코드 없음");
            if (string.IsNullOrWhiteSpace(gmarketCode))
                notes.Add("G 노출코드 없음");
            if (string.IsNullOrWhiteSpace(product.MainImageUrl))
                requiredErrors.Add("기본이미지 URL 없음(필수)");

            var productName = TrimAtWord(product.CommonProductName, 45, out var nameTrimmed);
            if (nameTrimmed)
                notes.Add("ESM 상품명 45자 이하 자동 축약");

            SetNumber(sheet, rowNumber, 1, sequence);
            SetText(sheet, rowNumber, 2, "옥션/G마켓");
            SetText(sheet, rowNumber, 3, "rkghrud");
            SetText(sheet, rowNumber, 4, "rkghrud");
            SetText(sheet, rowNumber, 5, productName);
            SetText(sheet, rowNumber, 11, esmCode);
            SetText(sheet, rowNumber, 12, auctionCode);
            SetText(sheet, rowNumber, 13, gmarketCode);
            SetText(sheet, rowNumber, 14, "90");
            SetNumber(sheet, rowNumber, 15, product.Price);
            SetNumber(sheet, rowNumber, 16, product.Price);
            SetNumber(sheet, rowNumber, 21, 99999);
            SetNumber(sheet, rowNumber, 22, 99999);

            if (product.Options.Count > 0)
            {
                SetText(sheet, rowNumber, 23, "단독형");
                SetText(sheet, rowNumber, 24, product.EsmOptionName);
                SetText(sheet, rowNumber, 25, string.Join("\n", product.Options.Select(option =>
                    $"{option.Name},정상,노출,99999,99999")));
            }
            else
            {
                SetText(sheet, rowNumber, 23, "미사용");
            }

            SetText(sheet, rowNumber, 26, product.MainImageUrl);
            SetText(sheet, rowNumber, 27, string.Join(",", product.AdditionalImageUrls.Take(9)));
            SetText(sheet, rowNumber, 28, product.DetailHtml);
            SetText(sheet, rowNumber, 30, "일반택배");
            SetText(sheet, rowNumber, 31, "20223695");
            SetText(sheet, rowNumber, 32, "46262933");
            SetText(sheet, rowNumber, 33, "4886443");
            SetText(sheet, rowNumber, 34, "-13");
            SetText(sheet, rowNumber, 35, "-13");
            SetText(sheet, rowNumber, 36, "10013");
            SetNumber(sheet, rowNumber, 37, 3000);
            SetText(sheet, rowNumber, 38, "36");
            SetText(sheet, rowNumber, 39, "235804");
            SetText(sheet, rowNumber, 40, "인증대상아님");
            SetText(sheet, rowNumber, 43, "인증대상아님");
            SetText(sheet, rowNumber, 46, "해당사항없음");
            SetText(sheet, rowNumber, 47, "인증대상아님");
            SetText(sheet, rowNumber, 50, "해당사항없음");
            SetText(sheet, rowNumber, 51, "인증대상아님");
            SetText(sheet, rowNumber, 53, "해당없음");
            SetText(sheet, rowNumber, 54, "해외수입");
            SetText(sheet, rowNumber, 55, "174");
            SetText(sheet, rowNumber, 56, "단일원산지");
            SetText(sheet, rowNumber, 62, "구매가능");
            SetText(sheet, rowNumber, 63, "과세상품");

            reportRows.Add(new MarketExcelReportRow(
                "ESM",
                product.GsCode,
                productName,
                requiredErrors.Count > 0 ? "ERROR" : notes.Count == 0 ? "OK" : "WARN",
                string.Join(" / ", requiredErrors.Concat(notes)),
                outputPath));

            rowNumber++;
            sequence++;
        }

        workbook.SaveAs(outputPath);
        return outputPath;
    }

    private Cafe24MarketDataService? LoadCafe24MarketData(string sourceWorkbookPath, IProgress<string>? progress)
    {
        try
        {
            return Cafe24MarketDataService.TryCreateAsync(
                    sourceWorkbookPath,
                    message => progress?.Report(message),
                    CancellationToken.None,
                    _cafe24TokenPath)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            progress?.Report($"Cafe24 이미지 URL 조회 생략: {ex.Message}");
            return null;
        }
    }

    private List<MarketSourceProduct> LoadSourceProducts(
        string sourceWorkbookPath,
        HashSet<string> selectedGsCodes,
        IReadOnlyDictionary<string, CategoryMatchRecord> categoryMap,
        Cafe24MarketDataService? cafe24Data)
    {
        using var workbook = WorkbookFileLoader.OpenReadOnly(sourceWorkbookPath);
        var sheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name.Trim() == "분리추출후")
                    ?? workbook.Worksheets.First();
        var headers = BuildHeaderMap(sheet, 1);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        var products = new List<MarketSourceProduct>();

        for (var row = 2; row <= lastRow; row++)
        {
            var gsCode = ExtractGsCode(FirstNonEmpty(
                GetCell(sheet, headers, row, "자체 상품코드"),
                GetCell(sheet, headers, row, "상품코드"),
                GetCell(sheet, headers, row, "상품명")));
            if (string.IsNullOrWhiteSpace(gsCode))
                continue;
            if (!selectedGsCodes.Contains(gsCode))
                continue;

            var rowValues = BuildRowValues(sheet, headers, row);
            if (cafe24Data is not null)
            {
                try
                {
                    cafe24Data.TryApplyAsync(rowValues, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch
                {
                    // Cafe24 조회 실패 시 해당 상품은 이미지 URL 없음 경고로 처리한다.
                }
            }

            categoryMap.TryGetValue(gsCode, out var category);
            var productName = FirstNonEmpty(
                GetRowValue(rowValues, "홈런_공통마켓상품명"),
                GetRowValue(rowValues, "공통마켓상품명"),
                GetRowValue(rowValues, "상품명"));
            var detailHtml = BuildDetailHtml(GetRowValue(rowValues, "상품 상세설명", "상세설명"));
            var cafe24Images = GetCafe24ImageUrls(rowValues);
            var mainImage = ResolveMainImageUrl(gsCode, rowValues, detailHtml, cafe24Images);
            var additionalImages = ResolveAdditionalImageUrls(mainImage, cafe24Images);
            var localListingImages = FindListingImages(ResolveExportRoot(sourceWorkbookPath), gsCode);

            var options = ParseOptions(
                GetRowValue(rowValues, "옵션입력"),
                GetRowValue(rowValues, "옵션추가금"));
            var optionName = InferOptionName(options);

            var product = new MarketSourceProduct
            {
                SourceRow = row,
                GsCode = gsCode,
                CommonProductName = NormalizeSpaces(productName),
                Price = ResolvePrice(GetRowValue(rowValues, "판매가"), GetRowValue(rowValues, "상품가")),
                ConsumerPrice = ResolvePrice(GetRowValue(rowValues, "소비자가"), ""),
                MainImageUrl = mainImage,
                AdditionalImageUrls = additionalImages,
                LocalListingImagePaths = localListingImages,
                DetailHtml = detailHtml,
                Options = options,
                EsmOptionName = optionName.Name,
                ElevenstOptionCode = optionName.ElevenstCode,
                Category = category,
                ElevenstCategoryCode = ExtractCode(GetRowValue(rowValues, "11번가카테고리코드", "11번가카테고리코드/경로")),
                EsmCategoryCode = ExtractCode(GetRowValue(rowValues, "ESM카테고리코드", "ESM카테고리코드/경로")),
                AuctionCategoryCode = ExtractCode(GetRowValue(rowValues, "옥션카테고리코드", "옥션카테고리코드/경로")),
                GmarketCategoryCode = ExtractCode(GetRowValue(rowValues, "G마켓카테고리코드", "G마켓카테고리코드/경로")),
            };

            if (product.ConsumerPrice <= 0 && product.Price > 0)
                product.ConsumerPrice = RoundToTen(product.Price * 1.2m);

            products.Add(product);
        }

        return products;
    }

    private IReadOnlyDictionary<string, CategoryMatchRecord> LoadCategoryMatchMap(string sourceWorkbookPath)
    {
        var result = new Dictionary<string, CategoryMatchRecord>(StringComparer.OrdinalIgnoreCase);
        var candidates = FindCategoryMatchCandidates(sourceWorkbookPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var candidate in candidates)
        {
            try
            {
                using var workbook = WorkbookFileLoader.OpenReadOnly(candidate);
                var sheet = workbook.Worksheets.FirstOrDefault();
                if (sheet is null)
                    continue;
                var headers = BuildHeaderMap(sheet, 1);
                var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
                for (var row = 2; row <= lastRow; row++)
                {
                    var gsCode = ExtractGsCode(GetCell(sheet, headers, row, "상품코드", "자체 상품코드"));
                    if (string.IsNullOrWhiteSpace(gsCode) || result.ContainsKey(gsCode))
                        continue;

                    result[gsCode] = new CategoryMatchRecord(
                        FirstNonEmpty(
                            GetCell(sheet, headers, row, "11번가카테고리코드/경로", "11번가카테고리"),
                            JoinCodePath(
                                GetCell(sheet, headers, row, "11번가카테고리코드"),
                                GetCell(sheet, headers, row, "11번가카테고리경로"))),
                        FirstNonEmpty(
                            GetCell(sheet, headers, row, "옥션카테고리코드/경로", "옥션카테고리"),
                            JoinCodePath(
                                GetCell(sheet, headers, row, "옥션카테고리코드"),
                                GetCell(sheet, headers, row, "옥션카테고리경로"))),
                        FirstNonEmpty(
                            GetCell(sheet, headers, row, "G마켓카테고리코드/경로"),
                            JoinCodePath(
                                GetCell(sheet, headers, row, "G마켓카테고리코드"),
                                GetCell(sheet, headers, row, "G마켓카테고리경로")),
                            GetCell(sheet, headers, row, "G마켓카테고리경로")),
                        FirstNonEmpty(
                            GetCell(sheet, headers, row, "ESM카테고리코드/경로"),
                            JoinCodePath(
                                GetCell(sheet, headers, row, "ESM카테고리코드"),
                                GetCell(sheet, headers, row, "ESM카테고리경로")),
                            GetCell(sheet, headers, row, "ESM카테고리경로")));
                }
            }
            catch
            {
                // 다음 후보 파일을 시도한다.
            }
        }

        return result;
    }

    private EsmFullCategoryCodes LoadEsmFullCategoryCodes(IProgress<string>? progress)
    {
        var path = Path.Combine(_projectRoot, "data", "category_reference", "esm_full_category_codes.xls");
        if (!File.Exists(path))
        {
            progress?.Report("ESM 전체 카테고리 코드 파일이 없어 카테고리 보강을 건너뜁니다.");
            return EsmFullCategoryCodes.Empty;
        }

        object? excelObject = null;
        object? workbookObject = null;
        object? worksheetObject = null;

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
            dynamic worksheet = workbook.Worksheets[1];
            worksheetObject = worksheet;
            dynamic usedRange = worksheet.UsedRange;
            object[,] values = usedRange.Value2;
            var rowCount = values.GetLength(0);

            var entries = new List<EsmFullCategoryEntry>(rowCount);
            for (var row = 2; row <= rowCount; row++)
            {
                var esmPath = GetExcelArrayValue(values, row, 1);
                var sitePath = GetExcelArrayValue(values, row, 2);
                var site = GetExcelArrayValue(values, row, 3);
                var esmCode = GetExcelArrayValue(values, row, 4);
                var siteCode = GetExcelArrayValue(values, row, 5);
                if (string.IsNullOrWhiteSpace(esmPath) || string.IsNullOrWhiteSpace(site) || string.IsNullOrWhiteSpace(esmCode))
                    continue;
                entries.Add(new EsmFullCategoryEntry(esmPath, sitePath, site, esmCode, siteCode));
            }

            progress?.Report($"ESM 전체 카테고리 코드 로드: {entries.Count:N0}개");
            return new EsmFullCategoryCodes(entries);
        }
        catch (Exception ex)
        {
            progress?.Report($"ESM 카테고리 코드 로드 실패: {ex.Message}");
            return EsmFullCategoryCodes.Empty;
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
            ReleaseComObject(worksheetObject);
            ReleaseComObject(workbookObject);
            ReleaseComObject(excelObject);
        }
    }

    private static string GetExcelArrayValue(object[,] values, int row, int column)
        => (Convert.ToString(values[row, column], CultureInfo.InvariantCulture) ?? "").Trim();

    private static IEnumerable<string> FindCategoryMatchCandidates(string sourceWorkbookPath)
    {
        var dirs = new List<string>();
        var current = Path.GetDirectoryName(sourceWorkbookPath);
        for (var i = 0; i < 4 && !string.IsNullOrWhiteSpace(current); i++)
        {
            dirs.Add(current);
            current = Path.GetDirectoryName(current);
        }

        var sourceStem = Path.GetFileNameWithoutExtension(sourceWorkbookPath);
        var sourcePrefix = GetCategoryMatchSourcePrefix(sourceStem);

        foreach (var dir in dirs.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(sourcePrefix))
            {
                foreach (var file in Directory.GetFiles(dir, $"{sourcePrefix}*category_match*.xlsx", SearchOption.TopDirectoryOnly)
                             .OrderByDescending(File.GetLastWriteTimeUtc))
                {
                    yield return file;
                }
            }

            foreach (var name in new[] { "category_match_v5_cli.xlsx", "category_match_v4_cli.xlsx" })
            {
                var direct = Path.Combine(dir, name);
                if (File.Exists(direct))
                    yield return direct;

                var final = Path.Combine(dir, "final", name);
                if (File.Exists(final))
                    yield return final;
            }

            foreach (var file in Directory.GetFiles(dir, "*category_match*.xlsx", SearchOption.TopDirectoryOnly)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                yield return file;
            }
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

    private static string ResolveMarketOutputDirectory(string sourceWorkbookPath)
    {
        var dir = Path.GetDirectoryName(sourceWorkbookPath)
                  ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var root = dir;
        var name = Path.GetFileName(root);
        if (name.StartsWith("llm_result_", StringComparison.OrdinalIgnoreCase)
            || name.Equals("final", StringComparison.OrdinalIgnoreCase))
        {
            root = Path.GetDirectoryName(root) ?? dir;
            if (name.Equals("final", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(root).StartsWith("llm_result_", StringComparison.OrdinalIgnoreCase))
            {
                root = Path.GetDirectoryName(root) ?? dir;
            }
        }

        return Path.Combine(root, "market_uploads");
    }

    private static string SaveReport(string outputDirectory, string stamp, IReadOnlyList<MarketExcelReportRow> rows)
    {
        var path = Path.Combine(outputDirectory, $"마켓엑셀_검수리포트_{stamp}.xlsx");
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("검수리포트");
        var headers = new[] { "마켓", "GS코드", "상품명", "상태", "메모", "파일" };
        for (var col = 1; col <= headers.Length; col++)
            sheet.Cell(1, col).SetValue(headers[col - 1]);

        var row = 2;
        foreach (var item in rows)
        {
            sheet.Cell(row, 1).SetValue(item.Market);
            sheet.Cell(row, 2).SetValue(item.GsCode);
            sheet.Cell(row, 3).SetValue(item.ProductName);
            sheet.Cell(row, 4).SetValue(item.Status);
            sheet.Cell(row, 5).SetValue(item.Memo);
            sheet.Cell(row, 6).SetValue(item.FilePath);
            row++;
        }

        sheet.Columns().AdjustToContents(8, 60);
        workbook.SaveAs(path);
        return path;
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLWorksheet sheet, int headerRow)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastColumn = sheet.Row(headerRow).LastCellUsed()?.Address.ColumnNumber ?? 0;
        for (var col = 1; col <= lastColumn; col++)
        {
            var header = sheet.Cell(headerRow, col).GetFormattedString().Trim();
            if (!string.IsNullOrWhiteSpace(header) && !result.ContainsKey(header))
                result[header] = col;
        }
        return result;
    }

    private static string GetCell(IXLWorksheet sheet, Dictionary<string, int> headers, int row, params string[] names)
    {
        foreach (var name in names)
        {
            if (headers.TryGetValue(name, out var col))
                return sheet.Cell(row, col).GetFormattedString().Trim();
        }
        return "";
    }

    private static Dictionary<string, object?> BuildRowValues(IXLWorksheet sheet, Dictionary<string, int> headers, int row)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (header, col) in headers)
        {
            result[header] = sheet.Cell(row, col).GetFormattedString().Trim();
        }
        return result;
    }

    private static string GetRowValue(IReadOnlyDictionary<string, object?> row, params string[] names)
    {
        foreach (var name in names)
        {
            if (row.TryGetValue(name, out var value) && value is not null)
                return value.ToString()?.Trim() ?? "";
        }
        return "";
    }

    private static List<string> GetCafe24ImageUrls(IReadOnlyDictionary<string, object?> row)
    {
        if (!row.TryGetValue("_cafe24_image_urls", out var value) || value is not IEnumerable<string> urls)
            return new List<string>();

        return urls
            .Select(url => url?.Trim() ?? "")
            .Where(MarketImageUrlGuard.IsAllowedUploadUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(9)
            .ToList();
    }

    private static List<string> ResolveAdditionalImageUrls(string mainImageUrl, IReadOnlyList<string> cafe24Images)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var main = (mainImageUrl ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(main))
            seen.Add(main);

        foreach (var image in cafe24Images)
        {
            var clean = (image ?? "").Trim();
            if (!MarketImageUrlGuard.IsAllowedUploadUrl(clean))
                continue;
            if (seen.Add(clean))
                result.Add(clean);
            if (result.Count >= 9)
                break;
        }

        return result;
    }

    private static IReadOnlyList<string> BuildElevenstImageCells(
        MarketSourceProduct product,
        ZipArchive imageZip,
        ref bool hasImageZip)
    {
        var remoteImages = new[] { product.MainImageUrl }
            .Concat(product.AdditionalImageUrls)
            .Where(MarketImageUrlGuard.IsAllowedUploadUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (remoteImages.Count > 0 && IsUploadSizedImage(remoteImages[0]))
        {
            var sizedRemoteImages = remoteImages
                .Where(IsUploadSizedImage)
                .Take(4)
                .ToList();
            if (sizedRemoteImages.Count > 0)
                return sizedRemoteImages;
        }

        var localImages = product.LocalListingImagePaths
            .Where(IsLocalUploadSizedImage)
            .Take(4)
            .ToList();
        if (localImages.Count == 0)
            return remoteImages.Take(4).ToList();

        var cells = new List<string>();
        for (var i = 0; i < localImages.Count; i++)
        {
            var fileName = BuildElevenstZipImageName(product.GsCode, i);
            AddFileToZip(imageZip, localImages[i], fileName);
            cells.Add(fileName);
            hasImageZip = true;
        }

        return cells;
    }

    private static string BuildElevenstZipImageName(string gsCode, int index)
    {
        var safeCode = Regex.Replace(NormalizeGsCode(gsCode), @"[^A-Z0-9]", "");
        var suffix = index == 0 ? "main" : $"add{index}";
        return $"{safeCode}_{suffix}.jpg";
    }

    private static void AddFileToZip(ZipArchive archive, string sourcePath, string entryName)
    {
        archive.CreateEntryFromFile(sourcePath, entryName, CompressionLevel.Optimal);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 임시/빈 파일 삭제 실패는 업로드 파일 생성 자체를 막지 않는다.
        }
    }

    private static bool IsLocalUploadSizedImage(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault();
            return frame is not null && frame.PixelWidth >= 600 && frame.PixelHeight >= 600;
        }
        catch
        {
            return false;
        }
    }

    private static List<string> FindListingImages(string exportRoot, string gsCode)
    {
        if (string.IsNullOrWhiteSpace(exportRoot) || string.IsNullOrWhiteSpace(gsCode))
            return new List<string>();

        var gsBase = Regex.Replace(gsCode.Trim(), @"[A-Z]$", "", RegexOptions.IgnoreCase);
        var selection = LoadImageSelection(exportRoot, gsBase);
        var listingRoot = Path.Combine(exportRoot, "listing_images");
        if (!Directory.Exists(listingRoot))
            return new List<string>();

        var searchDirs = new List<string> { listingRoot };
        try
        {
            searchDirs.AddRange(Directory.GetDirectories(listingRoot));
        }
        catch
        {
            // 폴더 검색 실패 시 기본 listing_images만 사용한다.
        }

        foreach (var dir in searchDirs)
        {
            var gsFolder = Path.Combine(dir, gsBase);
            if (!Directory.Exists(gsFolder))
                gsFolder = Path.Combine(dir, gsCode);
            if (!Directory.Exists(gsFolder))
                continue;

            var allFiles = Directory.GetFiles(gsFolder)
                .Where(file => Regex.IsMatch(file, @"\.(jpg|jpeg|png|bmp|webp)$", RegexOptions.IgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (allFiles.Count == 0)
                continue;

            if (selection?.MainIndex is not null)
            {
                var (mainPath, addPaths) = Cafe24UploadSupport.PickImagesBySelection(gsFolder, selection);
                if (mainPath is not null)
                {
                    var selected = new List<string> { mainPath };
                    selected.AddRange(addPaths);
                    return selected;
                }
            }

            return allFiles;
        }

        return new List<string>();
    }

    private static ImageSelection? LoadImageSelection(string exportRoot, string gsBase)
    {
        var selectionsPath = Path.Combine(exportRoot, "image_selections.json");
        if (!File.Exists(selectionsPath))
            return null;

        try
        {
            var json = File.ReadAllText(selectionsPath);
            using var doc = JsonDocument.Parse(json);
            var gs9 = gsBase.Length >= 9 ? gsBase[..9] : gsBase;
            if (!doc.RootElement.TryGetProperty(gs9, out var sel))
                return null;

            int? mainIdx = sel.TryGetProperty("main", out var main) && main.ValueKind == JsonValueKind.Number
                ? main.GetInt32()
                : null;
            int? mainIdxB = sel.TryGetProperty("mainB", out var mainB) && mainB.ValueKind == JsonValueKind.Number
                ? mainB.GetInt32()
                : null;
            var addIndices = new List<int>();
            if (sel.TryGetProperty("additional", out var addArr) && addArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in addArr.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number)
                        addIndices.Add(item.GetInt32());
                }
            }

            return new ImageSelection(mainIdx, addIndices, mainIdxB);
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveExportRoot(string sourceWorkbookPath)
    {
        var file = Path.GetFullPath(sourceWorkbookPath);
        var parent = Path.GetDirectoryName(file) ?? Directory.GetCurrentDirectory();
        var parentName = Path.GetFileName(parent);
        var grandParent = Path.GetDirectoryName(parent) ?? parent;
        var grandName = Path.GetFileName(grandParent);
        if (parentName.StartsWith("llm_result", StringComparison.OrdinalIgnoreCase) && grandName == "llm_chunks")
            return Path.GetDirectoryName(grandParent) ?? grandParent;
        if (parentName.StartsWith("llm_result", StringComparison.OrdinalIgnoreCase))
            return grandParent;
        return parent;
    }

    private static List<OptionExportItem> ParseOptions(string optionInput, string optionPrices)
    {
        if (string.IsNullOrWhiteSpace(optionInput))
            return new List<OptionExportItem>();

        var source = optionInput.Trim();
        var match = Regex.Match(source, @"\{(?<body>.+)\}", RegexOptions.Singleline);
        if (match.Success)
            source = match.Groups["body"].Value;

        var rawValues = source
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (rawValues.Count == 0)
            return new List<OptionExportItem>();

        var prices = optionPrices
            .Split('|', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m)
            .ToList();

        var result = new List<OptionExportItem>();
        for (var i = 0; i < rawValues.Count; i++)
        {
            var name = EnsureAlphabetPrefix(rawValues[i], i);
            result.Add(new OptionExportItem(name, i < prices.Count ? prices[i] : 0m));
        }
        return result;
    }

    private static (string Name, string ElevenstCode) InferOptionName(IReadOnlyList<OptionExportItem> options)
    {
        if (options.Count == 0)
            return ("종류", "23");

        var values = options.Select(option => StripOptionPrefix(option.Name)).ToList();
        if (values.All(value => Regex.IsMatch(value, @"\d{2,3}")))
            return ("사이즈", "02");

        var colorWords = new[]
        {
            "블랙", "화이트", "카키", "핑크", "스카이", "옐로우", "골드", "실버", "레드", "그린", "블루", "그레이",
            "투명", "브라운", "베이지", "네이비", "퍼플", "오렌지"
        };
        if (values.Any(value => colorWords.Any(color => value.Contains(color, StringComparison.OrdinalIgnoreCase))))
            return ("색상", "01");

        return ("종류", "23");
    }

    private static string EnsureAlphabetPrefix(string value, int index)
    {
        var normalized = NormalizeSpaces(value);
        if (OptionPrefixRegex.IsMatch(normalized))
            return normalized;

        var prefix = index < 26 ? ((char)('A' + index)).ToString() : $"A{index + 1}";
        return $"{prefix} {normalized}".Trim();
    }

    private static string StripOptionPrefix(string value)
        => OptionPrefixRegex.Replace(value ?? "", "").Trim();

    private static string BuildDetailHtml(string sourceDetailHtml)
    {
        var html = MarketImageUrlGuard.RemoveUnsafeImageTags(sourceDetailHtml).Trim();
        if (html.Contains(DetailIntroImageUrl, StringComparison.OrdinalIgnoreCase))
            return html;

        var intro = $"<center><img src='{DetailIntroImageUrl}' /></center>";
        if (string.IsNullOrWhiteSpace(html))
            return intro;
        return intro + html;
    }

    private static List<string> ExtractImageUrls(string value)
        => ImageUrlRegex.Matches(value ?? "")
            .Select(match => match.Value.Trim())
            .Where(MarketImageUrlGuard.IsAllowedUploadUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string ResolveMainImageUrl(
        string gsCode,
        IReadOnlyDictionary<string, object?> rowValues,
        string detailHtml,
        IReadOnlyList<string> cafe24Images)
    {
        var candidates = new List<string>();
        candidates.AddRange(cafe24Images);
        candidates.AddRange(FindGsRepresentativeUrls(gsCode, detailHtml));
        candidates.AddRange(BuildGsRepresentativeUrlCandidates(gsCode, detailHtml));
        candidates.AddRange(FindGsRepresentativeUrls(gsCode, GetRowValue(rowValues,
            "이미지등록(목록)",
            "이미지등록(추가)",
            "이미지등록(상세)",
            "상품 상세설명",
            "상세설명")));
        candidates.AddRange(ExtractImageUrls(detailHtml)
            .Where(url => !string.Equals(url, DetailIntroImageUrl, StringComparison.OrdinalIgnoreCase)));

        var unique = candidates
            .Where(MarketImageUrlGuard.IsAllowedUploadUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var url in unique)
        {
            if (IsUploadSizedImage(url))
                return url;
        }

        return unique.FirstOrDefault() ?? "";
    }

    private static IEnumerable<string> FindGsRepresentativeUrls(string gsCode, string value)
    {
        if (string.IsNullOrWhiteSpace(gsCode))
            return Array.Empty<string>();

        var code = NormalizeGsCode(gsCode);
        var expected = $"{code}1";
        return ExtractImageUrls(value)
            .Where(url =>
            {
                var file = GetUrlFileStem(url);
                return string.Equals(file, expected, StringComparison.OrdinalIgnoreCase)
                       || file.StartsWith(expected, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static IEnumerable<string> BuildGsRepresentativeUrlCandidates(string gsCode, string value)
    {
        if (string.IsNullOrWhiteSpace(gsCode))
            return Array.Empty<string>();

        var code = NormalizeGsCode(gsCode);
        var candidates = new List<string>();
        foreach (var url in ExtractImageUrls(value))
        {
            try
            {
                var uri = new Uri(url);
                var path = uri.LocalPath.Replace('\\', '/');
                var folderMarker = "/" + code + "/";
                var folderIndex = path.IndexOf(folderMarker, StringComparison.OrdinalIgnoreCase);
                if (folderIndex < 0)
                    continue;

                var prefixPath = path[..(folderIndex + folderMarker.Length)];
                var candidatePath = prefixPath + code + "1.jpg";
                var builder = new UriBuilder(uri) { Path = candidatePath, Query = "" };
                candidates.Add(builder.Uri.ToString());
            }
            catch
            {
                // URL 후보 생성 실패는 다음 이미지로 넘어간다.
            }
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string GetUrlFileStem(string url)
    {
        try
        {
            var uri = new Uri(url);
            return Path.GetFileNameWithoutExtension(uri.LocalPath);
        }
        catch
        {
            return Path.GetFileNameWithoutExtension(url);
        }
    }

    private static bool IsUploadSizedImage(string url)
    {
        var info = GetImageDimensionInfo(url);
        return info.IsImage && info.Width >= 600 && info.Height >= 600;
    }

    private static ImageDimensionInfo GetImageDimensionInfo(string url)
    {
        lock (ImageDimensionCache)
        {
            if (ImageDimensionCache.TryGetValue(url, out var cached))
                return cached;
        }

        var info = ReadImageDimensionInfo(url);
        lock (ImageDimensionCache)
        {
            ImageDimensionCache[url] = info;
        }
        return info;
    }

    private static ImageDimensionInfo ReadImageDimensionInfo(string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            using var response = ImageHttpClient.Send(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode)
                return ImageDimensionInfo.NotImage;
            using var stream = response.Content.ReadAsStream(cts.Token);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault();
            return frame is null
                ? ImageDimensionInfo.NotImage
                : new ImageDimensionInfo(true, frame.PixelWidth, frame.PixelHeight);
        }
        catch
        {
            return ImageDimensionInfo.NotImage;
        }
    }

    private static HttpClient CreateImageHttpClient()
        => new(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

    private static string FirstPublicUrl(string value)
        => ExtractImageUrls(value).FirstOrDefault() ?? "";

    private static bool IsPublicUrl(string value)
        => value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
           || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static decimal ResolvePrice(string primary, string fallback)
    {
        foreach (var value in new[] { primary, fallback })
        {
            var normalized = Regex.Replace(value ?? "", @"[^\d.]", "");
            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                return Math.Round(parsed, 0, MidpointRounding.AwayFromZero);
        }
        return 0m;
    }

    private static decimal RoundToTen(decimal value)
        => Math.Round(value / 10m, 0, MidpointRounding.AwayFromZero) * 10m;

    private static string ExtractGsCode(string value)
    {
        var match = GsCodeRegex.Match(value ?? "");
        return match.Success ? NormalizeGsCode(match.Value) : "";
    }

    private static string NormalizeGsCode(string value)
        => (value ?? "").Trim().ToUpperInvariant();

    private static string ExtractCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var text = value.Trim();
        var slash = text.IndexOf(" / ", StringComparison.Ordinal);
        if (slash > 0)
            text = text[..slash].Trim();

        var match = Regex.Match(text, @"\d{5,}");
        return match.Success ? match.Value : "";
    }

    private static string ExtractPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var slash = value.IndexOf(" / ", StringComparison.Ordinal);
        return slash >= 0 ? value[(slash + 3)..].Trim() : value.Trim();
    }

    private static string NormalizeCategoryPath(string value)
        => Regex.Replace(ExtractPath(value), @"\s+", "")
            .Replace("＞", ">", StringComparison.Ordinal)
            .Replace("〉", ">", StringComparison.Ordinal)
            .Trim();

    private static string NormalizeSpaces(string value)
        => Regex.Replace(value ?? "", @"\s+", " ").Trim();

    private static string TrimAtWord(string value, int maxLength, out bool trimmed)
    {
        var text = NormalizeSpaces(value);
        trimmed = false;
        if (text.Length <= maxLength)
            return text;

        trimmed = true;
        var cut = text[..maxLength].Trim();
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace >= Math.Min(20, maxLength - 8))
            cut = cut[..lastSpace].Trim();
        return cut;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string JoinCodePath(string? code, string? path)
    {
        var cleanCode = NormalizeSpaces(code ?? "");
        var cleanPath = NormalizeSpaces(path ?? "");
        if (string.IsNullOrWhiteSpace(cleanCode))
            return cleanPath;
        if (string.IsNullOrWhiteSpace(cleanPath))
            return cleanCode;
        return $"{cleanCode} / {cleanPath}";
    }

    private static void ClearRows(IXLWorksheet sheet, int startRow)
    {
        var lastRow = Math.Max(sheet.LastRowUsed()?.RowNumber() ?? startRow, startRow + 500);
        for (var row = startRow; row <= lastRow; row++)
            sheet.Row(row).Clear(XLClearOptions.Contents);
    }

    private static void SetText(IXLWorksheet sheet, int row, int column, string? value)
    {
        var cell = sheet.Cell(row, column);
        cell.Style.NumberFormat.Format = "@";
        cell.SetValue(value ?? "");
    }

    private static void SetNumber(IXLWorksheet sheet, int row, int column, decimal value)
        => sheet.Cell(row, column).SetValue(value);

    private static void ConvertXlsxToXls(string sourceXlsx, string targetXls)
    {
        object? excel = null;
        object? workbook = null;
        try
        {
            var excelType = Type.GetTypeFromProgID("Excel.Application")
                            ?? throw new InvalidOperationException("Excel COM을 사용할 수 없습니다.");
            excel = Activator.CreateInstance(excelType);
            SetProperty(excel!, "Visible", false);
            SetProperty(excel!, "DisplayAlerts", false);
            var workbooks = GetProperty(excel!, "Workbooks");
            workbook = Invoke(workbooks!, "Open", sourceXlsx);
            Invoke(workbook!, "SaveAs", targetXls, 56);
        }
        finally
        {
            if (workbook is not null)
            {
                try { Invoke(workbook, "Close", false); } catch { }
            }
            if (excel is not null)
            {
                try { Invoke(excel, "Quit"); } catch { }
            }
            ReleaseComObject(workbook);
            ReleaseComObject(excel);
        }
    }

    private static object? GetProperty(object target, string name)
        => target.GetType().InvokeMember(name, System.Reflection.BindingFlags.GetProperty, null, target, Array.Empty<object>());

    private static void SetProperty(object target, string name, object value)
        => target.GetType().InvokeMember(name, System.Reflection.BindingFlags.SetProperty, null, target, new[] { value });

    private static object? Invoke(object target, string name, params object?[] args)
        => target.GetType().InvokeMember(name, System.Reflection.BindingFlags.InvokeMethod, null, target, args);

    private static string GetExcelText(object worksheet, int row, int column)
    {
        var cells = GetProperty(worksheet, "Cells");
        var cell = Invoke(cells!, "Item", row, column);
        var text = GetProperty(cell!, "Text")?.ToString() ?? "";
        ReleaseComObject(cell);
        return text.Trim();
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try { Marshal.FinalReleaseComObject(value); } catch { }
        }
    }

    private sealed class MarketSourceProduct
    {
        public int SourceRow { get; init; }
        public string GsCode { get; init; } = "";
        public string CommonProductName { get; init; } = "";
        public decimal Price { get; init; }
        public decimal ConsumerPrice { get; set; }
        public string MainImageUrl { get; init; } = "";
        public IReadOnlyList<string> AdditionalImageUrls { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> LocalListingImagePaths { get; init; } = Array.Empty<string>();
        public string DetailHtml { get; init; } = "";
        public IReadOnlyList<OptionExportItem> Options { get; init; } = Array.Empty<OptionExportItem>();
        public string EsmOptionName { get; init; } = "종류";
        public string ElevenstOptionCode { get; init; } = "23";
        public CategoryMatchRecord? Category { get; init; }
        public string ElevenstCategoryCode { get; init; } = "";
        public string EsmCategoryCode { get; init; } = "";
        public string AuctionCategoryCode { get; init; } = "";
        public string GmarketCategoryCode { get; init; } = "";
    }

    private sealed record OptionExportItem(string Name, decimal AdditionalPrice);

    private sealed record ElevenstExportPaths(string ExcelPath, string? ImageZipPath);

    private sealed record CategoryMatchRecord(
        string ElevenstRaw,
        string AuctionRaw,
        string GmarketPath,
        string EsmPath);

    private sealed record MarketExcelReportRow(
        string Market,
        string GsCode,
        string ProductName,
        string Status,
        string Memo,
        string FilePath);

    private sealed record ImageDimensionInfo(bool IsImage, int Width, int Height)
    {
        public static readonly ImageDimensionInfo NotImage = new(false, 0, 0);
    }

    private sealed record EsmFullCategoryEntry(
        string EsmPath,
        string SitePath,
        string Site,
        string EsmCode,
        string SiteCode);

    private sealed class EsmFullCategoryCodes
    {
        public static readonly EsmFullCategoryCodes Empty = new(Array.Empty<EsmFullCategoryEntry>());

        private readonly Dictionary<string, EsmFullCategoryEntry> _byEsmPathAndSite;
        private readonly Dictionary<string, EsmFullCategoryEntry> _bySitePathAndSite;
        private readonly Dictionary<string, EsmFullCategoryEntry> _byEsmCodeAndSite;
        private readonly Dictionary<string, EsmFullCategoryEntry> _bySiteCodeAndSite;
        private readonly Dictionary<string, List<EsmFullCategoryEntry>> _entriesBySite;

        public EsmFullCategoryCodes(IEnumerable<EsmFullCategoryEntry> entries)
        {
            _byEsmPathAndSite = new Dictionary<string, EsmFullCategoryEntry>(StringComparer.OrdinalIgnoreCase);
            _bySitePathAndSite = new Dictionary<string, EsmFullCategoryEntry>(StringComparer.OrdinalIgnoreCase);
            _byEsmCodeAndSite = new Dictionary<string, EsmFullCategoryEntry>(StringComparer.OrdinalIgnoreCase);
            _bySiteCodeAndSite = new Dictionary<string, EsmFullCategoryEntry>(StringComparer.OrdinalIgnoreCase);
            _entriesBySite = new Dictionary<string, List<EsmFullCategoryEntry>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                if (!_entriesBySite.TryGetValue(entry.Site, out var siteEntries))
                {
                    siteEntries = new List<EsmFullCategoryEntry>();
                    _entriesBySite[entry.Site] = siteEntries;
                }
                siteEntries.Add(entry);

                var esmKey = BuildKey(entry.Site, NormalizeCategoryPath(entry.EsmPath));
                if (!_byEsmPathAndSite.ContainsKey(esmKey))
                    _byEsmPathAndSite[esmKey] = entry;

                var siteKey = BuildKey(entry.Site, NormalizeCategoryPath(entry.SitePath));
                if (!_bySitePathAndSite.ContainsKey(siteKey))
                    _bySitePathAndSite[siteKey] = entry;

                var codeKey = BuildKey(entry.Site, NormalizeSpaces(entry.EsmCode));
                if (!_byEsmCodeAndSite.ContainsKey(codeKey))
                    _byEsmCodeAndSite[codeKey] = entry;

                var siteCodeKey = BuildKey(entry.Site, NormalizeSpaces(entry.SiteCode));
                if (!string.IsNullOrWhiteSpace(entry.SiteCode) && !_bySiteCodeAndSite.ContainsKey(siteCodeKey))
                    _bySiteCodeAndSite[siteCodeKey] = entry;
            }
        }

        public string FindEsmCodeByEsmPath(string path)
            => FindByEsmPath("A옥션", path)?.EsmCode
               ?? FindByEsmPath("G마켓", path)?.EsmCode
               ?? "";

        public string FindEsmCodeBySitePath(string site, string path)
            => FindBySitePath(site, path)?.EsmCode ?? "";

        public string FindEsmCodeBySimilarSitePath(string site, string path)
            => FindBySimilarSitePath(site, path)?.EsmCode ?? "";

        public string FindEsmCodeBySiteCode(string site, string siteCode)
        {
            var normalized = NormalizeSpaces(siteCode ?? "");
            return string.IsNullOrWhiteSpace(normalized)
                ? ""
                : _bySiteCodeAndSite.GetValueOrDefault(BuildKey(site, normalized))?.EsmCode ?? "";
        }

        public string FindSiteCodeByEsmPath(string site, string path)
            => FindByEsmPath(site, path)?.SiteCode ?? "";

        public string FindSiteCodeBySitePath(string site, string path)
            => FindBySitePath(site, path)?.SiteCode ?? "";

        public string FindSiteCodeBySimilarSitePath(string site, string path)
            => FindBySimilarSitePath(site, path)?.SiteCode ?? "";

        public string FindSiteCodeByEsmCode(string site, string esmCode)
        {
            var normalized = NormalizeSpaces(esmCode ?? "");
            return string.IsNullOrWhiteSpace(normalized)
                ? ""
                : _byEsmCodeAndSite.GetValueOrDefault(BuildKey(site, normalized))?.SiteCode ?? "";
        }

        private EsmFullCategoryEntry? FindByEsmPath(string site, string path)
        {
            var normalized = NormalizeCategoryPath(path);
            return string.IsNullOrWhiteSpace(normalized)
                ? null
                : _byEsmPathAndSite.GetValueOrDefault(BuildKey(site, normalized));
        }

        private EsmFullCategoryEntry? FindBySitePath(string site, string path)
        {
            var normalized = NormalizeCategoryPath(path);
            return string.IsNullOrWhiteSpace(normalized)
                ? null
                : _bySitePathAndSite.GetValueOrDefault(BuildKey(site, normalized));
        }

        private EsmFullCategoryEntry? FindBySimilarSitePath(string site, string path)
        {
            var tokens = BuildPathTokens(path);
            if (tokens.Count == 0 || !_entriesBySite.TryGetValue(site, out var entries))
                return null;

            return entries
                .Select(entry => (Entry: entry, Score: ScorePathTokens(tokens, BuildPathTokens(entry.SitePath))))
                .Where(item => item.Score >= 4)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Entry.SitePath.Length)
                .Select(item => item.Entry)
                .FirstOrDefault();
        }

        private static string BuildKey(string site, string path)
            => $"{site.Trim()}|{path}";

        private static HashSet<string> BuildPathTokens(string path)
        {
            var normalized = NormalizeCategoryPath(path);
            if (string.IsNullOrWhiteSpace(normalized))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return normalized
                .Split(new[] { '>', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(token => token.Trim())
                .Where(token => token.Length >= 2)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static int ScorePathTokens(HashSet<string> desired, HashSet<string> candidate)
        {
            if (desired.Count == 0 || candidate.Count == 0)
                return 0;

            var score = desired.Count(candidate.Contains) * 2;
            foreach (var token in desired)
            {
                if (candidate.Any(item =>
                        item.Contains(token, StringComparison.OrdinalIgnoreCase)
                        || token.Contains(item, StringComparison.OrdinalIgnoreCase)))
                {
                    score++;
                }
            }

            return score;
        }
    }
}
