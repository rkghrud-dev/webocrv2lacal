using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClosedXML.Excel;
using KeywordOcr.App.Services;
using Microsoft.Win32;

namespace KeywordOcr.App;

public partial class MainWindow : Window
{
    private readonly string _legacyRoot;
    private readonly string _pythonRoot;   // v3/backend — Python import 경로
    private readonly string _v3Root;
    private static readonly string[] UpdateLogEntries =
    {
        "[업데이트 1 | 2026-04-26]\r\n- Cafe24 신규등록 흐름을 작업흐름 탭에 연결\r\n- LLM 결과 불러오기 후 신규등록 대상 목록 자동 로드\r\n- 설치 후 설정값과 기본 작업 흐름 유지 보강",
        "[업데이트 2 | 2026-05-04]\r\n- 키워드 2.0/3.0 통합 검색형 규칙 반영\r\n- 구매자 검색어 중심 상품명 생성으로 변경\r\n- 영어 단어/로마자/중문 표현 제외 및 OCR 숫자 필터 강화\r\n- 유효 검색 단어를 기존보다 약 5개 더 담도록 조정",
        "[업데이트 3 | 2026-05-04]\r\n- B마켓 전용 로고 설정과 listing_images_B 출력 흐름 보강\r\n- 준비몰 로고 파일 선택 시 설정 즉시 저장\r\n- 설치형 배포 스크립트에서 dist\\setup.exe 생성",
        "[업데이트 4 | 2026-05-04]\r\n- Cafe24 토큰 JSON 자동 리프레시 추가\r\n- 홈런마켓/준비몰 업로드 및 신규등록 시작 시 토큰 갱신 후 저장\r\n- Cafe24 업로드/신규등록 후 품목별 재고관리 사용안함 자동 적용\r\n- 상단 업데이트 메뉴에서 변경 이력 확인 기능 추가",
        "[업데이트 5 | 2026-05-04]\r\n- 키워드 피드백 탭 추가\r\n- 원본 LLM 엑셀과 수정 엑셀 변경점 비교 저장 지원\r\n- 변경점 백업, 누적 규칙 파일, Codex CLI 붙여넣기 명령 생성 구조 추가\r\n- 피드백은 3회 이상 자체 검토 후 규칙 후보로 누적되도록 명령어 생성",
        "[업데이트 6 | 2026-05-06]\r\n- V4 작업 패키지 저장/불러오기 추가\r\n- EXPORT 작업 폴더를 ZIP으로 보관하고 다른 PC에서 복원 가능\r\n- 복원 시 업로드용 엑셀, 이미지 선택, 최신 V4 결과를 자동 연결\r\n- API 키, Cafe24 토큰, 쿠키, 개인 설정, 임시 잠금 파일은 패키지에서 제외",
        "[업데이트 7 | 2026-05-07]\r\n- 옵션 메뉴에 전원 옵션과 작업후 종료 추가\r\n- 작업 중 절전 방지, 완료 후 프로그램 종료/PC 절전/Windows 종료 선택 지원\r\n- 이미지 선택을 즉시 저장해 자동 종료 후에도 대표/추가 이미지 선택 상태 유지\r\n- 지난 작업 이어하기와 자동저장 ZIP 상품코드 표시 추가\r\n- KEYWORDV4 마켓별 상품명 고유화: 롯데ON/네이버/쿠팡ESM 상품명을 서로 다르게 생성\r\n- 작업 중단/닫기 시 현재 작업 자동저장, Cafe24 토큰 리프레쉬 버튼 추가",
        "[업데이트 8 | 2026-05-07]\r\n- V4 이미지 CLI 자동 실행 진행상황 표시 추가\r\n- 현재 배치/전체 배치, 완료 상품수/전체 상품수, 퍼센트, 예상 남은 시간 표시\r\n- Codex 출력이 들어오는 동안 최근 처리 로그를 진행 영역에 함께 표시\r\n- V4 결과 불러오기 초기 폴더와 홈런마켓 토큰 경로 저장 보강",
        "[업데이트 9 | 2026-05-07]\r\n- V4 기본 상품명 생성 기준을 Cafe24 대표상품명 수준으로 강화\r\n- 롯데ON/네이버/Cafe24/쿠팡ESM 상품명이 서로 다른 길이와 검색어 순서를 갖도록 보강\r\n- V4 결과 엑셀 로드/업로드 전 판매가 0원 행을 상품가로 원본 엑셀까지 자동 보정",
        "[업데이트 10 | 2026-05-07]\r\n- 홈런마켓 상품명을 롯데ON/네이버/Cafe24공통 3종 구조로 재정리\r\n- Cafe24 공통 상품명을 쿠팡ESM/11번가와 같이 쓰도록 V4 규칙 반영\r\n- 기존 Cafe24 전체상품명 CSV의 핵심명/규격/소재/기능/사용처 조립 패턴 반영",
        "[업데이트 11 | 2026-05-07]\r\n- Cafe24 기존 상품명 중 긴 상품명 TOP500 기준으로 V4 상품명 정보량 강화\r\n- Cafe24 공통 상품명 목표를 핵심명/규격/소재/기능/사용처/대상 포함형으로 보강\r\n- 롯데ON/네이버/Cafe24공통 3종 생성 명령에서 예전 5종 문구 제거",
        "[업데이트 12 | 2026-05-08]\r\n- Cafe24 공통 상품명은 대표 검색어가 맨 앞에 오도록 생성 규칙 수정\r\n- 긴 상품명은 유지하되 용도/사용자대상 문구는 앞이 아니라 중후반에 배치\r\n- 마켓별 상품명 차이는 검색형 대표어와 유의어 순서 혼합으로 유지"
    };
    private string? _sourcePath;
    private string? _lastOutputRoot;
    private string? _lastOutputFile;
    private string? _lastUploadLogPath;
    private CancellationTokenSource? _cts;
    private readonly ObservableCollection<ProductItem> _products = new();
    private readonly ObservableCollection<PriceRow> _priceRows = new();
    private readonly ObservableCollection<string> _imageGsCodes = new();
    private readonly ObservableCollection<ImageThumbnailItem> _imageThumbnails = new();
    private readonly ObservableCollection<KeywordFeedbackChangeItem> _keywordFeedbackChanges = new();
    private readonly ObservableCollection<WorkspacePackageListItem> _workspacePackages = new();
    private readonly ObservableCollection<WorkspaceKeywordEditRow> _workspaceKeywordRows = new();
    private readonly Dictionary<string, ImageSelection> _imageSelections = new(StringComparer.OrdinalIgnoreCase);
    private bool _selectingBMarket; // true면 B마켓 대표 선택 모드
    private string _bMarketTokenPath = ""; // 준비몰 토큰 JSON 경로 (비어 있으면 기본 경로 사용)
    private string? _imageListingRoot;
    private JobHistoryService? _jobHistory;
    private ProductProgressService? _productProgress;
    private ProductProgressState? _currentProductProgress;
    private string _settingsPath = "";
    private bool _syncingKeywordVersion;
    private bool _syncingCafe24MarketTargetChecks;
    private bool _preventSleepDuringWork = true;
    private bool _sleepPreventionActive;
    private CompletionPowerAction _completionPowerAction = CompletionPowerAction.None;
    private ProductDatabase? _productDb;
    private int _currentSessionId;
    private readonly ObservableCollection<ImageThumbnailItem> _imageThumbnailsB = new();

    // 상품 선택 목록
    private readonly ObservableCollection<UploadProductItem> _cafe24Items = new();
    private readonly ObservableCollection<UploadProductItem> _coupangItems = new();
    private readonly ObservableCollection<UploadProductItem> _basicCafe24Items = new();
    private int _cafe24LastClickIndex = -1;
    private int _coupangLastClickIndex = -1;
    private int _basicCafe24LastClickIndex = -1;
    private Services.UploadHistoryStore _uploadHistory = new();
    private KeywordFeedbackSessionResult? _lastKeywordFeedbackSession;
    private string? _workspaceEditorWorkbookPath;
    private const string WorkspaceUploadHistoryFileName = "upload_history_by_gscode.json";

    public MainWindow()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        InitializeComponent();

        (_v3Root, _legacyRoot, _pythonRoot) = ResolveApplicationRoots();

        _settingsPath = Path.Combine(_legacyRoot, "app_settings.json");

        ProductList.ItemsSource = _products;
        PriceGrid.ItemsSource = _priceRows;
        ImageGsListBox.ItemsSource = _imageGsCodes;
        ThumbnailPanel.ItemsSource = _imageThumbnails;
        ThumbnailPanelB.ItemsSource = _imageThumbnailsB;
        ProductDataGrid.ItemsSource = _products;
        Cafe24ProductList.ItemsSource = _cafe24Items;
        CoupangProductList.ItemsSource = _coupangItems;
        BasicCafe24ProductGrid.ItemsSource = _basicCafe24Items;
        KeywordFeedbackGrid.ItemsSource = _keywordFeedbackChanges;
        WorkspacePackageGrid.ItemsSource = _workspacePackages;
        WorkspaceKeywordGrid.ItemsSource = _workspaceKeywordRows;
        FeedbackRootPathText.Text = KeywordFeedbackService.DefaultFeedbackRoot;

        // 설정 탭 초기값
        SettingsLegacyRoot.Text = _legacyRoot;
        SettingsV3Root.Text = _v3Root;
        Cafe24DateTag.Text = DateTime.Now.ToString("yyyyMMdd");
        LoadTokenInfo();
        LoadAppSettings();
        SyncPowerOptionMenuChecks();
        ApplyDefaultWorkflowSelections();
        if (string.IsNullOrEmpty(SettingsBTokenPath.Text))
            LoadTokenInfoB(); // 설정 파일 없을 때 기본 경로로 시도
        SyncCafe24MarketTargetCheckBoxes(true, true);

        _jobHistory = new JobHistoryService(_legacyRoot);
        _productProgress = new ProductProgressService(_legacyRoot);

        var dbPath = Path.Combine(_legacyRoot, "keywordocr.db");
        _productDb = new ProductDatabase(dbPath);

        RefreshHistoryGrid();
        RefreshWorkspacePackageList();

        Log("KeywordOCR v4 시작");
        Log($"Python 루트: {_pythonRoot}");
        LogReleaseNotes();
    }

    private void LogReleaseNotes()
    {
        Log($"업데이트 로그: 총 {UpdateLogEntries.Length}회");
        foreach (var line in UpdateLogEntries.Last().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            Log(line);
        }
    }

    private static string BuildUpdateLogText()
        => $"KeywordOCR v3 업데이트 로그\r\n총 업데이트 {UpdateLogEntries.Length}회\r\n\r\n"
           + string.Join("\r\n\r\n", UpdateLogEntries.Reverse());

    private void ShowUpdateLog_Click(object sender, RoutedEventArgs e)
    {
        var textBox = new TextBox
        {
            Text = BuildUpdateLogText(),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily = new FontFamily("Consolas, Malgun Gothic"),
            FontSize = 12,
            Padding = new Thickness(14),
            BorderThickness = new Thickness(0),
            Background = Brushes.White
        };

        var window = new Window
        {
            Title = "업데이트 정보",
            Owner = this,
            Width = 680,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = textBox
        };
        window.ShowDialog();
    }

    private void PreventSleepMenu_Click(object sender, RoutedEventArgs e)
    {
        _preventSleepDuringWork = PreventSleepMenuItem.IsChecked == true;
        SyncPowerOptionMenuChecks();
        SaveCurrentAppSettings();
        Log(_preventSleepDuringWork ? "전원 옵션: 작업 중 절전 방지 ON" : "전원 옵션: 작업 중 절전 방지 OFF");
    }

    private void PowerActionMenu_Click(object sender, RoutedEventArgs e)
    {
        _completionPowerAction = sender switch
        {
            MenuItem item when item == PowerActionCloseAppMenuItem => CompletionPowerAction.CloseApp,
            MenuItem item when item == PowerActionSleepMenuItem => CompletionPowerAction.Sleep,
            MenuItem item when item == PowerActionShutdownMenuItem => CompletionPowerAction.Shutdown,
            _ => CompletionPowerAction.None,
        };
        SyncPowerOptionMenuChecks();
        SaveCurrentAppSettings();
        Log($"전원 옵션: {GetCompletionPowerActionLabel(_completionPowerAction)}");
    }

    private void ShutdownAfterWorkQuick_Click(object sender, RoutedEventArgs e)
    {
        _completionPowerAction = ShutdownAfterWorkQuickMenuItem.IsChecked == true
            ? CompletionPowerAction.Shutdown
            : CompletionPowerAction.None;
        SyncPowerOptionMenuChecks();
        SaveCurrentAppSettings();
        Log($"전원 옵션: {GetCompletionPowerActionLabel(_completionPowerAction)}");
    }

    private void CancelShutdown_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PowerManagementService.CancelShutdown();
            Log("예약된 Windows 종료를 취소했습니다.");
            StatusText.Text = "예약 종료 취소";
        }
        catch (Exception ex)
        {
            Log($"예약 종료 취소 실패: {ex.Message}");
            MessageBox.Show(ex.Message, "예약 종료 취소 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SyncPowerOptionMenuChecks()
    {
        if (PreventSleepMenuItem is not null)
            PreventSleepMenuItem.IsChecked = _preventSleepDuringWork;
        if (PowerActionNoneMenuItem is not null)
            PowerActionNoneMenuItem.IsChecked = _completionPowerAction == CompletionPowerAction.None;
        if (PowerActionCloseAppMenuItem is not null)
            PowerActionCloseAppMenuItem.IsChecked = _completionPowerAction == CompletionPowerAction.CloseApp;
        if (PowerActionSleepMenuItem is not null)
            PowerActionSleepMenuItem.IsChecked = _completionPowerAction == CompletionPowerAction.Sleep;
        if (PowerActionShutdownMenuItem is not null)
            PowerActionShutdownMenuItem.IsChecked = _completionPowerAction == CompletionPowerAction.Shutdown;
        if (ShutdownAfterWorkQuickMenuItem is not null)
            ShutdownAfterWorkQuickMenuItem.IsChecked = _completionPowerAction == CompletionPowerAction.Shutdown;
    }

    private static CompletionPowerAction ParseCompletionPowerAction(string? value)
    {
        return Enum.TryParse<CompletionPowerAction>(value, ignoreCase: true, out var action)
            ? action
            : CompletionPowerAction.None;
    }

    private static string GetCompletionPowerActionLabel(CompletionPowerAction action)
        => action switch
        {
            CompletionPowerAction.CloseApp => "작업 완료 후 프로그램 종료",
            CompletionPowerAction.Sleep => "작업 완료 후 PC 절전",
            CompletionPowerAction.Shutdown => "작업 완료 후 Windows 종료",
            _ => "작업 완료 후 아무것도 안 함",
        };

    private void ApplyDefaultWorkflowSelections()
    {
        TestChunkSizeCombo.SelectedIndex = 4; // 분할안함
        SetKeywordVersionSelection("3.0");
    }

    private static (string V3Root, string LegacyRoot, string PythonRoot) ResolveApplicationRoots()
    {
        var baseDir = Path.GetFullPath(AppContext.BaseDirectory);
        var candidates = new List<string>();
        var current = baseDir;
        for (var i = 0; i < 12; i++)
        {
            candidates.Add(current);
            var parent = Directory.GetParent(current);
            if (parent is null)
                break;
            current = parent.FullName;
        }

        var v3Root = candidates.FirstOrDefault(IsV3Root)
            ?? Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));

        var legacyRoot = File.Exists(Path.Combine(v3Root, "app_settings.json"))
            || Directory.Exists(Path.Combine(v3Root, "backend"))
                ? v3Root
                : Path.GetFullPath(Path.Combine(v3Root, ".."));

        var pythonRoot = ResolvePythonRoot(v3Root, legacyRoot, baseDir);
        return (v3Root, legacyRoot, pythonRoot);
    }

    private static bool IsV3Root(string root)
    {
        return Directory.Exists(Path.Combine(root, "backend", "app"))
            && (Directory.Exists(Path.Combine(root, "KeywordOcr.App"))
                || Directory.Exists(Path.Combine(root, "Bridge")));
    }

    private static string ResolvePythonRoot(string v3Root, string legacyRoot, string baseDir)
    {
        var candidates = new[]
        {
            Path.Combine(v3Root, "backend"),
            Path.Combine(baseDir, "backend"),
            legacyRoot
        };

        return candidates.FirstOrDefault(path => Directory.Exists(Path.Combine(path, "app")))
            ?? Path.Combine(v3Root, "backend");
    }

    private static string GetDefaultExportRoot()
        => PathDefaults.ExportRoot;

    #region ═══ 드래그 앤 드롭 ═══

    private void SyncCafe24MarketTargetCheckBoxes(bool homeSelected, bool readySelected)
    {
        _syncingCafe24MarketTargetChecks = true;
        try
        {
            if (Cafe24HomeCheckBox is not null) Cafe24HomeCheckBox.IsChecked = homeSelected;
            if (TestCafe24HomeCheckBox is not null) TestCafe24HomeCheckBox.IsChecked = homeSelected;
            if (Cafe24ReadyCheckBox is not null) Cafe24ReadyCheckBox.IsChecked = readySelected;
            if (TestCafe24ReadyCheckBox is not null) TestCafe24ReadyCheckBox.IsChecked = readySelected;
        }
        finally
        {
            _syncingCafe24MarketTargetChecks = false;
        }
    }

    private void Cafe24MarketTargetCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingCafe24MarketTargetChecks || sender is not CheckBox checkBox)
        {
            return;
        }

        var homeSelected = IsCafe24HomeSelected();
        var readySelected = IsCafe24ReadySelected();

        if (checkBox == Cafe24HomeCheckBox || checkBox == TestCafe24HomeCheckBox)
        {
            homeSelected = checkBox.IsChecked == true;
        }
        else if (checkBox == Cafe24ReadyCheckBox || checkBox == TestCafe24ReadyCheckBox)
        {
            readySelected = checkBox.IsChecked == true;
        }

        SyncCafe24MarketTargetCheckBoxes(homeSelected, readySelected);
    }

    private bool IsCafe24HomeSelected() => Cafe24HomeCheckBox?.IsChecked == true || TestCafe24HomeCheckBox?.IsChecked == true;

    private bool IsCafe24ReadySelected() => Cafe24ReadyCheckBox?.IsChecked == true || TestCafe24ReadyCheckBox?.IsChecked == true;

    private bool TryGetSelectedCafe24Markets(out bool homeSelected, out bool readySelected, out string marketLabel)
    {
        homeSelected = IsCafe24HomeSelected();
        readySelected = IsCafe24ReadySelected();
        marketLabel = GetSelectedCafe24MarketLabel(homeSelected, readySelected);
        if (homeSelected || readySelected)
        {
            return true;
        }

        MessageBox.Show("Cafe24 대상 몰을 하나 이상 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private static string GetSelectedCafe24MarketLabel(bool homeSelected, bool readySelected)
    {
        var markets = new List<string>();
        if (homeSelected) markets.Add("홈런마켓");
        if (readySelected) markets.Add("준비몰");
        return markets.Count == 0 ? "선택 없음" : string.Join(" + ", markets);
    }

    private void ShowTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tabName })
        {
            return;
        }

        if (FindName(tabName) is TabItem tab)
        {
            tab.Visibility = Visibility.Visible;
            tab.IsSelected = true;
        }
    }

    private void FeedbackBrowseOriginal_Click(object sender, RoutedEventArgs e)
    {
        var file = SelectFeedbackExcelFile("원본 LLM 결과 엑셀 선택");
        if (file is null)
            return;

        FeedbackOriginalPathText.Text = file;
        Log($"피드백 원본 엑셀 선택: {Path.GetFileName(file)}");
    }

    private void FeedbackBrowseEdited_Click(object sender, RoutedEventArgs e)
    {
        var file = SelectFeedbackExcelFile("수정 완료 엑셀 선택");
        if (file is null)
            return;

        FeedbackEditedPathText.Text = file;
        Log($"피드백 수정 엑셀 선택: {Path.GetFileName(file)}");
    }

    private void FeedbackCompare_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var originalPath = CleanPathText(FeedbackOriginalPathText.Text);
            var editedPath = CleanPathText(FeedbackEditedPathText.Text);
            ValidateFeedbackPaths(originalPath, editedPath);

            var service = new KeywordFeedbackService();
            var changes = service.Compare(originalPath, editedPath);
            _keywordFeedbackChanges.Clear();
            foreach (var change in changes)
                _keywordFeedbackChanges.Add(change);

            _lastKeywordFeedbackSession = null;
            SetFeedbackStatus($"변경점 비교 완료: {changes.Count}개. 반영하지 않을 항목은 표의 반영 체크를 해제한 뒤 저장하세요.");
            Log($"키워드 피드백 변경점 비교: {changes.Count}개");
        }
        catch (Exception ex)
        {
            SetFeedbackStatus("변경점 비교 실패: " + ex.Message);
            MessageBox.Show(ex.Message, "피드백 비교 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            Log("피드백 비교 실패: " + ex.Message);
        }
    }

    private void FeedbackSaveSession_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var originalPath = CleanPathText(FeedbackOriginalPathText.Text);
            var editedPath = CleanPathText(FeedbackEditedPathText.Text);
            ValidateFeedbackPaths(originalPath, editedPath);

            if (_keywordFeedbackChanges.Count == 0)
            {
                FeedbackCompare_Click(sender, e);
                if (_keywordFeedbackChanges.Count == 0)
                    return;
            }

            var service = new KeywordFeedbackService();
            var result = service.SaveSession(
                originalPath,
                editedPath,
                _keywordFeedbackChanges.ToList(),
                CleanPathText(FeedbackRootPathText.Text));
            _lastKeywordFeedbackSession = result;

            Clipboard.SetText(result.CommandText);
            SetFeedbackStatus(
                $"피드백 세션 저장 완료: 전체 {result.ChangeCount}개, CLI 반영 대상 {result.IncludedCount}개. Codex 명령어를 클립보드에 복사했습니다.\n세션: {result.SessionDir}");
            Log($"피드백 세션 저장: {result.SessionDir}");
            Log($"피드백 CLI 명령어 복사 완료: {result.IncludedCount}/{result.ChangeCount}개 반영 대상");
        }
        catch (Exception ex)
        {
            SetFeedbackStatus("피드백 세션 저장 실패: " + ex.Message);
            MessageBox.Show(ex.Message, "피드백 저장 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            Log("피드백 저장 실패: " + ex.Message);
        }
    }

    private void FeedbackCopyCommand_Click(object sender, RoutedEventArgs e)
    {
        if (_lastKeywordFeedbackSession is null)
        {
            MessageBox.Show("먼저 선택 변경점 저장 / CLI 생성을 실행하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(_lastKeywordFeedbackSession.CommandText);
        SetFeedbackStatus("Codex CLI 명령어를 다시 클립보드에 복사했습니다.");
        Log("피드백 CLI 명령어 복사 완료");
    }

    private void FeedbackOpenSession_Click(object sender, RoutedEventArgs e)
    {
        if (_lastKeywordFeedbackSession is null)
        {
            MessageBox.Show("아직 저장된 피드백 세션이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenFolder(_lastKeywordFeedbackSession.SessionDir);
    }

    private void FeedbackOpenRules_Click(object sender, RoutedEventArgs e)
    {
        var root = CleanPathText(FeedbackRootPathText.Text);
        if (string.IsNullOrWhiteSpace(root))
            root = KeywordFeedbackService.DefaultFeedbackRoot;

        var rulesPath = _lastKeywordFeedbackSession?.RulesPath
            ?? Path.Combine(root, "rules", "keyword_rule_feedback.md");
        Directory.CreateDirectory(Path.GetDirectoryName(rulesPath)!);
        if (!File.Exists(rulesPath))
        {
            File.WriteAllText(
                rulesPath,
                "# KeywordOCR 누적 피드백 규칙\r\n\r\n아직 누적된 규칙이 없습니다.\r\n",
                Encoding.UTF8);
        }

        Process.Start(new ProcessStartInfo(rulesPath) { UseShellExecute = true });
    }

    private void FeedbackOpenRoot_Click(object sender, RoutedEventArgs e)
    {
        var root = CleanPathText(FeedbackRootPathText.Text);
        if (string.IsNullOrWhiteSpace(root))
            root = KeywordFeedbackService.DefaultFeedbackRoot;
        Directory.CreateDirectory(root);
        OpenFolder(root);
    }

    private void FeedbackOpenLocalFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_lastKeywordFeedbackSession is null)
        {
            MessageBox.Show("먼저 선택 변경점 저장 / CLI 생성을 실행하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenFolder(_lastKeywordFeedbackSession.LocalFeedbackDir);
    }

    private string? SelectFeedbackExcelFile(string title)
    {
        var initialDirectory = Directory.Exists(_lastOutputRoot)
            ? _lastOutputRoot
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var dlg = new OpenFileDialog
        {
            Filter = "Excel|*.xlsx;*.xls|모든 파일|*.*",
            Title = title,
            InitialDirectory = initialDirectory
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private static string CleanPathText(string? value)
        => (value ?? "").Trim().Trim('"');

    private static void ValidateFeedbackPaths(string originalPath, string editedPath)
    {
        if (string.IsNullOrWhiteSpace(originalPath) || !File.Exists(originalPath))
            throw new InvalidOperationException("원본 LLM 엑셀 파일을 선택하세요.");
        if (string.IsNullOrWhiteSpace(editedPath) || !File.Exists(editedPath))
            throw new InvalidOperationException("수정 완료 엑셀 파일을 선택하세요.");
    }

    private void SetFeedbackStatus(string text)
    {
        FeedbackStatusText.Text = text;
        StatusText.Text = text.Split('\n')[0];
    }

    private static void OpenFolder(string folderPath)
    {
        Directory.CreateDirectory(folderPath);
        Process.Start(new ProcessStartInfo("explorer.exe", folderPath));
    }

    #region ═══ 작업 패키지 저장 / 불러오기 ═══

    private void SaveWorkspacePackage_Click(object sender, RoutedEventArgs e)
    {
        var (workspaceRoot, sourceFile, resultFile, productCount) = ResolveWorkspacePackageSource();
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            MessageBox.Show("저장할 결과 폴더가 없습니다.\n먼저 결과를 불러오거나 실행 이력을 선택하세요.",
                "작업 패키지 저장", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var initialDir = Directory.Exists(GetDefaultExportRoot())
            ? GetDefaultExportRoot()
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var dlg = new SaveFileDialog
        {
            Filter = "KeywordOCR 작업 패키지|*.zip|모든 파일|*.*",
            Title = "작업 패키지 저장",
            InitialDirectory = initialDir,
            FileName = WorkspacePackageService.BuildDefaultPackageFileName(productCount),
            AddExtension = true,
            DefaultExt = ".zip",
            OverwritePrompt = true,
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            StatusText.Text = "작업 패키지 저장 중...";
            Log($"작업 패키지 저장 시작: {workspaceRoot}");
            SaveImageSelectionsToFile(markHistory: true, log: false);
            ExportUploadHistoryToWorkspace(workspaceRoot);
            var result = WorkspacePackageService.CreatePackage(
                workspaceRoot,
                dlg.FileName,
                sourceFile,
                resultFile,
                productCount,
                "v4",
                ResolveWorkspacePackageSelectedCodes());

            StatusText.Text = "작업 패키지 저장 완료";
            Log($"작업 패키지 저장 완료: {result.PackagePath}");
            Log($"포함 {result.IncludedFileCount}개, 제외 {result.ExcludedFileCount}개");
            SaveWorkspaceResumeState(workspaceRoot, result.PackagePath, sourceFile, resultFile, productCount, result.Manifest.SelectedCodes);
            RefreshWorkspacePackageList();
            MessageBox.Show(
                $"작업 패키지 저장 완료\n\n파일: {result.PackagePath}\n포함: {result.IncludedFileCount}개\n제외: {result.ExcludedFileCount}개",
                "작업 패키지 저장", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "작업 패키지 저장 실패";
            Log($"작업 패키지 저장 실패: {ex.Message}");
            MessageBox.Show(ex.Message, "작업 패키지 저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadWorkspacePackage_Click(object sender, RoutedEventArgs e)
    {
        var initialDir = Directory.Exists(GetDefaultExportRoot())
            ? GetDefaultExportRoot()
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var dlg = new OpenFileDialog
        {
            Filter = "KeywordOCR 작업 패키지|*.zip|모든 파일|*.*",
            Title = "작업 패키지 불러오기",
            InitialDirectory = initialDir,
            Multiselect = false,
        };

        if (dlg.ShowDialog() != true)
            return;

        LoadWorkspacePackageFromPath(dlg.FileName);
    }

    private void LoadWorkspacePackageFromPath(string zipPath)
    {
        try
        {
            RestoreWorkspacePackageFromPath(zipPath, showMessage: true);
        }
        catch (Exception ex)
        {
            StatusText.Text = "작업 패키지 불러오기 실패";
            Log($"작업 패키지 복원 실패: {ex.Message}");
            MessageBox.Show(ex.Message, "작업 패키지 불러오기 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private (string? WorkspaceRoot, string? SourceFile, string? ResultFile, int ProductCount) ResolveWorkspacePackageSource()
    {
        var selectedJob = MainTabControl.SelectedItem == HistoryTab ? GetSelectedJob() : null;
        if (selectedJob != null && Directory.Exists(selectedJob.OutputRoot))
        {
            return (
                selectedJob.OutputRoot,
                selectedJob.SourceFile,
                selectedJob.OutputFile,
                selectedJob.ProductCount);
        }

        var productCount = _products.Count > 0
            ? _products.Count(p => p.IsSelected)
            : 0;
        return (_lastOutputRoot, _sourcePath, _lastOutputFile, productCount);
    }

    private List<string> ResolveWorkspacePackageSelectedCodes()
    {
        var selectedJob = MainTabControl.SelectedItem == HistoryTab ? GetSelectedJob() : null;
        if (selectedJob?.SelectedCodes is { Count: > 0 })
            return NormalizeGsCodes(selectedJob.SelectedCodes);

        if (_products.Count > 0)
            return NormalizeGsCodes(_products.Where(p => p.IsSelected).Select(p => p.Code));

        if (_basicCafe24Items.Count > 0)
            return NormalizeGsCodes(_basicCafe24Items.Where(p => p.IsChecked).Select(p => p.GsCode));

        if (_workspaceKeywordRows.Count > 0)
            return NormalizeGsCodes(_workspaceKeywordRows.Select(r => r.GsCode));

        return new List<string>();
    }

    private static List<string> NormalizeGsCodes(IEnumerable<string> codes)
        => codes
            .Select(code => (code ?? "").Trim().ToUpperInvariant())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private string GetWorkspaceResumeStatePath()
        => Path.Combine(_legacyRoot, "last_workspace_state.json");

    private void SaveWorkspaceResumeState(
        string workspaceRoot,
        string? packagePath,
        string? sourceFile,
        string? resultFile,
        int productCount,
        IReadOnlyList<string>? selectedCodes = null)
    {
        try
        {
            var state = new WorkspaceResumeState
            {
                SavedAt = DateTimeOffset.Now,
                WorkspaceRoot = workspaceRoot,
                PackagePath = packagePath ?? "",
                SourceFileName = string.IsNullOrWhiteSpace(sourceFile) ? "" : Path.GetFileName(sourceFile),
                ResultFile = resultFile ?? "",
                ProductCount = productCount,
                SelectedCodes = selectedCodes?.ToList() ?? ResolveWorkspacePackageSelectedCodes(),
                ImageSelectionsPath = WorkspacePackageService.FindImageSelections(workspaceRoot) ?? "",
            };
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetWorkspaceResumeStatePath(), json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log($"지난 작업 상태 저장 실패: {ex.Message}");
        }
    }

    private WorkspaceResumeState? LoadWorkspaceResumeState()
    {
        try
        {
            var path = GetWorkspaceResumeStatePath();
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<WorkspaceResumeState>(File.ReadAllText(path, Encoding.UTF8));
        }
        catch
        {
            return null;
        }
    }

    private void ResumeLastWorkspace_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var state = LoadWorkspaceResumeState();
            if (state != null && Directory.Exists(state.WorkspaceRoot))
            {
                ApplyWorkspaceFolder(
                    state.WorkspaceRoot,
                    state.SourceFileName,
                    state.ProductCount,
                    status: "이어하기",
                    memo: $"지난 작업: {state.SavedAt:MM/dd HH:mm}",
                    selectedCodes: state.SelectedCodes);
                return;
            }

            if (state != null && File.Exists(state.PackagePath))
            {
                RestoreWorkspacePackageFromPath(state.PackagePath, showMessage: false);
                return;
            }

            RefreshWorkspacePackageList();
            var latest = _workspacePackages.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
            if (latest != null && File.Exists(latest.PackagePath))
            {
                RestoreWorkspacePackageFromPath(latest.PackagePath, showMessage: false);
                return;
            }

            MessageBox.Show("이어갈 자동저장 작업을 찾지 못했습니다.", "지난 작업 이어하기",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "지난 작업 이어하기 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyWorkspaceFolder(
        string workspaceRoot,
        string? sourceFileName,
        int productCount,
        string status,
        string memo,
        IReadOnlyList<string>? selectedCodes = null)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            throw new DirectoryNotFoundException($"작업 폴더를 찾을 수 없습니다: {workspaceRoot}");

        var uploadWorkbook = WorkspacePackageService.FindLatestUploadWorkbook(workspaceRoot);
        var latestV4Result = WorkspacePackageService.FindLatestV4Result(workspaceRoot);
        var imageSelections = WorkspacePackageService.FindImageSelections(workspaceRoot);

        _lastOutputRoot = workspaceRoot;
        _testOutputRoot = workspaceRoot;
        _testSkipOcrFolder = workspaceRoot;
        _lastOutputFile = latestV4Result ?? uploadWorkbook;

        TestOutputPathText.Text = $"결과 폴더: {workspaceRoot}";
        TestOpenOutputButton.IsEnabled = true;
        TestSkipOcrFolderText.Text = workspaceRoot;
        OutputFileText.Text = _lastOutputFile ?? workspaceRoot;
        OpenOutputFolderButton.IsEnabled = true;
        OpenUploadExcelButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastOutputFile);
        Cafe24UploadButton.IsEnabled = true;
        Cafe24CreateButton.IsEnabled = true;

        var listingDir = Path.Combine(workspaceRoot, "listing_images");
        var listingDirB = Path.Combine(workspaceRoot, "listing_images_B");
        if (Directory.Exists(listingDir) || Directory.Exists(listingDirB))
            LoadListingImagesFromRoot(workspaceRoot);

        if (!string.IsNullOrWhiteSpace(uploadWorkbook) && File.Exists(uploadWorkbook))
        {
            LoadBasicCafe24ProductList(uploadWorkbook);
            try
            {
                RefreshV4ImageCliCodexCommands(workspaceRoot, uploadWorkbook);
            }
            catch (Exception ex)
            {
                Log($"V4 CLI 명령어 재생성 건너뜀: {ex.Message}");
            }
        }

        TryAutoLoadLatestV4Result();
        if (!string.IsNullOrWhiteSpace(_lastOutputFile))
            OutputFileText.Text = _lastOutputFile;

        var job = new JobRecord
        {
            SourceFile = sourceFileName ?? "",
            OutputRoot = workspaceRoot,
            OutputFile = _lastOutputFile ?? "",
            ProductCount = productCount,
            SelectedCodes = selectedCodes?.ToList() ?? new List<string>(),
            Model = "V4 이어하기",
            MakeListing = Directory.Exists(listingDir) || Directory.Exists(listingDirB),
            Status = status,
            Memo = memo,
            ImageSelected = !string.IsNullOrWhiteSpace(imageSelections) && File.Exists(imageSelections),
        };
        _jobHistory?.Add(job);
        RefreshHistoryGrid();
        ImportUploadHistoryFromWorkspace(workspaceRoot);
        RefreshWorkspacePackageList();

        if (!string.IsNullOrWhiteSpace(imageSelections))
            Log($"이미지 선택 자동 연결: {Path.GetFileName(imageSelections)}");

        LoadWorkspaceEditorFromCurrent();
        WorkspaceStatusText.Text = $"지난 작업 이어하기: {workspaceRoot}";
        WorkspaceTab.IsSelected = true;
    }

    private void ApplyRestoredWorkspacePackage(WorkspacePackageRestoreResult result, string packagePath)
    {
        _lastOutputRoot = result.RestoredFolder;
        _testOutputRoot = result.RestoredFolder;
        _testSkipOcrFolder = result.RestoredFolder;
        TestOutputPathText.Text = $"결과 폴더: {result.RestoredFolder}";
        TestOpenOutputButton.IsEnabled = true;
        TestSkipOcrFolderText.Text = result.RestoredFolder;

        OpenOutputFolderButton.IsEnabled = true;
        OpenUploadExcelButton.IsEnabled = result.UploadWorkbookPath != null || result.LatestV4ResultPath != null;
        Cafe24UploadButton.IsEnabled = true;
        Cafe24CreateButton.IsEnabled = true;

        var listingDir = Path.Combine(result.RestoredFolder, "listing_images");
        var listingDirB = Path.Combine(result.RestoredFolder, "listing_images_B");
        if (Directory.Exists(listingDir) || Directory.Exists(listingDirB))
            LoadListingImagesFromRoot(result.RestoredFolder);

        if (!string.IsNullOrWhiteSpace(result.UploadWorkbookPath) && File.Exists(result.UploadWorkbookPath))
        {
            _lastOutputFile = result.UploadWorkbookPath;
            OutputFileText.Text = result.UploadWorkbookPath;
            LoadBasicCafe24ProductList(result.UploadWorkbookPath);
            try
            {
                RefreshV4ImageCliCodexCommands(result.RestoredFolder, result.UploadWorkbookPath);
            }
            catch (Exception ex)
            {
                Log($"V4 CLI 명령어 재생성 건너뜀: {ex.Message}");
            }
            Log($"업로드용 엑셀 자동 연결: {Path.GetFileName(result.UploadWorkbookPath)}");
        }
        else
        {
            _lastOutputFile = result.LatestV4ResultPath;
            OutputFileText.Text = result.LatestV4ResultPath ?? result.RestoredFolder;
        }

        TryAutoLoadLatestV4Result();
        if (!string.IsNullOrWhiteSpace(_lastOutputFile))
            OutputFileText.Text = _lastOutputFile;

        var job = new JobRecord
        {
            SourceFile = result.Manifest.SourceFileName,
            OutputRoot = result.RestoredFolder,
            OutputFile = _lastOutputFile ?? "",
            ProductCount = result.Manifest.ProductCount,
            SelectedCodes = result.Manifest.SelectedCodes.ToList(),
            Model = "V4 패키지",
            MakeListing = Directory.Exists(listingDir) || Directory.Exists(listingDirB),
            Status = "복원",
            Memo = $"패키지: {Path.GetFileName(packagePath)}",
            ImageSelected = !string.IsNullOrWhiteSpace(result.ImageSelectionsPath) && File.Exists(result.ImageSelectionsPath),
        };
        _jobHistory?.Add(job);
        RefreshHistoryGrid();
        ImportUploadHistoryFromWorkspace(result.RestoredFolder);
        SaveWorkspaceResumeState(
            result.RestoredFolder,
            packagePath,
            result.Manifest.SourceFileName,
            _lastOutputFile,
            result.Manifest.ProductCount,
            result.Manifest.SelectedCodes);
        RefreshWorkspacePackageList();

        if (!string.IsNullOrWhiteSpace(result.ImageSelectionsPath))
            Log($"이미지 선택 자동 연결: {Path.GetFileName(result.ImageSelectionsPath)}");

        LoadWorkspaceEditorFromCurrent();
        WorkspaceTab.IsSelected = true;
    }

    private void RestoreWorkspacePackageFromPath(string packagePath, bool showMessage)
    {
        StatusText.Text = "작업 패키지 복원 중...";
        Log($"작업 패키지 복원 시작: {packagePath}");
        var result = WorkspacePackageService.RestorePackage(packagePath, GetDefaultExportRoot());
        ApplyRestoredWorkspacePackage(result, packagePath);

        StatusText.Text = "작업 패키지 불러오기 완료";
        WorkspaceStatusText.Text = $"복원 완료: {result.RestoredFolder}";
        Log($"작업 패키지 복원 완료: {result.RestoredFolder}");
        if (showMessage)
        {
            MessageBox.Show(
                $"작업 패키지 불러오기 완료\n\n폴더: {result.RestoredFolder}",
                "작업 패키지 불러오기", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void AutoSaveWorkspacePackage(string reason)
    {
        var (workspaceRoot, sourceFile, resultFile, productCount) = ResolveWorkspacePackageSource();
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            return;

        try
        {
            SaveImageSelectionsToFile(markHistory: true, log: false);
            ExportUploadHistoryToWorkspace(workspaceRoot);
            var packageDir = GetWorkspacePackageFolder();
            Directory.CreateDirectory(packageDir);
            var fileName = WorkspacePackageService.BuildDefaultPackageFileName(productCount)
                .Replace("작업보관", "자동저장", StringComparison.Ordinal);
            var packagePath = Path.Combine(packageDir, fileName);
            var result = WorkspacePackageService.CreatePackage(
                workspaceRoot,
                packagePath,
                sourceFile,
                resultFile,
                productCount,
                "v4",
                ResolveWorkspacePackageSelectedCodes());
            WorkspaceStatusText.Text = $"자동저장 완료: {Path.GetFileName(result.PackagePath)}";
            Log($"작업 ZIP 자동저장({reason}): {result.PackagePath}");
            SaveWorkspaceResumeState(workspaceRoot, result.PackagePath, sourceFile, resultFile, productCount, result.Manifest.SelectedCodes);
            RefreshWorkspacePackageList();
        }
        catch (Exception ex)
        {
            WorkspaceStatusText.Text = "자동저장 실패";
            Log($"작업 ZIP 자동저장 실패({reason}): {ex.Message}");
        }
    }

    private void StopAndSaveWork_Click(object sender, RoutedEventArgs e)
    {
        var running = _cts is { IsCancellationRequested: false };
        if (running)
        {
            StatusText.Text = "작업 중단 요청 중...";
            Log("작업 중단 요청: 현재까지 생성된 파일을 자동저장합니다.");
            _cts?.Cancel();
        }
        else
        {
            StatusText.Text = "현재 작업 자동저장 중...";
            Log("현재 작업 수동 자동저장 시작");
        }

        SaveInterruptedWorkspaceProgress(running ? "사용자 중단" : "수동 중간저장");
    }

    private void HandleOperationCanceled(string logMessage, string saveReason)
    {
        Log(logMessage);
        StatusText.Text = "취소됨 — 현재 작업 자동저장 중...";
        SaveInterruptedWorkspaceProgress(saveReason);
    }

    private void SaveInterruptedWorkspaceProgress(string reason)
    {
        var workspaceRoot = ResolveCurrentWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            Log($"중단 자동저장 건너뜀({reason}): 작업 폴더를 아직 찾지 못했습니다.");
            StatusText.Text = "취소됨";
            return;
        }

        try
        {
            _lastOutputRoot = workspaceRoot;
            _testOutputRoot ??= workspaceRoot;

            var latestResult = WorkspacePackageService.FindLatestV4Result(workspaceRoot);
            var latestUpload = WorkspacePackageService.FindLatestUploadWorkbook(workspaceRoot);
            if (!string.IsNullOrWhiteSpace(latestResult))
                _lastOutputFile = latestResult;
            else if (string.IsNullOrWhiteSpace(_lastOutputFile) && !string.IsNullOrWhiteSpace(latestUpload))
                _lastOutputFile = latestUpload;

            SaveImageSelectionsToFile(markHistory: true, log: false);
            AutoSaveWorkspacePackage(reason);
            StatusText.Text = "중단됨 — 현재 작업 자동저장 완료";
            Log($"중단 자동저장 완료({reason}): {workspaceRoot}");
        }
        catch (Exception ex)
        {
            StatusText.Text = "중단됨 — 자동저장 확인 필요";
            Log($"중단 자동저장 실패({reason}): {ex.Message}");
        }
    }

    private string? ResolveCurrentWorkspaceRoot()
    {
        if (!string.IsNullOrWhiteSpace(_lastOutputRoot) && Directory.Exists(_lastOutputRoot))
            return _lastOutputRoot;
        if (!string.IsNullOrWhiteSpace(_testOutputRoot) && Directory.Exists(_testOutputRoot))
            return _testOutputRoot;
        if (!string.IsNullOrWhiteSpace(_testSkipOcrFolder) && Directory.Exists(_testSkipOcrFolder))
            return _testSkipOcrFolder;

        var selectedJob = GetSelectedJob();
        if (selectedJob != null && Directory.Exists(selectedJob.OutputRoot))
            return selectedJob.OutputRoot;

        return null;
    }

    private static string GetWorkspacePackageFolder()
        => Path.Combine(PathDefaults.ExportRoot, "_작업패키지");

    private void ExportUploadHistoryToWorkspace(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            return;
        _uploadHistory.ExportTo(Path.Combine(workspaceRoot, WorkspaceUploadHistoryFileName));
    }

    private void ImportUploadHistoryFromWorkspace(string workspaceRoot)
    {
        var path = Path.Combine(workspaceRoot, WorkspaceUploadHistoryFileName);
        _uploadHistory.ImportFrom(path);
    }

    private void RefreshWorkspacePackageList()
    {
        _workspacePackages.Clear();
        var folder = GetWorkspacePackageFolder();
        WorkspacePackageFolderText.Text = folder;
        if (!Directory.Exists(folder))
            return;

        foreach (var zip in Directory.GetFiles(folder, "*.zip")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(200))
        {
            var item = WorkspacePackageListItem.TryRead(zip);
            if (item != null)
                _workspacePackages.Add(item);
        }
    }

    private void LoadWorkspaceEditor(string workbookPath)
    {
        var result = WorkspaceWorkbookEditService.Load(workbookPath);
        _workspaceEditorWorkbookPath = result.WorkbookPath;
        _workspaceKeywordRows.Clear();
        foreach (var row in result.Rows)
        {
            row.UploadStatus = BuildUploadStatus(row.GsCode);
            _workspaceKeywordRows.Add(row);
        }

        WorkspaceWorkbookText.Text = result.WorkbookPath;
        WorkspaceStatusText.Text = $"표 로드 완료: {_workspaceKeywordRows.Count}개 상품";
        Log($"기존 작업내용 표 로드: {Path.GetFileName(result.WorkbookPath)} / {_workspaceKeywordRows.Count}개");
    }

    private void TryLoadWorkspaceEditor(string? workbookPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(workbookPath) && File.Exists(workbookPath))
                LoadWorkspaceEditor(workbookPath);
        }
        catch (Exception ex)
        {
            Log($"기존 작업내용 표 로드 실패: {ex.Message}");
        }
    }

    private void LoadWorkspaceEditorFromCurrent()
    {
        var workbook = !string.IsNullOrWhiteSpace(_lastOutputFile) && File.Exists(_lastOutputFile)
            ? _lastOutputFile
            : (!string.IsNullOrWhiteSpace(_lastOutputRoot) && Directory.Exists(_lastOutputRoot)
                ? WorkspacePackageService.FindLatestV4Result(_lastOutputRoot)
                  ?? WorkspacePackageService.FindLatestUploadWorkbook(_lastOutputRoot)
                : null);
        if (!string.IsNullOrWhiteSpace(workbook) && File.Exists(workbook))
            LoadWorkspaceEditor(workbook);
    }

    private string BuildUploadStatus(string gsCode)
    {
        var hist = string.IsNullOrWhiteSpace(gsCode) ? null : _uploadHistory.Get(gsCode);
        var parts = new List<string>();
        if (hist?.HomeMarket != null) parts.Add("홈 " + hist.HomeMarket.Value.ToString("MM-dd HH:mm"));
        if (hist?.ReadyMarket != null) parts.Add("준 " + hist.ReadyMarket.Value.ToString("MM-dd HH:mm"));
        if (hist?.Coupang != null) parts.Add("쿠 " + hist.Coupang.Value.ToString("MM-dd HH:mm"));
        return parts.Count == 0 ? "-" : string.Join(" / ", parts);
    }

    private void RefreshWorkspaceUploadStatuses()
    {
        foreach (var row in _workspaceKeywordRows)
            row.UploadStatus = BuildUploadStatus(row.GsCode);
    }

    private void WorkspaceRefresh_Click(object sender, RoutedEventArgs e) => RefreshWorkspacePackageList();

    private void WorkspaceOpenZip_Click(object sender, RoutedEventArgs e) => LoadWorkspacePackage_Click(sender, e);

    private void WorkspaceLoadCurrent_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadWorkspaceEditorFromCurrent();
            WorkspaceTab.IsSelected = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "기존 작업내용 불러오기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void WorkspaceLoadSelectedPackage_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspacePackageGrid.SelectedItem is not WorkspacePackageListItem item)
        {
            MessageBox.Show("불러올 ZIP을 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            RestoreWorkspacePackageFromPath(item.PackagePath, showMessage: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "작업 패키지 불러오기 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void WorkspacePackageGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        => WorkspaceLoadSelectedPackage_Click(sender, e);

    private void WorkspaceSaveWorkbook_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveWorkspaceEditorWorkbook();
            AutoSaveWorkspacePackage("기존 작업내용 수정 저장");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "수정 저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void WorkspaceSaveZip_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveWorkspaceEditorWorkbook();
            var (workspaceRoot, sourceFile, resultFile, productCount) = ResolveWorkspacePackageSource();
            if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
                throw new InvalidOperationException("ZIP으로 만들 작업 폴더가 없습니다.");

            ExportUploadHistoryToWorkspace(workspaceRoot);
            var dlg = new SaveFileDialog
            {
                Filter = "KeywordOCR 작업 패키지|*.zip|모든 파일|*.*",
                Title = "수정 작업 ZIP 저장",
                InitialDirectory = GetWorkspacePackageFolder(),
                FileName = WorkspacePackageService.BuildDefaultPackageFileName(productCount)
                    .Replace("작업보관", "수정본", StringComparison.Ordinal),
                AddExtension = true,
                DefaultExt = ".zip",
                OverwritePrompt = true,
            };
            if (dlg.ShowDialog() != true)
                return;

            SaveImageSelectionsToFile(markHistory: true, log: false);
            var result = WorkspacePackageService.CreatePackage(
                workspaceRoot,
                dlg.FileName,
                sourceFile,
                resultFile,
                productCount,
                "v4",
                ResolveWorkspacePackageSelectedCodes());
            SaveWorkspaceResumeState(workspaceRoot, result.PackagePath, sourceFile, resultFile, productCount, result.Manifest.SelectedCodes);
            Clipboard.SetText(result.PackagePath);
            WorkspaceStatusText.Text = $"수정 ZIP 저장 완료: {Path.GetFileName(result.PackagePath)}";
            Log($"수정 작업 ZIP 저장: {result.PackagePath}");
            RefreshWorkspacePackageList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "수정 ZIP 저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void WorkspaceOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastOutputRoot) && Directory.Exists(_lastOutputRoot))
            OpenFolder(_lastOutputRoot);
    }

    private void SaveWorkspaceEditorWorkbook()
    {
        if (string.IsNullOrWhiteSpace(_workspaceEditorWorkbookPath) || !File.Exists(_workspaceEditorWorkbookPath))
            throw new InvalidOperationException("먼저 편집할 엑셀 파일을 불러오세요.");

        WorkspaceWorkbookEditService.Save(_workspaceEditorWorkbookPath, _workspaceKeywordRows);
        _lastOutputFile = _workspaceEditorWorkbookPath;
        OutputFileText.Text = _workspaceEditorWorkbookPath;
        LoadBasicCafe24ProductList(_workspaceEditorWorkbookPath);
        WorkspaceStatusText.Text = $"수정 저장 완료: {Path.GetFileName(_workspaceEditorWorkbookPath)}";
        Log($"기존 작업내용 수정 저장: {_workspaceEditorWorkbookPath}");
    }

    #endregion


    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x2e));
            DropZone.Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xFF));
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        DropZone.Background = Brushes.White;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        DropZone.Background = Brushes.White;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var file = files.FirstOrDefault(f =>
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            return ext is ".csv" or ".xlsx" or ".xls";
        });

        if (file == null)
        {
            MessageBox.Show("CSV 또는 Excel 파일만 지원합니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoadFile(file);
    }

    #endregion

    #region ═══ 파일 선택 / 로딩 ═══

    private void SelectFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "CSV/Excel|*.csv;*.xlsx;*.xls|모든 파일|*.*",
            Title = "원본 파일 선택",
        };
        if (dlg.ShowDialog() == true)
            LoadFile(dlg.FileName);
    }

    private void LoadFile(string filePath)
    {
        _sourcePath = filePath;
        DropZoneFile.Text = filePath;
        DropZoneText.Text = "선택된 파일:";
        TestDropZoneFile.Text = filePath;
        Log($"파일 선택: {Path.GetFileName(filePath)}");
        LoadProductList(filePath);
    }

    private void LoadProductList(string filePath)
    {
        _products.Clear();
        try
        {
            _currentProductProgress = _productProgress?.GetOrCreate(filePath);
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var items = ext is ".xlsx" or ".xls"
                ? ReadProductsFromExcel(filePath)
                : ReadProductsFromCsv(filePath);

            if (items.Count == 0)
            {
                Log("상품코드를 찾지 못했습니다. 전체 파일이 처리됩니다.");
                ProductListPanel.Visibility = Visibility.Collapsed;
                TestProductListPanel.Visibility = Visibility.Collapsed;
                SetPipelineEnabled(true);
                return;
            }

            foreach (var (code, name) in items)
                _products.Add(new ProductItem { Code = code, Name = name, IsSelected = false });

            ProductListPanel.Visibility = Visibility.Visible;
            TestProductListPanel.Visibility = Visibility.Visible;
            ProductDataGrid.ItemsSource = _products;
            ApplyHistoryToProducts();
            AutoSelectInitialPendingBatch();
            SetPipelineEnabled(true);
            Log($"상품 {items.Count}개 로드됨");
        }
        catch (Exception ex)
        {
            Log($"파일 읽기 오류: {ex.Message}");
            ProductListPanel.Visibility = Visibility.Collapsed;
            TestProductListPanel.Visibility = Visibility.Collapsed;
            SetPipelineEnabled(true);
        }
    }

    private static readonly string[] CodeColumns =
        { "상품코드", "자체상품코드", "자체 상품코드", "상품코드B", "코드", "코드B", "GS코드", "product_code", "gs_code" };

    private static readonly string[] NameColumns =
        { "상품명", "제품명", "product_name", "name" };

    private List<(string code, string name)> ReadProductsFromExcel(string filePath)
    {
        var results = new List<(string code, string name)>();
        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheets.First();
        var headerRow = ws.FirstRowUsed();
        if (headerRow == null) return results;

        int nameCol = -1;
        var codeCols = new List<int>();
        var lastCol = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;

        for (int c = 1; c <= lastCol; c++)
        {
            var header = headerRow.Cell(c).GetString().Trim();
            if (CodeColumns.Any(h => h.Equals(header, StringComparison.OrdinalIgnoreCase)))
                codeCols.Add(c);
            if (nameCol < 0 && NameColumns.Any(h => h.Equals(header, StringComparison.OrdinalIgnoreCase)))
                nameCol = c;
        }

        if (codeCols.Count == 0) return results;

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var seen = new HashSet<string>();

        for (int r = headerRow.RowNumber() + 1; r <= lastRow; r++)
        {
            // 코드 컬럼들 중 비어있지 않은 첫 번째 값 사용
            var code = "";
            foreach (var cc in codeCols)
            {
                code = ws.Cell(r, cc).GetString().Trim();
                if (!string.IsNullOrEmpty(code)) break;
            }
            if (string.IsNullOrEmpty(code)) continue;

            var name = nameCol > 0 ? ws.Cell(r, nameCol).GetString().Trim() : "";

            // 코드 컬럼과 상품명 컬럼 둘 다에서 GS코드 찾기
            var gsMatch = Regex.Match(code, @"(GS\d{7})([A-Za-z])?", RegexOptions.IgnoreCase);
            if (!gsMatch.Success)
                gsMatch = Regex.Match(name, @"(GS\d{7})([A-Za-z])?", RegexOptions.IgnoreCase);

            if (gsMatch.Success && gsMatch.Groups[2].Success)
            {
                var suffix = gsMatch.Groups[2].Value.ToUpper();
                if (suffix != "A") continue;
            }
            var displayCode = gsMatch.Success ? gsMatch.Groups[1].Value : code;
            if (!seen.Add(displayCode)) continue;

            results.Add((displayCode, name));
        }
        return results;
    }

    private List<(string code, string name)> ReadProductsFromCsv(string filePath)
    {
        var results = new List<(string code, string name)>();
        string[] lines;
        try { lines = File.ReadAllLines(filePath, Encoding.UTF8); }
        catch { lines = File.ReadAllLines(filePath, Encoding.GetEncoding(949)); }

        if (lines.Length < 2) return results;

        var headers = ParseCsvLine(lines[0]);
        int codeIdx = -1, nameIdx = -1;
        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i].Trim();
            if (codeIdx < 0 && CodeColumns.Any(c => c.Equals(h, StringComparison.OrdinalIgnoreCase)))
                codeIdx = i;
            if (nameIdx < 0 && NameColumns.Any(c => c.Equals(h, StringComparison.OrdinalIgnoreCase)))
                nameIdx = i;
        }

        if (codeIdx < 0) return results;

        var seen = new HashSet<string>();
        for (int r = 1; r < lines.Length; r++)
        {
            var cols = ParseCsvLine(lines[r]);
            if (codeIdx >= cols.Length) continue;
            var code = cols[codeIdx].Trim();
            if (string.IsNullOrEmpty(code)) continue;

            var name = (nameIdx >= 0 && nameIdx < cols.Length) ? cols[nameIdx].Trim() : "";

            // 코드 컬럼과 상품명 컬럼 둘 다에서 GS코드 찾기
            var gsMatch = Regex.Match(code, @"(GS\d{7})([A-Za-z])?", RegexOptions.IgnoreCase);
            if (!gsMatch.Success)
                gsMatch = Regex.Match(name, @"(GS\d{7})([A-Za-z])?", RegexOptions.IgnoreCase);

            if (gsMatch.Success && gsMatch.Groups[2].Success)
            {
                var suffix = gsMatch.Groups[2].Value.ToUpper();
                if (suffix != "A") continue;
            }
            var displayCode = gsMatch.Success ? gsMatch.Groups[1].Value : code;
            if (!seen.Add(displayCode)) continue;

            results.Add((displayCode, name));
        }
        return results;
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuote = false;
        var sb = new StringBuilder();
        foreach (char c in line)
        {
            if (c == '"') inQuote = !inQuote;
            else if (c == ',' && !inQuote) { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }

    #endregion

    #region ═══ 상품 선택 ═══

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in _products) p.IsSelected = true;
        ProductList.Items.Refresh();
        ProductDataGrid.Items.Refresh();
        UpdateProductCount();
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in _products) p.IsSelected = false;
        ProductList.Items.Refresh();
        ProductDataGrid.Items.Refresh();
        UpdateProductCount();
    }

    private void SelectNextPending10_Click(object sender, RoutedEventArgs e) => SelectNextPendingBatch(10);
    private void SelectNextPending20_Click(object sender, RoutedEventArgs e) => SelectNextPendingBatch(20);
    private void SelectNextPending30_Click(object sender, RoutedEventArgs e) => SelectNextPendingBatch(30);

    private void SelectCustomPending_Click(object sender, RoutedEventArgs e)
    {
        var fallback = ParseInt(SelectPendingCountBox, 10);
        var count = ParseInt(TestSelectPendingCountBox, fallback);
        if (count <= 0) count = 10;
        SelectPendingCountBox.Text = count.ToString(CultureInfo.InvariantCulture);
        TestSelectPendingCountBox.Text = count.ToString(CultureInfo.InvariantCulture);
        SelectNextPendingBatch(count);
    }

    private void SelectAllPending_Click(object sender, RoutedEventArgs e)
    {
        var pendingCount = _products.Count(p => !p.LastProcessedAt.HasValue);
        SelectNextPendingBatch(pendingCount);
    }

    private void ResetProductProgress_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_sourcePath) || _productProgress is null)
            return;

        var answer = MessageBox.Show(
            "현재 엑셀의 완료 기록을 초기화할까요?\n상품 파일은 변경하지 않고 선택 상태 DB만 비웁니다.",
            "완료 기록 초기화",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK)
            return;

        _productProgress.Reset(_sourcePath);
        _currentProductProgress = _productProgress.GetOrCreate(_sourcePath);
        ApplyHistoryToProducts();
        SelectNextPendingBatch(Math.Min(10, _products.Count));
        Log("현재 엑셀의 상품 완료 기록을 초기화했습니다.");
    }

    private void SelectNextPendingBatch(int count)
    {
        if (_products.Count == 0)
            return;

        foreach (var p in _products)
            p.IsSelected = false;

        var selected = 0;
        foreach (var product in _products.Where(p => !p.LastProcessedAt.HasValue).Take(Math.Max(0, count)))
        {
            product.IsSelected = true;
            selected++;
        }

        ProductList.Items.Refresh();
        ProductDataGrid.Items.Refresh();
        UpdateProductCount();
        StatusText.Text = selected > 0
            ? $"미완료 상품 {selected}개 선택"
            : "미완료 상품 없음";
        Log(selected > 0
            ? $"미완료 상품 {selected}개 자동 선택"
            : "미완료 상품이 없습니다.");
    }

    private void AutoSelectInitialPendingBatch()
    {
        if (_products.Count == 0)
            return;

        var pendingCount = _products.Count(p => !p.LastProcessedAt.HasValue);
        var count = _products.Count > 30 ? 10 : pendingCount;
        SelectNextPendingBatch(Math.Min(count, pendingCount));
    }

    private void ProductCheck_Changed(object sender, RoutedEventArgs e) => UpdateProductCount();

    /// <summary>
    /// _jobHistory에서 각 상품의 마지막 처리 날짜를 _products에 반영
    /// </summary>
    private void ApplyHistoryToProducts()
    {
        if (_products.Count == 0) return;

        // GS코드 → 가장 최근 처리 시각
        var lastProcessed = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        if (_jobHistory != null)
        {
            foreach (var record in _jobHistory.Records)
            {
                foreach (var code in record.SelectedCodes)
                {
                    if (!lastProcessed.TryGetValue(code, out var existing) || record.Timestamp > existing)
                        lastProcessed[code] = record.Timestamp;
                }
            }
        }

        foreach (var product in _products)
        {
            var sourceCompletedAt = _productProgress?.GetCompletedAt(_currentProductProgress, product.Code);
            if (sourceCompletedAt == null && _productDb != null)
            {
                var dbDate = _productDb.GetLastProcessedDate(product.Code);
                if (!string.IsNullOrEmpty(dbDate) && DateTime.TryParse(dbDate, out var parsed))
                    sourceCompletedAt = parsed;
            }
            product.LastProcessedAt = sourceCompletedAt
                                      ?? (lastProcessed.TryGetValue(product.Code, out var date) ? date : null);
        }

        // 미완료 항목을 위로 올려 다음 10/20/30개 선택이 빠르게 되도록 정렬
        var sorted = _products
            .OrderBy(p => p.LastProcessedAt.HasValue)
            .ThenByDescending(p => p.LastProcessedAt)
            .ToList();

        _products.Clear();
        foreach (var p in sorted)
            _products.Add(p);

        ProductList.Items.Refresh();
        ProductDataGrid.Items.Refresh();
        UpdateProductCount();
    }

    private void UpdateProductCount()
    {
        var selected = _products.Count(p => p.IsSelected);
        var completed = _products.Count(p => p.LastProcessedAt.HasValue);
        var pending = Math.Max(0, _products.Count - completed);
        var text = $"({selected}/{_products.Count} 선택 · 완료 {completed} / 미완료 {pending})";
        ProductCountText.Text = text;
        TestProductCountText.Text = text;
    }

    private void MarkSelectedProductsCompleted(string reason)
    {
        if (string.IsNullOrWhiteSpace(_sourcePath) || _productProgress is null || _products.Count == 0)
            return;

        var selectedCodes = _products
            .Where(p => p.IsSelected)
            .Select(p => p.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedCodes.Count == 0)
            return;

        var completedAt = DateTime.Now;
        _productProgress.MarkCompleted(_sourcePath, selectedCodes, completedAt);
        _currentProductProgress = _productProgress.GetOrCreate(_sourcePath);
        foreach (var product in _products.Where(p => selectedCodes.Contains(p.Code, StringComparer.OrdinalIgnoreCase)))
        {
            product.LastProcessedAt = completedAt;
            _productDb?.UpsertProduct(product.Code, product.Name, _sourcePath);
        }

        ApplyHistoryToProducts();
        Log($"상품 완료 DB 저장({reason}): {selectedCodes.Count}개");
    }

    #endregion

    #region ═══ 필터링 ═══

    private string? CreateFilteredFile()
    {
        if (_products.Count == 0 || _products.All(p => p.IsSelected))
            return _sourcePath;

        var selectedCodes = new HashSet<string>(
            _products.Where(p => p.IsSelected).Select(p => p.Code),
            StringComparer.OrdinalIgnoreCase);

        if (selectedCodes.Count == 0) return null;

        var ext = Path.GetExtension(_sourcePath!).ToLowerInvariant();
        var dir = Path.GetDirectoryName(_sourcePath!)!;
        var baseName = Path.GetFileNameWithoutExtension(_sourcePath!);
        var filteredPath = Path.Combine(dir, $"{baseName}_filtered{ext}");

        try
        {
            if (ext is ".xlsx" or ".xls")
                CreateFilteredExcel(_sourcePath!, filteredPath, selectedCodes);
            else
                CreateFilteredCsv(_sourcePath!, filteredPath, selectedCodes);

            Log($"선택된 {selectedCodes.Count}개 상품으로 필터링 완료");
            return filteredPath;
        }
        catch (Exception ex)
        {
            Log($"필터링 오류: {ex.Message}, 원본 파일 사용");
            return _sourcePath;
        }
    }

    private void CreateFilteredExcel(string source, string dest, HashSet<string> codes)
    {
        using var wb = new XLWorkbook(source);
        var ws = wb.Worksheets.First();
        var headerRow = ws.FirstRowUsed()!;
        var lastCol = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

        int codeCol = -1, nameCol = -1;
        for (int c = 1; c <= lastCol; c++)
        {
            var header = headerRow.Cell(c).GetString().Trim();
            if (codeCol < 0 && CodeColumns.Any(h => h.Equals(header, StringComparison.OrdinalIgnoreCase)))
                codeCol = c;
            if (nameCol < 0 && NameColumns.Any(h => h.Equals(header, StringComparison.OrdinalIgnoreCase)))
                nameCol = c;
        }
        if (codeCol < 0) return;

        var rowsToDelete = new List<int>();
        for (int r = headerRow.RowNumber() + 1; r <= lastRow; r++)
        {
            var code = ws.Cell(r, codeCol).GetString().Trim();
            var name = nameCol > 0 ? ws.Cell(r, nameCol).GetString().Trim() : "";

            var gsMatch = Regex.Match(code, @"(GS\d{7})", RegexOptions.IgnoreCase);
            if (!gsMatch.Success)
                gsMatch = Regex.Match(name, @"(GS\d{7})", RegexOptions.IgnoreCase);

            var checkCode = gsMatch.Success ? gsMatch.Value : code;
            if (!codes.Contains(checkCode))
                rowsToDelete.Add(r);
        }

        for (int i = rowsToDelete.Count - 1; i >= 0; i--)
            ws.Row(rowsToDelete[i]).Delete();

        wb.SaveAs(dest);
    }

    private void CreateFilteredCsv(string source, string dest, HashSet<string> codes)
    {
        string[] lines;
        Encoding enc;
        try { lines = File.ReadAllLines(source, Encoding.UTF8); enc = Encoding.UTF8; }
        catch { enc = Encoding.GetEncoding(949); lines = File.ReadAllLines(source, enc); }

        if (lines.Length < 2) return;

        var headers = ParseCsvLine(lines[0]);
        int codeIdx = -1, nameIdx = -1;
        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i].Trim();
            if (codeIdx < 0 && CodeColumns.Any(c => c.Equals(h, StringComparison.OrdinalIgnoreCase)))
                codeIdx = i;
            if (nameIdx < 0 && NameColumns.Any(c => c.Equals(h, StringComparison.OrdinalIgnoreCase)))
                nameIdx = i;
        }
        if (codeIdx < 0) return;

        var output = new List<string> { lines[0] };
        for (int r = 1; r < lines.Length; r++)
        {
            var cols = ParseCsvLine(lines[r]);
            if (codeIdx >= cols.Length) continue;
            var code = cols[codeIdx].Trim();
            var name = (nameIdx >= 0 && nameIdx < cols.Length) ? cols[nameIdx].Trim() : "";

            var gsMatch = Regex.Match(code, @"(GS\d{7})", RegexOptions.IgnoreCase);
            if (!gsMatch.Success)
                gsMatch = Regex.Match(name, @"(GS\d{7})", RegexOptions.IgnoreCase);

            var checkCode = gsMatch.Success ? gsMatch.Value : code;
            if (codes.Contains(checkCode))
                output.Add(lines[r]);
        }
        File.WriteAllLines(dest, output, enc);
    }

    #endregion

    #region ═══ STEP 1: 파이프라인 실행 ═══

    private ListingImageSettings BuildListingSettings()
    {
        var listingSize = Math.Max(ParseInt(SettingsListingSize, 1000), 1000);
        SettingsListingSize.Text = listingSize.ToString(CultureInfo.InvariantCulture);
        var s = new ListingImageSettings(
            MakeListing: MakeListingCheck.IsChecked == true,
            ListingSize: listingSize,
            LogoPath: SettingsLogoPath.Text.Trim(),
            LogoRatio: ParseInt(SettingsLogoRatio, 14),
            LogoOpacity: ParseInt(SettingsLogoOpacity, 65),
            LogoPosition: (SettingsLogoPos.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "tr",
            UseAutoContrast: SettingsAutoContrast.IsChecked == true,
            UseSharpen: SettingsSharpen.IsChecked == true,
            UseSmallRotate: SettingsSmallRotate.IsChecked == true,
            RotateZoom: ParseDouble(SettingsRotateZoom, 1.04),
            JpegQualityMin: ParseInt(SettingsJpegMin, 88),
            JpegQualityMax: ParseInt(SettingsJpegMax, 92),
            FlipLeftRight: SettingsFlipLR.IsChecked == true,
            LogoPathB: SettingsLogoPathB.Text.Trim(),
            ImgTag: SettingsImgTag.Text.Trim(),
            ImgTagB: SettingsImgTagB.Text.Trim(),
            ANameMin: ParseInt(SettingsANameMin, 80),
            ANameMax: ParseInt(SettingsANameMax, 100),
            BNameMin: ParseInt(SettingsBNameMin, 63),
            BNameMax: ParseInt(SettingsBNameMax, 98),
            ATagCount: ParseInt(SettingsATagCount, 20),
            BTagCount: ParseInt(SettingsBTagCount, 14),
            KeywordVersion: GetSelectedKeywordVersion(),
            HomeMarketTokenPath: SettingsTokenPath.Text.Trim(),
            BMarketTokenPath: _bMarketTokenPath,
            PreventSleepDuringWork: _preventSleepDuringWork,
            CompletionPowerAction: _completionPowerAction.ToString()
        );
        SaveAppSettings(s);
        return s;
    }

    private void SaveCurrentAppSettings()
    {
        try
        {
            BuildListingSettings();
        }
        catch
        {
            SaveAppSettings(new ListingImageSettings(
                PreventSleepDuringWork: _preventSleepDuringWork,
                CompletionPowerAction: _completionPowerAction.ToString()));
        }
    }

    private void SaveAppSettings(ListingImageSettings s)
    {
        try
        {
            var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch { }
    }

    private void LoadAppSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var json = File.ReadAllText(_settingsPath);
            var s = JsonSerializer.Deserialize<ListingImageSettings>(json);
            if (s is null) return;

            SettingsLogoPath.Text = s.LogoPath;
            SettingsLogoPathB.Text = s.LogoPathB;
            SettingsImgTag.Text = s.ImgTag;
            SettingsImgTagB.Text = s.ImgTagB;
            SettingsLogoRatio.Text = s.LogoRatio.ToString();
            SettingsLogoOpacity.Text = s.LogoOpacity.ToString();
            SettingsListingSize.Text = Math.Max(s.ListingSize, 1000).ToString(CultureInfo.InvariantCulture);
            SettingsJpegMin.Text = s.JpegQualityMin.ToString();
            SettingsJpegMax.Text = s.JpegQualityMax.ToString();
            SettingsRotateZoom.Text = s.RotateZoom.ToString(CultureInfo.InvariantCulture);
            SettingsAutoContrast.IsChecked = s.UseAutoContrast;
            SettingsSharpen.IsChecked = s.UseSharpen;
            SettingsSmallRotate.IsChecked = s.UseSmallRotate;
            SettingsFlipLR.IsChecked = s.FlipLeftRight;
            MakeListingCheck.IsChecked = s.MakeListing;
            SettingsANameMin.Text = s.ANameMin.ToString();
            SettingsANameMax.Text = s.ANameMax.ToString();
            SettingsBNameMin.Text = s.BNameMin.ToString();
            SettingsBNameMax.Text = s.BNameMax.ToString();
            SettingsATagCount.Text = s.ATagCount.ToString();
            SettingsBTagCount.Text = s.BTagCount.ToString();
            SetKeywordVersionSelection(string.IsNullOrWhiteSpace(s.KeywordVersion) ? "2.0" : s.KeywordVersion);
            _preventSleepDuringWork = s.PreventSleepDuringWork;
            _completionPowerAction = ParseCompletionPowerAction(s.CompletionPowerAction);

            // 로고 위치 콤보박스
            SetComboSelection(SettingsLogoPos, s.LogoPosition);

            // 홈런마켓 토큰 경로
            if (!string.IsNullOrWhiteSpace(s.HomeMarketTokenPath))
            {
                SettingsTokenPath.Text = s.HomeMarketTokenPath;
                LoadTokenInfo();
            }

            // B마켓 토큰 경로
            if (!string.IsNullOrWhiteSpace(s.BMarketTokenPath))
            {
                _bMarketTokenPath = s.BMarketTokenPath;
                SettingsBTokenPath.Text = s.BMarketTokenPath;
                LoadTokenInfoB();
            }
            else
            {
                LoadTokenInfoB(); // 기본 경로로 시도
            }
        }
        catch { }
    }

    private string GetSelectedKeywordVersion()
    {
        var selected = GetComboSelectedText(TestKeywordVersionCombo)
            ?? GetComboSelectedText(KeywordVersionCombo);

        return selected switch
        {
            "1.0" => "1.0",
            "2.0" => "2.0",
            "3.0" => "3.0",
            _ => "3.0",
        };
    }

    private void SetKeywordVersionSelection(string? version)
    {
        var trimmed = version?.Trim();
        var normalized = string.Equals(trimmed, "1.0", StringComparison.OrdinalIgnoreCase) ? "1.0"
            : string.Equals(trimmed, "2.0", StringComparison.OrdinalIgnoreCase) ? "2.0"
            : string.Equals(trimmed, "3.0", StringComparison.OrdinalIgnoreCase) ? "3.0"
            : "3.0";
        _syncingKeywordVersion = true;
        try
        {
            SetComboSelection(KeywordVersionCombo, normalized);
            SetComboSelection(TestKeywordVersionCombo, normalized);
        }
        finally
        {
            _syncingKeywordVersion = false;
        }
    }

    private void KeywordVersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingKeywordVersion) return;

        var normalized = GetComboSelectedText(sender as ComboBox) switch
        {
            "1.0" => "1.0",
            "2.0" => "2.0",
            "3.0" => "3.0",
            _ => "3.0",
        };

        SetKeywordVersionSelection(normalized);

        if (!string.IsNullOrWhiteSpace(_testOutputRoot) && Directory.Exists(_testOutputRoot))
            RefreshTestCodexCommands(_testOutputRoot);
    }

    private static string? GetComboSelectedText(ComboBox? comboBox)
        => (comboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim();

    private static void SetComboSelection(ComboBox comboBox, string? value)
    {
        if (comboBox is null || string.IsNullOrWhiteSpace(value)) return;
        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private async void RunPipeline_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSource()) return;
        if (_products.Count > 0 && !_products.Any(p => p.IsSelected))
        {
            MessageBox.Show("처리할 상품을 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetRunning(true);
        _cts = new CancellationTokenSource();
        var completed = false;

        try
        {
            var inputFile = CreateFilteredFile();
            if (inputFile == null) { SetRunning(false); return; }

            var settings = BuildListingSettings();
            var bridge = new PythonPipelineBridgeService(_v3Root, _pythonRoot);
            var progress = new Progress<string>(msg => Log(msg));
            var selectedModel = (ModelCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            var selectedKeywordVersion = GetSelectedKeywordVersion();

            if (settings.MakeListing)
            {
                // ── 2-Phase 실행: 이미지 먼저 → 피커 → 분석 병렬 ──
                Log($"Phase 1: 이미지 다운로드 + 가공 시작... (모델: {selectedModel}, 키워드 버전: {selectedKeywordVersion})");
                StatusText.Text = "Phase 1: 이미지 처리 중...";
                ProgressBar.IsIndeterminate = true;

                var phase1 = await bridge.RunPipelineAsync(
                    inputFile, settings, progress, _cts.Token, phase: "images", model: selectedModel, keywordVersion: selectedKeywordVersion);

                _lastOutputRoot = phase1.OutputRoot;
                Log($"Phase 1 완료 — 이미지 폴더: {phase1.OutputRoot}");

                // Phase 2: 분석 백그라운드 시작
                Log("Phase 2: OCR + Vision + 키워드 생성 (백그라운드)...");
                StatusText.Text = "이미지 선택 중... (백그라운드에서 키워드 생성 중)";
                var phase2Progress = new Progress<string>(msg => Log($"[Phase2] {msg}"));
                var phase2Task = bridge.RunPipelineAsync(
                    inputFile, settings, phase2Progress, _cts.Token,
                    phase: "analysis", exportRoot: phase1.OutputRoot, model: selectedModel, keywordVersion: selectedKeywordVersion);

                // 이미지 선택 탭으로 전환 + 이미지 로드
                LoadListingImagesFromRoot(phase1.OutputRoot);

                // Phase 2 완료 대기
                var phase2Result = await phase2Task;
                OnPipelineComplete(phase2Result);
                completed = true;
            }
            else
            {
                // ── 기존 단일 실행 (이미지 없이 키워드만) ──
                Log($"전체 파이프라인 실행 시작... (모델: {selectedModel}, 키워드 버전: {selectedKeywordVersion})");
                StatusText.Text = "실행 중...";
                ProgressBar.IsIndeterminate = true;

                var result = await bridge.RunPipelineAsync(inputFile, settings, progress, _cts.Token, model: selectedModel, keywordVersion: selectedKeywordVersion);
                OnPipelineComplete(result);
                completed = true;
            }
        }
        catch (OperationCanceledException) { HandleOperationCanceled("작업 취소됨", "파이프라인 중단"); }
        catch (Exception ex) { HandlePipelineError(ex); }
        finally
        {
            SetRunning(false);
            ProgressBar.IsIndeterminate = false;
            if (completed)
                RunCompletionPowerActionIfNeeded("파이프라인 완료 후 전원 동작");
        }
    }

    private async void RunKeywordOnly_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSource()) return;

        SetRunning(true);
        _cts = new CancellationTokenSource();
        var completed = false;

        try
        {
            var inputFile = CreateFilteredFile() ?? _sourcePath!;
            var settings = BuildListingSettings() with { MakeListing = false };
            var bridge = new PythonPipelineBridgeService(_v3Root, _pythonRoot);
            var progress = new Progress<string>(msg => Log(msg));
            var selectedModel = (ModelCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            var selectedKeywordVersion = GetSelectedKeywordVersion();

            Log($"키워드만 생성 시작... (모델: {selectedModel}, 키워드 버전: {selectedKeywordVersion})");
            StatusText.Text = "키워드 생성 중...";
            ProgressBar.IsIndeterminate = true;

            var result = await bridge.RunPipelineAsync(inputFile, settings, progress, _cts.Token, model: selectedModel, keywordVersion: selectedKeywordVersion);
            OnPipelineComplete(result);
            completed = true;
        }
        catch (OperationCanceledException) { HandleOperationCanceled("작업 취소됨", "키워드 생성 중단"); }
        catch (Exception ex) { HandlePipelineError(ex); }
        finally
        {
            SetRunning(false);
            ProgressBar.IsIndeterminate = false;
            if (completed)
                RunCompletionPowerActionIfNeeded("키워드 생성 완료 후 전원 동작");
        }
    }

    private async void RunListingOnly_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSource()) return;

        SetRunning(true);
        _cts = new CancellationTokenSource();
        var completed = false;

        try
        {
            var inputFile = CreateFilteredFile() ?? _sourcePath!;
            var settings = BuildListingSettings();
            var bridge = new PythonPipelineBridgeService(_v3Root, _pythonRoot);
            var progress = new Progress<string>(msg => Log(msg));
            var selectedModel = (ModelCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            var selectedKeywordVersion = GetSelectedKeywordVersion();

            Log($"대표이미지만 생성 시작... (모델: {selectedModel}, 키워드 버전: {selectedKeywordVersion})");
            StatusText.Text = "대표이미지 생성 중...";
            ProgressBar.IsIndeterminate = true;

            var result = await bridge.RunPipelineAsync(inputFile, settings, progress, _cts.Token, model: selectedModel, keywordVersion: selectedKeywordVersion);
            OnPipelineComplete(result);
            completed = true;
        }
        catch (OperationCanceledException) { HandleOperationCanceled("작업 취소됨", "대표이미지 생성 중단"); }
        catch (Exception ex) { HandlePipelineError(ex); }
        finally
        {
            SetRunning(false);
            ProgressBar.IsIndeterminate = false;
            if (completed)
                RunCompletionPowerActionIfNeeded("대표이미지 생성 완료 후 전원 동작");
        }
    }

    private void OnPipelineComplete(PythonPipelineBridgeResult result)
    {
        _lastOutputRoot = result.OutputRoot;
        _lastOutputFile = result.OutputFile;

        OpenUploadExcelButton.IsEnabled = true;
        OpenOutputFolderButton.IsEnabled = true;
        Cafe24UploadButton.IsEnabled = true;
        Cafe24CreateButton.IsEnabled = true;

        Log($"완료: {result.OutputFile}");
        StatusText.Text = "완료 — Cafe24 업로드 가능";
        OutputFileText.Text = result.OutputFile;

        var uploadFile = FindLatestFile(_lastOutputRoot, "업로드용_*.xlsx");
        if (uploadFile != null)
        {
            Clipboard.SetText(uploadFile);
            Log($"업로드용 엑셀 클립보드 복사: {Path.GetFileName(uploadFile)}");
        }

        // 결과 폴더 자동 열기
        if (_completionPowerAction == CompletionPowerAction.None
            && !string.IsNullOrEmpty(_lastOutputRoot)
            && Directory.Exists(_lastOutputRoot))
            Process.Start(new ProcessStartInfo("explorer.exe", _lastOutputRoot));

        // 완료 알림 + 앱 포커스
        if (_completionPowerAction == CompletionPowerAction.None)
        {
            Activate();
            Topmost = true;
            Topmost = false;
            System.Media.SystemSounds.Asterisk.Play();
            MessageBox.Show(
                $"파이프라인 완료!\n\n" +
                $"파일: {Path.GetFileName(result.OutputFile)}\n" +
                $"폴더: {_lastOutputRoot}",
                "작업 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            System.Media.SystemSounds.Asterisk.Play();
            Log("파이프라인 완료: 전원 옵션이 켜져 있어 완료 알림창을 띄우지 않습니다.");
        }

        // 실행 이력 저장
        var selectedModel = (ModelCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var job = new JobRecord
        {
            SourceFile = _sourcePath ?? "",
            OutputRoot = result.OutputRoot,
            OutputFile = result.OutputFile,
            ProductCount = _products.Count(p => p.IsSelected),
            SelectedCodes = _products.Where(p => p.IsSelected).Select(p => p.Code).ToList(),
            Model = selectedModel,
            MakeListing = MakeListingCheck.IsChecked == true,
            Status = "완료",
        };
        _jobHistory?.Add(job);
        RefreshHistoryGrid();
        MarkSelectedProductsCompleted("파이프라인 완료");
        ApplyHistoryToProducts();
        TryLoadWorkspaceEditor(result.OutputFile);
        AutoSaveWorkspacePackage("파이프라인 완료");
    }

    private void HandlePipelineError(Exception ex)
    {
        Log($"오류: {ex.Message}");
        StatusText.Text = "오류 발생";
        MessageBox.Show(ex.Message, "파이프라인 오류", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    #endregion

    #region ═══ 테스트실행 (OCR Only + LLM 수동) ═══

    private string? _testOutputRoot;
    private string? _testLlmResultFile;
    private List<string> _testLlmResultFiles = new();
    private string? _testSkipOcrFolder;
    private string? _lastMarketExcelOutputFolder;
    private readonly List<V4ImageCliBatchInfo> _v4ImageCliBatches = new();

    private int GetTestChunkSize()
    {
        var selected = (TestChunkSizeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "10개";
        if (selected == "분할안함") return 0;
        return int.TryParse(selected.Replace("개", ""), out var n) ? n : 10;
    }

    private void TestSkipOcrCheck_Changed(object sender, RoutedEventArgs e)
    {
        var isChecked = TestSkipOcrCheck.IsChecked == true;
        TestSkipOcrFolderPanel.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
        TestRunOcrOnlyButton.Content = isChecked ? "V5 이미지 CLI 재생성" : "V5 이미지 CLI 키워드 생성 + Cafe24 자동등록";
        TestRunOcrOnlyButton.Background = isChecked
            ? new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#e67e22"))
            : new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#159A6B"));
    }

    private void TestSkipOcrSelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "업로드용 엑셀|업로드용_*.xlsx|모든 파일|*.*",
            Title = "기존 업로드용 엑셀 선택 (OCR결과 포함된 파일)",
            InitialDirectory = Directory.Exists(GetDefaultExportRoot()) ? GetDefaultExportRoot() : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };

        if (dlg.ShowDialog() == true)
        {
            _testSkipOcrFolder = Path.GetDirectoryName(dlg.FileName)!;
            TestSkipOcrFolderText.Text = _testSkipOcrFolder;
            Log($"OCR 재사용 폴더: {_testSkipOcrFolder}");
            Log($"업로드용 엑셀: {Path.GetFileName(dlg.FileName)}");
        }
    }

    private async void TestRunOcrOnly_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSource()) return;
        if (_products.Count > 0 && !_products.Any(p => p.IsSelected))
        {
            MessageBox.Show("처리할 상품을 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // DB에 작업 세션 기록
        if (_productDb != null && _sourcePath != null)
        {
            var selectedCount = _products.Count(p => p.IsSelected);
            _currentSessionId = _productDb.CreateWorkSession(_sourcePath, selectedCount);
            foreach (var p in _products.Where(p => p.IsSelected))
                _productDb.UpsertProduct(p.Code, p.Name, _sourcePath);
        }

        SetRunning(true);
        _cts = new CancellationTokenSource();
        var chunkSize = GetTestChunkSize();
        var completed = false;

        try
        {
            var inputFile = CreateFilteredFile();
            if (inputFile == null) { SetRunning(false); return; }

            var settings = BuildListingSettings();
            var bridge = new PythonPipelineBridgeService(_v3Root, _pythonRoot);
            var progress = new Progress<string>(msg => Log(msg));
            var selectedModel = (ModelCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            var selectedKeywordVersion = GetSelectedKeywordVersion();

            Log($"V5 실행 (병렬): 이미지 다운로드/가공 + Codex 병렬 실행...");
            StatusText.Text = "V5 이미지 준비 중...";
            ProgressBar.IsIndeterminate = true;

            if (settings.MakeListing)
            {
                Log("Phase 1: 이미지 다운로드 + 가공...");
                var phase1 = await bridge.RunPipelineAsync(
                    inputFile, settings, progress, _cts.Token, phase: "images", model: selectedModel, keywordVersion: selectedKeywordVersion);

                _testOutputRoot = phase1.OutputRoot;
                Log($"Phase 1 완료 — 이미지 폴더: {phase1.OutputRoot}");

                // 이미지 없는 상품 감지 + 스킵
                CheckAndSkipNoImageProducts(phase1.OutputRoot);

                LoadListingImagesFromRoot(phase1.OutputRoot);

                OnTestV4ImageCliReady(phase1);
                await RunCodexCommandsParallelAsync();
                completed = true;
            }
            else
            {
                var result = await bridge.RunPipelineAsync(
                    inputFile, settings, progress, _cts.Token, phase: "images", model: selectedModel,
                    keywordVersion: selectedKeywordVersion);
                OnTestV4ImageCliReady(result);
                await RunCodexCommandsParallelAsync();
                completed = true;
            }
        }
        catch (OperationCanceledException) { HandleOperationCanceled("작업 취소됨", "V5 이미지 CLI 중단"); }
        catch (Exception ex) { HandlePipelineError(ex); }
        finally
        {
            SetRunning(false);
            ProgressBar.IsIndeterminate = false;
            if (completed)
            {
                _productDb?.CompleteWorkSession(_currentSessionId, "COMPLETED", outputRoot: _testOutputRoot);
                AutoSaveCompletedWorkZip();
                RunCompletionPowerActionIfNeeded("V5 이미지 CLI 완료 후 전원 동작");
            }
        }
    }

    private async Task TestRunSkipOcr_Execute()
    {
        if (string.IsNullOrEmpty(_testSkipOcrFolder) || !Directory.Exists(_testSkipOcrFolder))
        {
            MessageBox.Show("기존 폴더를 먼저 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 업로드용 엑셀 찾기
        var uploadFiles = Directory.GetFiles(_testSkipOcrFolder, "업로드용_*.xlsx");
        if (uploadFiles.Length == 0)
        {
            MessageBox.Show("선택한 폴더에 업로드용 엑셀이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Array.Sort(uploadFiles);
        var uploadPath = uploadFiles[^1]; // 최신 파일
        var chunkSize = GetTestChunkSize();

        SetRunning(true);
        _cts = new CancellationTokenSource();
        ProgressBar.IsIndeterminate = true;
        StatusText.Text = "V5 키워드 재생성 중...";
        Log($"기존 폴더 재사용: 업로드용 엑셀과 이미지 폴더로 V5 이미지 CLI 명령 재생성");
        Log($"엑셀: {Path.GetFileName(uploadPath)}");
        var completed = false;

        try
        {
            var exportRoot = _testSkipOcrFolder;

            _testOutputRoot = exportRoot;
            var result = new PythonPipelineBridgeResult(exportRoot, uploadPath);
            OnTestV4ImageCliReady(result);
            await RunCodexCommandsAsync(confirm: false, manageRunning: false);
            completed = true;
        }
        catch (Exception ex)
        {
            Log($"청크 재생성 오류: {ex.Message}");
            MessageBox.Show(ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetRunning(false);
            ProgressBar.IsIndeterminate = false;
            if (completed)
                RunCompletionPowerActionIfNeeded("V5 키워드 재생성 완료 후 전원 동작");
        }
    }

    private List<string> _codexCommands = new();
    private List<string> _codexCommandsExt = new();

    private sealed record V4ImageCliGroup(string GsCode, string ProductName, List<string> ImageFiles);
    private sealed record V4ImageCliBatchInfo(int BatchNo, int ProductCount, string Codes, string? ResultFile = null);
    private const int V4ImageCliDetailImageLimit = 5;
    private const int V4ImageCliTotalImageLimit = 5;
    private const int V4ImageCliProductsPerCommand = 5;
    private string? _v4ImageCliUploadFile;
    private string? _v4ImageCliFinalResultFile;

    private static string GetKeywordVersionSuffix(string version)
        => version switch
        {
            "1.0" => "v1_0",
            "3.0" => "v3_0",
            _ => "v2_0",
        };

    private static string GetKeywordVersionLabel(string version)
        => version switch
        {
            "1.0" => "v1.0 확장형",
            "3.0" => "v3.0 통합 검색형",
            _ => "v2.0 통합 검색형",
        };

    private static string GetChunksRoot(string outputRoot)
        => Path.Combine(outputRoot, "llm_chunks");

    private static string GetActiveChunksMarkerPath(string outputRoot, string version)
        => Path.Combine(GetChunksRoot(outputRoot), $"_active_{GetKeywordVersionSuffix(version)}.txt");

    private string GetActiveChunksDir(string outputRoot, string? version = null)
    {
        var selectedVersion = version ?? GetSelectedKeywordVersion();
        var versionSuffix = GetKeywordVersionSuffix(selectedVersion);
        var chunksRoot = GetChunksRoot(outputRoot);
        if (!Directory.Exists(chunksRoot))
            return chunksRoot;

        var markerPath = GetActiveChunksMarkerPath(outputRoot, selectedVersion);
        try
        {
            if (File.Exists(markerPath))
            {
                var markedDir = File.ReadAllText(markerPath).Trim();
                if (!string.IsNullOrWhiteSpace(markedDir) && Directory.Exists(markedDir))
                    return markedDir;
            }
        }
        catch { }

        if (Directory.GetFiles(chunksRoot, "chunk_*.xlsx").Length > 0)
            return chunksRoot;

        var versionedSessions = Directory.GetDirectories(chunksRoot, $"session_*_{versionSuffix}_*")
            .Where(dir => Directory.GetFiles(dir, "chunk_*.xlsx").Length > 0)
            .OrderByDescending(Directory.GetCreationTimeUtc)
            .ToArray();
        if (versionedSessions.Length > 0)
            return versionedSessions[0];

        var anySessions = Directory.GetDirectories(chunksRoot, "session_*")
            .Where(dir => Directory.GetFiles(dir, "chunk_*.xlsx").Length > 0)
            .OrderByDescending(Directory.GetCreationTimeUtc)
            .ToArray();
        if (anySessions.Length > 0)
            return anySessions[0];

        return chunksRoot;
    }

    private IEnumerable<string> GetPreferredLlmDirs(string outputRoot, string version)
    {
        var versionSuffix = GetKeywordVersionSuffix(version);
        var chunksRoot = GetChunksRoot(outputRoot);
        var activeChunksDir = GetActiveChunksDir(outputRoot, version);

        return new[]
        {
            Path.Combine(activeChunksDir, $"llm_result_{versionSuffix}"),
            Path.Combine(outputRoot, $"llm_result_{versionSuffix}"),
            Path.Combine(activeChunksDir, $"llm_result_ext_{versionSuffix}"),
            Path.Combine(outputRoot, $"llm_result_ext_{versionSuffix}"),
            Path.Combine(outputRoot, "llm_result_v5_cli"),
            Path.Combine(outputRoot, "llm_result_v4_cli"),
            Path.Combine(chunksRoot, $"llm_result_{versionSuffix}"),
            Path.Combine(chunksRoot, $"llm_result_ext_{versionSuffix}"),
            Path.Combine(activeChunksDir, "llm_result"),
            Path.Combine(outputRoot, "llm_result"),
            Path.Combine(activeChunksDir, "llm_result_ext"),
            Path.Combine(outputRoot, "llm_result_ext"),
            Path.Combine(chunksRoot, "llm_result"),
            Path.Combine(chunksRoot, "llm_result_ext"),
        }.Distinct().Where(Directory.Exists);
    }

    private static string GetKeywordVersionCommandGuide(string version, bool extended)
    {
        if (version == "1.0")
        {
            return extended
                ? "키워드 버전 1.0 확장형으로 처리해. 핵심상품명은 맨 앞에 두고, 온토픽 범위에서만 실무 유사어와 사용처를 조금 더 활용하되 다른 상품군, 오타 확장, 과장 문구는 금지해."
                : "키워드 버전 1.0 확장형으로 처리해. 핵심상품명은 맨 앞에 두고, 온토픽 범위에서 검색 커버리지를 조금 더 확보하되 다른 상품군, 오타 확장, 과장 문구는 금지해.";
        }

        if (version == "3.0")
        {
            return extended
                ? "키워드 버전 3.0 통합 검색형으로 처리해. 모든 결과물은 한국어만 사용하고 영어 단어·로마자·중문 표현은 제거해. 한국어 대체어가 명확하면 한국어 검색어로 변환하고 불명확하면 제거해. 상품 설명문이 아니라 실제 구매자가 검색창에 입력할 가능성이 높은 단어 중심으로 상품명/검색어설정/검색키워드를 구성해. 근거는 원본 상품명과 OCR/Vision 텍스트만 사용하고 외부 검색/연관어/자동완성은 금지해. OCR 숫자는 단위 붙은 규격만 유지하고 순수 숫자/가격/바코드/깨진 숫자는 제외해. 유효 검색 단어가 있으면 기존보다 약 5개 더 풍부하게 담되 억지로 늘리지 마. A마켓은 기능·규격·재질·호환성 중심, B마켓은 용도·사용처·대상 중심으로 독립 작성하고 A/B 뒷부분 토큰이 50% 이상 겹치지 않게 해."
                : "키워드 버전 3.0 통합 검색형으로 처리해. 모든 결과물은 한국어만 사용하고 영어 단어·로마자·중문 표현은 제거해. 한국어 대체어가 명확하면 한국어 검색어로 변환하고 불명확하면 제거해. 상품 설명문이 아니라 실제 구매자가 검색창에 입력할 가능성이 높은 단어 중심으로 상품명/검색어설정/검색키워드를 구성해. 근거는 원본 상품명과 OCR/Vision 텍스트만 사용하고 외부 검색/연관어/자동완성은 금지해. OCR 숫자는 단위 붙은 규격만 유지하고 순수 숫자/가격/바코드/깨진 숫자는 제외해. 유효 검색 단어가 있으면 기존보다 약 5개 더 풍부하게 담되 억지로 늘리지 마. A마켓은 기능·규격·재질·호환성 중심, B마켓은 용도·사용처·대상 중심으로 독립 작성하고 A/B 뒷부분 토큰이 50% 이상 겹치지 않게 해.";
        }

        return extended
            ? "키워드 버전 2.0/3.0 통합 검색형으로 처리해. 모든 결과물은 한국어만 사용하고 영어 단어·로마자·중문 표현은 제거해. 상품명/OCR/Vision 근거 안에서 구매자가 검색창에 칠 단어 중심으로 확장하고, 무관 카테고리·오타 확장·과장 문구는 금지해. 유효 검색 단어가 있으면 기존보다 약 5개 더 풍부하게 담되 억지로 늘리지 마."
            : "키워드 버전 2.0/3.0 통합 검색형으로 처리해. 모든 결과물은 한국어만 사용하고 영어 단어·로마자·중문 표현은 제거해. 상품명/OCR/Vision 근거 안에서 구매자 검색어 중심으로 조립하고, 무관 카테고리·오타 확장·과장 문구는 금지해. 유효 검색 단어가 있으면 기존보다 약 5개 더 풍부하게 담되 억지로 늘리지 마.";
    }

    private static string BuildTestCodexCommand(string workingDir, string instruction)
        => $"cd \"{workingDir}\"; codex --full-auto \"{EscapePowerShellDoubleQuoted(instruction)}\"";

    private static string BuildTestCodexImageCommand(string workingDir, IEnumerable<string> imageFiles, string instruction)
    {
        var imageArgs = string.Join(" ", imageFiles.Select(path => $"-i \"{EscapePowerShellDoubleQuoted(path)}\""));
        return $"cd \"{workingDir}\"; codex exec --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox -C \"{EscapePowerShellDoubleQuoted(workingDir)}\" {imageArgs} \"{EscapePowerShellDoubleQuoted(instruction)}\"";
    }

    private static string WriteV4ImageCliBatchScript(
        string outputRoot,
        int batchNo,
        IEnumerable<string> imageFiles,
        string instruction)
    {
        var scriptDir = Path.Combine(outputRoot, "v5_codex_cli");
        Directory.CreateDirectory(scriptDir);

        var scriptPath = Path.Combine(scriptDir, $"run_batch_{batchNo:000}.ps1");
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = \"Stop\"");
        sb.AppendLine("$OutputEncoding = [System.Text.UTF8Encoding]::new($false)");
        sb.AppendLine("[Console]::InputEncoding = [System.Text.Encoding]::UTF8");
        sb.AppendLine("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8");
        sb.AppendLine("$env:PYTHONIOENCODING = \"utf-8\"");
        sb.AppendLine($"Set-Location -LiteralPath \"{EscapePowerShellDoubleQuoted(outputRoot)}\"");
        sb.AppendLine();
        sb.AppendLine("$prompt = @'");
        sb.AppendLine((instruction ?? string.Empty).Replace("\r\n'@\r\n", "\r\n' @\r\n"));
        sb.AppendLine("'@");
        sb.AppendLine();
        sb.AppendLine("$prompt | codex exec --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox -C \".\" `");

        var images = imageFiles.Where(File.Exists).ToList();
        for (var i = 0; i < images.Count; i++)
        {
            var rel = Path.GetRelativePath(outputRoot, images[i]).Replace('/', Path.DirectorySeparatorChar);
            sb.AppendLine($"    -i \".\\{EscapePowerShellDoubleQuoted(rel)}\" `");
        }
        sb.AppendLine("    -");

        File.WriteAllText(scriptPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return $"powershell -ExecutionPolicy Bypass -File \"{EscapePowerShellDoubleQuoted(scriptPath)}\"";
    }

    private static string EscapePowerShellDoubleQuoted(string value)
        => (value ?? string.Empty)
            .Replace("`", "``")
            .Replace("$", "`$")
            .Replace("\"", "`\"");

    private static string WriteParallelMasterScript(string outputRoot, List<string> batchCommands, int parallelCount)
    {
        var scriptDir = Path.Combine(outputRoot, "v5_codex_cli");
        Directory.CreateDirectory(scriptDir);
        var masterPath = Path.Combine(scriptDir, "run_all_parallel.ps1");
        var maxParallel = Math.Clamp(parallelCount, 1, 10);

        var sb = new StringBuilder();
        sb.AppendLine("# V5 Codex CLI 병렬 실행 스크립트");
        sb.AppendLine($"# 총 {batchCommands.Count}개 배치, {maxParallel}개씩 동시 실행");
        sb.AppendLine("$ErrorActionPreference = \"Stop\"");
        sb.AppendLine("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8");
        sb.AppendLine($"$maxParallel = {maxParallel}");
        sb.AppendLine();

        sb.AppendLine("$scripts = @(");
        for (var i = 0; i < batchCommands.Count; i++)
        {
            var batchScript = Path.Combine(scriptDir, $"run_batch_{i + 1:000}.ps1");
            sb.AppendLine($"    \"{EscapePowerShellDoubleQuoted(batchScript)}\"");
        }
        sb.AppendLine(")");
        sb.AppendLine();

        sb.AppendLine(@"$running = @()
foreach ($script in $scripts) {
    while ($running.Count -ge $maxParallel) {
        $running = @($running | Where-Object { -not $_.HasExited })
        if ($running.Count -ge $maxParallel) { Start-Sleep -Milliseconds 500 }
    }
    $batchName = [System.IO.Path]::GetFileNameWithoutExtension($script)
    Write-Host ""[시작] $batchName"" -ForegroundColor Cyan
    $proc = Start-Process powershell -ArgumentList ""-ExecutionPolicy Bypass -File `""$script`"""" -PassThru -NoNewWindow
    $running += $proc
}
Write-Host ""모든 배치 시작됨. 완료 대기 중..."" -ForegroundColor Yellow
$running | ForEach-Object { $_.WaitForExit() }
Write-Host ""전체 완료!"" -ForegroundColor Green");

        File.WriteAllText(masterPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return masterPath;
    }

    private static string GetCodexInputOutputPriorityGuide()
        => "지시서 안의 입력 파일명/저장 파일명과 이 명령이 충돌하면 이 명령을 우선해.";

    private static string GetCategoryMatchingCommandGuide(string versionSuffix, bool extended, string? inputFileName = null)
    {
        var resultDir = extended ? $"llm_result_ext_{versionSuffix}" : $"llm_result_{versionSuffix}";
        var outputStem = extended ? $"category_match_ext_{versionSuffix}" : $"category_match_{versionSuffix}";
        var outputFile = string.IsNullOrWhiteSpace(inputFileName)
            ? $"{resultDir}/{outputStem}.xlsx"
            : $"{resultDir}/{Path.GetFileNameWithoutExtension(inputFileName)}_{outputStem}.xlsx";

        return $"키워드 저장 후 상품코드/상품명 기준 마켓별 카테고리 매칭 파일도 `{outputFile}`로 추가 생성해. " +
               "카테고리 기준표는 `category_reference/` 또는 같은 폴더의 `*_categories.csv`, `lotteon_standard_categories.csv`, `lotteon_display_categories.csv`, `esm_auction_gmarket_category_matching.csv`를 우선 사용해. " +
               "포함 열: 상품코드, 상품명, 네이버카테고리코드/경로, 쿠팡카테고리코드/경로, 11번가카테고리코드/경로, " +
               "롯데ON표준카테고리코드/경로, 롯데ON전시카테고리코드/경로, 옥션카테고리코드/경로, G마켓카테고리경로, ESM카테고리경로, 확신도, 검수필요, 매칭근거, " +
               "마켓플러스검증상태, 마켓플러스차단마켓, 마켓플러스검증메모, G마켓옵션위험, 롯데ON옵션위험, 옵션검수필요. " +
               "마켓플러스검증상태는 PASS/WARN/BLOCK으로 기록하고, 상품명 100자 초과, 옵션명/옵션값 25자 또는 50바이트 초과 위험, G마켓/옥션 권장 옵션명 불일치 위험, 롯데ON 표준/전시카테고리 누락 또는 옵션명 매칭 필요 여부를 표시해. " +
               "ESM 매칭표는 사이트=G마켓 행의 G/A 카테고리명을 G마켓카테고리경로로 기록해. " +
               "외부 검색 없이 상품명/OCR/생성 키워드와 제공된 카테고리표만 근거로 해.";
    }

    private void RefreshTestCodexCommands(string outputRoot)
    {
        var selectedKeywordVersion = GetSelectedKeywordVersion();
        var versionSuffix = GetKeywordVersionSuffix(selectedKeywordVersion);
        var versionLabel = GetKeywordVersionLabel(selectedKeywordVersion);

        var skillMd = Path.Combine(outputRoot, "keyword_skill.md");
        var skillMdExt = Path.Combine(outputRoot, "keyword_skill_extended.md");
        if (File.Exists(skillMd) && File.Exists(skillMdExt))
            TestSkillMdPathText.Text = $"keyword_skill.md + extended 생성됨 · 현재 명령 버전 {selectedKeywordVersion}";
        else if (File.Exists(skillMd))
            TestSkillMdPathText.Text = $"keyword_skill.md 생성됨 · 현재 명령 버전 {selectedKeywordVersion}";
        else
            TestSkillMdPathText.Text = $"현재 명령 버전 {selectedKeywordVersion}";

        _codexCommands.Clear();
        _codexCommandsExt.Clear();
        _v4ImageCliBatches.Clear();

        var chunksDir = GetActiveChunksDir(outputRoot, selectedKeywordVersion);
        if (Directory.Exists(chunksDir))
        {
            var chunkFiles = Directory.GetFiles(chunksDir, "chunk_*.xlsx");
            if (chunkFiles.Length > 0)
            {
                Array.Sort(chunkFiles);
                Directory.CreateDirectory(Path.Combine(chunksDir, $"llm_result_{versionSuffix}"));
                Directory.CreateDirectory(Path.Combine(chunksDir, $"llm_result_ext_{versionSuffix}"));

                TestCodexCmdTitle.Text = $"Codex 병렬 실행 ({chunkFiles.Length}개 세션 × 2세트, {versionLabel})";
                foreach (var cf in chunkFiles)
                {
                    var fileName = Path.GetFileName(cf);
                    var outputFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_llm_{versionSuffix}.xlsx";
                    var cmd = BuildTestCodexCommand(
                        chunksDir,
                        $"keyword_skill.md 지시서에 따라 {fileName} 파일의 키워드를 채워서 llm_result_{versionSuffix}/{outputFileName} 로 저장해. {GetCodexInputOutputPriorityGuide()} {GetKeywordVersionCommandGuide(selectedKeywordVersion, extended: false)} {GetCategoryMatchingCommandGuide(versionSuffix, extended: false, inputFileName: fileName)}");
                    _codexCommands.Add(cmd);

                    var cmdExt = BuildTestCodexCommand(
                        chunksDir,
                        $"keyword_skill_extended.md 지시서에 따라 {fileName} 파일의 키워드를 채워서 llm_result_ext_{versionSuffix}/{outputFileName} 로 저장해. {GetCodexInputOutputPriorityGuide()} {GetKeywordVersionCommandGuide(selectedKeywordVersion, extended: true)} {GetCategoryMatchingCommandGuide(versionSuffix, extended: true, inputFileName: fileName)}");
                    _codexCommandsExt.Add(cmdExt);
                }

                Log($"분할 엑셀 {chunkFiles.Length}개 → 기본/확장 2세트 명령어 생성 ({versionLabel})");
                BuildCodexCommandCards();
                TestCodexCmdPanel.Visibility = Visibility.Visible;
                StatusText.Text = $"1차 가공 완료 — LLM 키워드 처리 대기 ({versionLabel})";
                return;
            }
        }

        Directory.CreateDirectory(Path.Combine(outputRoot, $"llm_result_{versionSuffix}"));
        Directory.CreateDirectory(Path.Combine(outputRoot, $"llm_result_ext_{versionSuffix}"));

        var uploadFile = !string.IsNullOrWhiteSpace(_lastOutputFile) &&
                         string.Equals(Path.GetDirectoryName(_lastOutputFile), outputRoot, StringComparison.OrdinalIgnoreCase)
            ? _lastOutputFile
            : FindLatestFile(outputRoot, "업로드용_*.xlsx");
        var uploadName = !string.IsNullOrWhiteSpace(uploadFile) ? Path.GetFileName(uploadFile) : "업로드용 엑셀";

        _codexCommands.Add(BuildTestCodexCommand(
            outputRoot,
            $"keyword_skill.md 지시서에 따라 {uploadName} 파일의 키워드를 채워서 llm_result_{versionSuffix}/ 아래에 저장해. 파일명은 입력 파일명 기준으로 `_llm_{versionSuffix}.xlsx` 형식으로 저장해. {GetCodexInputOutputPriorityGuide()} {GetKeywordVersionCommandGuide(selectedKeywordVersion, extended: false)} {GetCategoryMatchingCommandGuide(versionSuffix, extended: false, inputFileName: uploadName)}"));
        _codexCommandsExt.Add(BuildTestCodexCommand(
            outputRoot,
            $"keyword_skill_extended.md 지시서에 따라 {uploadName} 파일의 키워드를 채워서 llm_result_ext_{versionSuffix}/ 아래에 저장해. 파일명은 입력 파일명 기준으로 `_llm_{versionSuffix}.xlsx` 형식으로 저장해. {GetCodexInputOutputPriorityGuide()} {GetKeywordVersionCommandGuide(selectedKeywordVersion, extended: true)} {GetCategoryMatchingCommandGuide(versionSuffix, extended: true, inputFileName: uploadName)}"));

        TestCodexCmdTitle.Text = $"Codex 실행 (기본 + 확장, {versionLabel})";
        Log($"keyword_skill.md + extended → Codex에서 실행 ({versionLabel})");
        BuildCodexCommandCards();
        TestCodexCmdPanel.Visibility = Visibility.Visible;
        StatusText.Text = $"1차 가공 완료 — LLM 키워드 처리 대기 ({versionLabel})";
    }

    private void OnTestOcrComplete(PythonPipelineBridgeResult result)
    {
        _testOutputRoot = result.OutputRoot;
        _lastOutputFile = result.OutputFile;
        TestOutputPathText.Text = $"결과 폴더: {result.OutputRoot}";
        TestOpenOutputButton.IsEnabled = true;

        // OCR 제외 모드용 폴더 자동 설정 (재실행 시 폴더 재선택 불필요)
        _testSkipOcrFolder = result.OutputRoot;
        TestSkipOcrFolderText.Text = result.OutputRoot;

        // 이미지 선택 탭 자동 로드 (listing_images 폴더가 있으면)
        var listingDir = Path.Combine(result.OutputRoot, "listing_images");
        if (Directory.Exists(listingDir) && Directory.GetDirectories(listingDir).Length > 0)
            LoadListingImagesFromRoot(result.OutputRoot);

        RefreshTestCodexCommands(result.OutputRoot);

        var selectedKeywordVersion = GetSelectedKeywordVersion();
        var versionSuffix = GetKeywordVersionSuffix(selectedKeywordVersion);
        var chunksDir = GetActiveChunksDir(result.OutputRoot, selectedKeywordVersion);
        var hasChunks = Directory.Exists(chunksDir) && Directory.GetFiles(chunksDir, "chunk_*.xlsx").Length > 0;
        var llmDir = hasChunks
            ? Path.Combine(chunksDir, $"llm_result_{versionSuffix}")
            : Path.Combine(result.OutputRoot, $"llm_result_{versionSuffix}");
        var llmDirExt = hasChunks
            ? Path.Combine(chunksDir, $"llm_result_ext_{versionSuffix}")
            : Path.Combine(result.OutputRoot, $"llm_result_ext_{versionSuffix}");

        Log($"1차 가공 완료!");
        Log($"기본 결과 → {llmDir}");
        Log($"확장 결과 → {llmDirExt}");

        Activate();
        System.Media.SystemSounds.Asterisk.Play();

        if (!string.IsNullOrEmpty(result.OutputRoot) && Directory.Exists(result.OutputRoot))
            Process.Start(new ProcessStartInfo("explorer.exe", result.OutputRoot));
    }

    private async Task<PythonFreeKeywordV4Result> RunV4LocalKeywordsAsync(
        PythonPipelineBridgeService bridge,
        PythonPipelineBridgeResult pipelineResult)
    {
        var uploadFile = ResolveUploadWorkbook(pipelineResult);
        if (string.IsNullOrWhiteSpace(uploadFile) || !File.Exists(uploadFile))
            throw new FileNotFoundException("V5 키워드 생성용 업로드용 엑셀을 찾지 못했습니다.", uploadFile);

        Log("Phase 3: V4 로컬 키워드 분석 실행 (Google OCR/API 사용 안 함)...");
        StatusText.Text = "V4 로컬 키워드 생성 중...";
        var progress = new Progress<string>(msg => Log($"[V4] {msg}"));
        return await bridge.RunFreeKeywordV4Async(
            pipelineResult.OutputRoot,
            uploadFile,
            progress,
            _cts?.Token ?? CancellationToken.None);
    }

    private static string ResolveUploadWorkbook(PythonPipelineBridgeResult pipelineResult)
    {
        if (!string.IsNullOrWhiteSpace(pipelineResult.OutputFile) && File.Exists(pipelineResult.OutputFile))
            return pipelineResult.OutputFile;

        if (!string.IsNullOrWhiteSpace(pipelineResult.OutputRoot) && Directory.Exists(pipelineResult.OutputRoot))
        {
            var uploadFiles = Directory.GetFiles(pipelineResult.OutputRoot, "업로드용_*.xlsx")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray();
            if (uploadFiles.Length > 0)
                return uploadFiles[0];
        }

        return "";
    }

    private static string WriteV4ImageCliSkillMd(string outputRoot, string uploadFile)
    {
        var skillPath = Path.Combine(outputRoot, "keyword_skill_v5_image_cli.md");
        var uploadName = Path.GetFileName(uploadFile);
        var content =
$@"# KeywordOCR V5 이미지 CLI 지시서

## 실행 방식
- 이 지시서는 내부 OCR/Tesseract/Google OCR 결과를 쓰지 않고, Codex CLI에 첨부된 상품 이미지를 직접 보고 분석하는 방식입니다.
- 첨부된 이미지는 숫자 파일명 상세이미지만 사용합니다. 대표/추가 이미지(`GS`로 시작하는 파일)는 키워드 근거로 첨부하지 않는 것이 기본입니다.
- 숫자 파일명(`1`, `2`, `3`...)은 상세이미지로 보고, 이미지 안의 문구/규격/특징/사용법을 직접 읽어 상품 근거로 삼으세요.
- `GS`로 시작하는 대표/추가 이미지는 V3 업로드용 이미지 가공 결과로만 취급하고, 이 지시서의 키워드 근거로 섞지 마세요.
- Google OCR, Google Vision, 외부 검색, 자동완성, 연관검색어 수집은 사용하지 마세요.
- 이미지가 불명확하면 원본 상품명과 엑셀의 기존 값까지만 사용하고, 보이지 않는 재질/규격/사용처를 창작하지 마세요.

## 입력/출력
- 입력 엑셀: `{uploadName}`
- 결과 엑셀은 화면 명령어가 지정한 경로를 우선합니다.
- 결과 파일이 없으면 입력 엑셀을 복사해 만든 뒤 해당 상품 행만 업데이트하세요.
- 결과 파일이 이미 있으면 그 파일을 열어 해당 상품 행만 업데이트하세요.
- `분리추출후` 시트와 `B마켓` 시트가 있으면 둘 다 처리하세요.
- 키워드 결과 저장 후 같은 상품 기준으로 마켓별 카테고리 매칭 파일도 생성하세요.

## 홈런마켓 3계열 + 준비몰 4상품명 키워드 구조
- 홈런마켓이 메인이고 준비몰은 서브입니다.
- 프로그램상 홈런마켓 업로드 계열은 4가지입니다: `쿠팡`, `ESM/11번가/Cafe24 공통`, `네이버`, `롯데ON`.
- 결과 설계상 필요한 상품명은 5가지입니다: `쿠팡`, `Cafe24 공통(ESM/11번가 fallback 겸용)`, `네이버`, `롯데ON`, `준비몰 B마켓`.
- 마켓별 핵심 원리를 먼저 나눠 생각하세요.
  - 쿠팡: 쿠팡 API 업로드용입니다. 브랜드/핵심특징/실제상품명/메인키워드/세부키워드 구조를 참고하고, 검색태그와 검색필터로 보조 키워드를 분산합니다.
  - ESM/11번가/Cafe24 공통: 하나의 공통 상품명을 사용합니다. 쿠팡식 구조를 참고하되 글자수는 60~80자를 유지하고, ESM 검증 시에는 별도 엑셀 생성 단계에서 45자 안팎으로 줄일 수 있어야 합니다.
  - 네이버: 키워드 단위를 깨지 않고 검색 인식영역에 정확히 배치하는 게임입니다. 상품명 적합도를 최우선으로 두고, 태그는 보조 키워드 분산용으로 씁니다.
  - 롯데ON: 네이버보다 표준적이고 공통명보다 조금 간결한 중간형 상품명입니다.
  - 준비몰 B마켓: 현재는 별도 차별화하지 말고 `Cafe24 공통` 상품명과 같은 값을 사용합니다.
- `Cafe24 공통`은 홈런 Cafe24 업로드용이면서 ESM/11번가에도 같이 쓰는 공통 상품명입니다. 쿠팡 전용 컬럼이 비어 있을 때만 쿠팡 fallback으로 사용됩니다.
- `분리추출후` 시트의 기존 `상품명`, `검색어설정`, `검색키워드`는 `Cafe24 공통`으로 유지/갱신하세요. `상품명`은 짧은 요약명이 아니라 실제 판매에 쓰는 대표상품명이므로 검색형 핵심어, 다른명칭, 용도, 사용처, 규격/소재를 충분히 넣어 작성하세요.
- `분리추출후` 시트에 아래 홈런마켓 마켓별 컬럼이 없으면 새로 추가하고, 있으면 갱신하세요.
  - `홈런_네이버상품명`
  - `홈런_네이버태그`
  - `홈런_롯데ON상품명`
  - `홈런_롯데ON검색키워드`
  - `홈런_쿠팡상품명`
  - `홈런_쿠팡검색태그`
  - `홈런_공통마켓상품명`
  - `홈런_공통마켓검색키워드`
- Cafe24 후보 풀을 기준으로 아래 10종 키워드 컬럼도 함께 갱신하세요. 추후 마켓이 늘어나면 Cafe24 풀에서 분기합니다.
  - `홈런_Cafe24검색어설정`
  - `홈런_Cafe24검색키워드`
  - `홈런_스마트스토어태그`
  - `홈런_스마트스토어검색키워드`
  - `홈런_쿠팡검색태그`
  - `홈런_쿠팡검색키워드`
  - `홈런_ESM검색키워드`
  - `홈런_11번가검색키워드`
  - `홈런_롯데ON검색키워드`
  - `홈런_공통마켓검색키워드`
- `홈런_쿠팡상품명`, `홈런_쿠팡검색태그`는 쿠팡 API 업로드가 먼저 참조하는 전용 컬럼입니다. 비워두지 말고 쿠팡 구조에 맞게 별도 작성하세요.
- `홈런_공통마켓상품명`은 `분리추출후.상품명`과 같은 `Cafe24 공통` 상품명을 넣으세요. 두 컬럼을 서로 다른 4번째 상품명처럼 만들지 마세요.
- `B마켓` 시트는 준비몰 기존 방식입니다. 지금은 `상품명`, `검색어설정`, `검색키워드` 모두 `Cafe24 공통` 값과 동일하게 넣으세요. 홈런마켓 마켓별 컬럼은 억지로 추가하지 마세요.

## 검색형 키워드 우선 원칙
- 3종 후보(`후보키워드_정확형`, `후보키워드_용도형`, `후보키워드_확장형`)는 모두 첫 항목부터 검색형 단어가 나와야 합니다.
- 검색형 단어란 구매자가 네이버쇼핑/쿠팡/오픈마켓 검색창에 직접 입력할 법한 상품명·카테고리명·표준명·다른명칭·핵심 규격 조합입니다.
- 일반 설명어, 마케팅 수식어, 문장형 표현, 선택 유도 문구, 의미 없는 영어/로마자보다 실제 상품을 찾는 명사형 검색어를 앞에 둡니다.
- 원본명/OCR/이미지 문구에서 유의어, 비슷한 이름, 다르게 불리는 이름이 보이면 반드시 후보에 수집하세요. 핵심 유의어는 `정확형` 앞쪽에도 넣고, 보조 유의어와 롱테일 이름은 `확장형`에 모으세요.
- 합성어는 통째 표현이 실제 검색어로 자연스러우면 유지하되, 내부 핵심 명사도 분리해 후보에 넣으세요. 예: `나비 경첩`은 `나비 경첩`, `경첩`, `가구 경첩`처럼 다루고, 이후 같은 상품에서 `경첩`만 반복해 점수가 떨어지지 않도록 중복을 줄이세요.
- 붙여쓰기 합성어가 원본/OCR에 나오더라도 후보에는 사람이 검색하는 띄어쓰기 형태를 우선하세요. 예: `나비경첩` → `나비 경첩`.
- 영어는 재질/규격/기능/공식 모델명을 나타낼 때만 남깁니다. OCR에서 읽힌 영어라도 브랜드 장식, 배경 문구, 의미 없는 약어, 상품 특성과 무관한 단어는 제외하세요.

## 네이버 키워드 3분류와 검색 인식영역
- 네이버용 후보는 상품명 작성 전에 `붙박이 키워드`, `배열고정 키워드`, `조립형 키워드`로 나누세요.
- 붙박이 키워드는 반드시 붙여 써야 하는 키워드입니다. 예: `토종꿀`.
- 배열고정 키워드는 순서가 바뀌면 검색 의도가 달라지거나 적합도가 떨어지는 키워드입니다. 예: `알레르망 냉감패드`.
- 조립형 키워드는 상품명, 속성, 태그, 스토어명 등 검색 인식영역에서 조합될 수 있는 보조 키워드입니다.
- 네이버 상품명은 핵심 검색어 조합을 중간에 끊지 마세요. `알레르망 냉감패드`가 핵심이면 `알레르망 휴비스 냉감패드`처럼 사이에 다른 단어를 끼우지 않습니다.
- 네이버 검색 인식영역은 `상품명`, `스토어명`, `브랜드`, `제조사`, `카테고리`, `속성`, `태그`입니다.
- 중요한 키워드는 상품명 앞쪽에 두고, 상품명에 못 넣은 보조 키워드는 태그/속성/브랜드/제조사/카테고리 등 다른 인식영역에 분산하세요.
- 네이버 태그는 상품명에 못 넣은 보조 키워드 중심으로 작성하고, 상품명에 이미 들어간 단어는 가급적 중복하지 마세요.
- 네이버 태그에는 카테고리명, 브랜드명, 판매처명을 넣지 마세요.

## 5가지 상품명 작성 기준
- 1) 홈런 롯데ON: `홈런_롯데ON상품명`, `홈런_롯데ON검색키워드`
- 2) 홈런 네이버: `홈런_네이버상품명`, `홈런_네이버태그`
- 3) 홈런 쿠팡 API: `홈런_쿠팡상품명`, `홈런_쿠팡검색태그`
- 4) 홈런 Cafe24 공통(ESM/11번가 fallback 겸용): `분리추출후`의 `상품명`, `검색어설정`, `검색키워드`, 그리고 같은 값을 `홈런_공통마켓상품명`, `홈런_공통마켓검색키워드`에 반영
- 5) 준비몰 B마켓: `B마켓`의 `상품명`, `검색어설정`, `검색키워드`. 지금은 4번 `Cafe24 공통`과 같은 값을 넣음
- 같은 상품이라도 마켓별 상품명은 서로 다르게 작성하세요. 한 상품명을 길이만 늘리거나 줄여 복사하지 말고, 앞부분 검색어부터 대표검색어/동의어/용도형 검색어를 섞어 각 마켓에 다른 이름이 올라가게 하세요.
- 같은 기본 GS코드에서 나온 색상/규격/사이즈 파생상품도 상품명 전체 구조를 그대로 복사하지 마세요. 핵심 정체성은 유지하되 앞부분 대표검색어, 용도어, 규격 위치를 조금씩 바꿔 사람이 볼 때 별도 상품명처럼 보이게 작성하세요.
- 가격차 때문에 자동 분리된 상품군도 같은 기본 GS코드 상품으로 보세요. `GS1234567-1`, `GS1234567-2`처럼 분리된 행이나 같은 7자리 기본 GS코드의 여러 옵션 행은 상세이미지/상품정체성/키워드 풀을 공유하되, 각 행의 대표 규격·옵션 범위·사용처 순서를 달리해 상품명 전체가 복붙처럼 보이지 않게 하세요. 가격 숫자는 상품명에 넣지 마세요.
- 롯데ON 상품명은 50~65자, 네이버 상품명은 50~70자, 쿠팡 상품명은 70~95자, Cafe24 공통(ESM/11번가 fallback 겸용) 상품명은 60~80자를 목표로 하세요. 준비몰 B마켓 상품명은 지금은 Cafe24 공통과 같은 값을 사용합니다.
- Cafe24 공통 상품명은 쿠팡식 공식 `[브랜드/노출브랜드] + [메인키워드] + [핵심속성] + [용도/대상] + [규격/수량]`을 참고해 구조만 정리하되, Cafe24/ESM/11번가에서 같이 쓸 수 있도록 60~80자 안에서 정보량을 유지하세요.
- Cafe24 공통에는 할인/이벤트/무료배송/과장 수식어를 넣지 말고, 대표검색어 + 핵심속성 + 용도/대상 + 규격/수량 + 소재/기능/사용처를 자연스럽게 조립하세요.
- 공통 검색키워드는 상품명에 없는 관련 검색어, 동의어, 용도, 대상, 소재, 형태, 스타일, 기능, 원산지, 사용상황을 보완합니다. 상품명에 이미 들어간 단어는 가급적 중복하지 마세요.
- Cafe24 공통 상품명은 네이버보다 풍부해야 하며 ESM/11번가 fallback으로도 쓸 수 있어야 합니다. 이미지/원본에서 확인 가능한 대표검색어 + 유의어/다른명칭 + 용도/사용처 + 규격/소재 중 4가지 이상이 들어가야 합니다.
- 롯데ON 상품명은 네이버와 Cafe24 공통의 중간 느낌으로 작성하세요. 네이버처럼 핵심 검색어 조합을 존중하되, Cafe24 공통보다 간결하고 표준적인 업로드명이어야 합니다.
- Cafe24 공통 상품명은 반드시 첫 단어부터 `정확형 대표검색어` 또는 `다른명칭 중 실제 검색어로 강한 단어`로 시작하세요. `작업용`, `사용자`, `설비 작업자`, `차량 운전자`, `욕실`, `주방`, `누수 보수` 같은 용도/상황/대상어를 맨 앞에 두지 마세요.
- Cafe24 공통의 앞 2~4단어 안에는 상품 정체성을 바로 알 수 있는 검색어가 있어야 합니다. 예: `방수 가스켓`, `미니 깔때기`, `연관솔 8mm`, `컵홀더 고정 실리콘`, `관통 노브 M5`.
- 상품명 길이와 정보량은 네이버/롯데ON < Cafe24 공통 순서로 보되, 롯데ON은 네이버와 공통명의 중간 느낌으로 조정하세요. 단, 글자 수를 채우기 위한 범용어 반복은 금지합니다.
- Cafe24 공통 상품명이 60자 이하로 짧거나, 상품 정체성/다른명칭/용도/사용처/사용자대상/규격/소재/기능 중 4가지 이하만 담겼으면 실패로 보고 다시 작성하세요.
- 상품명, 네이버 태그, 후보 키워드는 단어별 띄어쓰기를 지키세요. 단, `검색어설정`, `홈런_Cafe24검색어설정`, `홈런_ESM검색키워드`, `홈런_11번가검색키워드`, `홈런_공통마켓검색키워드`는 업로드 규격에 맞춰 붙여쓰기+쉼표 구분으로 작성합니다.
- 상품명 끝부분에 옵션값을 쭉 나열하거나 `선택`, `옵션선택`, `색상선택`, `사이즈선택` 같은 선택 유도 문구를 붙이지 마세요.
- 키워드와 상품명에 수식절/서술형 표현을 넣지 마세요. `~에 좋은`, `~할때 쓰는`, `~용으로 쓰는`, `~에 사용하는`, `~를 위한`, `~에 적합한`, `~하기 좋은`, `~고정할때 쓰는` 같은 관형절·서술형 수식어는 금지입니다. 반드시 명사 조합으로만 작성하세요. 예: `문 고정할때 쓰는 스토퍼` → `문 고정 스토퍼`, `피부에 좋은 크림` → `피부 보습 크림`, `벽에 붙이는 후크` → `벽부착 후크`.
- 옵션은 실제 옵션 컬럼에서 처리합니다. 상품명/검색키워드에는 대표 규격이나 핵심 옵션군만 자연스럽게 녹이고, L/M/XL/색상 전체 목록을 마지막에 나열하지 마세요.
- 키워드 컬럼에서는 순수 사이즈/옵션값(`1M`, `2mm`, `35mm`, `L`, `XL`, `10개`)을 최대한 제외하세요. 상품 식별에 가까운 규격/재질 코드(`M8`, `86형`, `PA66`, `304`, `ABS`, `EVA`)만 유지할 수 있습니다.
- `인테리어 소품`, `스타일링 소품`처럼 범용적인 `소품` 표현은 상품명에서 되도록 제외하고, `오브제`, `장식`, `스타일링`, `볼륨 연출`처럼 더 구체적인 검색어로 바꾸세요. 단, 실제 상품군의 대표 검색어가 `소품`인 경우만 보조 키워드에 제한적으로 남깁니다.

## OCR결과 시트 갱신
- 결과 엑셀 안의 `OCR결과` 시트는 구형 OCR 원문 보관용으로 남기지 말고, V4 이미지 직접 분석 요약 시트로 갱신하세요.
- 기존 `OCR결과` 시트가 있으면 내용을 교체하고, 없으면 새로 만드세요.
- 상품별 1행 이상으로 `GS코드`, `원본상품명`, `상품정체성`, `대표검색어`, `다른명칭`, `규격`, `옵션`, `소재`, `핵심특징`, `사용처`, `사용자대상`, `제작도구`, `이미지판독요약`, `키워드소스문장`, `추천키워드1`, `추천키워드2`, `추천키워드3`, `추천키워드4`, `추천키워드5`, `후보키워드_정확형`, `후보키워드_용도형`, `후보키워드_확장형`, `붙박이키워드`, `배열고정키워드`, `조립형키워드`, `인식영역_배치_메모`, `신뢰도`, `검수필요`, `검수메모` 열을 포함하세요.
- `후보키워드_정확형`: 첫 항목부터 표준명/카테고리명/핵심 규격이 들어간 검색형 단어로 시작하고, 상품 정체성, 소재, 호환성 중심의 가장 정확한 구매 검색어 후보를 쉼표로 구분해 작성하세요.
- `후보키워드_용도형`: 첫 항목부터 검색 가능한 상품명+사용처 조합으로 시작하고, 사용처, 상황, 사용자, 문제 해결 맥락 중심의 검색어 후보를 쉼표로 구분해 작성하세요.
- `후보키워드_확장형`: 첫 항목부터 다른 명칭/유의어 중 검색 가능성이 큰 단어로 시작하고, 동의어, 다르게 불리는 이름, 롱테일 조합, 마켓별로 실험할 만한 보조 검색어 후보를 쉼표로 구분해 작성하세요.
- `붙박이키워드`: 붙여 써야 검색어 단위가 유지되는 네이버 핵심어를 쉼표로 구분해 작성하세요.
- `배열고정키워드`: 순서가 바뀌면 안 되는 네이버 핵심 조합을 쉼표로 구분해 작성하세요.
- `조립형키워드`: 상품명/속성/태그/스토어명 등 검색 인식영역에서 조합 가능한 보조 키워드를 쉼표로 구분해 작성하세요.
- `인식영역_배치_메모`: 어떤 키워드를 상품명, 태그, 속성, 브랜드, 제조사, 카테고리 중 어느 영역에 배치했는지 짧게 적으세요.
- 3종 후보는 사람이 비교 분석할 초안입니다. 최종 상품명/검색어설정/검색키워드에 전부 억지로 넣지 말고, 근거가 약한 후보는 `검수메모`에 남기세요.
- `이미지판독요약`에는 상세이미지에서 직접 읽은 문구와 눈으로 확인한 형태/사용 장면을 짧게 정리하세요.
- `키워드소스문장`에는 상품명과 검색어를 만든 근거 문장을 한국어로 남기세요. 보이지 않는 사실은 `검수필요`에 표시하세요.

## Cafe24 공통 정보량 기준
- 기존 `Cafe24_전체상품_정리_20260507_223023.csv`의 긴 상품명 패턴은 정보량 참고용으로만 사용하세요. 지금 목표는 90자 이상이 아니라 60~80자입니다.
- Cafe24 공통 상품명은 Cafe24/ESM/11번가 fallback으로 쓰므로 너무 짧게 줄이지 말고, 60~80자 안에서 검색결과에서 바로 이해되는 정보량을 유지하세요.
- 긴 상품명 일부에 보이는 같은 단어 반복은 그대로 흉내 내지 마세요. 같은 명사/어근을 반복해서 채우는 방식은 실패입니다.
- Cafe24 공통 상품명은 최소한 `정확형 대표검색어`, `다른명칭/동의어`, `규격/수량`, `소재/색상`, `기능/문제해결`, `사용처`, `사용자대상 또는 작업상황` 중 5가지 이상을 담아야 합니다.
- 사용처와 사용자대상은 상품에서 자연스럽게 추론 가능한 범위로만 작성하세요. 예: 설비 작업자, DIY 사용자, 차량 운전자, 매장 운영자, 공방 작업자, 배관 설치 현장.
- `선물`, `인테리어`, `DIY`, `다용도`, `보호`, `정리`, `고정`, `방지`, `교체용`처럼 기존 긴 상품명에서 자주 보인 단어라도 상품과 맞을 때만 쓰고, 범용 채움말로 반복하지 마세요.
- Cafe24 공통은 한 문장형 설명이 아니라 검색어 조립형 상품명입니다. 조사와 문장 종결어미를 빼고, 공백 1칸으로 자연스럽게 이어 붙이세요.
- Cafe24 공통의 긴 구조는 `대표검색어/표준명`을 맨 앞에 놓은 뒤 `규격/수량`, `동의어/확장명`, `소재/색상`, `기능/문제해결`, `사용처`, `사용자대상/작업상황`을 뒤에 붙이는 방식으로 작성하세요.

## 어제 3종 상품명 기준 반영
- `어제_마지막_업로드_5개_3종상품명_초반키워드혼합_소품정리_20260507.xlsx`에서 사용한 구조를 기본 품질 기준으로 삼으세요.
- 먼저 상품별 키워드 풀을 `정확형 검색어`, `동의어/비슷한 이름`, `용도 키워드`, `사용자대상 키워드`, `확장 검색어`로 분리하세요. 이 다섯 묶음은 상품명 작성 전 단계이며, 후보만 만들고 실제 상품명에 반영하지 않으면 실패입니다.
- 키워드 풀 수량은 최소 기준을 지키세요. `정확형 검색어` 8개 이상, `동의어/비슷한 이름` 8개 이상, `용도 키워드` 10개 이상, `사용자대상 키워드` 6개 이상, `확장 검색어` 10개 이상을 목표로 하세요. 상세이미지 근거가 부족한 경우에만 줄이고 `검수 메모`에 이유를 적으세요.
- 결과 엑셀에 `3종상품명` 시트가 없으면 만들고, 있으면 해당 상품 행을 갱신하세요. 시트명은 유지하되 열은 `순번`, `상품코드`, `업로드상태`, `카페24상품번호`, `기존 상품명`, `롯데온용 상품명`, `롯데온 글자수`, `네이버용 상품명`, `네이버 글자수`, `Cafe24공통 상품명`, `Cafe24공통 글자수`, `준비몰 상품명`, `준비몰 글자수`, `정확형 검색어`, `동의어/비슷한 이름`, `용도 키워드`, `사용자대상 키워드`, `확장 검색어`, `A마켓 기존 검색어`, `B마켓 보강 검색어`, `OCR/상품분석 메모`, `검수 메모` 순서로 두세요. 기존 결과에 `쿠팡ESM용 상품명`, `쿠팡용 상품명` 열이 이미 있으면 `Cafe24공통 상품명`과 같은 값을 함께 갱신하세요.
- 결과 엑셀에 `키워드검수` 시트도 만들고 상품별 `상품코드`, `구분`, `키워드` 형태로 정확형/동의어/용도/사용자대상/확장 검색어를 길게 풀어 쓰세요.
- 마켓별 실제 업로드 컬럼은 `3종상품명` 시트의 결과와 일치해야 합니다. `홈런_롯데ON상품명`은 `롯데온용 상품명`, `홈런_네이버상품명`은 `네이버용 상품명`, `홈런_공통마켓상품명`과 `분리추출후.상품명`은 `Cafe24공통 상품명`, `B마켓.상품명`은 `준비몰 상품명`을 사용하세요.
- 5가지 상품명은 같은 첫 2~3단어를 반복하지 마세요. 단, Cafe24 공통과 준비몰은 현재 같은 값을 사용하므로 예외입니다. Cafe24 공통은 반드시 검색형 대표어로 시작해야 하므로 `용도형/사용자대상`을 맨 앞에 두지 말고, 대표검색어 또는 강한 유의어를 앞에 둔 뒤 용도/사용자대상/확장형 검색어를 중후반에 섞어 넣으세요.
- 예시 방향: `차크라 스톤...`, `천연 원석 차크라 돌...`, `힐링 스톤 차크라 원석...`처럼 같은 상품이라도 초반 검색어가 달라야 합니다.
- `사용자대상 키워드`는 상품명에 전부 넣지는 않더라도 Cafe24 공통/준비몰 상품명과 검색키워드에는 적극 반영하세요. 예: 매장 운영자, DIY 사용자, 설비 작업자, 민감 잇몸 사용자, 어항 관리 사용자.
- `OCR/상품분석 메모`에는 어떤 이미지 문구와 상품분석 근거로 3종 상품명을 만들었는지 적고, 단위가 불확실하거나 근거가 약하면 `검수 메모`에 남기세요.

## 상품명 조립 공식
- 상품명은 단어를 무작위로 길게 붙이는 방식이 아니라 아래 순서로 조립하세요.
- 롯데ON용: `정확형 대표검색어` + `핵심 규격/소재` + `동의어 1개` + `짧은 용도 1개`. 표준적이지만 38자 미만이면 안 됩니다.
- 네이버용: `붙박이/배열고정 핵심검색어` + `대표속성` + `용도/대상` + `규격/수량`. 롯데ON과 같은 첫 2~3단어로 시작하면 안 되며, 핵심 검색어 조합을 중간에 끊으면 실패입니다.
- 쿠팡 API: `[브랜드/노출브랜드 또는 대표검색어] + [핵심특징] + [실제상품명] + [메인키워드] + [세부키워드]`를 기본으로, 상품명에 못 넣은 보조어는 `홈런_쿠팡검색태그`에 콤마 구분 20개 이내로 분산하세요.
- Cafe24 공통(ESM/11번가 fallback 겸용): `[브랜드/노출브랜드 또는 대표검색어] + [메인키워드] + [핵심속성] + [용도/대상] + [규격/수량]`를 기본으로, 60~80자 안에서 검색결과에서 바로 이해되고 클릭되는 이름으로 작성하세요.
- 준비몰 B마켓: 지금은 Cafe24 공통과 같은 상품명을 사용하세요. 별도 차별화는 나중에 합니다.
- 가격차/옵션차로 분리된 같은 기본 GS코드 상품은 같은 키워드 풀을 쓰되, `대표 규격`, `옵션 범위`, `사용처`, `앞부분 검색어`를 회전시켜 각 행의 첫 4단어가 같지 않게 하세요.
- 최종 검수에서 `정확형/동의어/용도/사용자/확장` 중 실제 상품명에 반영된 묶음이 3개 미만이면 실패입니다.

## 상품명 구조
- 앞부분: 핵심 검색어, 표준명, 다른 명칭
- 중간: 용도, 특징, 규격, 사이즈, 옵션, 소재
- 뒤쪽: 사용처, 사용하는 사람, 제작/설치/보관 상황
- 너무 짧게 끝내지 말고, 특히 `분리추출후.상품명`은 이미지에서 확인 가능한 별칭/용도/특징/사용처를 풍부하게 넣으세요.
- 단, `소품`처럼 너무 넓은 단어로 글자 수를 채우지 말고 상품을 더 정확히 설명하는 명사로 대체하세요.
- 단, 상품명 마지막에 옵션값만 나열하는 방식은 금지합니다. 예: `L M XL 선택`, `블랙 화이트 선택`, `1호 2호 3호 선택`처럼 끝내지 마세요.

## A/B 마켓 분리
- A마켓(`분리추출후`)은 검색어/규격/기능/소재 중심입니다.
- B마켓(`B마켓`)은 사용처/사용자/상황/용도 중심입니다.
- 핵심상품명과 규격은 공유해도 되지만, 뒤쪽 토큰은 같은 말 반복이 되지 않게 다르게 구성하세요.
- `검색어설정`은 반드시 붙여쓰기+쉼표 구분입니다. 단어 내부에도 단어 사이에도 띄어쓰기가 없어야 합니다. 예: `투명CD케이스,CD공케이스,싱글CD케이스,디스크보관함`
- `검색키워드`도 붙여쓰기+쉼표 구분입니다. 예: `투명CD케이스,공케이스,앨범정리,미디어보관`
- `상품명`만 띄어쓰기를 넣어 자연스럽게 작성합니다.

## 제외
- GS코드, 가격, 배송, 할인, 이벤트, 최저가, 무료배송, 문의, AS 문구는 제외합니다.
- OCR처럼 보이는 페이지 번호, 바코드, 의미 없는 단위 없는 숫자는 제외합니다.
- 다른 상품 이미지에서 나온 단어를 섞지 마세요.

## 카테고리 매칭
- `category_reference/` 폴더의 CSV 기준표를 우선 사용하세요.
- 외부 검색 없이 상품명, 첨부 상세이미지에서 읽은 문구, 생성 키워드, 제공된 카테고리 기준표만 근거로 하세요.
- 키워드 결과 엑셀과 별도로 `category_match_v4_cli` 형식의 엑셀을 생성하세요.
- 네이버/쿠팡/11번가/롯데ON/옥션/G마켓/ESM 카테고리 경로와 코드, 확신도, 검수필요, 매칭근거를 포함하세요.
- V3 업로드 루틴에서 쓰던 카테고리 컬럼은 유지하거나 새 결과 파일에 반영할 수 있으면 반영하세요.";

        File.WriteAllText(skillPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return skillPath;
    }

    private void EnsureV3UploadSupportFiles(string outputRoot)
    {
        var copied = CopyMarketCategoryReferences(outputRoot);
        CopyCategoryTreeFiles(outputRoot);
        Log(copied > 0
            ? $"V3 업로드용 카테고리 기준표 {copied}개 복사"
            : "V3 업로드용 카테고리 기준표 원본을 찾지 못했습니다.");
    }

    private int CopyMarketCategoryReferences(string outputRoot)
    {
        var srcDir = FindMarketCategoryReferenceSource();
        if (string.IsNullOrWhiteSpace(srcDir) || !Directory.Exists(srcDir))
            return 0;

        var files = new[]
        {
            "naver_categories.csv",
            "coupang_categories.csv",
            "11st_categories.csv",
            "lotteon_categories.csv",
            "lotteon_standard_categories.csv",
            "lotteon_display_categories.csv",
            "auction_categories.csv",
            "esm_auction_gmarket_category_matching.csv",
        };
        var refDir = Path.Combine(outputRoot, "category_reference");
        Directory.CreateDirectory(refDir);

        var copied = 0;
        foreach (var fileName in files)
        {
            var src = Path.Combine(srcDir, fileName);
            if (!File.Exists(src))
                continue;
            File.Copy(src, Path.Combine(refDir, fileName), overwrite: true);
            copied++;
        }
        return copied;
    }

    private string? FindMarketCategoryReferenceSource()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var candidates = new[]
        {
            Path.Combine(desktop, "프로젝트", "market-category-matcher", "data", "processed"),
            Path.Combine(Directory.GetParent(_v3Root)?.FullName ?? "", "market-category-matcher", "data", "processed"),
            Path.Combine(_v3Root, "category_reference"),
        };
        return candidates.FirstOrDefault(Directory.Exists);
    }

    private void CopyCategoryTreeFiles(string outputRoot)
    {
        var serviceDir = Path.Combine(_pythonRoot, "app", "services");
        foreach (var fileName in new[] { "naver_category_tree.txt", "coupang_category_tree.txt" })
        {
            var src = Path.Combine(serviceDir, fileName);
            if (File.Exists(src))
                File.Copy(src, Path.Combine(outputRoot, fileName), overwrite: true);
        }
    }

    private void OnTestV4ImageCliReady(PythonPipelineBridgeResult pipelineResult)
    {
        _testOutputRoot = pipelineResult.OutputRoot;
        var uploadFile = ResolveUploadWorkbook(pipelineResult);
        if (string.IsNullOrWhiteSpace(uploadFile) || !File.Exists(uploadFile))
            throw new FileNotFoundException("V5 이미지 CLI용 업로드용 엑셀을 찾지 못했습니다.", uploadFile);

        _lastOutputRoot = pipelineResult.OutputRoot;
        _lastOutputFile = uploadFile;
        TestOutputPathText.Text = $"결과 폴더: {pipelineResult.OutputRoot}";
        TestOpenOutputButton.IsEnabled = true;

        _testSkipOcrFolder = pipelineResult.OutputRoot;
        TestSkipOcrFolderText.Text = pipelineResult.OutputRoot;

        var listingDir = Path.Combine(pipelineResult.OutputRoot, "listing_images");
        if (Directory.Exists(listingDir) && Directory.GetDirectories(listingDir).Length > 0)
            LoadListingImagesFromRoot(pipelineResult.OutputRoot);

        EnsureV3UploadSupportFiles(pipelineResult.OutputRoot);
        RefreshV4ImageCliCodexCommands(pipelineResult.OutputRoot, uploadFile);
        LoadBasicCafe24ProductList(uploadFile);
        TryLoadWorkspaceEditor(uploadFile);
        AutoSaveWorkspacePackage("V5 이미지 준비");

        StatusText.Text = "V5 이미지 준비 완료 — Codex 자동 실행 대기";
        Log("V5 이미지 CLI 명령어 생성 완료");
        Log($"업로드용 엑셀 → {uploadFile}");

        Activate();
        System.Media.SystemSounds.Asterisk.Play();

        if (!string.IsNullOrEmpty(pipelineResult.OutputRoot) && Directory.Exists(pipelineResult.OutputRoot))
            Process.Start(new ProcessStartInfo("explorer.exe", pipelineResult.OutputRoot));
    }

    private void RefreshV4ImageCliCodexCommands(string outputRoot, string uploadFile)
    {
        _codexCommands.Clear();
        _codexCommandsExt.Clear();
        _v4ImageCliBatches.Clear();

        var skillPath = WriteV4ImageCliSkillMd(outputRoot, uploadFile);
        var uploadName = Path.GetFileName(uploadFile);
        var resultDir = Path.Combine(outputRoot, "llm_result_v5_cli");
        Directory.CreateDirectory(resultDir);
        var uploadStem = Path.GetFileNameWithoutExtension(uploadName);
        var outputName = $"{uploadStem}_llm_v5_cli.xlsx";
        _v4ImageCliUploadFile = uploadFile;
        _v4ImageCliFinalResultFile = Path.Combine(resultDir, outputName);
        var groups = CollectV4ImageCliGroups(outputRoot, uploadFile);

        var batchNo = 0;
        foreach (var batch in groups.Chunk(V4ImageCliProductsPerCommand))
        {
            batchNo++;
            var batchGroups = batch.ToList();
            var imageFiles = batchGroups
                .SelectMany(group => group.ImageFiles)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var batchSummary = string.Join("\n", batchGroups.Select(group =>
            {
                var baseCode = NormalizeGsBaseCode(group.GsCode);
                var fileNames = string.Join(", ", group.ImageFiles.Select(Path.GetFileName));
                return $"- {group.GsCode} 또는 {baseCode} / 원본명: {group.ProductName} / 첨부이미지: {fileNames}";
            }));
            var batchOutputName = $"{uploadStem}_batch_{batchNo:000}_llm_v5_cli.xlsx";
            var batchOutputPath = Path.Combine(resultDir, batchOutputName);
            var batchOutputRel = $"llm_result_v5_cli/{batchOutputName}";
            var categoryGuide = GetCategoryMatchingCommandGuide("v5_cli", extended: false, inputFileName: batchOutputName)
                .Replace("상품명/OCR/생성 키워드", "상품명/이미지판독요약/생성 키워드", StringComparison.Ordinal);
            var instruction =
                $"keyword_skill_v5_image_cli.md 지시서의 모든 규칙을 따라 첨부된 이미지들을 직접 보고 `{uploadName}`의 아래 {batchGroups.Count}개 상품 행만 분석/수정해.\n" +
                $"대상 상품:\n{batchSummary}\n" +
                "각 상품은 위 첨부이미지 파일명 묶음끼리만 근거로 사용하고, 다른 상품 이미지의 단어/특징을 섞지 마. " +
                "이미지 안의 문구는 직접 읽고, 형태/구성/사용처는 이미지에서 보이는 범위에서만 추론해. 내부 OCR 파일이나 Google OCR/API는 사용하지 마. " +
                $"결과 파일 `{batchOutputRel}`이 없으면 `{uploadName}`을 복사해서 만들고, 있으면 기존 결과 파일을 열어 해당 행만 업데이트해. " +
                "지시서에 명시된 홈런마켓 3계열 + 준비몰 4상품명 구조, 상품명 조립 공식, OCR결과/3종상품명/키워드검수 시트 규칙을 모두 적용해. " +
                "중요: 검색어설정과 검색키워드는 반드시 붙여쓰기+쉼표 구분이야. 단어 내부에도 사이에도 띄어쓰기 절대 금지. 예: 투명CD케이스,CD공케이스,싱글CD케이스. 상품명만 띄어쓰기 있는 자연어로 작성해. " +
                "키워드/상품명에 '~에 좋은', '~할때 쓰는', '~용으로 쓰는', '~에 사용하는' 같은 수식절/서술형 표현 금지. 반드시 명사 조합만 써. 예: '문 고정할때 쓰는 스토퍼'→'문고정 스토퍼'. " +
                $"{categoryGuide} " +
                $"완료 후 배치 {batchNo} 처리 GS코드 목록과 결과 파일 경로만 출력해.";

            _codexCommands.Add(WriteV4ImageCliBatchScript(outputRoot, batchNo, imageFiles, instruction));
            _v4ImageCliBatches.Add(new V4ImageCliBatchInfo(
                batchNo,
                batchGroups.Count,
                string.Join(", ", batchGroups.Select(group => group.GsCode)),
                batchOutputPath));
        }

        var masterScript = WriteParallelMasterScript(outputRoot, _codexCommands, _parallelCount);

        TestCodexCmdTitle.Text = $"V5 이미지 CLI 실행 명령어 ({groups.Count}개 상품, {_codexCommands.Count}개 배치, 이미지 직접 분석, 병렬 {_parallelCount}개)";
        TestSkillMdPathText.Text = $"keyword_skill_v5_image_cli.md 생성됨 · 상품별 이미지 첨부 · 병렬 실행 스크립트 생성됨";
        BuildCodexCommandCards();
        TestCodexCmdPanel.Visibility = Visibility.Visible;

        Log($"keyword_skill_v5_image_cli.md 생성: {skillPath}");
        Log($"V5 이미지 CLI 명령어 생성: {groups.Count}개 상품 / {_codexCommands.Count}개 배치 → {_v4ImageCliFinalResultFile}");
        Log($"병렬 실행 스크립트: {masterScript}");
    }

    private static List<V4ImageCliGroup> CollectV4ImageCliGroups(string outputRoot, string uploadFile)
    {
        var workbookItems = File.Exists(uploadFile)
            ? Cafe24CreateProductService.ExtractGsCodesFromWorkbook(uploadFile)
            : Array.Empty<(string GsCode, string ProductName)>();
        var items = workbookItems.Count > 0
            ? workbookItems
            : DiscoverGsCodesFromImageFolders(outputRoot);

        var groups = new List<V4ImageCliGroup>();
        foreach (var (gsCode, productName) in items)
        {
            var images = CollectImagesForGs(outputRoot, gsCode);
            if (images.Count == 0)
                continue;
            groups.Add(new V4ImageCliGroup(gsCode, productName, images));
        }
        return groups;
    }

    private static IReadOnlyList<(string GsCode, string ProductName)> DiscoverGsCodesFromImageFolders(string outputRoot)
    {
        var ocrTmp = Path.Combine(outputRoot, "_ocr_tmp");
        if (!Directory.Exists(ocrTmp))
            return Array.Empty<(string, string)>();

        return Directory.GetDirectories(ocrTmp)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && Regex.IsMatch(name, @"^GS\d{7}", RegexOptions.IgnoreCase))
            .Select(name => (name!.ToUpperInvariant(), ""))
            .ToList();
    }

    private static List<string> CollectImagesForGs(string outputRoot, string gsCode)
    {
        var baseCode = NormalizeGsBaseCode(gsCode);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRange(IEnumerable<string> paths, int max)
        {
            foreach (var path in paths.Where(File.Exists).Take(max))
            {
                if (seen.Add(path))
                    result.Add(path);
            }
        }

        var ocrTmp = Path.Combine(outputRoot, "_ocr_tmp");
        if (Directory.Exists(ocrTmp))
        {
            var productDirs = Directory.GetDirectories(ocrTmp)
                .Where(dir => Path.GetFileName(dir).StartsWith(baseCode, StringComparison.OrdinalIgnoreCase)
                           || Path.GetFileName(dir).StartsWith(gsCode, StringComparison.OrdinalIgnoreCase))
                .OrderBy(dir => dir, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var dir in productDirs)
            {
                var all = Directory.GetFiles(dir)
                    .Where(IsSupportedImage)
                    .OrderBy(ImageSortKey)
                    .ToList();
                AddRange(all.Where(path => Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"^\d+$")), V4ImageCliDetailImageLimit);
            }
        }

        return result.Take(V4ImageCliTotalImageLimit).ToList();
    }

    private static IEnumerable<string> FindImagesUnder(string root, string baseCode)
    {
        if (!Directory.Exists(root))
            return Enumerable.Empty<string>();
        return Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(IsSupportedImage)
            .Where(path => Path.GetFileName(path).Contains(baseCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(ImageSortKey);
    }

    private static bool IsSupportedImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp";
    }

    private static string ImageSortKey(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        return int.TryParse(stem, out var n)
            ? $"0000{n:000000}"
            : $"1000{stem}";
    }

    private static string NormalizeGsBaseCode(string gsCode)
    {
        var match = Regex.Match(gsCode ?? "", @"^(GS\d{7})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : (gsCode ?? "").ToUpperInvariant();
    }

    private void OnTestV4Complete(PythonPipelineBridgeResult pipelineResult, PythonFreeKeywordV4Result v4Result)
    {
        _testOutputRoot = pipelineResult.OutputRoot;
        _lastOutputFile = !string.IsNullOrWhiteSpace(v4Result.KeywordResultFile)
            ? v4Result.KeywordResultFile
            : pipelineResult.OutputFile;

        TestOutputPathText.Text = $"결과 폴더: {pipelineResult.OutputRoot}";
        TestOpenOutputButton.IsEnabled = true;

        _testSkipOcrFolder = pipelineResult.OutputRoot;
        TestSkipOcrFolderText.Text = pipelineResult.OutputRoot;

        var listingDir = Path.Combine(pipelineResult.OutputRoot, "listing_images");
        if (Directory.Exists(listingDir) && Directory.GetDirectories(listingDir).Length > 0)
            LoadListingImagesFromRoot(pipelineResult.OutputRoot);

        if (!string.IsNullOrWhiteSpace(v4Result.KeywordResultFile) && File.Exists(v4Result.KeywordResultFile))
        {
            _testLlmResultFile = v4Result.KeywordResultFile;
            _testLlmResultFiles = new List<string> { v4Result.KeywordResultFile };
            TestLlmResultFileText.Text = $"V4 로컬 초안: {Path.GetFileName(v4Result.KeywordResultFile)}";
            TestCafe24UploadButton.IsEnabled = true;
            TestCafe24CreateButton.IsEnabled = true;
            TestCoupangUploadButton.IsEnabled = true;
            TestNaverUploadButton.IsEnabled = true;

            LoadBasicCafe24ProductList(v4Result.KeywordResultFile);
            Log($"V5 결과 엑셀 → {v4Result.KeywordResultFile}");
            Log($"신규등록 목록 자동 로드: {_basicCafe24Items.Count}개");
        }

        TestSkillMdPathText.Text = $"V4 로컬 분석 엑셀: {v4Result.AnalysisFile}";
        RefreshV4CodexCommands(pipelineResult.OutputRoot, v4Result.AnalysisFile);
        StatusText.Text = "V4 로컬 키워드 생성 완료";
        Log("V4 로컬 키워드 생성 완료!");
        Log($"분석 엑셀 → {v4Result.AnalysisFile}");

        Activate();
        System.Media.SystemSounds.Asterisk.Play();

        if (!string.IsNullOrEmpty(pipelineResult.OutputRoot) && Directory.Exists(pipelineResult.OutputRoot))
            Process.Start(new ProcessStartInfo("explorer.exe", pipelineResult.OutputRoot));
    }

    private string WriteV4KeywordSkillMd(string outputRoot, string analysisFile, string? uploadFile)
    {
        var skillPath = Path.Combine(outputRoot, "keyword_skill_v4_local.md");
        var ocrTmp = Path.Combine(outputRoot, "_ocr_tmp");
        var listingImages = Path.Combine(outputRoot, "listing_images");
        var listingImagesB = Path.Combine(outputRoot, "listing_images_B");
        var uploadName = !string.IsNullOrWhiteSpace(uploadFile) ? Path.GetFileName(uploadFile) : "업로드용 엑셀 또는 chunk_*.xlsx";

        var sb = new StringBuilder();
        sb.AppendLine("# KeywordOCR V4 로컬 키워드 생성 지시서");
        sb.AppendLine();
        sb.AppendLine("## 실행 모드");
        sb.AppendLine("- 확인 질문 없이 끝까지 자동 실행하세요.");
        sb.AppendLine("- Google OCR, Google Vision, 외부 API, 외부 검색, 자동완성, 연관검색어 수집을 사용하지 마세요.");
        sb.AppendLine("- 이 지시서는 기존 `keyword_skill.md`, `keyword_skill_extended.md`보다 우선합니다.");
        sb.AppendLine("- 지시서와 화면 명령어가 충돌하면 화면 명령어의 입력/출력 파일명을 우선합니다.");
        sb.AppendLine();
        sb.AppendLine("## 입력 파일");
        sb.AppendLine($"- 키워드 작성 대상 엑셀: `{uploadName}`");
        sb.AppendLine($"- V4 로컬 분석 엑셀: `{analysisFile}`");
        sb.AppendLine($"- 이미지 루트 폴더: `{outputRoot}`");
        sb.AppendLine($"- 상세 이미지/OCR 폴더: `{ocrTmp}`");
        sb.AppendLine($"- A마켓 대표/추가 이미지 폴더: `{listingImages}`");
        sb.AppendLine($"- B마켓 대표/추가 이미지 폴더: `{listingImagesB}`");
        sb.AppendLine();
        sb.AppendLine("## V4 핵심 원칙");
        sb.AppendLine("- 키워드는 기존 Google OCR 결과가 아니라, 다운받은 이미지 폴더를 로컬 OCR/이미지 근거로 분석한 결과에서 만듭니다.");
        sb.AppendLine("- `V4 로컬 분석 엑셀`의 `상품요약` 시트를 최우선 근거로 사용하세요.");
        sb.AppendLine("- `상품요약`의 상품정체성, 대표검색어, 다른명칭, 규격, 옵션, 소재, 핵심특징, 사용처, 사용자대상, 제작도구, 키워드소스문장, 추천키워드1~5를 반드시 읽으세요.");
        sb.AppendLine("- `이미지상세` 시트는 상세이미지별 OCR 텍스트와 파일 역할을 확인하는 보조 근거입니다.");
        sb.AppendLine("- 숫자 파일명(`1`, `2`, `3`...)은 보통 상세이미지입니다. 설명 문구, 규격, 구성품, 사용법, 특징을 추출하는 근거로 봅니다.");
        sb.AppendLine("- `GS`로 시작하는 이미지는 대표/추가 이미지입니다. 상품의 형태, 재질감, 구성, 색상 옵션, 실제 사용 장면을 확인하는 보조 근거로 봅니다.");
        sb.AppendLine("- 실행 환경에서 이미지 직접 확인이 가능하면 대표/추가 이미지와 상세이미지를 직접 열어 형태와 사용처를 확인하세요. 이미지 직접 확인이 불가능하면 `V4 로컬 분석 엑셀`의 OCR/분석 결과만 사용하고 시각 사실을 창작하지 마세요.");
        sb.AppendLine("- 추가 OCR이 꼭 필요하면 설치된 로컬 CLI 도구만 사용하세요. 예: tesseract, Python/Pillow/OpenCV. 유료 API나 네트워크 OCR은 금지입니다.");
        sb.AppendLine();
        sb.AppendLine("## 상품 분석 순서");
        sb.AppendLine("1. 상품코드별로 원본 상품명과 V4 분석 엑셀의 상품정체성을 대조해 실제 상품을 확정합니다.");
        sb.AppendLine("2. 대표검색어와 다른명칭을 모아 사용자가 검색창에 입력할 법한 표준명/별칭을 정합니다.");
        sb.AppendLine("3. 상세 OCR에서 규격, 사이즈, 수량, 소재, 기능, 구성품, 옵션을 추립니다.");
        sb.AppendLine("4. 이미지와 OCR에서 사용처, 사용하는 사람, 제작/설치/보관 상황을 추립니다.");
        sb.AppendLine("5. 추천키워드1~5는 초안으로만 보고, 상품명/검색어설정/검색키워드 구조에 맞춰 다시 조립합니다.");
        sb.AppendLine();
        sb.AppendLine("## 상품명 구성 규칙");
        sb.AppendLine("- 상품명 앞부분: 핵심 검색어, 표준명, 다른 명칭을 배치합니다.");
        sb.AppendLine("- 중간 부분: 용도, 특징, 규격, 사이즈, 옵션, 소재를 배치합니다.");
        sb.AppendLine("- 뒤쪽 부분: 사용처, 사용하는 사람, 제작 상황, 설치 상황을 배치합니다.");
        sb.AppendLine("- 너무 짧게 끝내지 말고, 근거가 있는 다른 명칭/용도/특징/사용처를 풍부하게 넣습니다.");
        sb.AppendLine("- 같은 상품이라도 마켓별 상품명은 서로 다르게 작성합니다. 한 상품명을 길이만 늘리거나 줄여 복사하지 말고, 앞부분 검색어부터 대표검색어/동의어/용도형 검색어를 섞습니다.");
        sb.AppendLine("- 쿠팡 상품명은 쿠팡 API 전용으로 브랜드/핵심특징/실제상품명/메인키워드/세부키워드 구조를 참고하고, 보조어는 쿠팡검색태그에 분산합니다.");
        sb.AppendLine("- Cafe24 공통 상품명은 ESM/11번가/Cafe24 fallback으로 쓰며 60~80자를 목표로 합니다. ESM 엑셀 생성 시에는 45자 안팎으로 줄일 수 있게 핵심어가 앞쪽에 있어야 합니다.");
        sb.AppendLine("- 네이버 상품명은 독립적으로 핵심 검색어 조합을 끊지 않고, 롯데ON 상품명은 네이버와 Cafe24 공통 사이의 중간 느낌으로 표준적이되 너무 짧지 않게 작성합니다.");
        sb.AppendLine("- 준비몰 B마켓 상품명은 지금은 Cafe24 공통 상품명과 같은 값을 사용합니다.");
        sb.AppendLine("- Cafe24 공통 상품명은 반드시 대표 검색어 또는 강한 유의어로 시작합니다. 용도/사용자대상/작업상황은 길이를 풍부하게 만드는 중후반 요소로만 사용합니다.");
        sb.AppendLine("- 단어 나열은 자연스러운 공백 1칸으로 정리합니다.");
        sb.AppendLine("- GS코드, 배송/할인/이벤트/가격/최저가/무료배송/문의/AS 문구는 제외합니다.");
        sb.AppendLine("- OCR 노이즈, 페이지 번호, 바코드, 의미 없는 단위 없는 숫자는 제외합니다.");
        sb.AppendLine("- 영어/로마자/중문 표현은 상품 규격이나 통상 검색어로 꼭 필요한 경우만 제한적으로 사용합니다.");
        sb.AppendLine("- `인테리어 소품`, `스타일링 소품`처럼 범용적인 `소품` 표현은 상품명에서 되도록 제외하고, `오브제`, `장식`, `스타일링`, `볼륨 연출`처럼 더 구체적인 검색어로 바꿉니다.");
        sb.AppendLine();
        sb.AppendLine("## A/B 마켓 분리");
        sb.AppendLine("- A마켓 상품명은 검색어/규격/기능/소재 중심으로 구성합니다.");
        sb.AppendLine("- B마켓 상품명은 사용처/사용자/상황/용도 중심으로 구성합니다.");
        sb.AppendLine("- A와 B는 핵심상품명과 규격은 공유해도 되지만, 뒤쪽 토큰은 같은 말 반복이 되지 않게 각도를 다르게 잡습니다.");
        sb.AppendLine("- A/B 검색어설정은 반드시 붙여쓰기+쉼표 구분입니다. 단어 내부에도 단어 사이에도 띄어쓰기 금지. 예: `투명CD케이스,CD공케이스,싱글CD케이스`");
        sb.AppendLine("- 검색키워드도 붙여쓰기+쉼표 구분입니다. 상품명만 띄어쓰기를 넣어 자연스럽게 작성합니다.");
        sb.AppendLine();
        sb.AppendLine("## 출력");
        sb.AppendLine("- 입력 엑셀의 기존 컬럼과 행 순서를 유지하고, 상품명/검색어설정/검색키워드 관련 컬럼만 채웁니다.");
        sb.AppendLine("- 화면 명령어에서 지정한 `llm_result_v4_local` 또는 `llm_result_ext_v4_local` 폴더와 파일명으로 저장합니다.");
        sb.AppendLine("- 완료 후 처리 건수와 결과 파일 경로만 출력합니다.");

        File.WriteAllText(skillPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return skillPath;
    }

    private static void CopyV4KeywordSkillMd(string skillPath, string targetDir)
    {
        if (string.IsNullOrWhiteSpace(skillPath) || !File.Exists(skillPath) || !Directory.Exists(targetDir))
            return;

        File.Copy(skillPath, Path.Combine(targetDir, Path.GetFileName(skillPath)), overwrite: true);
    }

    private void RefreshV4CodexCommands(string outputRoot, string analysisFile)
    {
        _codexCommands.Clear();
        _codexCommandsExt.Clear();
        _v4ImageCliBatches.Clear();

        var selectedKeywordVersion = GetSelectedKeywordVersion();
        var chunksDir = GetActiveChunksDir(outputRoot, selectedKeywordVersion);
        var uploadFile = !string.IsNullOrWhiteSpace(_lastOutputFile) && File.Exists(_lastOutputFile)
            ? _lastOutputFile
            : FindLatestFile(outputRoot, "업로드용_*.xlsx");
        var v4SkillPath = WriteV4KeywordSkillMd(outputRoot, analysisFile, uploadFile);

        var analysisName = Path.GetFileName(analysisFile);
        var v4Guide =
            $"`keyword_skill_v4_local.md` 지시서를 최우선으로 따르고, V4 분석 엑셀 `{analysisFile}`을 반드시 함께 읽어. " +
            "다운받은 상세/대표/추가 이미지 폴더를 로컬 OCR/이미지 근거로 해석해 상품정체성, 다른명칭, 특징, 사용처를 보강해. " +
            "Google OCR/API/외부 검색/자동완성은 사용하지 마.";

        if (Directory.Exists(chunksDir))
        {
            var chunkFiles = Directory.GetFiles(chunksDir, "chunk_*.xlsx");
            if (chunkFiles.Length > 0)
            {
                Array.Sort(chunkFiles);
                CopyV4KeywordSkillMd(v4SkillPath, chunksDir);
                Directory.CreateDirectory(Path.Combine(chunksDir, "llm_result_v4_local"));
                Directory.CreateDirectory(Path.Combine(chunksDir, "llm_result_ext_v4_local"));

                TestCodexCmdTitle.Text = $"V4 LLM 붙여넣기 명령어 ({chunkFiles.Length}개 세션 × 2세트, 분석: {analysisName})";
                foreach (var cf in chunkFiles)
                {
                    var fileName = Path.GetFileName(cf);
                    var outputFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_llm_v4_local.xlsx";
                    _codexCommands.Add(BuildTestCodexCommand(
                        chunksDir,
                        $"{v4Guide} `{fileName}` 파일의 키워드를 채우고 `llm_result_v4_local/{outputFileName}` 로 저장해. {GetCodexInputOutputPriorityGuide()}"));
                    _codexCommandsExt.Add(BuildTestCodexCommand(
                        chunksDir,
                        $"{v4Guide} 확장 키워드셋으로 `{fileName}` 파일의 키워드를 더 풍부하게 채우고 `llm_result_ext_v4_local/{outputFileName}` 로 저장해. {GetCodexInputOutputPriorityGuide()}"));
                }

                TestSkillMdPathText.Text = $"keyword_skill_v4_local.md 생성됨 · 분석: {analysisName}";
                BuildCodexCommandCards();
                TestCodexCmdPanel.Visibility = Visibility.Visible;
                Log($"keyword_skill_v4_local.md 생성: {v4SkillPath}");
                Log($"V4 LLM 붙여넣기 명령어 생성: {chunkFiles.Length}개 청크, 분석 엑셀 {analysisName}");
                StatusText.Text = "V4 분석 완료 — LLM 붙여넣기 명령어 생성됨";
                return;
            }
        }

        Directory.CreateDirectory(Path.Combine(outputRoot, "llm_result_v4_local"));
        Directory.CreateDirectory(Path.Combine(outputRoot, "llm_result_ext_v4_local"));
        var uploadName = !string.IsNullOrWhiteSpace(uploadFile) ? Path.GetFileName(uploadFile) : "업로드용 엑셀";
        var outputName = $"{Path.GetFileNameWithoutExtension(uploadName)}_llm_v4_local.xlsx";

        TestCodexCmdTitle.Text = $"V4 LLM 붙여넣기 명령어 (분석: {analysisName})";
        _codexCommands.Add(BuildTestCodexCommand(
            outputRoot,
            $"{v4Guide} `{uploadName}` 파일의 키워드를 채우고 `llm_result_v4_local/{outputName}` 로 저장해. {GetCodexInputOutputPriorityGuide()}"));
        _codexCommandsExt.Add(BuildTestCodexCommand(
            outputRoot,
            $"{v4Guide} 확장 키워드셋으로 `{uploadName}` 파일의 키워드를 더 풍부하게 채우고 `llm_result_ext_v4_local/{outputName}` 로 저장해. {GetCodexInputOutputPriorityGuide()}"));

        TestSkillMdPathText.Text = $"keyword_skill_v4_local.md 생성됨 · 분석: {analysisName}";
        BuildCodexCommandCards();
        TestCodexCmdPanel.Visibility = Visibility.Visible;
        Log($"keyword_skill_v4_local.md 생성: {v4SkillPath}");
        Log($"V4 LLM 붙여넣기 명령어 생성: 분석 엑셀 {analysisName}");
        StatusText.Text = "V4 분석 완료 — LLM 붙여넣기 명령어 생성됨";
    }

    private void BuildCodexCommandCards()
    {
        TestCodexCmdList.Items.Clear();

        // ── 기본 키워드셋 섹션 ──
        _AddSectionHeader("기본 키워드셋", "#a29bfe");
        _AddCommandCards(_codexCommands, "기본", "#2d2d44", "#6c5ce7");

        // ── 확장 키워드셋 섹션 (PDF 보고서 규칙) ──
        if (_codexCommandsExt.Count > 0)
        {
            _AddSectionHeader("확장 키워드셋 (SEO 최적화)", "#00b894");
            _AddCommandCards(_codexCommandsExt, "확장", "#2d3d44", "#00b894");
        }
    }

    private void _AddSectionHeader(string title, string colorHex)
    {
        var header = new TextBlock
        {
            Text = $"▸ {title}",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)),
            Margin = new Thickness(0, 8, 0, 4)
        };
        TestCodexCmdList.Items.Add(header);
    }

    private void _AddCommandCards(List<string> commands, string label, string bgHex, string accentHex)
    {
        for (int i = 0; i < commands.Count; i++)
        {
            var idx = i;
            var cmd = commands[i];

            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bgHex)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var stack = new StackPanel();

            var header = new TextBlock
            {
                Text = commands.Count > 1 ? $"{label} 세션 {i + 1}" : $"{label} 실행 명령어",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accentHex)),
                Margin = new Thickness(0, 0, 0, 4)
            };

            var cmdText = new TextBox
            {
                Text = cmd,
                FontSize = 10,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1e1e2e")),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#f8f8f2")),
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(6, 4, 6, 4)
            };

            var copyBtn = new Button
            {
                Content = "복사",
                Height = 24,
                FontSize = 10,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accentHex)),
                Foreground = System.Windows.Media.Brushes.White
            };
            var capturedLabel = label;
            copyBtn.Click += (s, e) =>
            {
                Clipboard.SetText(cmd);
                Log($"{capturedLabel} 세션 {idx + 1} 명령어 복사됨");
                StatusText.Text = $"{capturedLabel} 세션 {idx + 1} 명령어 복사 완료";
            };

            stack.Children.Add(header);
            stack.Children.Add(cmdText);
            stack.Children.Add(copyBtn);
            border.Child = stack;
            TestCodexCmdList.Items.Add(border);
        }
    }

    private void TestOpenOutput_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_testOutputRoot) && Directory.Exists(_testOutputRoot))
            Process.Start(new ProcessStartInfo("explorer.exe", _testOutputRoot));
    }

    private void TestOpenChunksFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_testOutputRoot)) return;
        var chunksDir = GetActiveChunksDir(_testOutputRoot, GetSelectedKeywordVersion());
        if (Directory.Exists(chunksDir))
            Process.Start(new ProcessStartInfo("explorer.exe", chunksDir));
    }

    private void TestCopyAllCodexCmd_Click(object sender, RoutedEventArgs e)
    {
        var sections = new List<string>();
        if (_codexCommands.Count > 0)
        {
            sections.Add("# ── 기본 키워드셋 ──");
            sections.AddRange(_codexCommands.Select((c, i) => $"# 기본 세션 {i + 1}\n{c}"));
        }
        if (_codexCommandsExt.Count > 0)
        {
            sections.Add("\n# ── 확장 키워드셋 (SEO 최적화) ──");
            sections.AddRange(_codexCommandsExt.Select((c, i) => $"# 확장 세션 {i + 1}\n{c}"));
        }
        if (sections.Count > 0)
        {
            Clipboard.SetText(string.Join("\n\n", sections));
            var total = _codexCommands.Count + _codexCommandsExt.Count;
            Log($"전체 Codex 명령어 {total}개 복사됨 (기본 {_codexCommands.Count} + 확장 {_codexCommandsExt.Count})");
            StatusText.Text = $"전체 {total}개 명령어 복사 완료";
        }
    }

    private async void TestRunCodexAuto_Click(object sender, RoutedEventArgs e)
        => await RunCodexCommandsAsync(confirm: true, manageRunning: true);

    private void ShowCodexProgress(string title, double percent, string detail)
    {
        var safePercent = Math.Clamp(percent, 0, 100);
        Dispatcher.Invoke(() =>
        {
            TestCodexProgressPanel.Visibility = Visibility.Visible;
            TestCodexProgressText.Text = title;
            TestCodexProgressDetailText.Text = detail;
            TestCodexProgressBar.Value = safePercent;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = safePercent;
            StatusText.Text = title;
        });
    }

    private static string FormatDurationShort(TimeSpan value)
    {
        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours}시간 {value.Minutes}분";
        if (value.TotalMinutes >= 1)
            return $"{(int)value.TotalMinutes}분 {value.Seconds}초";
        return $"{Math.Max(1, (int)Math.Ceiling(value.TotalSeconds))}초";
    }

    private static string BuildRemainingText(Stopwatch stopwatch, int completedUnits, int totalUnits)
    {
        if (completedUnits <= 0 || totalUnits <= completedUnits)
            return completedUnits <= 0 ? "예상 남은 시간: 계산 중" : "예상 남은 시간: 0초";

        var averageSeconds = stopwatch.Elapsed.TotalSeconds / completedUnits;
        var remaining = TimeSpan.FromSeconds(averageSeconds * (totalUnits - completedUnits));
        return $"예상 남은 시간: 약 {FormatDurationShort(remaining)}";
    }

    private static string ShortenForStatus(string value, int maxLength = 110)
    {
        var normalized = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }

    private async Task RunCodexCommandsAsync(bool confirm, bool manageRunning)
    {
        if (_codexCommands.Count == 0)
        {
            if (confirm)
                MessageBox.Show("실행할 기본 Codex 명령어가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                Log("Codex 자동 실행 건너뜀: 실행할 기본 명령어가 없습니다.");
            return;
        }

        if (confirm)
        {
            var answer = MessageBox.Show(
                $"기본 키워드셋 {_codexCommands.Count}개를 Codex CLI로 순차 실행합니다.\n확장 키워드셋은 자동 실행하지 않습니다.",
                "Codex 자동 실행",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK)
                return;
        }

        if (_cts is null || _cts.IsCancellationRequested)
            _cts = new CancellationTokenSource();

        if (manageRunning)
            SetRunning(true);
        var isV4ImageCliRun = _v4ImageCliBatches.Count == _codexCommands.Count && _v4ImageCliBatches.Count > 0;
        var totalProducts = isV4ImageCliRun
            ? _v4ImageCliBatches.Sum(batch => batch.ProductCount)
            : _codexCommands.Count;
        var completedProducts = 0;
        var stopwatch = Stopwatch.StartNew();
        ShowCodexProgress(
            isV4ImageCliRun
                ? $"Codex 자동 실행 시작 — 배치 0/{_codexCommands.Count}, 상품 0/{totalProducts}"
                : $"Codex 자동 실행 시작 — 세션 0/{_codexCommands.Count}",
            0,
            "첫 배치를 시작합니다.");
        Log($"Codex 자동 실행 시작: 기본 {_codexCommands.Count}개" +
            (isV4ImageCliRun ? $" / 상품 {totalProducts}개" : ""));
        var completed = false;

        try
        {
            for (var i = 0; i < _codexCommands.Count; i++)
            {
                var cmd = _codexCommands[i];
                var batchInfo = isV4ImageCliRun
                    ? _v4ImageCliBatches[i]
                    : new V4ImageCliBatchInfo(i + 1, 1, $"세션 {i + 1}");
                var batchStartProduct = completedProducts + 1;
                var batchEndProduct = Math.Min(totalProducts, completedProducts + batchInfo.ProductCount);
                var startPercent = totalProducts > 0
                    ? completedProducts * 100.0 / totalProducts
                    : i * 100.0 / _codexCommands.Count;
                var etaText = BuildRemainingText(stopwatch, completedProducts, totalProducts);
                var runningTitle = isV4ImageCliRun
                    ? $"Codex 실행 중 — 배치 {i + 1}/{_codexCommands.Count}, 상품 {batchStartProduct}-{batchEndProduct}/{totalProducts}"
                    : $"Codex 실행 중 — 세션 {i + 1}/{_codexCommands.Count}";
                ShowCodexProgress(
                    runningTitle,
                    startPercent,
                    isV4ImageCliRun
                        ? $"현재 상품: {batchInfo.Codes} · 완료 {completedProducts}/{totalProducts} · {etaText}"
                        : $"완료 {i}/{_codexCommands.Count} · {BuildRemainingText(stopwatch, i, _codexCommands.Count)}");

                Log(isV4ImageCliRun
                    ? $"Codex 배치 {i + 1}/{_codexCommands.Count} 실행... 상품 {batchStartProduct}-{batchEndProduct}/{totalProducts} ({batchInfo.Codes})"
                    : $"Codex 세션 {i + 1}/{_codexCommands.Count} 실행...");

                var latestOutputUpdate = DateTime.MinValue;
                await RunPowerShellCommandAsync(
                    cmd,
                    _cts?.Token ?? CancellationToken.None,
                    line =>
                    {
                        if (DateTime.Now - latestOutputUpdate < TimeSpan.FromSeconds(1))
                            return;
                        latestOutputUpdate = DateTime.Now;
                        ShowCodexProgress(
                            runningTitle,
                            startPercent,
                            $"현재 상품: {batchInfo.Codes} · 최근 출력: {ShortenForStatus(line)}");
                    });

                completedProducts += batchInfo.ProductCount;
                var donePercent = totalProducts > 0
                    ? completedProducts * 100.0 / totalProducts
                    : (i + 1) * 100.0 / _codexCommands.Count;
                var completedTitle = isV4ImageCliRun
                    ? $"Codex 진행 — 배치 {i + 1}/{_codexCommands.Count} 완료, 상품 {completedProducts}/{totalProducts}"
                    : $"Codex 진행 — 세션 {i + 1}/{_codexCommands.Count} 완료";
                ShowCodexProgress(
                    completedTitle,
                    donePercent,
                    isV4ImageCliRun
                        ? $"완료 상품: {completedProducts}/{totalProducts} · 남은 상품: {Math.Max(0, totalProducts - completedProducts)} · {BuildRemainingText(stopwatch, completedProducts, totalProducts)}"
                        : $"완료 {i + 1}/{_codexCommands.Count} · {BuildRemainingText(stopwatch, i + 1, _codexCommands.Count)}");

                Log(isV4ImageCliRun
                    ? $"Codex 배치 {i + 1}/{_codexCommands.Count} 완료 — 상품 {completedProducts}/{totalProducts}"
                    : $"Codex 세션 {i + 1}/{_codexCommands.Count} 완료");
            }

            StatusText.Text = "Codex 자동 실행 완료";
            ShowCodexProgress("Codex 자동 실행 완료", 100, $"전체 처리 완료 · 총 소요 {FormatDurationShort(stopwatch.Elapsed)}");
            Log("Codex 자동 실행 완료");
            TryMergeV4ImageCliBatchResults();
            TryAutoLoadLatestV4Result();
            MarkSelectedProductsCompleted("Codex 자동 실행 완료");
            await RunV5AutoCafe24CreateAfterKeywordAsync();
            completed = true;
        }
        catch (OperationCanceledException)
        {
            Log("Codex 자동 실행 취소됨");
            StatusText.Text = "Codex 자동 실행 취소됨";
            ShowCodexProgress("Codex 자동 실행 취소됨", ProgressBar.Value, "현재까지 생성된 파일과 선택 상태를 자동저장합니다.");
            SaveInterruptedWorkspaceProgress("Codex 자동 실행 중단");
        }
        catch (Exception ex)
        {
            Log($"Codex 자동 실행 오류: {ex.Message}");
            MessageBox.Show(ex.Message, "Codex 자동 실행 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Codex 자동 실행 오류";
            ShowCodexProgress("Codex 자동 실행 오류", ProgressBar.Value, ShortenForStatus(ex.Message, 160));
        }
        finally
        {
            if (manageRunning)
            {
                SetRunning(false);
                ProgressBar.IsIndeterminate = false;
                if (completed)
                    RunCompletionPowerActionIfNeeded("Codex 자동 실행 완료 후 전원 동작");
            }
        }
    }

    private int _parallelCount = 5;
    private bool _parallelRunning;

    private void ParallelPresetRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (ParallelCustomValue is null) return;
        if (Parallel3Radio?.IsChecked == true) { _parallelCount = 3; ParallelCustomValue.IsEnabled = false; }
        else if (Parallel5Radio?.IsChecked == true) { _parallelCount = 5; ParallelCustomValue.IsEnabled = false; }
        else if (Parallel10Radio?.IsChecked == true) { _parallelCount = 10; ParallelCustomValue.IsEnabled = false; }
        else if (ParallelCustomRadio?.IsChecked == true)
        {
            ParallelCustomValue.IsEnabled = true;
            ParallelCustomValue.Focus();
            if (int.TryParse(ParallelCustomValue.Text, out var v))
                _parallelCount = Math.Clamp(v, 1, 20);
        }
        SyncParallelCountCombo();
    }

    private void ParallelCustomValue_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ParallelCustomRadio?.IsChecked == true && int.TryParse(ParallelCustomValue.Text, out var v))
        {
            _parallelCount = Math.Clamp(v, 1, 20);
            SyncParallelCountCombo();
        }
    }

    private void SyncParallelCountCombo()
    {
        if (ParallelCountCombo is null) return;
        var idx = _parallelCount - 1;
        if (idx >= 0 && idx < ParallelCountCombo.Items.Count)
            ParallelCountCombo.SelectedIndex = idx;
    }

    private void ParallelCountCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ParallelCountCombo?.SelectedItem is ComboBoxItem item
            && int.TryParse(item.Content?.ToString(), out var count))
        {
            _parallelCount = Math.Clamp(count, 1, 10);
        }
    }

    private async void RunAllParallel_Click(object sender, RoutedEventArgs e)
    {
        if (_codexCommands.Count == 0)
        {
            MessageBox.Show("실행할 Codex 명령어가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            $"총 {_codexCommands.Count}개 배치를 동시 {_parallelCount}개씩 병렬 실행합니다.\n" +
            "모든 배치가 완료된 후 업로드가 가능합니다.\n\n진행하시겠습니까?",
            "Codex 병렬 실행",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK)
            return;

        await RunCodexCommandsParallelAsync();
    }

    private async Task RunCodexCommandsParallelAsync()
    {
        if (_parallelRunning) return;
        _parallelRunning = true;

        if (_cts is null || _cts.IsCancellationRequested)
            _cts = new CancellationTokenSource();

        SetRunning(true);
        RunAllParallelButton.IsEnabled = false;
        SetUploadButtonsEnabled(false);

        var isV4 = _v4ImageCliBatches.Count == _codexCommands.Count && _v4ImageCliBatches.Count > 0;
        var totalBatches = _codexCommands.Count;
        var totalProducts = isV4 ? _v4ImageCliBatches.Sum(b => b.ProductCount) : totalBatches;
        var completedBatches = 0;
        var completedProducts = 0;
        var batchLock = new object();
        var stopwatch = Stopwatch.StartNew();

        ShowParallelProgress($"병렬 실행 시작 — {_parallelCount}개 동시, 총 {totalBatches}배치 / {totalProducts}상품", 0, "시작 중...");

        var completed = false;
        try
        {
            var semaphore = new SemaphoreSlim(_parallelCount, _parallelCount);
            var tasks = _codexCommands.Select(async (cmd, idx) =>
            {
                await semaphore.WaitAsync(_cts?.Token ?? CancellationToken.None);
                try
                {
                    var batchInfo = isV4 ? _v4ImageCliBatches[idx] : new V4ImageCliBatchInfo(idx + 1, 1, $"세션 {idx + 1}");
                    Dispatcher.Invoke(() => Log($"[병렬] 배치 {idx + 1}/{totalBatches} 시작 — {batchInfo.Codes}"));

                    await RunPowerShellCommandAsync(cmd, _cts?.Token ?? CancellationToken.None);

                    lock (batchLock)
                    {
                        completedBatches++;
                        completedProducts += batchInfo.ProductCount;
                    }

                    var pct = totalProducts > 0 ? completedProducts * 100.0 / totalProducts : completedBatches * 100.0 / totalBatches;
                    var eta = BuildRemainingText(stopwatch, completedProducts, totalProducts);
                    ShowParallelProgress(
                        $"병렬 실행 중 — {completedBatches}/{totalBatches}배치 완료, 상품 {completedProducts}/{totalProducts}",
                        pct,
                        $"배치 {idx + 1} 완료 ({batchInfo.Codes}) · {eta}");
                    Dispatcher.Invoke(() => Log($"[병렬] 배치 {idx + 1}/{totalBatches} 완료 — {batchInfo.Codes}"));
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            await Task.WhenAll(tasks);

            ShowParallelProgress("전체 병렬 실행 완료", 100,
                $"총 {totalBatches}배치 / {totalProducts}상품 · 소요 {FormatDurationShort(stopwatch.Elapsed)}");
            Log($"[병렬] 전체 완료: {totalBatches}배치, {totalProducts}상품, {FormatDurationShort(stopwatch.Elapsed)}");
            StatusText.Text = "Codex 병렬 실행 완료 — 업로드 가능";
            TryMergeV4ImageCliBatchResults();
            TryAutoLoadLatestV4Result();
            MarkSelectedProductsCompleted("Codex 병렬 실행 완료");
            await RunV5AutoCafe24CreateAfterKeywordAsync();
            completed = true;
        }
        catch (OperationCanceledException)
        {
            Log("[병렬] 실행 취소됨");
            ShowParallelProgress("병렬 실행 취소됨", ParallelProgressBar.Value, "취소됨 — 완료된 배치 결과는 유지됩니다.");
            SaveInterruptedWorkspaceProgress("Codex 병렬 실행 중단");
        }
        catch (Exception ex)
        {
            Log($"[병렬] 오류: {ex.Message}");
            ShowParallelProgress("병렬 실행 오류", ParallelProgressBar.Value, ShortenForStatus(ex.Message, 160));
            MessageBox.Show(ex.Message, "병렬 실행 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _parallelRunning = false;
            RunAllParallelButton.IsEnabled = true;
            SetRunning(false);
            ProgressBar.IsIndeterminate = false;
            SetUploadButtonsEnabled(true);
            if (completed)
                RunCompletionPowerActionIfNeeded("Codex 병렬 실행 완료 후 전원 동작");
        }
    }

    private void ShowParallelProgress(string title, double percent, string detail)
    {
        var safe = Math.Clamp(percent, 0, 100);
        Dispatcher.Invoke(() =>
        {
            ParallelProgressPanel.Visibility = Visibility.Visible;
            ParallelProgressText.Text = title;
            ParallelProgressBar.Value = safe;
            ParallelDetailText.Text = detail;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = safe;
            StatusText.Text = title;
        });
    }

    private async Task RunV5AutoCafe24CreateAfterKeywordAsync()
    {
        if (V5AutoCafe24CreateCheckBox?.IsChecked != true)
            return;

        var files = _testLlmResultFiles.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            Log("[V5 자동등록] 최종 키워드 결과 파일을 찾지 못해 Cafe24 자동등록을 건너뜁니다.");
            return;
        }

        if (_basicCafe24Items.Count == 0)
            LoadBasicCafe24ProductList(files[0]);

        if (_basicCafe24Items.Count == 0)
        {
            Log("[V5 자동등록] Cafe24 등록 대상 목록이 비어 있어 자동등록을 건너뜁니다.");
            return;
        }

        Log("[V5 자동등록] 키워드 생성 완료 후 Cafe24 신규등록을 시작합니다. API 마켓 업로드는 자동 실행하지 않습니다.");
        StatusText.Text = "V5 Cafe24 자동 신규등록 중...";
        var ok = await RunBasicCafe24CreateAsync(
            runLinkedMarketUploads: false,
            showDialogs: false,
            sourceLabel: "V5 자동");
        Log(ok
            ? "[V5 자동등록] Cafe24 신규등록 완료"
            : "[V5 자동등록] Cafe24 신규등록이 완료되지 않았습니다. 로그를 확인하세요.");
    }

    private void SetUploadButtonsEnabled(bool enabled)
    {
        Dispatcher.Invoke(() =>
        {
            foreach (var btn in FindVisualChildren<Button>(this))
            {
                var content = btn.Content?.ToString() ?? "";
                if (content.Contains("Cafe24") || content.Contains("업로드") || content.Contains("네이버") || content.Contains("롯데"))
                {
                    if (content.Contains("업로드") || content.Contains("등록") || content.Contains("전송"))
                        btn.IsEnabled = enabled;
                }
            }
        });
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var sub in FindVisualChildren<T>(child)) yield return sub;
        }
    }

    private async Task RunPowerShellCommandAsync(
        string command,
        CancellationToken cancellationToken,
        Action<string>? outputReceived = null)
    {
        var wrappedCommand =
            "$OutputEncoding = [System.Text.UTF8Encoding]::new($false); " +
            "[Console]::InputEncoding = [System.Text.Encoding]::UTF8; " +
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " +
            "$env:PYTHONIOENCODING = 'utf-8'; " +
            command;

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(wrappedCommand);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var outputDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorBuffer = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                outputDone.TrySetResult(true);
                return;
            }
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Log($"[Codex] {e.Data}");
                outputReceived?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                errorDone.TrySetResult(true);
                return;
            }
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                errorBuffer.AppendLine(e.Data);
                Log($"[Codex] {e.Data}");
                outputReceived?.Invoke(e.Data);
            }
        };

        if (!process.Start())
            throw new InvalidOperationException("PowerShell 프로세스를 시작하지 못했습니다.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
        });

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(outputDone.Task, errorDone.Task);

        if (process.ExitCode != 0)
        {
            var detail = errorBuffer.ToString().Trim();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(detail)
                    ? $"Codex CLI 실행 실패: exit code {process.ExitCode}"
                    : detail);
        }
    }

    private bool TryMergeV4ImageCliBatchResults()
    {
        if (_v4ImageCliBatches.Count == 0
            || string.IsNullOrWhiteSpace(_v4ImageCliUploadFile)
            || string.IsNullOrWhiteSpace(_v4ImageCliFinalResultFile)
            || !File.Exists(_v4ImageCliUploadFile))
        {
            return false;
        }

        var batches = _v4ImageCliBatches
            .Where(batch => !string.IsNullOrWhiteSpace(batch.ResultFile) && File.Exists(batch.ResultFile))
            .ToList();
        if (batches.Count == 0)
            return false;

        try
        {
            var finalDir = Path.GetDirectoryName(_v4ImageCliFinalResultFile);
            if (!string.IsNullOrWhiteSpace(finalDir))
            {
                Directory.CreateDirectory(finalDir);
                var finalSubDir = Path.Combine(finalDir, "final");
                Directory.CreateDirectory(finalSubDir);
            }

            File.Copy(_v4ImageCliUploadFile, _v4ImageCliFinalResultFile, overwrite: true);
            using var finalWb = new XLWorkbook(_v4ImageCliFinalResultFile);

            foreach (var batch in batches)
            {
                var batchCodes = BuildGsCodeMatchSet(batch.Codes);
                if (batchCodes.Count == 0)
                    continue;

                using var batchWb = new XLWorkbook(batch.ResultFile!);
                foreach (var sourceWs in batchWb.Worksheets)
                {
                    MergeV4BatchWorksheet(finalWb, sourceWs, batchCodes);
                }
            }

            finalWb.SaveAs(_v4ImageCliFinalResultFile);
            Log($"V4 병렬 배치 결과 병합 완료: {Path.GetFileName(_v4ImageCliFinalResultFile)}");

            MergeBatchCategoryMatchFiles(batches);

            // final 폴더에 최종 파일 복사
            var finalDir2 = Path.GetDirectoryName(_v4ImageCliFinalResultFile);
            if (!string.IsNullOrWhiteSpace(finalDir2))
            {
                var finalSubDir = Path.Combine(finalDir2, "final");
                Directory.CreateDirectory(finalSubDir);
                var finalCopy = Path.Combine(finalSubDir, Path.GetFileName(_v4ImageCliFinalResultFile));
                File.Copy(_v4ImageCliFinalResultFile, finalCopy, overwrite: true);
                var catFile = Path.Combine(finalDir2, "category_match_v4_cli.xlsx");
                if (File.Exists(catFile))
                    File.Copy(catFile, Path.Combine(finalSubDir, "category_match_v4_cli.xlsx"), overwrite: true);
                Log($"최종 결과를 final 폴더에 저장: {finalSubDir}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Log($"V4 병렬 배치 결과 병합 실패: {ex.Message}");
            return false;
        }
    }

    private void MergeBatchCategoryMatchFiles(List<V4ImageCliBatchInfo> batches)
    {
        try
        {
            var batchCatFiles = batches
                .Where(b => !string.IsNullOrWhiteSpace(b.ResultFile))
                .Select(b => Path.GetDirectoryName(b.ResultFile)!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(dir => Directory.GetFiles(dir, "*category_match*.xlsx"))
                .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (batchCatFiles.Count <= 1)
                return;

            var finalDir = Path.GetDirectoryName(_v4ImageCliFinalResultFile);
            if (string.IsNullOrWhiteSpace(finalDir))
                return;

            var mergedPath = Path.Combine(finalDir, "category_match_v4_cli.xlsx");

            using var first = new XLWorkbook(batchCatFiles[0]);
            var ws = first.Worksheet(1);

            for (var i = 1; i < batchCatFiles.Count; i++)
            {
                using var other = new XLWorkbook(batchCatFiles[i]);
                var otherWs = other.Worksheet(1);
                var otherLastRow = otherWs.LastRowUsed()?.RowNumber() ?? 1;
                var otherLastCol = otherWs.LastColumnUsed()?.ColumnNumber() ?? 1;
                var nextRow = (ws.LastRowUsed()?.RowNumber() ?? 1) + 1;

                for (var r = 2; r <= otherLastRow; r++)
                {
                    for (var c = 1; c <= otherLastCol; c++)
                        ws.Cell(nextRow, c).Value = otherWs.Cell(r, c).Value;
                    nextRow++;
                }
            }

            first.SaveAs(mergedPath);
            Log($"카테고리 매칭 파일 병합 완료: {Path.GetFileName(mergedPath)} ({ws.LastRowUsed()?.RowNumber() ?? 1 - 1}개 상품)");
        }
        catch (Exception ex)
        {
            Log($"카테고리 매칭 파일 병합 실패: {ex.Message}");
        }
    }

    private static HashSet<string> BuildGsCodeMatchSet(string codes)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in (codes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(raw.ToUpperInvariant());
            set.Add(NormalizeGsBaseCode(raw));
        }
        return set;
    }

    private static void MergeV4BatchWorksheet(XLWorkbook finalWb, IXLWorksheet sourceWs, HashSet<string> batchCodes)
    {
        var sourceHeaderRow = sourceWs.FirstRowUsed();
        if (sourceHeaderRow is null)
            return;

        var sourceLastCol = sourceWs.LastColumnUsed()?.ColumnNumber() ?? 0;
        var sourceLastRow = sourceWs.LastRowUsed()?.RowNumber() ?? 0;
        if (sourceLastCol <= 0 || sourceLastRow <= sourceHeaderRow.RowNumber())
            return;

        var sourceHeaders = ReadHeaderColumns(sourceHeaderRow);
        var sourceGsCol = FindGsLikeColumn(sourceHeaders);

        var matchingRows = new List<int>();
        for (var rowNo = sourceHeaderRow.RowNumber() + 1; rowNo <= sourceLastRow; rowNo++)
        {
            var rowGs = ExtractGsCodeFromWorksheetRow(sourceWs.Row(rowNo), sourceGsCol, sourceLastCol);
            if (MatchesBatchGsCode(rowGs, batchCodes))
                matchingRows.Add(rowNo);
        }
        if (matchingRows.Count == 0)
            return;

        var targetWs = finalWb.Worksheets.FirstOrDefault(ws =>
            string.Equals(ws.Name, sourceWs.Name, StringComparison.OrdinalIgnoreCase));
        if (targetWs is null)
        {
            targetWs = finalWb.Worksheets.Add(sourceWs.Name);
            CopyWorksheetRowValues(sourceHeaderRow, targetWs.Row(1), sourceLastCol);
        }
        else
        {
            CopyWorksheetRowValues(sourceHeaderRow, targetWs.Row(1), sourceLastCol);
        }

        var targetHeaderRow = targetWs.FirstRowUsed() ?? targetWs.Row(1);
        var targetHeaders = ReadHeaderColumns(targetHeaderRow);
        var targetGsCol = FindGsLikeColumn(targetHeaders);

        foreach (var sourceRowNo in matchingRows)
        {
            var sourceRow = sourceWs.Row(sourceRowNo);
            var sourceGs = ExtractGsCodeFromWorksheetRow(sourceRow, sourceGsCol, sourceLastCol);
            var targetRowNo = FindMatchingTargetRow(targetWs, targetGsCol, sourceGs, batchCodes);
            if (targetRowNo <= 0)
                targetRowNo = (targetWs.LastRowUsed()?.RowNumber() ?? 1) + 1;

            CopyWorksheetRowValues(sourceRow, targetWs.Row(targetRowNo), sourceLastCol);
        }
    }

    private static int FindGsLikeColumn(Dictionary<string, int> headers)
    {
        foreach (var key in new[] { "상품코드", "GS코드", "자체상품코드", "자체 상품코드", "product_code", "productcode" })
        {
            if (headers.TryGetValue(key, out var col))
                return col;
        }

        foreach (var pair in headers)
        {
            var normalized = Regex.Replace(pair.Key, @"\s+", "", RegexOptions.CultureInvariant);
            if (normalized.Contains("상품코드", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("GS코드", StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return -1;
    }

    private static string ExtractGsCodeFromWorksheetRow(IXLRow row, int preferredCol, int lastCol)
    {
        if (preferredCol > 0)
        {
            var fromPreferred = ExtractGsCodeFromText(row.Cell(preferredCol).GetString());
            if (!string.IsNullOrWhiteSpace(fromPreferred))
                return fromPreferred;
        }

        for (var col = 1; col <= lastCol; col++)
        {
            var found = ExtractGsCodeFromText(row.Cell(col).GetString());
            if (!string.IsNullOrWhiteSpace(found))
                return found;
        }

        return "";
    }

    private static string ExtractGsCodeFromText(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"GS\d{7}(?:-\d+)?", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : "";
    }

    private static bool MatchesBatchGsCode(string gsCode, HashSet<string> batchCodes)
    {
        if (string.IsNullOrWhiteSpace(gsCode))
            return false;

        return batchCodes.Contains(gsCode.ToUpperInvariant())
               || batchCodes.Contains(NormalizeGsBaseCode(gsCode));
    }

    private static int FindMatchingTargetRow(IXLWorksheet targetWs, int targetGsCol, string sourceGs, HashSet<string> batchCodes)
    {
        var lastRow = targetWs.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = targetWs.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow <= 1 || lastCol <= 0)
            return -1;

        for (var rowNo = 2; rowNo <= lastRow; rowNo++)
        {
            var targetGs = ExtractGsCodeFromWorksheetRow(targetWs.Row(rowNo), targetGsCol, lastCol);
            if (string.Equals(targetGs, sourceGs, StringComparison.OrdinalIgnoreCase))
                return rowNo;
            if (MatchesBatchGsCode(targetGs, batchCodes)
                && string.Equals(NormalizeGsBaseCode(targetGs), NormalizeGsBaseCode(sourceGs), StringComparison.OrdinalIgnoreCase))
                return rowNo;
        }

        return -1;
    }

    private static void CopyWorksheetRowValues(IXLRow sourceRow, IXLRow targetRow, int lastCol)
    {
        for (var col = 1; col <= lastCol; col++)
        {
            targetRow.Cell(col).Value = sourceRow.Cell(col).Value;
        }
    }

    private void TryAutoLoadLatestV4Result()
    {
        if (string.IsNullOrWhiteSpace(_testOutputRoot) || !Directory.Exists(_testOutputRoot))
            return;

        var candidates = Directory.GetFiles(_testOutputRoot, "*_llm_v5_cli.xlsx", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(_testOutputRoot, "*_llm_v4_cli.xlsx", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(_testOutputRoot, "*_llm_v4_local.xlsx", SearchOption.AllDirectories))
            .Where(File.Exists)
            .Where(path => !Path.GetFileName(path).Contains("_batch_", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
        if (candidates.Count == 0)
            return;

        var latest = !string.IsNullOrWhiteSpace(_v4ImageCliFinalResultFile) && File.Exists(_v4ImageCliFinalResultFile)
            ? _v4ImageCliFinalResultFile
            : candidates[0];
        NormalizeUploadWorkbookBeforeUse(latest);
        _testLlmResultFile = latest;
        _testLlmResultFiles = new List<string> { latest };
        _lastOutputFile = latest;
        TestLlmResultFileText.Text = $"V5 결과: {Path.GetFileName(latest)}";

        TestCafe24UploadButton.IsEnabled = true;
        TestCafe24CreateButton.IsEnabled = true;
        TestCoupangUploadButton.IsEnabled = true;
        TestNaverUploadButton.IsEnabled = true;

        LoadBasicCafe24ProductList(latest);
        TryLoadWorkspaceEditor(latest);
        AutoSaveWorkspacePackage("V5 결과 자동 선택");
        Log($"V5 LLM 결과 자동 선택: {latest}");
    }

    private string ResolveLlmResultInitialDirectory()
    {
        var candidates = new List<string?>();

        void AddOutputRootCandidates(string? root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return;

            candidates.Add(Path.Combine(root, "llm_result_v5_cli"));
            candidates.Add(Path.Combine(root, "llm_result_v4_cli"));
            candidates.Add(Path.Combine(root, "llm_result_v4_local"));
            candidates.AddRange(GetPreferredLlmDirs(root, GetSelectedKeywordVersion()));
            candidates.Add(root);
        }

        if (_testLlmResultFiles.Count > 0)
            candidates.Add(Path.GetDirectoryName(_testLlmResultFiles.FirstOrDefault(File.Exists) ?? ""));
        if (!string.IsNullOrWhiteSpace(_lastOutputFile) && File.Exists(_lastOutputFile))
            candidates.Add(Path.GetDirectoryName(_lastOutputFile));

        AddOutputRootCandidates(_testOutputRoot);
        AddOutputRootCandidates(_lastOutputRoot);

        var exportRoot = GetDefaultExportRoot();
        if (Directory.Exists(exportRoot))
        {
            foreach (var dir in Directory.GetDirectories(exportRoot).OrderByDescending(Directory.GetLastWriteTimeUtc).Take(12))
                AddOutputRootCandidates(dir);
            candidates.Add(exportRoot);
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        candidates.Add(Path.Combine(desktop, "llm_result_v5_cli"));
        candidates.Add(Path.Combine(desktop, "llm_result_v4_cli"));
        candidates.Add(desktop);

        return candidates
                   .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .FirstOrDefault() ?? desktop;
    }

    private void TestLoadLlmResult_Click(object sender, RoutedEventArgs e)
    {
        var startDir = ResolveLlmResultInitialDirectory();

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Excel|*.xlsx|모든 파일|*.*",
            Title = "LLM 결과 엑셀 선택 (여러 파일 선택 가능)",
            InitialDirectory = startDir,
            Multiselect = true,
        };

        if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
        {
            _testLlmResultFiles = dlg.FileNames.OrderBy(f => f).ToList();
            _testLlmResultFile = _testLlmResultFiles[0]; // 호환용

            // LLM 결과 파일에서 _testOutputRoot 자동 추론
            // 경로 예: .../exports/20260331_xxx/llm_chunks/llm_result/chunk_01_llm.xlsx
            var firstDir = Path.GetDirectoryName(_testLlmResultFiles[0])!;
            if (firstDir.Contains("llm_chunks"))
            {
                // llm_chunks 상위 = export root
                var chunksDir = firstDir;
                while (!string.IsNullOrEmpty(chunksDir) && Path.GetFileName(chunksDir) != "llm_chunks")
                    chunksDir = Path.GetDirectoryName(chunksDir);
                if (!string.IsNullOrEmpty(chunksDir))
                    _testOutputRoot = Path.GetDirectoryName(chunksDir);
            }
            else if (firstDir.Contains("llm_result"))
            {
                _testOutputRoot = Path.GetDirectoryName(firstDir);
            }
            if (string.IsNullOrEmpty(_testOutputRoot))
                _testOutputRoot = firstDir;

            if (_testLlmResultFiles.Count == 1)
                TestLlmResultFileText.Text = $"LLM 결과: {Path.GetFileName(_testLlmResultFiles[0])}";
            else
                TestLlmResultFileText.Text = $"LLM 결과: {_testLlmResultFiles.Count}개 파일 선택됨";

            TestCafe24UploadButton.IsEnabled = true;
            TestCafe24CreateButton.IsEnabled = true;
            TestCoupangUploadButton.IsEnabled = true;
            TestNaverUploadButton.IsEnabled = true;

            foreach (var f in _testLlmResultFiles)
            {
                NormalizeUploadWorkbookBeforeUse(f);
                Log($"LLM 결과 파일: {Path.GetFileName(f)}");
                if (!HasBMarketSheet(f))
                    Log($"  ⚠ B마켓 시트 없음: {Path.GetFileName(f)} — 준비몰 신규등록은 이 파일에서 스킵됩니다.");
            }

            LoadBasicCafe24ProductList(_testLlmResultFiles[0]);
            TryLoadWorkspaceEditor(_testLlmResultFiles[0]);
            LoadKeywordEditorFromExcel(_testLlmResultFiles[0]);
            AutoSaveWorkspacePackage("V5 결과 수동 선택");
            Log($"신규등록 목록 자동 로드: {_basicCafe24Items.Count}개 / 키워드 편집기 {_keywordEditorEntries.Count}개");
        }
    }

    /// <summary>엑셀 파일에 'B마켓' 시트가 있는지 확인</summary>
    private static bool HasBMarketSheet(string excelPath)
    {
        try
        {
            using var wb = WorkbookFileLoader.OpenReadOnly(excelPath);
            return wb.Worksheets.Any(ws =>
                string.Equals(ws.Name.Trim(), "B마켓", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private void NormalizeUploadWorkbookBeforeUse(string? excelPath)
    {
        if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
            return;

        try
        {
            var fixedCount = NormalizeUploadWorkbookSalePrices(excelPath);
            if (fixedCount > 0)
                Log($"판매가 0원 자동보정: {Path.GetFileName(excelPath)} / {fixedCount}개 행을 상품가로 저장");
        }
        catch (IOException ex)
        {
            Log($"판매가 자동보정 실패: {Path.GetFileName(excelPath)} 파일이 열려 있으면 닫고 다시 시도하세요. ({ex.Message})");
        }
        catch (Exception ex)
        {
            Log($"판매가 자동보정 실패: {Path.GetFileName(excelPath)} ({ex.Message})");
        }
    }

    private static int NormalizeUploadWorkbookSalePrices(string excelPath)
    {
        using var wb = new XLWorkbook(excelPath);
        var fixedCount = 0;

        foreach (var ws in wb.Worksheets)
        {
            var headerRow = ws.FirstRowUsed();
            if (headerRow == null)
                continue;

            var headers = ReadHeaderColumns(headerRow);
            var sellingCol = FindCol(headers, new[] { "판매가", "selling_price" });
            var productCol = FindCol(headers, new[] { "상품가", "product_price" });
            if (sellingCol <= 0 || productCol <= 0)
                continue;

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            for (var row = headerRow.RowNumber() + 1; row <= lastRow; row++)
            {
                var sellingCell = ws.Cell(row, sellingCol);
                var productCell = ws.Cell(row, productCol);
                var sellingPrice = GetDecimal(sellingCell);
                var productPrice = GetDecimal(productCell);
                if (sellingPrice > 0 || productPrice <= 0)
                    continue;

                var fixedPrice = CeilPriceToTen(productPrice);
                sellingCell.Value = Convert.ToDouble(fixedPrice, CultureInfo.InvariantCulture);
                sellingCell.Style.NumberFormat.Format = "0";
                fixedCount++;
            }
        }

        if (fixedCount > 0)
            wb.Save();

        return fixedCount;
    }

    private static decimal CeilPriceToTen(decimal value)
    {
        if (value <= 0m)
            return 0m;
        return Math.Ceiling(value / 10m) * 10m;
    }

    private static Dictionary<string, int> ReadHeaderColumns(IXLRow headerRow)
    {
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastCol = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
        for (var col = 1; col <= lastCol; col++)
        {
            var header = headerRow.Cell(col).GetString().Trim();
            if (!string.IsNullOrWhiteSpace(header) && !headers.ContainsKey(header))
                headers[header] = col;
        }
        return headers;
    }

    private void TestCafe24Create_Click(object sender, RoutedEventArgs e)
    {
        var files = _testLlmResultFiles.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            MessageBox.Show("LLM 결과 파일을 먼저 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        NormalizeUploadWorkbookBeforeUse(files[0]);
        LoadBasicCafe24ProductList(files[0]);
        Log($"상품 목록 {_basicCafe24Items.Count}개 로드 — 항목 선택 후 '신규등록 실행' 버튼을 클릭하세요.");
    }

    private void TestCafe24Upload_Click(object sender, RoutedEventArgs e)
    {
        var files = _testLlmResultFiles.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            MessageBox.Show("LLM 결과 파일을 먼저 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 첫 번째 파일로 기존 업로드 로직 호출 (단일 파일용 호환)
        _lastOutputRoot = _testOutputRoot ?? Path.GetDirectoryName(files[0])!;
        _lastOutputFile = files[0];

        // 기존 Cafe24 업로드 로직 재사용
        Cafe24Upload_Click(sender, e);
    }

    private async void TestCoupangUpload_Click(object sender, RoutedEventArgs e)
    {
        // _testOutputRoot에서 업로드용 엑셀 자동 탐색
        var sourcePath = FindUploadExcel();
        if (sourcePath == null)
        {
            MessageBox.Show("업로드용 엑셀 파일을 찾을 수 없습니다.\nCafe24 업로드를 먼저 실행하세요.", "알림",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dryRun = MarketDryRun.IsChecked == true;
        if (!dryRun)
        {
            var confirm = MessageBox.Show(
                $"쿠팡에 상품을 실제 등록합니다.\n\n파일: {Path.GetFileName(sourcePath)}\n\n계속하시겠습니까?",
                "쿠팡 실제 등록 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
        }

        TestCoupangUploadButton.IsEnabled = false;
        _cts = new CancellationTokenSource();

        try
        {
            StatusText.Text = dryRun ? "쿠팡 DRY RUN 중..." : "쿠팡 등록 중...";
            ProgressBar.IsIndeterminate = true;
            Log($"[쿠팡] 업로드 시작: {Path.GetFileName(sourcePath)} (DRY RUN: {dryRun})");

            var options = new CoupangUploadOptions
            {
                RowStart = ParseInt(MarketRowStart, 0),
                RowEnd = ParseInt(MarketRowEnd, 0),
                DryRun = dryRun,
                Cafe24TokenPath = GetHomeCafe24TokenPath(),
            };

            var service = new CoupangUploadService();
            var progress = new Progress<string>(msg => Log(msg));
            var result = await service.UploadAsync(sourcePath, options, progress, _cts.Token);

            var gridItems = result.Items.Select(item => new MarketResultRow
            {
                Market = "쿠팡",
                Row = item.Row,
                Name = item.Name,
                Status = item.Status,
                Info = !string.IsNullOrEmpty(item.SellerProductId) ? item.SellerProductId : item.Category,
                Error = item.Error,
            }).ToList();
            MarketUploadResultGrid.ItemsSource = gridItems;
            MarketUploadSummary.Text = $"[쿠팡] 성공 {result.SuccessCount} / 실패 {result.FailCount} / 전체 {result.TotalCount}";
            StatusText.Text = $"쿠팡 {(dryRun ? "DRY RUN" : "등록")} 완료";
            Log($"[쿠팡] 완료: 성공 {result.SuccessCount} / 실패 {result.FailCount} / 전체 {result.TotalCount}");
            foreach (var item in result.Items.Where(i => !string.IsNullOrEmpty(i.Error)))
                Log($"  [쿠팡 오류] 행{item.Row} {item.Name} → {item.Error}");
        }
        catch (OperationCanceledException) { Log("[쿠팡] 취소됨"); StatusText.Text = "취소됨"; }
        catch (Exception ex)
        {
            Log($"[쿠팡] 오류: {ex.Message}");
            StatusText.Text = "쿠팡 업로드 오류";
            MessageBox.Show(ex.Message, "쿠팡 업로드 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestCoupangUploadButton.IsEnabled = true;
            ProgressBar.IsIndeterminate = false;
        }
    }

    private async void TestNaverUpload_Click(object sender, RoutedEventArgs e)
    {
        var sourcePath = FindUploadExcel();
        if (sourcePath == null)
        {
            MessageBox.Show("업로드용 엑셀 파일을 찾을 수 없습니다.\nCafe24 업로드를 먼저 실행하세요.", "알림",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dryRun = MarketDryRun.IsChecked == true;
        if (!dryRun)
        {
            var confirm = MessageBox.Show(
                $"네이버 스마트스토어에 상품을 실제 등록합니다.\n\n파일: {Path.GetFileName(sourcePath)}\n\n계속하시겠습니까?",
                "네이버 실제 등록 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
        }

        TestNaverUploadButton.IsEnabled = false;
        _cts = new CancellationTokenSource();

        try
        {
            StatusText.Text = dryRun ? "네이버 DRY RUN 중..." : "네이버 등록 중...";
            ProgressBar.IsIndeterminate = true;
            Log($"[네이버] 업로드 시작: {Path.GetFileName(sourcePath)} (DRY RUN: {dryRun})");

            var options = new NaverUploadOptions
            {
                RowStart = ParseInt(MarketRowStart, 0),
                RowEnd = ParseInt(MarketRowEnd, 0),
                DryRun = dryRun,
                Cafe24TokenPath = GetHomeCafe24TokenPath(),
            };

            var service = new NaverUploadService();
            var progress = new Progress<string>(msg => Log(msg));
            var result = await service.UploadAsync(sourcePath, options, progress, _cts.Token);

            var gridItems = result.Items.Select(item => new MarketResultRow
            {
                Market = "네이버",
                Row = item.Row,
                Name = item.Name,
                Status = item.Status,
                Info = item.ProductId,
                Error = item.Error,
            }).ToList();
            MarketUploadResultGrid.ItemsSource = gridItems;
            MarketUploadSummary.Text = $"[네이버] 성공 {result.SuccessCount} / 실패 {result.FailCount} / 전체 {result.TotalCount}";
            StatusText.Text = $"네이버 {(dryRun ? "DRY RUN" : "등록")} 완료";
            Log($"[네이버] 완료: 성공 {result.SuccessCount} / 실패 {result.FailCount} / 전체 {result.TotalCount}");
            foreach (var item in result.Items.Where(i => !string.IsNullOrEmpty(i.Error)))
                Log($"  [네이버 오류] 행{item.Row} {item.Name} → {item.Error}");
        }
        catch (OperationCanceledException) { Log("[네이버] 취소됨"); StatusText.Text = "취소됨"; }
        catch (Exception ex)
        {
            Log($"[네이버] 오류: {ex.Message}");
            StatusText.Text = "네이버 업로드 오류";
            MessageBox.Show(ex.Message, "네이버 업로드 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestNaverUploadButton.IsEnabled = true;
            ProgressBar.IsIndeterminate = false;
        }
    }

    private string? GetHomeCafe24TokenPath()
    {
        var selected = string.IsNullOrWhiteSpace(SettingsTokenPath.Text) ? null : SettingsTokenPath.Text.Trim();
        if (IsCafe24TokenForMall(selected, "rkghrud1"))
            return selected;

        var homeTokenPath = DesktopKeyStore.GetPath("cafe24_token_rkghrud1.json");
        if (File.Exists(homeTokenPath))
            return homeTokenPath;

        return selected;
    }

    private static bool IsCafe24TokenForMall(string? tokenPath, string mallId)
    {
        if (string.IsNullOrWhiteSpace(tokenPath) || !File.Exists(tokenPath))
            return false;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(tokenPath));
            var root = document.RootElement;
            foreach (var key in new[] { "MallId", "MALL_ID", "mall_id" })
            {
                if (root.TryGetProperty(key, out var value)
                    && string.Equals(value.GetString(), mallId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private async Task RunDirectHomeMarketUploadsAsync(
        string sourcePath,
        IReadOnlySet<string>? selectedGs,
        bool runNaver,
        bool runLotteOn,
        bool runCoupang,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        NormalizeUploadWorkbookBeforeUse(sourcePath);
        var gridItems = new List<MarketResultRow>();
        var cafe24TokenPath = GetHomeCafe24TokenPath();

        if (runNaver)
        {
            try
            {
                StatusText.Text = "네이버 직접등록 중...";
                Log("── [네이버] 홈런마켓 직접등록 시작 ──");
                var service = new NaverUploadService();
                var result = await service.UploadAsync(sourcePath, new NaverUploadOptions
                {
                    DryRun = false,
                    AllowedGsCodes = selectedGs,
                    Cafe24TokenPath = cafe24TokenPath,
                }, progress, cancellationToken);

                gridItems.AddRange(result.Items.Select(item => new MarketResultRow
                {
                    Market = "네이버",
                    Row = item.Row,
                    Name = item.Name,
                    Status = item.Status,
                    Info = item.ProductId,
                    Error = item.Error,
                }));
                foreach (var item in result.Items)
                    RecordUploadToDb("", item.Name ?? "", "네이버", item.Status ?? "OK", item.ProductId, item.Error);
                Log($"[네이버] 직접등록 완료: 성공 {result.SuccessCount} / 실패 {result.FailCount} / 전체 {result.TotalCount}");
                Log($"[네이버] 로그 폴더: {result.LogDirectory}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log($"[네이버] 직접등록 오류: {ex.Message}");
                gridItems.Add(new MarketResultRow { Market = "네이버", Status = "ERROR", Error = ex.Message });
            }
        }

        if (runLotteOn)
        {
            try
            {
                StatusText.Text = "롯데ON 직접등록 중...";
                Log("── [롯데ON] 홈런마켓 직접등록 시작 ──");
                var service = new LotteOnUploadService();
                var result = await service.UploadAsync(sourcePath, new LotteOnUploadOptions
                {
                    DryRun = false,
                    AllowedGsCodes = selectedGs,
                    Cafe24TokenPath = cafe24TokenPath,
                }, progress, cancellationToken);

                gridItems.AddRange(result.Items.Select(item => new MarketResultRow
                {
                    Market = "롯데ON",
                    Row = item.Row,
                    Name = item.Name,
                    Status = item.Status,
                    Info = item.SpdNo,
                    Error = item.Error,
                }));
                foreach (var item in result.Items)
                    RecordUploadToDb("", item.Name ?? "", "롯데ON", item.Status ?? "OK", item.SpdNo, item.Error);
                Log($"[롯데ON] 직접등록 완료: 성공 {result.SuccessCount} / 실패 {result.FailCount} / 전체 {result.TotalCount}");
                Log($"[롯데ON] 로그 폴더: {result.LogDirectory}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log($"[롯데ON] 직접등록 오류: {ex.Message}");
                gridItems.Add(new MarketResultRow { Market = "롯데ON", Status = "ERROR", Error = ex.Message });
            }
        }

        if (runCoupang)
        {
            try
            {
                StatusText.Text = "쿠팡 직접등록 중...";
                Log("── [쿠팡] 홈런마켓 직접등록 시작 ──");
                var service = new CoupangUploadService();
                var result = await service.UploadAsync(sourcePath, new CoupangUploadOptions
                {
                    DryRun = false,
                    AllowedGsCodes = selectedGs,
                    Cafe24TokenPath = cafe24TokenPath,
                }, progress, cancellationToken);

                gridItems.AddRange(result.Items.Select(item => new MarketResultRow
                {
                    Market = "쿠팡",
                    Row = item.Row,
                    Name = item.Name,
                    Status = item.Status,
                    Info = !string.IsNullOrEmpty(item.SellerProductId) ? item.SellerProductId : item.Category,
                    Error = item.Error,
                }));
                foreach (var item in result.Items)
                    RecordUploadToDb("", item.Name ?? "", "쿠팡", item.Status ?? "OK", item.SellerProductId, item.Error);
                Log($"[쿠팡] 직접등록 완료: 성공 {result.SuccessCount} / 실패 {result.FailCount} / 전체 {result.TotalCount}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log($"[쿠팡] 직접등록 오류: {ex.Message}");
                gridItems.Add(new MarketResultRow { Market = "쿠팡", Status = "ERROR", Error = ex.Message });
            }
        }

        if (gridItems.Count > 0)
        {
            MarketUploadResultGrid.ItemsSource = gridItems;
            var ok = gridItems.Count(i => i.Status is "OK" or "SUCCESS" or "DRY_RUN" or "DRY_RUN_OK" or "SKIP_DUP");
            var fail = gridItems.Count - ok;
            MarketUploadSummary.Text = $"[직접등록] 성공 {ok} / 실패 {fail} / 전체 {gridItems.Count}";
            Log(MarketUploadSummary.Text);
        }
    }

    private string? FindUploadExcel()
    {
        // 1) LLM 결과 파일 우선 사용 (키워드/검색어/태그가 적용된 파일)
        if (_testLlmResultFiles.Count > 0)
        {
            var first = _testLlmResultFiles.FirstOrDefault(File.Exists);
            if (first != null)
            {
                Log($"LLM 결과 파일 사용: {Path.GetFileName(first)}");
                NormalizeUploadWorkbookBeforeUse(first);
                return first;
            }
        }
        // 2) _testOutputRoot에서 업로드용 엑셀 탐색
        if (!string.IsNullOrEmpty(_testOutputRoot) && Directory.Exists(_testOutputRoot))
        {
            var found = FindLatestFile(_testOutputRoot, "업로드용_*.xlsx");
            if (found != null)
            {
                NormalizeUploadWorkbookBeforeUse(found);
                return found;
            }
        }
        // 3) _lastOutputRoot에서 업로드용 엑셀 탐색
        if (!string.IsNullOrEmpty(_lastOutputRoot) && Directory.Exists(_lastOutputRoot))
        {
            var found = FindLatestFile(_lastOutputRoot, "업로드용_*.xlsx");
            if (found != null)
            {
                NormalizeUploadWorkbookBeforeUse(found);
                return found;
            }
        }
        return null;
    }

    private async Task TryUploadLatestMarketPlusCategoryMapAsync(
        string uploadFile,
        IEnumerable<string> llmResultFiles,
        CancellationToken cancellationToken)
    {
        try
        {
            var candidateRoots = new List<string?>
            {
                Path.GetDirectoryName(uploadFile),
                _testOutputRoot,
                _lastOutputRoot
            };

            var priorityFiles = new List<string?> { uploadFile };
            foreach (var file in llmResultFiles)
            {
                priorityFiles.Add(file);

                var fileDir = Path.GetDirectoryName(file);
                candidateRoots.Add(fileDir);
                if (!string.IsNullOrWhiteSpace(fileDir))
                    candidateRoots.Add(Path.GetDirectoryName(fileDir));
            }

            var uploader = new MarketPlusCategoryMapAutoUploader(
                _v3Root,
                new Progress<string>(msg => Log(msg)));

            await uploader.UploadLatestAsync(candidateRoots, priorityFiles, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log($"[카테고리맵] 자동 업로드 실패: {ex.Message}");
        }
    }

    private sealed class MarketResultRow
    {
        public string Market { get; set; } = "";
        public int Row { get; set; }
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";
        public string Info { get; set; } = "";
        public string Error { get; set; } = "";
    }

    #endregion

    #region ═══ STEP 2: Cafe24 업로드 ═══

    private Cafe24UploadOptions BuildUploadOptions()
    {
        return new Cafe24UploadOptions
        {
            TokenFilePath = GetHomeCafe24TokenPath(),
            DateTag = Cafe24DateTag.Text.Trim(),
            ExportDir = _lastOutputRoot ?? "",
            MainIndex = ParseInt(Cafe24MainIdx, 2),
            AddStart = ParseInt(Cafe24AddStart, 3),
            AddMax = ParseInt(Cafe24AddMax, 10),
            RetryCount = ParseInt(Cafe24RetryCount, 1),
            RetryDelaySeconds = ParseDouble(Cafe24RetryDelay, 1.0),
            MatchMode = (Cafe24MatchMode.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "PREFIX",
            MatchPrefix = ParseInt(Cafe24MatchPrefix, 20),
        };
    }

    private async void Cafe24Upload_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastOutputRoot) || !Directory.Exists(_lastOutputRoot))
        {
            MessageBox.Show("먼저 STEP 1을 실행하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryGetSelectedCafe24Markets(out var runHomeMarket, out var runReadyMarket, out var marketLabel))
        {
            return;
        }

        var uploadFile = !string.IsNullOrEmpty(_lastOutputFile) && File.Exists(_lastOutputFile)
            ? _lastOutputFile
            : FindLatestFile(_lastOutputRoot, "업로드용_*.xlsx");
        if (uploadFile == null)
        {
            MessageBox.Show("업로드용 엑셀 파일을 찾을 수 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Cafe24에 이미지 업로드 + 옵션가격을 반영합니다.\n\n" +
            $"대상 몰: {marketLabel}\n" +
            $"업로드 파일: {Path.GetFileName(uploadFile)}\n" +
            $"결과 폴더: {_lastOutputRoot}\n\n계속하시겠습니까?",
            "Cafe24 업로드 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        SavePriceReviewJson();

        Cafe24UploadButton.IsEnabled = false;
        _cts = new CancellationTokenSource();

        try
        {
            await AutoRefreshAllCafe24TokensAsync();
            StatusText.Text = "Cafe24 업로드 중...";
            ProgressBar.IsIndeterminate = true;

            var options = BuildUploadOptions();
            var priceDataPath = Path.Combine(_lastOutputRoot, "cafe24_price_upload_data.json");
            if (File.Exists(priceDataPath))
                options.PriceDataPath = priceDataPath;

            var uploadService = new Cafe24UploadService(_v3Root, _legacyRoot);
            var progress = new Progress<string>(msg => Log(msg));
            var totalCount = 0;
            var totalSuccess = 0;
            var totalError = 0;
            var totalSkipped = 0;
            string? lastLogPath = null;

            void Accumulate(Cafe24UploadResult result)
            {
                totalCount += result.TotalCount;
                totalSuccess += result.SuccessCount;
                totalError += result.ErrorCount;
                totalSkipped += result.SkippedCount;
                if (!string.IsNullOrWhiteSpace(result.LogPath))
                {
                    lastLogPath = result.LogPath;
                }
            }

            async Task RunReadyMarketUploadAsync()
            {
                StatusText.Text = "준비몰 Cafe24 업로드 중...";
                var resultB = await uploadService.UploadBMarketAsync(uploadFile, _lastOutputRoot, options, progress, _cts.Token, _bMarketTokenPath);
                Accumulate(resultB);
                if (resultB.TotalCount > 0)
                {
                    Log($"Cafe24 준비몰 업로드 완료: 성공 {resultB.SuccessCount} / 오류 {resultB.ErrorCount} / 스킵 {resultB.SkippedCount}");
                }
                else
                {
                    Log("Cafe24 준비몰 업로드 스킵: B마켓 시트 또는 대상 상품이 없습니다.");
                }
            }

            if (runHomeMarket)
            {
                StatusText.Text = "홈런마켓 Cafe24 업로드 중...";
                var result = await uploadService.UploadAsync(uploadFile, _lastOutputRoot, options, progress, _cts.Token);
                Accumulate(result);
                Log($"Cafe24 홈런마켓 업로드 완료: 성공 {result.SuccessCount} / 오류 {result.ErrorCount} / 스킵 {result.SkippedCount}");
            }

            if (runReadyMarket)
            {
                if (runHomeMarket)
                {
                    try
                    {
                        await RunReadyMarketUploadAsync();
                    }
                    catch (Cafe24ReauthenticationRequiredException exB)
                    {
                        Log($"준비몰 Cafe24 토큰 오류: {exB.Message}");
                        MessageBox.Show("준비몰 토큰이 만료됐습니다. 설정 탭에서 토큰 파일을 교체해 주세요.", "준비몰 토큰 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch (Exception exB)
                    {
                        Log($"준비몰 업로드 오류 (홈런마켓은 성공): {exB.Message}");
                    }
                }
                else
                {
                    await RunReadyMarketUploadAsync();
                }
            }

            _lastUploadLogPath = lastLogPath;
            StatusText.Text = $"업로드 완료 ({marketLabel}, 성공: {totalSuccess})";
            UploadSummaryText.Text = $"{marketLabel} | 총 {totalCount} | 성공 {totalSuccess} | 오류 {totalError} | 스킵 {totalSkipped}";
            OpenUploadLogButton.IsEnabled = !string.IsNullOrEmpty(lastLogPath);
            if (!string.IsNullOrEmpty(lastLogPath))
            {
                LoadUploadLog(lastLogPath);
            }
            RefreshWorkspaceUploadStatuses();
            AutoSaveWorkspacePackage("Cafe24 업로드 이력 갱신");
        }
        catch (OperationCanceledException) { Log("업로드 취소됨"); StatusText.Text = "취소됨"; }
        catch (Cafe24ReauthenticationRequiredException ex)
        {
            Log($"Cafe24 토큰 오류: {ex.Message}");
            StatusText.Text = "토큰 오류";
            MessageBox.Show("Cafe24 토큰이 만료됐습니다. 설정 탭에서 토큰 파일을 교체해 주세요.", "Cafe24 토큰 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Log($"Cafe24 오류: {ex.Message}");
            StatusText.Text = "업로드 오류";
            MessageBox.Show(ex.Message, "Cafe24 업로드 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cafe24UploadButton.IsEnabled = true;
            ProgressBar.IsIndeterminate = false;
        }
    }

    private async void Cafe24Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastOutputRoot) || !Directory.Exists(_lastOutputRoot))
        {
            MessageBox.Show("먼저 STEP 1을 실행하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryGetSelectedCafe24Markets(out var runHomeMarket, out var runReadyMarket, out var marketLabel))
        {
            return;
        }

        var uploadFile = !string.IsNullOrEmpty(_lastOutputFile) && File.Exists(_lastOutputFile)
            ? _lastOutputFile
            : FindLatestFile(_lastOutputRoot, "업로드용_*.xlsx");
        if (uploadFile == null)
        {
            MessageBox.Show("업로드용 엑셀을 찾을 수 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IReadOnlySet<string>? selectedGs = _cafe24Items.Count > 0
            ? new HashSet<string>(_cafe24Items.Where(i => i.IsChecked).Select(i => i.GsCode), StringComparer.OrdinalIgnoreCase)
            : null;
        var autoNaverLotteOn = AutoNaverLotteOnCheckBox.IsChecked == true;
        var runDirectNaver = runHomeMarket && (DirectNaverUploadCheckBox.IsChecked == true || autoNaverLotteOn);
        var runDirectLotteOn = runHomeMarket && (DirectLotteOnUploadCheckBox.IsChecked == true || autoNaverLotteOn);
        var runDirectCoupang = runHomeMarket && (DirectCoupangUploadCheckBox.IsChecked == true || autoNaverLotteOn);
        var directMarketLabel = string.Join(", ", new[]
        {
            runDirectNaver ? "네이버" : "",
            runDirectLotteOn ? "롯데ON" : "",
            runDirectCoupang ? "쿠팡" : "",
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        // 준비몰 선택 시 B마켓 시트 존재 여부 미리 확인
        var bMarketNote = "";
        if (runReadyMarket && !HasBMarketSheet(uploadFile))
            bMarketNote = "\n\n⚠ 업로드 파일에 B마켓 시트가 없습니다.\n   준비몰 신규등록은 스킵됩니다.";
        var directMarketNote = !string.IsNullOrWhiteSpace(directMarketLabel)
            ? $"\n홈런마켓 직접등록: {directMarketLabel}"
            : "\n홈런마켓 직접등록: 안 함";

        var confirm = MessageBox.Show(
            $"Cafe24에 신규 상품을 등록합니다.\n\n" +
            $"대상 몰: {marketLabel}\n" +
            $"업로드 파일: {Path.GetFileName(uploadFile)}" +
            $"{directMarketNote}{bMarketNote}\n\n계속하시겠습니까?",
            "신규상품 등록 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        // 네이버(홈런마켓) 중복 확인
        if (runHomeMarket)
        {
            StatusText.Text = "네이버 중복 확인 중...";
            var duplicateInfo = await CheckNaverDuplicatesAsync(uploadFile);
            if (duplicateInfo.Count > 0)
            {
                var dupLines = duplicateInfo
                    .Select(d => $"  • {d.GsCode}  {d.ProductName}")
                    .ToList();
                var msg = $"다음 {duplicateInfo.Count}개 상품이 네이버(홈런마켓)에 이미 등록되어 있습니다:\n\n" +
                          string.Join("\n", dupLines) +
                          "\n\n이미 등록된 상품도 포함하여 계속 진행하시겠습니까?";
                var dupResult = MessageBox.Show(msg, "네이버 중복 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (dupResult != MessageBoxResult.Yes) return;
            }
        }

        Cafe24CreateButton.IsEnabled = false;
        _cts = new CancellationTokenSource();

        try
        {
            await AutoRefreshAllCafe24TokensAsync();
            StatusText.Text = "신규상품 등록 중...";
            ProgressBar.IsIndeterminate = true;

            var options = BuildUploadOptions();
            var createService = new Cafe24CreateProductService(_v3Root, _legacyRoot);
            var progress = new Progress<string>(msg => Log(msg));
            var totalCreated = 0;
            var totalError = 0;
            var totalSkipped = 0;

            async Task RunReadyMarketCreateAsync()
            {
                StatusText.Text = "준비몰 신규상품 등록 중...";
                Log("── [준비몰] 신규등록 시작 ──");
                var resultB = await createService.CreateBMarketAsync(uploadFile, _lastOutputRoot, progress, _cts.Token, _bMarketTokenPath, selectedGs);
                totalCreated += resultB.CreatedCount;
                totalError += resultB.ErrorCount;
                totalSkipped += resultB.SkippedCount;
                if (resultB.TotalCount > 0)
                {
                    foreach (var item in _cafe24Items.Where(i => i.IsChecked))
                    {
                        _uploadHistory.Mark(item.GsCode, "readymarket");
                        item.ReadyMarketStatus = UploadProductItem.FormatDate(DateTime.Now);
                        RecordUploadToDb(item.GsCode, item.ProductName, "준비몰", "OK");
                    }
                    Log($"[준비몰] 신규등록 완료: 생성 {resultB.CreatedCount} / 오류 {resultB.ErrorCount} / 스킵 {resultB.SkippedCount}");
                }
                else
                {
                    Log("[준비몰] 신규등록 스킵: B마켓 시트에 등록 대상이 없습니다.");
                }
            }

            if (runHomeMarket)
            {
                StatusText.Text = "홈런마켓 신규상품 등록 중...";
                var aTokenPath = GetHomeCafe24TokenPath();
                var result = await createService.CreateAsync(uploadFile, _lastOutputRoot, progress, _cts.Token, tokenPath: aTokenPath, allowedGsCodes: selectedGs);
                totalCreated += result.CreatedCount;
                totalError += result.ErrorCount;
                totalSkipped += result.SkippedCount;
                // 업로드 이력 기록
                foreach (var item in _cafe24Items.Where(i => i.IsChecked))
                {
                    _uploadHistory.Mark(item.GsCode, "homemarket");
                    item.HomeMarketStatus = UploadProductItem.FormatDate(DateTime.Now);
                    RecordUploadToDb(item.GsCode, item.ProductName, "홈런마켓", "OK");
                }
                Log($"[홈런마켓] 신규등록 완료: 생성 {result.CreatedCount} / 오류 {result.ErrorCount} / 스킵 {result.SkippedCount}");

                if (runDirectNaver || runDirectLotteOn || runDirectCoupang)
                {
                    var directUploadFile = FindDirectMarketWorkbook(uploadFile);
                    if (!string.Equals(directUploadFile, uploadFile, StringComparison.OrdinalIgnoreCase))
                        Log($"[직접등록] 최종 V4 엑셀 사용: {Path.GetFileName(directUploadFile)}");
                    await RunDirectHomeMarketUploadsAsync(directUploadFile, selectedGs, runDirectNaver, runDirectLotteOn, runDirectCoupang, progress, _cts.Token);
                }
            }

            if (runReadyMarket)
            {
                if (runHomeMarket)
                {
                    try
                    {
                        await RunReadyMarketCreateAsync();
                    }
                    catch (Cafe24ReauthenticationRequiredException exB)
                    {
                        Log($"[준비몰] 토큰 오류: {exB.Message}");
                        MessageBox.Show("준비몰 토큰이 만료됐습니다. 설정 탭에서 토큰 파일을 교체해 주세요.", "준비몰 토큰 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch (Exception exB)
                    {
                        Log($"[준비몰] 신규등록 오류: {exB.Message}");
                    }
                }
                else
                {
                    await RunReadyMarketCreateAsync();
                }
            }

            StatusText.Text = $"등록 완료 ({marketLabel}, 생성: {totalCreated})";
            RefreshWorkspaceUploadStatuses();
            AutoSaveWorkspacePackage("Cafe24 신규등록 이력 갱신");
        }
        catch (OperationCanceledException) { Log("등록 취소됨"); StatusText.Text = "취소됨"; }
        catch (Cafe24ReauthenticationRequiredException ex)
        {
            Log($"Cafe24 토큰 오류: {ex.Message}");
            StatusText.Text = "토큰 오류";
            MessageBox.Show("Cafe24 토큰이 만료됐습니다. 설정 탭에서 토큰 파일을 교체해 주세요.", "Cafe24 토큰 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Log($"등록 오류: {ex.Message}");
            MessageBox.Show(ex.Message, "신규상품 등록 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cafe24CreateButton.IsEnabled = true;
            ProgressBar.IsIndeterminate = false;
        }
    }

    private void CoupangSource_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void CoupangSource_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            CoupangSourcePath.Text = files[0];
        }
    }

    private void CoupangBrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Excel Files|*.xlsx;*.xls",
            Title = "가공파일 선택",
            InitialDirectory = Directory.Exists(GetDefaultExportRoot()) ? GetDefaultExportRoot() : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dlg.ShowDialog() == true)
        {
            CoupangSourcePath.Text = dlg.FileName;
        }
    }

    private async void CoupangUpload_Click(object sender, RoutedEventArgs e)
    {
        var sourcePath = CoupangSourcePath.Text.Trim();
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            // _lastOutputRoot에서 자동 탐색
            if (!string.IsNullOrEmpty(_lastOutputRoot) && Directory.Exists(_lastOutputRoot))
            {
                var found = FindLatestFile(_lastOutputRoot, "업로드용_*.xlsx");
                if (found != null)
                {
                    sourcePath = found;
                    CoupangSourcePath.Text = sourcePath;
                }
                else
                {
                    MessageBox.Show("가공파일(업로드용 엑셀)을 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                MessageBox.Show("가공파일(업로드용 엑셀)을 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var dryRun = CoupangDryRun.IsChecked == true;
        if (!dryRun)
        {
            var confirm = MessageBox.Show(
                $"쿠팡에 상품을 실제 등록합니다.\n\n파일: {Path.GetFileName(sourcePath)}\n\n계속하시겠습니까?",
                "쿠팡 실제 등록 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
        }

        CoupangUploadButton.IsEnabled = false;
        _cts = new CancellationTokenSource();

        try
        {
            StatusText.Text = dryRun ? "쿠팡 DRY RUN 중..." : "쿠팡 등록 중...";
            ProgressBar.IsIndeterminate = true;
            Log($"쿠팡 업로드 시작: {Path.GetFileName(sourcePath)} (DRY RUN: {dryRun})");

            var options = new CoupangUploadOptions
            {
                DryRun = dryRun,
                Cafe24TokenPath = GetHomeCafe24TokenPath(),
            };

            // 체크된 행만 처리 (목록이 있을 때)
            IReadOnlySet<int>? selectedRows = _coupangItems.Count > 0
                ? new HashSet<int>(_coupangItems.Where(i => i.IsChecked).Select(i => i.RowNum))
                : null;

            var service = new CoupangUploadService();
            var progress = new Progress<string>(msg => Log(msg));

            var result = await service.UploadAsync(sourcePath, options, progress, _cts.Token, selectedRows);

            // 업로드 이력 기록
            if (!dryRun)
            {
                foreach (var item in _coupangItems.Where(i => i.IsChecked && !string.IsNullOrEmpty(i.GsCode)))
                {
                    _uploadHistory.Mark(item.GsCode, "coupang");
                    item.CoupangStatus = UploadProductItem.FormatDate(DateTime.Now);
                    RecordUploadToDb(item.GsCode, item.ProductName, "쿠팡", "OK");
                }
            }

            // 결과 그리드
            var gridItems = result.Items.Select(item => new CoupangResultRow
            {
                Row = item.Row,
                Name = item.Name,
                Status = item.Status,
                Info = !string.IsNullOrEmpty(item.SellerProductId) ? item.SellerProductId : item.Category,
                Error = item.Error,
            }).ToList();
            CoupangResultGrid.ItemsSource = gridItems;
            CoupangSummaryText.Text = $"성공 {result.SuccessCount} / 실패 {result.FailCount} / 전체 {result.TotalCount}";
            StatusText.Text = $"쿠팡 {(dryRun ? "DRY RUN" : "등록")} 완료";
            Log($"쿠팡 완료: 성공 {result.SuccessCount} / 실패 {result.FailCount} / 전체 {result.TotalCount}");
            if (!dryRun)
            {
                RefreshWorkspaceUploadStatuses();
                AutoSaveWorkspacePackage("쿠팡 업로드 이력 갱신");
            }
        }
        catch (OperationCanceledException) { Log("쿠팡 업로드 취소됨"); StatusText.Text = "취소됨"; }
        catch (Exception ex)
        {
            Log($"쿠팡 오류: {ex.Message}");
            StatusText.Text = "쿠팡 업로드 오류";
            MessageBox.Show(ex.Message, "쿠팡 업로드 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CoupangUploadButton.IsEnabled = true;
            ProgressBar.IsIndeterminate = false;
        }
    }

    private sealed class CoupangResultRow
    {
        public int Row { get; set; }
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";
        public string Info { get; set; } = "";
        public string Error { get; set; } = "";
    }

    private void LoadUploadLog(string? logPath)
    {
        if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath)) return;

        try
        {
            using var wb = new XLWorkbook(logPath);
            var ws = wb.Worksheets.First();
            var rows = new List<UploadResultRow>();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

            for (int r = 2; r <= lastRow; r++)
            {
                rows.Add(new UploadResultRow
                {
                    Gs = ws.Cell(r, 1).GetString(),
                    ProductNo = ws.Cell(r, 2).GetString(),
                    Status = ws.Cell(r, 3).GetString(),
                    MainImage = ws.Cell(r, 4).GetString(),
                    AddCount = ws.Cell(r, 5).GetString(),
                    PriceStatus = ws.Cell(r, 6).GetString(),
                });
            }
            UploadResultGrid.ItemsSource = rows;
        }
        catch (Exception ex)
        {
            Log($"로그 읽기 오류: {ex.Message}");
        }
    }

    private void OpenUploadLog_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastUploadLogPath) && File.Exists(_lastUploadLogPath))
            Process.Start(new ProcessStartInfo(_lastUploadLogPath) { UseShellExecute = true });
    }

    #endregion

    #region ═══ 옵션 가격 관리 ═══

    private void LoadPriceData_Click(object sender, RoutedEventArgs e)
    {
        string? gptFile = null;

        // 결과 폴더에서 자동 탐색
        if (!string.IsNullOrEmpty(_lastOutputRoot))
            gptFile = FindLatestFile(_lastOutputRoot, "상품전처리GPT_*.xlsx");

        // 없으면 파일 선택
        if (gptFile == null)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Excel|*.xlsx|모든 파일|*.*",
                Title = "상품전처리GPT 파일 선택",
            };
            if (dlg.ShowDialog() != true) return;
            gptFile = dlg.FileName;
        }

        LoadPriceFromExcel(gptFile);
    }

    private void LoadPriceFromExcel(string filePath)
    {
        _priceRows.Clear();
        try
        {
            using var wb = new XLWorkbook(filePath);

            // 분리추출전 시트에서 옵션+공급가 로드
            var sheetName = wb.Worksheets.Any(w => w.Name == "분리추출전") ? "분리추출전" : wb.Worksheets.First().Name;
            var ws = wb.Worksheet(sheetName);
            var headerRow = ws.FirstRowUsed();
            if (headerRow == null) return;

            var lastCol = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
            var cols = new Dictionary<string, int>();

            for (int c = 1; c <= lastCol; c++)
            {
                var h = headerRow.Cell(c).GetString().Trim();
                if (!string.IsNullOrEmpty(h))
                    cols[h] = c;
            }

            int codeCol = FindCol(cols, CodeColumns);
            int nameCol = FindCol(cols, NameColumns);
            int supplyCol = FindCol(cols, new[] { "공급가", "supply_price" });
            int sellingCol = FindCol(cols, new[] { "판매가", "selling_price" });
            int consumerCol = FindCol(cols, new[] { "소비자가", "consumer_price" });

            if (codeCol < 0) { Log("상품코드 컬럼을 찾을 수 없습니다."); return; }

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            for (int r = headerRow.RowNumber() + 1; r <= lastRow; r++)
            {
                var code = ws.Cell(r, codeCol).GetString().Trim();
                if (string.IsNullOrEmpty(code)) continue;

                var gsMatch = Regex.Match(code, @"(GS\d{7})", RegexOptions.IgnoreCase);
                var gsCode = gsMatch.Success ? gsMatch.Value : code;

                var supply = supplyCol > 0 ? GetDecimal(ws.Cell(r, supplyCol)) : 0;
                var selling = sellingCol > 0 ? GetDecimal(ws.Cell(r, sellingCol)) : 0;
                var consumer = consumerCol > 0 ? GetDecimal(ws.Cell(r, consumerCol)) : 0;
                var optionName = nameCol > 0 ? ws.Cell(r, nameCol).GetString().Trim() : "";

                _priceRows.Add(new PriceRow
                {
                    IsChecked = true,
                    GsCode = gsCode,
                    OptionName = optionName,
                    SupplyPrice = supply,
                    SellingPrice = selling,
                    AdditionalAmount = 0,
                    ConsumerPrice = consumer,
                });
            }

            PriceFileText.Text = Path.GetFileName(filePath);
            PriceSummaryText.Text = $"{_priceRows.Count}개 행 로드됨";
            Log($"가격 데이터 로드: {_priceRows.Count}개 ({Path.GetFileName(filePath)})");
        }
        catch (Exception ex)
        {
            Log($"가격 파일 오류: {ex.Message}");
        }
    }

    private void RecalcPrices_Click(object sender, RoutedEventArgs e)
    {
        if (_priceRows.Count == 0) return;

        // GS코드별 그룹 → 최고 공급가 기준 추가금액 계산
        var groups = _priceRows.GroupBy(r => r.GsCode).ToList();
        foreach (var group in groups)
        {
            var items = group.ToList();
            if (items.Count <= 1) continue;

            var maxSupply = items.Max(r => r.SupplyPrice);
            foreach (var row in items)
            {
                row.AdditionalAmount = row.SupplyPrice - items.Min(r => r.SupplyPrice);
            }
        }

        PriceGrid.Items.Refresh();
        Log("추가금액 재계산 완료");
    }

    private void SavePriceData_Click(object sender, RoutedEventArgs e) => SavePriceReviewJson();

    private void SavePriceReviewJson()
    {
        if (_priceRows.Count == 0 || string.IsNullOrEmpty(_lastOutputRoot)) return;

        try
        {
            var checkedGs = _priceRows.Where(r => r.IsChecked)
                .Select(r => r.GsCode).Distinct().ToList();

            var editedAmounts = new Dictionary<string, List<decimal>>();
            foreach (var group in _priceRows.GroupBy(r => r.GsCode))
            {
                var amounts = group.Select(r => r.AdditionalAmount).ToList();
                if (amounts.Any(a => a != 0))
                    editedAmounts[group.Key] = amounts;
            }

            var data = new
            {
                checked_gs = checkedGs,
                edited_amounts = editedAmounts,
                image_selections = new Dictionary<string, object>(),
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            var path = Path.Combine(_lastOutputRoot, "cafe24_price_upload_data.json");
            File.WriteAllText(path, json, Encoding.UTF8);
            Log($"가격 데이터 저장: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Log($"저장 오류: {ex.Message}");
        }
    }

    private void PriceSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _priceRows) r.IsChecked = true;
        PriceGrid.Items.Refresh();
    }

    private void PriceDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _priceRows) r.IsChecked = false;
        PriceGrid.Items.Refresh();
    }

    #endregion

    #region ═══ 실행 이력 ═══

    private void RefreshHistoryGrid()
    {
        if (_jobHistory == null) return;
        HistoryGrid.ItemsSource = null;
        HistoryGrid.ItemsSource = _jobHistory.Records;
    }

    private void RefreshHistory_Click(object sender, RoutedEventArgs e) => RefreshHistoryGrid();

    private JobRecord? GetSelectedJob()
    {
        return HistoryGrid.SelectedItem as JobRecord;
    }

    private void HistoryGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        HistoryLoad_Click(sender, e);
    }

    private void HistoryLoad_Click(object sender, RoutedEventArgs e)
    {
        var job = GetSelectedJob();
        if (job == null) { MessageBox.Show("이력을 선택하세요.", "알림"); return; }

        if (!Directory.Exists(job.OutputRoot))
        {
            MessageBox.Show($"결과 폴더가 존재하지 않습니다.\n{job.OutputRoot}", "알림",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _lastOutputRoot = job.OutputRoot;
        _lastOutputFile = job.OutputFile;
        OutputFileText.Text = job.OutputFile;

        OpenUploadExcelButton.IsEnabled = true;
        OpenOutputFolderButton.IsEnabled = true;
        Cafe24UploadButton.IsEnabled = true;
        Cafe24CreateButton.IsEnabled = true;

        Log($"이력 불러오기: {job.DisplaySource} ({job.DisplayTime})");
        StatusText.Text = $"이력 로드됨 — {job.DisplaySource}";
    }

    private void HistoryOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var job = GetSelectedJob();
        if (job == null) return;
        if (Directory.Exists(job.OutputRoot))
            Process.Start(new ProcessStartInfo("explorer.exe", job.OutputRoot));
        else
            MessageBox.Show("폴더가 존재하지 않습니다.", "알림");
    }

    private void HistoryCopy_Click(object sender, RoutedEventArgs e)
    {
        var job = GetSelectedJob();
        if (job == null) { MessageBox.Show("이력을 선택하세요.", "알림"); return; }
        _jobHistory?.Clone(job);
        RefreshHistoryGrid();
        Log($"이력 복사: {job.DisplaySource}");
    }

    private void HistoryEditMemo_Click(object sender, RoutedEventArgs e)
    {
        var job = GetSelectedJob();
        if (job == null) { MessageBox.Show("이력을 선택하세요.", "알림"); return; }

        var dlg = new MemoDialog(job.Memo) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            job.Memo = dlg.MemoText;
            _jobHistory?.Update(job);
            RefreshHistoryGrid();
        }
    }

    private void HistoryDelete_Click(object sender, RoutedEventArgs e)
    {
        var job = GetSelectedJob();
        if (job == null) { MessageBox.Show("이력을 선택하세요.", "알림"); return; }

        var confirm = MessageBox.Show(
            $"이력을 삭제하시겠습니까?\n{job.DisplaySource} ({job.DisplayTime})",
            "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        _jobHistory?.Delete(job.Id);
        RefreshHistoryGrid();
        Log($"이력 삭제: {job.DisplaySource}");
    }

    private bool _historyAllSelected = false;
    private void HistorySelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_historyAllSelected)
            HistoryGrid.UnselectAll();
        else
            HistoryGrid.SelectAll();
        _historyAllSelected = !_historyAllSelected;
    }

    private void HistoryBulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = HistoryGrid.SelectedItems.Cast<JobRecord>().ToList();
        if (selected.Count == 0) { MessageBox.Show("삭제할 이력을 선택하세요.", "알림"); return; }

        var confirm = MessageBox.Show(
            $"{selected.Count}개 이력을 삭제하시겠습니까?",
            "선택 삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        foreach (var job in selected)
            _jobHistory?.Delete(job.Id);
        _historyAllSelected = false;
        RefreshHistoryGrid();
        Log($"이력 {selected.Count}개 일괄 삭제");
    }

    private void HistoryOpenCafe24Excel_Click(object sender, RoutedEventArgs e)
    {
        var job = GetSelectedJob();
        if (job == null) return;

        // 업로드용 엑셀 경로를 클립보드에 복사
        var uploadFile = FindLatestFile(job.OutputRoot, "업로드용_*.xlsx");
        if (uploadFile != null)
        {
            Clipboard.SetText(uploadFile);
            Log($"업로드용 엑셀 경로 클립보드 복사: {Path.GetFileName(uploadFile)}");
        }

        // Cafe24 상품 엑셀 관리 페이지 열기
        try
        {
            var store = new Cafe24ConfigStore(_v3Root, _legacyRoot);
            var state = store.LoadTokenState(
                string.IsNullOrWhiteSpace(SettingsTokenPath.Text) ? null : SettingsTokenPath.Text.Trim());
            var mallId = state.Config.MallId;
            if (!string.IsNullOrEmpty(mallId))
            {
                var url = $"https://{mallId}.cafe24.com/disp/admin/shop1/product/ProductExcelManage";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                Log("Cafe24 상품 엑셀 관리 페이지 열림");
            }
            else
            {
                MessageBox.Show("Mall ID가 설정되지 않았습니다. 설정 탭에서 토큰 파일을 확인하세요.", "알림");
            }
        }
        catch (Exception ex)
        {
            Log($"Cafe24 페이지 열기 오류: {ex.Message}");
        }
    }

    private async void HistoryCafe24Upload_Click(object sender, RoutedEventArgs e)
    {
        var job = GetSelectedJob();
        if (job == null) return;

        if (!TryGetSelectedCafe24Markets(out var runHomeMarket, out var runReadyMarket, out var marketLabel))
        {
            return;
        }

        if (!Directory.Exists(job.OutputRoot))
        {
            MessageBox.Show("결과 폴더가 존재하지 않습니다.", "알림"); return;
        }

        var uploadFile = FindLatestFile(job.OutputRoot, "업로드용_*.xlsx");
        if (uploadFile == null)
        {
            MessageBox.Show("업로드용 엑셀을 찾을 수 없습니다.", "알림"); return;
        }

        var confirm = MessageBox.Show(
            $"Cafe24에 이미지 + 옵션가격을 업로드합니다.\n\n" +
            $"대상 몰: {marketLabel}\n" +
            $"파일: {Path.GetFileName(uploadFile)}\n" +
            $"폴더: {job.OutputRoot}\n\n계속하시겠습니까?",
            "Cafe24 업로드 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        _lastOutputRoot = job.OutputRoot;
        _lastOutputFile = job.OutputFile;

        _cts = new CancellationTokenSource();
        Cafe24UploadButton.IsEnabled = false;

        try
        {
            StatusText.Text = "Cafe24 업로드 중...";
            ProgressBar.IsIndeterminate = true;

            SavePriceReviewJson();
            var options = BuildUploadOptions();
            options.ExportDir = job.OutputRoot;

            var priceDataPath = Path.Combine(job.OutputRoot, "cafe24_price_upload_data.json");
            if (File.Exists(priceDataPath))
                options.PriceDataPath = priceDataPath;

            var uploadService = new Cafe24UploadService(_v3Root, _legacyRoot);
            var progress = new Progress<string>(msg => Log(msg));
            var totalCount = 0;
            var totalSuccess = 0;
            var totalError = 0;
            var totalSkipped = 0;
            string? lastLogPath = null;

            void Accumulate(Cafe24UploadResult result)
            {
                totalCount += result.TotalCount;
                totalSuccess += result.SuccessCount;
                totalError += result.ErrorCount;
                totalSkipped += result.SkippedCount;
                if (!string.IsNullOrWhiteSpace(result.LogPath))
                {
                    lastLogPath = result.LogPath;
                }
            }

            async Task RunReadyMarketUploadAsync()
            {
                StatusText.Text = "준비몰 Cafe24 업로드 중...";
                var resultB = await uploadService.UploadBMarketAsync(uploadFile, job.OutputRoot, options, progress, _cts.Token, _bMarketTokenPath);
                Accumulate(resultB);
                if (resultB.TotalCount > 0)
                {
                    Log($"Cafe24 준비몰 업로드 완료: 성공 {resultB.SuccessCount} / 오류 {resultB.ErrorCount} / 스킵 {resultB.SkippedCount}");
                }
                else
                {
                    Log("Cafe24 준비몰 업로드 스킵: B마켓 시트 또는 대상 상품이 없습니다.");
                }
            }

            if (runHomeMarket)
            {
                StatusText.Text = "홈런마켓 Cafe24 업로드 중...";
                var result = await uploadService.UploadAsync(uploadFile, job.OutputRoot, options, progress, _cts.Token);
                Accumulate(result);
                Log($"Cafe24 홈런마켓 업로드 완료: 성공 {result.SuccessCount} / 오류 {result.ErrorCount} / 스킵 {result.SkippedCount}");
            }

            if (runReadyMarket)
            {
                if (runHomeMarket)
                {
                    try
                    {
                        await RunReadyMarketUploadAsync();
                    }
                    catch (Cafe24ReauthenticationRequiredException exB)
                    {
                        Log($"준비몰 Cafe24 토큰 오류: {exB.Message}");
                        MessageBox.Show("준비몰 토큰이 만료됐습니다. 설정 탭에서 토큰 파일을 교체해 주세요.", "준비몰 토큰 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch (Exception exB)
                    {
                        Log($"준비몰 업로드 오류 (홈런마켓은 성공): {exB.Message}");
                    }
                }
                else
                {
                    await RunReadyMarketUploadAsync();
                }
            }

            _lastUploadLogPath = lastLogPath;
            Log($"선택한 몰 Cafe24 업로드 완료: 대상 {marketLabel} / 성공 {totalSuccess} / 오류 {totalError} / 스킵 {totalSkipped}");
            StatusText.Text = $"업로드 완료 ({marketLabel}, 성공: {totalSuccess})";
            UploadSummaryText.Text = $"{marketLabel} | 총 {totalCount} | 성공 {totalSuccess} | 오류 {totalError} | 스킵 {totalSkipped}";
            OpenUploadLogButton.IsEnabled = !string.IsNullOrEmpty(lastLogPath);
            if (!string.IsNullOrEmpty(lastLogPath))
            {
                LoadUploadLog(lastLogPath);
            }
        }
        catch (OperationCanceledException) { Log("업로드 취소됨"); StatusText.Text = "취소됨"; }
        catch (Cafe24ReauthenticationRequiredException ex)
        {
            Log($"Cafe24 토큰 오류: {ex.Message}");
            StatusText.Text = "토큰 오류";
            MessageBox.Show("Cafe24 토큰이 만료됐습니다. 설정 탭에서 토큰 파일을 교체해 주세요.", "Cafe24 토큰 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Log($"Cafe24 오류: {ex.Message}");
            StatusText.Text = "업로드 오류";
            MessageBox.Show(ex.Message, "Cafe24 업로드 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cafe24UploadButton.IsEnabled = true;
            ProgressBar.IsIndeterminate = false;
        }
    }

    private async void HistoryCafe24Create_Click(object sender, RoutedEventArgs e)
    {
        var job = GetSelectedJob();
        if (job == null) return;

        if (!TryGetSelectedCafe24Markets(out var runHomeMarket, out var runReadyMarket, out var marketLabel))
        {
            return;
        }

        if (!Directory.Exists(job.OutputRoot))
        {
            MessageBox.Show("결과 폴더가 존재하지 않습니다.", "알림"); return;
        }

        var uploadFile = FindLatestFile(job.OutputRoot, "업로드용_*.xlsx");
        if (uploadFile == null)
        {
            MessageBox.Show("업로드용 엑셀을 찾을 수 없습니다.", "알림"); return;
        }

        var confirm = MessageBox.Show(
            $"Cafe24에 신규 상품을 등록합니다.\n\n" +
            $"대상 몰: {marketLabel}\n" +
            $"파일: {Path.GetFileName(uploadFile)}\n\n계속하시겠습니까?",
            "신규상품 등록 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        _lastOutputRoot = job.OutputRoot;
        _cts = new CancellationTokenSource();
        Cafe24CreateButton.IsEnabled = false;

        try
        {
            StatusText.Text = "신규상품 등록 중...";
            ProgressBar.IsIndeterminate = true;

            var createService = new Cafe24CreateProductService(_v3Root, _legacyRoot);
            var progress = new Progress<string>(msg => Log(msg));
            var totalCreated = 0;
            var totalError = 0;
            var totalSkipped = 0;

            async Task RunReadyMarketCreateAsync()
            {
                StatusText.Text = "준비몰 신규상품 등록 중...";
                Log("── [준비몰] 신규등록 시작 ──");
                var resultB = await createService.CreateBMarketAsync(uploadFile, job.OutputRoot, progress, _cts.Token, _bMarketTokenPath);
                totalCreated += resultB.CreatedCount;
                totalError += resultB.ErrorCount;
                totalSkipped += resultB.SkippedCount;
                if (resultB.TotalCount > 0)
                {
                    Log($"[준비몰] 신규등록 완료: 생성 {resultB.CreatedCount} / 오류 {resultB.ErrorCount} / 스킵 {resultB.SkippedCount}");
                }
                else
                {
                    Log("[준비몰] 신규등록 스킵: B마켓 시트에 등록 대상이 없습니다.");
                }
            }

            if (runHomeMarket)
            {
                StatusText.Text = "홈런마켓 신규상품 등록 중...";
                var aTokenPath = GetHomeCafe24TokenPath();
                var result = await createService.CreateAsync(uploadFile, job.OutputRoot, progress, _cts.Token, tokenPath: aTokenPath);
                totalCreated += result.CreatedCount;
                totalError += result.ErrorCount;
                totalSkipped += result.SkippedCount;
                Log($"[홈런마켓] 신규등록 완료: 생성 {result.CreatedCount} / 오류 {result.ErrorCount} / 스킵 {result.SkippedCount}");
            }

            if (runReadyMarket)
            {
                if (runHomeMarket)
                {
                    try
                    {
                        await RunReadyMarketCreateAsync();
                    }
                    catch (Exception exB)
                    {
                        Log($"[준비몰] 신규등록 오류: {exB.Message}");
                    }
                }
                else
                {
                    await RunReadyMarketCreateAsync();
                }
            }

            StatusText.Text = $"등록 완료 ({marketLabel}, 생성: {totalCreated})";
        }
        catch (OperationCanceledException) { Log("등록 취소됨"); StatusText.Text = "취소됨"; }
        catch (Exception ex)
        {
            Log($"등록 오류: {ex.Message}");
            MessageBox.Show(ex.Message, "신규상품 등록 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cafe24CreateButton.IsEnabled = true;
            ProgressBar.IsIndeterminate = false;
        }
    }

    private void HistoryOpenExcel_Click(object sender, RoutedEventArgs e)
    {
        var job = GetSelectedJob();
        if (job == null) return;
        var uploadFile = FindLatestFile(job.OutputRoot, "업로드용_*.xlsx");
        if (uploadFile != null && File.Exists(uploadFile))
            Process.Start(new ProcessStartInfo(uploadFile) { UseShellExecute = true });
        else
            MessageBox.Show("업로드용 엑셀을 찾을 수 없습니다.", "알림");
    }

    private void HistoryCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var job = GetSelectedJob();
        if (job == null) return;
        var uploadFile = FindLatestFile(job.OutputRoot, "업로드용_*.xlsx");
        if (uploadFile != null)
        {
            Clipboard.SetText(uploadFile);
            Log($"클립보드 복사: {uploadFile}");
        }
        else
        {
            Clipboard.SetText(job.OutputRoot);
            Log($"클립보드 복사: {job.OutputRoot}");
        }
    }

    private void HistoryViewProducts_Click(object sender, RoutedEventArgs e)
    {
        var job = GetSelectedJob();
        if (job == null) { MessageBox.Show("이력을 선택하세요.", "알림"); return; }

        if (job.SelectedCodes.Count == 0)
        {
            MessageBox.Show("선택된 상품코드가 없습니다.", "알림");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"작업: {job.DisplaySource} ({job.DisplayTime})");
        sb.AppendLine($"총 {job.SelectedCodes.Count}개 상품코드");
        sb.AppendLine(new string('─', 40));
        for (int i = 0; i < job.SelectedCodes.Count; i++)
            sb.AppendLine($"  {i + 1}. {job.SelectedCodes[i]}");

        MessageBox.Show(sb.ToString(), "상품목록", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void HistoryImageSelect_Click(object sender, RoutedEventArgs e)
    {
        var job = GetSelectedJob();
        if (job == null) { MessageBox.Show("이력을 선택하세요.", "알림"); return; }

        if (!Directory.Exists(job.OutputRoot))
        {
            MessageBox.Show($"결과 폴더가 존재하지 않습니다.\n{job.OutputRoot}", "알림",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 해당 작업의 결과를 로드
        _lastOutputRoot = job.OutputRoot;
        _lastOutputFile = job.OutputFile;

        // 이미지 불러오기 실행
        LoadImages_Click(sender, e);

        // 이미지선택 탭으로 이동
        ImageSelectionTab.IsSelected = true;
    }

    #endregion

    #region ═══ 설정 탭 ═══

    private void BrowseLogoPath_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg|모든 파일|*.*", Title = "로고 파일 선택" };
        if (dlg.ShowDialog() == true)
        {
            SettingsLogoPath.Text = dlg.FileName;
            BuildListingSettings();
        }
    }

    private void BrowseLogoPathB_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg|모든 파일|*.*", Title = "B마켓 로고 파일 선택" };
        if (dlg.ShowDialog() == true)
        {
            SettingsLogoPathB.Text = dlg.FileName;
            BuildListingSettings();
        }
    }

    private void BrowseTokenPath_Click(object sender, RoutedEventArgs e)
    {
        var keyDir = DesktopKeyStore.DirectoryPath;
        var dlg = new OpenFileDialog
        {
            Filter = "JSON|*.json|모든 파일|*.*",
            Title = "홈런마켓 토큰 JSON 파일 선택",
            InitialDirectory = Directory.Exists(keyDir) ? keyDir : ""
        };
        if (dlg.ShowDialog() == true)
        {
            SettingsTokenPath.Text = dlg.FileName;
            LoadTokenInfo();
            SaveAppSettings(BuildListingSettings());
            Log($"홈런마켓 토큰 파일 변경: {Path.GetFileName(dlg.FileName)}");
        }
    }

    private void LoadTokenInfo()
    {
        try
        {
            var store = new Cafe24ConfigStore(_v3Root, _legacyRoot);
            var tokenPath = string.IsNullOrWhiteSpace(SettingsTokenPath.Text)
                ? null : SettingsTokenPath.Text.Trim();
            var state = store.LoadTokenState(tokenPath);
            SettingsMallId.Text = state.Config.MallId;
            SettingsTokenStatus.Text = string.IsNullOrEmpty(state.Config.AccessToken)
                ? "토큰 없음" : $"토큰 로드됨 ({state.ConfigPath})";

            if (string.IsNullOrWhiteSpace(SettingsTokenPath.Text))
                SettingsTokenPath.Text = state.ConfigPath;
        }
        catch
        {
            SettingsMallId.Text = "";
            SettingsTokenStatus.Text = "토큰 파일을 찾을 수 없습니다.";
        }
    }

    // ─── 준비몰(B마켓) 토큰 관련 ───────────────────────────────────────────

    private void LoadTokenInfoB()
    {
        try
        {
            var store = new Cafe24ConfigStore(_v3Root, _legacyRoot);
            var path = string.IsNullOrWhiteSpace(_bMarketTokenPath) ? null : _bMarketTokenPath;
            var state = store.LoadTokenStateB(path);
            SettingsBMallId.Text = state.Config.MallId;
            SettingsBTokenStatus.Text = string.IsNullOrEmpty(state.Config.AccessToken)
                ? "토큰 없음" : $"토큰 로드됨 ({Path.GetFileName(state.ConfigPath)})";
            if (string.IsNullOrWhiteSpace(SettingsBTokenPath.Text))
                SettingsBTokenPath.Text = state.ConfigPath;
        }
        catch
        {
            SettingsBMallId.Text = "";
            SettingsBTokenStatus.Text = "토큰 파일을 찾을 수 없습니다.";
        }
    }

    private void BrowseTokenPathB_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "JSON|*.json|모든 파일|*.*",
            Title = "준비몰 토큰 JSON 파일 선택",
            InitialDirectory = DesktopKeyStore.DirectoryPath
        };
        if (dlg.ShowDialog() == true)
        {
            _bMarketTokenPath = dlg.FileName;
            SettingsBTokenPath.Text = dlg.FileName;
            LoadTokenInfoB();
            // 설정 저장
            var s = BuildListingSettings();
            SaveAppSettings(s);
            Log($"준비몰 토큰 파일 변경: {Path.GetFileName(dlg.FileName)}");
        }
    }

    private async void CheckTokenA_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "토큰 확인 중...";
            var store = new Cafe24ConfigStore(_v3Root, _legacyRoot);
            var tokenPath = string.IsNullOrWhiteSpace(SettingsTokenPath.Text) ? null : SettingsTokenPath.Text.Trim();
            var state = store.LoadTokenState(tokenPath);
            var client = new Cafe24ApiClient();
            await client.CheckTokenAsync(state.Config, CancellationToken.None);
            SettingsTokenStatus.Text = $"사용 가능 ({DateTime.Now:HH:mm:ss})";
            Log("홈런마켓 토큰 확인 완료 — 정상");
            MessageBox.Show("홈런마켓 토큰이 정상입니다.", "토큰 확인", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SettingsTokenStatus.Text = "토큰 오류";
            Log($"홈런마켓 토큰 확인 실패: {ex.Message}");
            MessageBox.Show($"토큰이 유효하지 않습니다. 토큰 파일을 교체해 주세요.\n\n{ex.Message}", "토큰 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { StatusText.Text = "대기 중"; }
    }

    private async void CheckTokenB_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "준비몰 토큰 확인 중...";
            var store = new Cafe24ConfigStore(_v3Root, _legacyRoot);
            var path = string.IsNullOrWhiteSpace(_bMarketTokenPath) ? null : _bMarketTokenPath;
            var state = store.LoadTokenStateB(path);
            var client = new Cafe24ApiClient();
            await client.CheckTokenAsync(state.Config, CancellationToken.None);
            SettingsBTokenStatus.Text = $"사용 가능 ({DateTime.Now:HH:mm:ss})";
            Log("준비몰 토큰 확인 완료 — 정상");
            MessageBox.Show("준비몰 토큰이 정상입니다.", "준비몰 토큰 확인", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SettingsBTokenStatus.Text = "토큰 오류";
            Log($"준비몰 토큰 확인 실패: {ex.Message}");
            MessageBox.Show($"준비몰 토큰이 유효하지 않습니다. 토큰 파일을 교체해 주세요.\n\n{ex.Message}", "준비몰 토큰 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { StatusText.Text = "대기 중"; }
    }

    private async void RefreshTokenA_Click(object sender, RoutedEventArgs e)
        => await RefreshCafe24TokenAsync(isBMarket: false);

    private async void RefreshTokenB_Click(object sender, RoutedEventArgs e)
        => await RefreshCafe24TokenAsync(isBMarket: true);

    private async Task RefreshCafe24TokenAsync(bool isBMarket)
    {
        var label = isBMarket ? "준비몰" : "홈런마켓";
        var statusText = isBMarket ? SettingsBTokenStatus : SettingsTokenStatus;

        try
        {
            StatusText.Text = $"{label} 토큰 리프레쉬 중...";
            statusText.Text = "리프레쉬 중...";

            var store = new Cafe24ConfigStore(_v3Root, _legacyRoot);
            var tokenPath = isBMarket
                ? (string.IsNullOrWhiteSpace(SettingsBTokenPath.Text) ? _bMarketTokenPath : SettingsBTokenPath.Text.Trim())
                : GetHomeCafe24TokenPath();
            var state = isBMarket
                ? store.LoadTokenStateB(tokenPath)
                : store.LoadTokenState(tokenPath);
            var client = new Cafe24ApiClient();

            var refreshed = await Cafe24TokenRefreshSupport.TryRefreshAndSaveAsync(
                store,
                client,
                state,
                CancellationToken.None,
                msg => Log(msg),
                label);

            if (!refreshed)
                Log($"{label} 토큰 리프레쉬 생략: refresh token/client 정보가 없거나 재인증이 필요할 수 있습니다. 기존 토큰 확인을 시도합니다.");

            await client.CheckTokenAsync(state.Config, CancellationToken.None);

            if (isBMarket)
            {
                _bMarketTokenPath = state.ConfigPath;
                SettingsBTokenPath.Text = state.ConfigPath;
                SettingsBMallId.Text = state.Config.MallId;
                SettingsBTokenStatus.Text = refreshed
                    ? $"리프레쉬 완료 ({DateTime.Now:HH:mm:ss})"
                    : $"기존 토큰 사용 가능 ({DateTime.Now:HH:mm:ss})";
                SaveAppSettings(BuildListingSettings());
            }
            else
            {
                SettingsTokenPath.Text = state.ConfigPath;
                SettingsMallId.Text = state.Config.MallId;
                SettingsTokenStatus.Text = refreshed
                    ? $"리프레쉬 완료 ({DateTime.Now:HH:mm:ss})"
                    : $"기존 토큰 사용 가능 ({DateTime.Now:HH:mm:ss})";
                SaveAppSettings(BuildListingSettings());
            }

            Log($"{label} 토큰 리프레쉬/확인 완료");
            MessageBox.Show(
                refreshed
                    ? $"{label} 토큰을 리프레쉬하고 저장했습니다."
                    : $"{label} 토큰 리프레쉬는 생략됐지만 현재 토큰은 사용 가능합니다.",
                $"{label} 토큰 리프레쉬",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            statusText.Text = "리프레쉬 실패";
            Log($"{label} 토큰 리프레쉬 실패: {ex.Message}");
            MessageBox.Show(
                $"{label} 토큰 리프레쉬에 실패했습니다. refresh token이 만료됐으면 새 토큰 JSON으로 교체해야 합니다.\n\n{ex.Message}",
                $"{label} 토큰 리프레쉬 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            StatusText.Text = "대기 중";
        }
    }

    #endregion

    #region ═══ 파일 열기 ═══

    private void OpenUploadExcel_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastOutputRoot)) return;
        var uploadFile = FindLatestFile(_lastOutputRoot, "업로드용_*.xlsx");
        if (uploadFile != null && File.Exists(uploadFile))
        {
            Process.Start(new ProcessStartInfo(uploadFile) { UseShellExecute = true });
            Log($"엑셀 열기: {Path.GetFileName(uploadFile)}");
        }
        else
            MessageBox.Show("업로드용 엑셀을 찾을 수 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastOutputRoot) && Directory.Exists(_lastOutputRoot))
            Process.Start(new ProcessStartInfo("explorer.exe", _lastOutputRoot));
    }

    #endregion

    #region ═══ 유틸 ═══

    private bool ValidateSource()
    {
        if (string.IsNullOrEmpty(_sourcePath) || !File.Exists(_sourcePath))
        {
            MessageBox.Show("파일을 먼저 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    // ── 상품 선택 목록 ────────────────────────────────────────────────

    private void LoadCafe24ProductList(string? xlsxPath = null)
    {
        xlsxPath ??= (!string.IsNullOrEmpty(_lastOutputFile) && File.Exists(_lastOutputFile))
            ? _lastOutputFile
            : FindLatestFile(_lastOutputRoot, "업로드용_*.xlsx");

        _cafe24Items.Clear();
        _cafe24LastClickIndex = -1;

        if (string.IsNullOrEmpty(xlsxPath) || !File.Exists(xlsxPath))
        {
            Cafe24SelectCountText.Text = "(업로드용 엑셀 없음 — STEP 1 먼저 실행)";
            return;
        }

        try
        {
            var entries = Services.Cafe24CreateProductService.ExtractGsCodesFromWorkbook(xlsxPath);
            foreach (var (gsCode, productName) in entries)
            {
                var hist = _uploadHistory.Get(gsCode);
                _cafe24Items.Add(new UploadProductItem
                {
                    GsCode = gsCode,
                    ProductName = productName,
                    HomeMarketStatus = UploadProductItem.FormatDate(hist?.HomeMarket),
                    ReadyMarketStatus = UploadProductItem.FormatDate(hist?.ReadyMarket),
                });
            }
            UpdateCafe24SelectCount();
        }
        catch (Exception ex)
        {
            Cafe24SelectCountText.Text = $"읽기 실패: {ex.Message}";
        }
    }

    private void LoadCoupangProductList(string? xlsxPath = null)
    {
        xlsxPath ??= CoupangSourcePath?.Text?.Trim();
        if (string.IsNullOrEmpty(xlsxPath) && !string.IsNullOrEmpty(_lastOutputRoot))
            xlsxPath = FindLatestFile(_lastOutputRoot, "업로드용_*.xlsx");

        _coupangItems.Clear();
        _coupangLastClickIndex = -1;

        if (string.IsNullOrEmpty(xlsxPath) || !File.Exists(xlsxPath))
        {
            CoupangSelectCountText.Text = "(파일 없음)";
            return;
        }

        try
        {
            var rows = Services.CoupangProductBuilder.ReadSourceFile(xlsxPath);
            var gsRegex = new System.Text.RegularExpressions.Regex(@"(GS\d{7}[A-Z0-9]*)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (var row in rows)
            {
                var rowNum = (int)row["_row_num"]!;
                var name = row.TryGetValue("상품명", out var n) ? n?.ToString() ?? "" : "";
                var codeField = row.TryGetValue("자체 상품코드", out var c) ? c?.ToString() ?? "" : "";
                var gsCode = gsRegex.Match(codeField).Success ? gsRegex.Match(codeField).Groups[1].Value.ToUpperInvariant()
                           : gsRegex.Match(name).Success ? gsRegex.Match(name).Groups[1].Value.ToUpperInvariant() : "";
                var hist = string.IsNullOrEmpty(gsCode) ? null : _uploadHistory.Get(gsCode);

                _coupangItems.Add(new UploadProductItem
                {
                    RowNum = rowNum,
                    GsCode = gsCode,
                    ProductName = name,
                    CoupangStatus = UploadProductItem.FormatDate(hist?.Coupang),
                });
            }
            UpdateCoupangSelectCount();
        }
        catch (Exception ex)
        {
            CoupangSelectCountText.Text = $"읽기 실패: {ex.Message}";
        }
    }

    private void UpdateCafe24SelectCount()
    {
        var total = _cafe24Items.Count;
        var selected = _cafe24Items.Count(i => i.IsChecked);
        Cafe24SelectCountText.Text = $"{selected}/{total} 선택";
    }

    private void UpdateCoupangSelectCount()
    {
        var total = _coupangItems.Count;
        var selected = _coupangItems.Count(i => i.IsChecked);
        CoupangSelectCountText.Text = $"{selected}/{total} 선택";
    }

    private void Cafe24SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _cafe24Items) item.IsChecked = true;
        UpdateCafe24SelectCount();
    }

    private void Cafe24DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _cafe24Items) item.IsChecked = false;
        UpdateCafe24SelectCount();
    }

    private void Cafe24RefreshList_Click(object sender, RoutedEventArgs e) => LoadCafe24ProductList();

    private void CoupangSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _coupangItems) item.IsChecked = true;
        UpdateCoupangSelectCount();
    }

    private void CoupangDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _coupangItems) item.IsChecked = false;
        UpdateCoupangSelectCount();
    }

    private void CoupangRefreshList_Click(object sender, RoutedEventArgs e) => LoadCoupangProductList();

    private void Cafe24ProductList_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        HandleProductListShiftClick(Cafe24ProductList, _cafe24Items, ref _cafe24LastClickIndex, e);
        UpdateCafe24SelectCount();
    }

    private void CoupangProductList_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        HandleProductListShiftClick(CoupangProductList, _coupangItems, ref _coupangLastClickIndex, e);
        UpdateCoupangSelectCount();
    }

    // ── 기본실행 탭 신규등록 목록 ─────────────────────────────────────────

    private void LoadBasicCafe24ProductList(string? xlsxPath = null)
    {
        xlsxPath ??= (!string.IsNullOrEmpty(_lastOutputFile) && File.Exists(_lastOutputFile))
            ? _lastOutputFile
            : FindLatestFile(_lastOutputRoot, "업로드용_*.xlsx");

        _basicCafe24Items.Clear();
        _basicCafe24LastClickIndex = -1;

        if (string.IsNullOrEmpty(xlsxPath) || !File.Exists(xlsxPath))
        {
            BasicCafe24CountText.Text = "(업로드용 엑셀 없음 — STEP 1 먼저 실행)";
            BasicCafe24RunButton.IsEnabled = false;
            DirectHomeMarketUploadButton.IsEnabled = false;
            SetMarketExcelButtonsEnabled(false);
            return;
        }

        try
        {
            var entries = Services.Cafe24CreateProductService.ExtractGsCodesFromWorkbook(xlsxPath);
            foreach (var (gsCode, productName) in entries)
            {
                var hist = _uploadHistory.Get(gsCode);
                _basicCafe24Items.Add(new UploadProductItem
                {
                    GsCode = gsCode,
                    ProductName = productName,
                    HomeMarketStatus = UploadProductItem.FormatDate(hist?.HomeMarket),
                    ReadyMarketStatus = UploadProductItem.FormatDate(hist?.ReadyMarket),
                });
            }
            UpdateBasicCafe24Count();
            BasicCafe24RunButton.IsEnabled = _basicCafe24Items.Count > 0;
            DirectHomeMarketUploadButton.IsEnabled = _basicCafe24Items.Count > 0;
            SetMarketExcelButtonsEnabled(_basicCafe24Items.Count > 0);
        }
        catch (Exception ex)
        {
            BasicCafe24CountText.Text = $"읽기 실패: {ex.Message}";
            BasicCafe24RunButton.IsEnabled = false;
            DirectHomeMarketUploadButton.IsEnabled = false;
            SetMarketExcelButtonsEnabled(false);
        }
    }

    private void UpdateBasicCafe24Count()
    {
        var total = _basicCafe24Items.Count;
        var selected = _basicCafe24Items.Count(i => i.IsChecked);
        BasicCafe24CountText.Text = $"{selected}/{total} 선택";
    }

    private void BasicCafe24SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _basicCafe24Items) item.IsChecked = true;
        UpdateBasicCafe24Count();
    }

    private void BasicCafe24DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _basicCafe24Items) item.IsChecked = false;
        UpdateBasicCafe24Count();
    }

    private void BasicCafe24Refresh_Click(object sender, RoutedEventArgs e)
    {
        var files = _testLlmResultFiles.Where(File.Exists).ToList();
        LoadBasicCafe24ProductList(files.Count > 0 ? files[0] : null);
    }

    private void BasicCafe24ProductList_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        HandleProductListShiftClick(BasicCafe24ProductGrid, _basicCafe24Items, ref _basicCafe24LastClickIndex, e);
        UpdateBasicCafe24Count();
    }

    private async void BasicCafe24Run_Click(object sender, RoutedEventArgs e)
        => await RunBasicCafe24CreateAsync(
            runLinkedMarketUploads: AutoNaverLotteOnCheckBox?.IsChecked == true,
            showDialogs: true,
            sourceLabel: "수동");

    private async Task<bool> RunBasicCafe24CreateAsync(
        bool runLinkedMarketUploads,
        bool showDialogs,
        string sourceLabel)
    {
        var files = _testLlmResultFiles.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            var message = "LLM 결과 파일을 먼저 선택하세요.";
            if (showDialogs)
                MessageBox.Show(message, "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                Log($"[V5 자동등록] {message}");
            return false;
        }

        var selectedGs = new HashSet<string>(
            _basicCafe24Items.Where(i => i.IsChecked).Select(i => i.GsCode),
            StringComparer.OrdinalIgnoreCase);

        if (selectedGs.Count == 0)
        {
            var message = "등록할 상품을 선택하세요.";
            if (showDialogs)
                MessageBox.Show(message, "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                Log($"[V5 자동등록] {message}");
            return false;
        }

        var doHome = TestCafe24HomeCheckBox.IsChecked == true;
        var doReady = TestCafe24ReadyCheckBox.IsChecked == true;
        var runDirectNaver = runLinkedMarketUploads && doHome && DirectNaverUploadCheckBox.IsChecked == true;
        var runDirectLotteOn = runLinkedMarketUploads && doHome && DirectLotteOnUploadCheckBox.IsChecked == true;
        var runDirectCoupang = runLinkedMarketUploads && doHome && DirectCoupangUploadCheckBox.IsChecked == true;

        _lastOutputRoot = _testOutputRoot ?? Path.GetDirectoryName(files[0])!;
        _lastOutputFile = files[0];

        var uploadFile = FindUploadExcel();
        if (uploadFile == null)
        {
            var message = "업로드용 엑셀을 찾을 수 없습니다. STEP 1을 먼저 실행하세요.";
            if (showDialogs)
                MessageBox.Show(message, "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                Log($"[V5 자동등록] {message}");
            return false;
        }

        if (runDirectNaver)
        {
            StatusText.Text = "네이버 중복 확인 중...";
            var duplicateInfo = await CheckNaverDuplicatesAsync(uploadFile);
            if (duplicateInfo.Count > 0)
            {
                var dupLines = duplicateInfo.Select(d => $"  • {d.GsCode}  {d.ProductName}");
                var msg = $"다음 {duplicateInfo.Count}개 상품이 네이버에 이미 등록되어 있습니다:\n\n" +
                          string.Join("\n", dupLines) +
                          "\n\n포함하여 계속 진행하시겠습니까?";
                if (!showDialogs || MessageBox.Show(msg, "네이버 중복 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return false;
            }
        }

        BasicCafe24RunButton.IsEnabled = false;
        _cts = new CancellationTokenSource();

        try
        {
            StatusText.Text = "카테고리맵 자동 업로드 중...";
            ProgressBar.IsIndeterminate = true;

            var createService = new Cafe24CreateProductService(_v3Root, _legacyRoot);
            var progress = new Progress<string>(msg => Log(msg));
            int totalCreated = 0, totalError = 0, totalSkipped = 0;

            Log($"[Cafe24 신규등록:{sourceLabel}] 대상 {selectedGs.Count}개 / 홈런마켓={doHome} / 준비몰={doReady}");
            await TryUploadLatestMarketPlusCategoryMapAsync(uploadFile, files, _cts.Token);
            StatusText.Text = "Cafe24 신규등록 중...";

            if (doHome)
            {
                var aTokenPath = GetHomeCafe24TokenPath();
                var result = await createService.CreateAsync(uploadFile, _lastOutputRoot, progress, _cts.Token,
                    tokenPath: aTokenPath, allowedGsCodes: selectedGs);
                totalCreated += result.CreatedCount;
                totalError += result.ErrorCount;
                totalSkipped += result.SkippedCount;
                foreach (var item in _basicCafe24Items.Where(i => i.IsChecked))
                {
                    _uploadHistory.Mark(item.GsCode, "homemarket");
                    item.HomeMarketStatus = UploadProductItem.FormatDate(DateTime.Now);
                    RecordUploadToDb(item.GsCode, item.ProductName, "홈런마켓", "OK");
                }
                Log($"[홈런마켓] 신규등록 완료: 생성 {result.CreatedCount} / 오류 {result.ErrorCount} / 스킵 {result.SkippedCount}");

                if (runDirectNaver || runDirectLotteOn || runDirectCoupang)
                {
                    var directUploadFile = FindDirectMarketWorkbook(uploadFile);
                    if (!string.Equals(directUploadFile, uploadFile, StringComparison.OrdinalIgnoreCase))
                        Log($"[직접등록] 최종 V4 엑셀 사용: {Path.GetFileName(directUploadFile)}");

                    await RunDirectHomeMarketUploadsAsync(
                        directUploadFile,
                        selectedGs,
                        runDirectNaver,
                        runDirectLotteOn,
                        runDirectCoupang,
                        progress,
                        _cts.Token);
                }
            }

            if (doReady)
            {
                StatusText.Text = "준비몰 신규상품 등록 중...";
                var resultB = await createService.CreateBMarketAsync(uploadFile, _lastOutputRoot, progress, _cts.Token,
                    _bMarketTokenPath, selectedGs);
                totalCreated += resultB.CreatedCount;
                totalError += resultB.ErrorCount;
                totalSkipped += resultB.SkippedCount;
                foreach (var item in _basicCafe24Items.Where(i => i.IsChecked))
                {
                    _uploadHistory.Mark(item.GsCode, "readymarket");
                    item.ReadyMarketStatus = UploadProductItem.FormatDate(DateTime.Now);
                    RecordUploadToDb(item.GsCode, item.ProductName, "준비몰", "OK");
                }
                Log($"[준비몰] 신규등록 완료: 생성 {resultB.CreatedCount} / 오류 {resultB.ErrorCount} / 스킵 {resultB.SkippedCount}");
            }

            UpdateBasicCafe24Count();
            Log($"신규등록 완료: 생성 {totalCreated} / 오류 {totalError} / 스킵 {totalSkipped}");
            StatusText.Text = "신규등록 완료";
            RefreshWorkspaceUploadStatuses();
            AutoSaveWorkspacePackage("Cafe24 신규등록 이력 갱신");
            return totalError == 0;
        }
        catch (OperationCanceledException)
        {
            Log("신규등록 취소됨");
            StatusText.Text = "취소됨";
            return false;
        }
        catch (Exception ex)
        {
            Log($"신규등록 오류: {ex.Message}");
            StatusText.Text = "오류 발생";
            if (showDialogs)
                MessageBox.Show(ex.Message, "신규등록 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            ProgressBar.IsIndeterminate = false;
            BasicCafe24RunButton.IsEnabled = _basicCafe24Items.Count > 0;
            _cts = null;
        }
    }

    private async void BasicDirectHomeMarketUpload_Click(object sender, RoutedEventArgs e)
    {
        var files = _testLlmResultFiles.Where(File.Exists).ToList();
        if (files.Count > 0)
        {
            _lastOutputRoot = _testOutputRoot ?? Path.GetDirectoryName(files[0])!;
            _lastOutputFile = files[0];
        }

        var uploadFile = FindUploadExcel();
        if (uploadFile == null)
        {
            MessageBox.Show("업로드용 엑셀을 찾을 수 없습니다. V5 결과를 먼저 불러오세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedGs = new HashSet<string>(
            _basicCafe24Items.Where(i => i.IsChecked).Select(i => i.GsCode),
            StringComparer.OrdinalIgnoreCase);
        if (selectedGs.Count == 0)
        {
            MessageBox.Show("직접등록할 상품을 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var runDirectNaver = DirectNaverUploadCheckBox.IsChecked == true;
        var runDirectLotteOn = DirectLotteOnUploadCheckBox.IsChecked == true;
        var runDirectCoupang = DirectCoupangUploadCheckBox.IsChecked == true;
        if (!runDirectNaver && !runDirectLotteOn && !runDirectCoupang)
        {
            MessageBox.Show("네이버, 롯데ON, 쿠팡 중 하나 이상 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (runDirectNaver)
        {
            StatusText.Text = "네이버 중복 확인 중...";
            var duplicateInfo = (await CheckNaverDuplicatesAsync(uploadFile))
                .Where(d => selectedGs.Contains(d.GsCode))
                .ToList();
            if (duplicateInfo.Count > 0)
            {
                var dupLines = duplicateInfo.Select(d => $"  • {d.GsCode}  {d.ProductName}");
                var msg = $"다음 {duplicateInfo.Count}개 상품이 네이버에 이미 등록되어 있습니다:\n\n" +
                          string.Join("\n", dupLines) +
                          "\n\n포함하여 계속 진행하시겠습니까?";
                if (MessageBox.Show(msg, "네이버 중복 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }
        }

        DirectHomeMarketUploadButton.IsEnabled = false;
        _cts = new CancellationTokenSource();

        try
        {
            StatusText.Text = "선택 API 직접등록 중...";
            ProgressBar.IsIndeterminate = true;
            var progress = new Progress<string>(msg => Log(msg));
            var directUploadFile = FindDirectMarketWorkbook(uploadFile);
            if (!string.Equals(directUploadFile, uploadFile, StringComparison.OrdinalIgnoreCase))
                Log($"[직접등록] 최종 V4 엑셀 사용: {Path.GetFileName(directUploadFile)}");

            await RunDirectHomeMarketUploadsAsync(
                directUploadFile,
                selectedGs,
                runDirectNaver,
                runDirectLotteOn,
                runDirectCoupang,
                progress,
                _cts.Token);

            StatusText.Text = "선택 API 직접등록 완료";
            AutoSaveWorkspacePackage("선택 API 직접등록 이력 갱신");
        }
        catch (OperationCanceledException)
        {
            Log("선택 API 직접등록 취소됨");
            StatusText.Text = "취소됨";
        }
        catch (Exception ex)
        {
            Log($"선택 API 직접등록 오류: {ex.Message}");
            StatusText.Text = "직접등록 오류";
            MessageBox.Show(ex.Message, "선택 API 직접등록 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ProgressBar.IsIndeterminate = false;
            DirectHomeMarketUploadButton.IsEnabled = _basicCafe24Items.Count > 0;
            _cts = null;
        }
    }

    private async void ExportElevenstExcel_Click(object sender, RoutedEventArgs e)
        => await RunMarketExcelExportAsync(exportElevenst: true, exportEsm: false);

    private async void ExportEsmExcel_Click(object sender, RoutedEventArgs e)
        => await RunMarketExcelExportAsync(exportElevenst: false, exportEsm: true);

    private async void ExportAllMarketExcel_Click(object sender, RoutedEventArgs e)
        => await RunMarketExcelExportAsync(exportElevenst: true, exportEsm: true);

    private async Task RunMarketExcelExportAsync(bool exportElevenst, bool exportEsm)
    {
        var files = _testLlmResultFiles.Where(File.Exists).ToList();
        if (files.Count > 0)
        {
            _lastOutputRoot = _testOutputRoot ?? Path.GetDirectoryName(files[0])!;
            _lastOutputFile = files[0];
        }

        var uploadFile = FindUploadExcel();
        if (uploadFile == null)
        {
            MessageBox.Show("V5 결과 엑셀을 먼저 불러오세요.", "마켓 엑셀 생성", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedGs = _basicCafe24Items
            .Where(item => item.IsChecked)
            .Select(item => item.GsCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedGs.Count == 0)
        {
            MessageBox.Show("엑셀로 만들 상품을 선택하세요.", "마켓 엑셀 생성", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetMarketExcelButtonsEnabled(false);
        ProgressBar.IsIndeterminate = true;
        StatusText.Text = "마켓 엑셀 생성 중...";
        MarketExcelExportStatusText.Text = "엑셀 다운로드: 생성 중...";

        try
        {
            var tokenPath = GetHomeCafe24TokenPath();
            var service = new MarketExcelExportService(_v3Root, tokenPath);
            var progress = new Progress<string>(message =>
            {
                Log($"[마켓엑셀] {message}");
                MarketExcelExportStatusText.Text = $"엑셀 다운로드: {message}";
            });

            var result = await Task.Run(() => service.Export(uploadFile, selectedGs, exportElevenst, exportEsm, progress));
            _lastMarketExcelOutputFolder = result.OutputDirectory;
            OpenMarketExcelFolderButton.IsEnabled = true;

            foreach (var file in result.Files)
                Log($"[마켓엑셀] {file.Market}: {file.Path}");
            Log($"[마켓엑셀] 검수리포트: {result.ReportPath}");

            var summary = $"엑셀 다운로드 완료: 상품 {result.ProductCount}개 / 파일 {result.Files.Count}개 / 경고 {result.WarningCount}건";
            MarketExcelExportStatusText.Text = summary;
            StatusText.Text = "마켓 엑셀 생성 완료";
            MessageBox.Show(
                $"{summary}\n\n폴더: {result.OutputDirectory}",
                "마켓 엑셀 생성 완료",
                MessageBoxButton.OK,
                result.WarningCount == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Log($"[마켓엑셀] 오류: {ex.Message}");
            MarketExcelExportStatusText.Text = $"엑셀 다운로드 오류: {ex.Message}";
            StatusText.Text = "마켓 엑셀 생성 오류";
            MessageBox.Show(ex.Message, "마켓 엑셀 생성 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ProgressBar.IsIndeterminate = false;
            SetMarketExcelButtonsEnabled(_basicCafe24Items.Count > 0);
        }
    }

    private void OpenMarketExcelFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastMarketExcelOutputFolder) || !Directory.Exists(_lastMarketExcelOutputFolder))
        {
            MessageBox.Show("아직 생성된 마켓 엑셀 폴더가 없습니다.", "마켓 엑셀", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenFolder(_lastMarketExcelOutputFolder);
    }

    private void SetMarketExcelButtonsEnabled(bool enabled)
    {
        if (ExportElevenstExcelButton is not null)
            ExportElevenstExcelButton.IsEnabled = enabled;
        if (ExportEsmExcelButton is not null)
            ExportEsmExcelButton.IsEnabled = enabled;
        if (ExportAllMarketExcelButton is not null)
            ExportAllMarketExcelButton.IsEnabled = enabled;
        if (OpenMarketExcelFolderButton is not null)
            OpenMarketExcelFolderButton.IsEnabled = enabled && !string.IsNullOrWhiteSpace(_lastMarketExcelOutputFolder)
                                                   && Directory.Exists(_lastMarketExcelOutputFolder);
    }

    private static void HandleProductListShiftClick(
        System.Windows.Controls.DataGrid grid,
        System.Collections.ObjectModel.ObservableCollection<UploadProductItem> items,
        ref int lastIndex,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        var hit = grid.InputHitTest(e.GetPosition(grid)) as System.Windows.DependencyObject;
        if (hit is null) return;

        // 클릭한 DataGridRow 찾기
        while (hit is not null && hit is not System.Windows.Controls.DataGridRow)
            hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);

        if (hit is not System.Windows.Controls.DataGridRow clickedRow) return;

        var item = clickedRow.Item as UploadProductItem;
        if (item is null) return;

        var clickedIndex = items.IndexOf(item);
        if (clickedIndex < 0) return;

        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
        {
            if (lastIndex >= 0)
            {
                var start = Math.Min(lastIndex, clickedIndex);
                var end = Math.Max(lastIndex, clickedIndex);
                var targetState = items[clickedIndex].IsChecked;
                for (var i = start; i <= end; i++)
                    items[i].IsChecked = targetState;
                e.Handled = true;
                return;
            }
        }

        lastIndex = clickedIndex;
    }

    private static string? FindLatestFile(string? dir, string pattern)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, pattern)
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
    }

    private string FindDirectMarketWorkbook(string cafe24UploadFile)
    {
        if (IsDirectMarketWorkbook(cafe24UploadFile))
            return cafe24UploadFile;

        if (string.IsNullOrWhiteSpace(_lastOutputRoot) || !Directory.Exists(_lastOutputRoot))
            return cafe24UploadFile;

        var candidates = new List<string>();
        foreach (var dir in new[]
                 {
                     Path.Combine(_lastOutputRoot, "llm_result_v5_cli"),
                     Path.Combine(_lastOutputRoot, "llm_result_v4_cli"),
                     Path.Combine(_lastOutputRoot, "llm_result_v4_local"),
                     Path.Combine(_lastOutputRoot, "llm_result"),
                 })
        {
            if (!Directory.Exists(dir))
                continue;

            candidates.AddRange(Directory.GetFiles(dir, "*_llm_v5_cli.xlsx"));
            candidates.AddRange(Directory.GetFiles(dir, "*_llm_v4_cli.xlsx"));
            candidates.AddRange(Directory.GetFiles(dir, "*_llm_v4_local.xlsx"));
            candidates.AddRange(Directory.GetFiles(dir, "업로드용_*.xlsx"));
        }

        var latest = candidates
            .Where(IsDirectMarketWorkbook)
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();

        return latest ?? cafe24UploadFile;
    }

    private static bool IsDirectMarketWorkbook(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        var name = Path.GetFileName(path);
        return name.Contains("업로드용_", StringComparison.OrdinalIgnoreCase)
               && !name.Contains("_batch_", StringComparison.OrdinalIgnoreCase)
               && !name.Contains("category_match", StringComparison.OrdinalIgnoreCase)
               && !name.Contains("_categories", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<List<(string GsCode, string ProductName)>> CheckNaverDuplicatesAsync(string uploadFile)
    {
        var result = new List<(string GsCode, string ProductName)>();
        try
        {
            var gsCodesInFile = Cafe24CreateProductService.ExtractGsCodesFromWorkbook(uploadFile);
            if (gsCodesInFile.Count == 0) return result;

            var naverClient = NaverCommerceApiClient.FromKeyFile();
            var existingCodes = await naverClient.GetExistingGsCodesAsync(CancellationToken.None);
            var existingSet = new HashSet<string>(existingCodes.Select(e => e.GsCode), StringComparer.OrdinalIgnoreCase);

            foreach (var (gsCode, productName) in gsCodesInFile)
            {
                if (existingSet.Contains(gsCode))
                    result.Add((gsCode, productName));
            }
        }
        catch (Exception ex)
        {
            Log($"네이버 중복 확인 실패 (스킵): {ex.Message}");
        }
        return result;
    }

    private static int FindCol(Dictionary<string, int> cols, string[] candidates)
    {
        foreach (var c in candidates)
            if (cols.TryGetValue(c, out var idx)) return idx;
        return -1;
    }

    private static decimal GetDecimal(IXLCell cell)
    {
        try { return (decimal)cell.GetDouble(); }
        catch { return decimal.TryParse(cell.GetString(), out var v) ? v : 0; }
    }

    private static int ParseInt(TextBox tb, int fallback)
    {
        return int.TryParse(tb.Text.Trim(), out var v) ? v : fallback;
    }

    private static double ParseDouble(TextBox tb, double fallback)
    {
        return double.TryParse(tb.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private void Log(string message)
    {
        var time = DateTime.Now.ToString("HH:mm:ss");
        Dispatcher.Invoke(() =>
        {
            LogBlock.AppendText($"[{time}] {message}\n");
            LogBlock.ScrollToEnd();
        });
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogBlock.Text = "";
    }

    private void SetPipelineEnabled(bool enabled)
    {
        RunPipelineButton.IsEnabled = enabled;
        RunKeywordOnlyButton.IsEnabled = enabled;
        RunListingOnlyButton.IsEnabled = enabled;
        TestRunOcrOnlyButton.IsEnabled = enabled;
    }

    private void SetRunning(bool running)
    {
        SetPipelineEnabled(!running);
        RunPipelineButton.Content = running ? "실행 중..." : "전체 실행 (전처리+OCR+키워드+이미지)";
        if (running)
            EnableSleepPreventionIfNeeded();
        else
            DisableSleepPrevention();
    }

    private void EnableSleepPreventionIfNeeded()
    {
        if (!_preventSleepDuringWork || _sleepPreventionActive)
            return;

        try
        {
            PowerManagementService.PreventSleep();
            _sleepPreventionActive = true;
            Log("전원 옵션: 작업 중 절전 방지 시작");
        }
        catch (Exception ex)
        {
            Log($"작업 중 절전 방지 실패: {ex.Message}");
        }
    }

    private void DisableSleepPrevention()
    {
        if (!_sleepPreventionActive)
            return;

        try
        {
            PowerManagementService.AllowSleep();
            Log("전원 옵션: 작업 중 절전 방지 해제");
        }
        catch (Exception ex)
        {
            Log($"작업 중 절전 방지 해제 실패: {ex.Message}");
        }
        finally
        {
            _sleepPreventionActive = false;
        }
    }

    private void RunCompletionPowerActionIfNeeded(string reason)
    {
        if (_completionPowerAction == CompletionPowerAction.None)
            return;

        try
        {
            PrepareWorkspaceForPowerAction(reason);
            Log($"전원 옵션 실행: {GetCompletionPowerActionLabel(_completionPowerAction)}");

            switch (_completionPowerAction)
            {
                case CompletionPowerAction.CloseApp:
                    Close();
                    break;
                case CompletionPowerAction.Sleep:
                    PowerManagementService.RequestSleep();
                    break;
                case CompletionPowerAction.Shutdown:
                    PowerManagementService.RequestShutdown(60);
                    StatusText.Text = "작업 완료 — 60초 후 Windows 종료";
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"작업 완료 후 전원 동작 실패: {ex.Message}");
            MessageBox.Show(ex.Message, "전원 옵션 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PrepareWorkspaceForPowerAction(string reason)
    {
        SaveImageSelectionsToFile(markHistory: true, log: true);
        AutoSaveWorkspacePackage(reason);
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        SaveImageSelectionsToFile(markHistory: true, log: false);
        SaveInterruptedWorkspaceProgress("프로그램 종료/중단");
        BuildListingSettings();
        DisableSleepPrevention();
        base.OnClosed(e);
    }

    #endregion

    #region ═══ 이미지 선택 ═══

    /// <summary>Phase 1 완료 후 이미지 로드 + 탭 전환</summary>
    private void LoadListingImagesFromRoot(string outputRoot)
    {
        _lastOutputRoot = outputRoot;
        LoadImages_Click(this, new RoutedEventArgs());
        ImageSelectionTab.Visibility = Visibility.Visible;
        ImageSelectionTab.IsSelected = true;
    }

    private void LoadImages_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastOutputRoot) || !Directory.Exists(_lastOutputRoot))
        {
            MessageBox.Show("먼저 파이프라인을 실행하거나 이력을 불러오세요.", "알림",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var listingRoot = Path.Combine(_lastOutputRoot, "listing_images");
        if (!Directory.Exists(listingRoot))
        {
            Log("listing_images 폴더를 찾을 수 없습니다.");
            return;
        }

        var dateDir = Directory.GetDirectories(listingRoot)
            .OrderByDescending(d => d).FirstOrDefault();
        if (dateDir == null)
        {
            Log("날짜 폴더를 찾을 수 없습니다.");
            return;
        }

        _imageListingRoot = dateDir;
        _imageGsCodes.Clear();
        _imageSelections.Clear();
        _imageThumbnails.Clear();

        // 기존 선택 불러오기
        var selectionsPath = Path.Combine(_lastOutputRoot, "image_selections.json");
        if (File.Exists(selectionsPath))
            LoadImageSelectionsFromJson(selectionsPath);

        var gsFolders = Cafe24UploadSupport.GetGsFolders(dateDir);
        foreach (var folder in gsFolders)
        {
            var gs = folder.Name.ToUpperInvariant();
            var gs9 = gs.Length >= 9 ? gs[..9] : gs;
            _imageGsCodes.Add(gs9);

            if (!_imageSelections.ContainsKey(gs9))
            {
                var fileCount = Directory.GetFiles(folder.FullName)
                    .Count(f => IsImageFile(f));
                var mainIdx = fileCount >= 2 ? 1 : 0;
                var addIndices = Enumerable.Range(2, Math.Max(0, fileCount - 2)).ToList();
                _imageSelections[gs9] = new ImageSelection(mainIdx, addIndices);
            }
        }

        // 파일 정보 표시
        var sourceName = _lastOutputFile != null ? Path.GetFileName(_lastOutputFile) : Path.GetFileName(_lastOutputRoot ?? "");
        var dateFolderName = Path.GetFileName(dateDir);
        ImageSourceFileText.Text = sourceName;
        ImageSourceDateText.Text = dateFolderName;
        ImageGsCountText.Text = $"{_imageGsCodes.Count}개";
        ImageSelectionStatus.Text = $"{_imageGsCodes.Count}개 상품";
        Log($"이미지 불러오기 완료: {_imageGsCodes.Count}개 상품 ({dateFolderName})");

        if (_imageGsCodes.Count > 0)
            ImageGsListBox.SelectedIndex = 0;
    }

    private void ImageGsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _imageThumbnails.Clear();
        _selectingBMarket = false;
        ImageSelectionStatus.Text = $"{_imageGsCodes.Count}개 상품 — A마켓 대표 선택";
        if (ImageGsListBox.SelectedItem is not string gs || _imageListingRoot == null) return;

        var folder = Path.Combine(_imageListingRoot, gs);
        if (!Directory.Exists(folder)) return;

        var files = Directory.GetFiles(folder)
            .Where(f => IsImageFile(f))
            .OrderBy(f => f)
            .ToList();

        _imageSelections.TryGetValue(gs, out var selection);

        for (int i = 0; i < files.Count; i++)
        {
            var item = new ImageThumbnailItem
            {
                Index = i,
                DisplayNumber = i + 1,
                FilePath = files[i],
                Thumbnail = LoadThumbnail(files[i]),
                IsMain = selection?.MainIndex == i,
                IsMainB = selection?.MainIndexB == i,
                IsAdditional = selection?.AdditionalIndices?.Contains(i) == true,
            };
            _imageThumbnails.Add(item);
        }

        _imageThumbnailsB.Clear();
        foreach (var src in _imageThumbnails)
        {
            _imageThumbnailsB.Add(new ImageThumbnailItem
            {
                Index = src.Index,
                DisplayNumber = src.DisplayNumber,
                FilePath = src.FilePath,
                Thumbnail = src.Thumbnail,
                IsMain = false,
                IsMainB = src.IsMainB,
                IsAdditional = false,
            });
        }
    }

    private void Thumbnail_LeftClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ImageThumbnailItem clicked) return;
        if (ImageGsListBox.SelectedItem is not string gs) return;

        if (_selectingBMarket)
        {
            // B마켓 대표이미지 선택
            foreach (var item in _imageThumbnails)
                item.IsMainB = false;
            clicked.IsMainB = true;
            clicked.IsAdditional = false;
            UpdateSelectionForGs(gs);

            // B마켓 선택 완료 → 다음 상품으로 이동
            _selectingBMarket = false;
            ImageSelectionStatus.Text = $"{_imageGsCodes.Count}개 상품 — A마켓 대표 선택";
            var currentIndex = ImageGsListBox.SelectedIndex;
            if (currentIndex < ImageGsListBox.Items.Count - 1)
            {
                ImageGsListBox.SelectedIndex = currentIndex + 1;
                ImageGsListBox.ScrollIntoView(ImageGsListBox.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        // A마켓 대표이미지 선택
        foreach (var item in _imageThumbnails)
            item.IsMain = false;
        clicked.IsMain = true;
        clicked.IsAdditional = false;
        UpdateSelectionForGs(gs);

        // 더블클릭이면 B마켓 선택 모드로 전환
        if (e.ClickCount >= 2)
        {
            _selectingBMarket = true;
            ImageSelectionStatus.Text = $"{_imageGsCodes.Count}개 상품 — B마켓 대표 선택 (클릭하세요)";
            e.Handled = true;
        }
    }

    private void Thumbnail_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ImageThumbnailItem clicked) return;
        if (ImageGsListBox.SelectedItem is not string gs) return;

        if (clicked.IsMain) return;
        clicked.IsAdditional = !clicked.IsAdditional;
        UpdateSelectionForGs(gs);
        e.Handled = true;
    }

    private void UpdateSelectionForGs(string gs)
    {
        var mainItem = _imageThumbnails.FirstOrDefault(t => t.IsMain);
        var mainBItem = _imageThumbnails.FirstOrDefault(t => t.IsMainB);
        var addItems = _imageThumbnails.Where(t => t.IsAdditional).Select(t => t.Index).ToList();
        _imageSelections[gs] = new ImageSelection(mainItem?.Index, addItems, mainBItem?.Index);
        SaveImageSelectionsToFile(markHistory: true, log: false);
    }

    private void SaveImageSelection_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveImageSelectionsToFile(markHistory: true, log: true))
        {
            MessageBox.Show("저장할 대상이 없습니다.", "알림"); return;
        }

        BasicRunTab.IsSelected = true;
        StatusText.Text = "이미지 선택 저장 완료";
        AutoSaveWorkspacePackage("이미지 선택 저장");
    }

    private bool SaveImageSelectionsToFile(bool markHistory, bool log)
    {
        if (string.IsNullOrEmpty(_lastOutputRoot) || !Directory.Exists(_lastOutputRoot))
            return false;

        var dict = new Dictionary<string, object>();
        foreach (var kvp in _imageSelections)
        {
            dict[kvp.Key] = new { main = kvp.Value.MainIndex, mainB = kvp.Value.MainIndexB, additional = kvp.Value.AdditionalIndices };
        }

        var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
        var path = Path.Combine(_lastOutputRoot, "image_selections.json");
        File.WriteAllText(path, json, Encoding.UTF8);
        if (log)
            Log($"이미지 선택 저장 완료: {_imageSelections.Count}개 상품");

        // 실행이력에 이미지 선택 완료 표시
        if (markHistory && _jobHistory != null)
        {
            var job = _jobHistory.Records.FirstOrDefault(r => r.OutputRoot == _lastOutputRoot);
            if (job != null && !job.ImageSelected)
            {
                job.ImageSelected = true;
                _jobHistory.Update(job);
                RefreshHistoryGrid();
            }
        }

        return true;
    }

    private void LoadImageSelectionsFromJson(string path)
    {
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                int? mainIdx = null;
                var addIndices = new List<int>();

                if (prop.Value.TryGetProperty("main", out var mainEl) && mainEl.ValueKind == JsonValueKind.Number)
                    mainIdx = mainEl.GetInt32();
                int? mainIdxB = null;
                if (prop.Value.TryGetProperty("mainB", out var mainBEl) && mainBEl.ValueKind == JsonValueKind.Number)
                    mainIdxB = mainBEl.GetInt32();
                if (prop.Value.TryGetProperty("additional", out var addEl) && addEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in addEl.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.Number) addIndices.Add(item.GetInt32());
                }

                _imageSelections[prop.Name] = new ImageSelection(mainIdx, addIndices, mainIdxB);
            }
            Log($"이미지 선택 불러옴: {_imageSelections.Count}개");
        }
        catch (Exception ex)
        {
            Log($"이미지 선택 로드 실패: {ex.Message}");
        }
    }

    private static BitmapImage LoadThumbnail(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.DecodePixelWidth = 150;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp";
    }

    private void ThumbnailB_LeftClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ImageThumbnailItem clicked) return;
        if (ImageGsListBox.SelectedItem is not string gs) return;

        foreach (var item in _imageThumbnailsB)
            item.IsMainB = false;
        clicked.IsMainB = true;

        foreach (var a in _imageThumbnails)
        {
            if (a.Index == clicked.Index)
                a.IsMainB = true;
            else
                a.IsMainB = false;
        }

        UpdateSelectionForGs(gs);
        e.Handled = true;
    }

    #endregion

    #region ═══ 이미지 없는 상품 스킵 ═══

    private void CheckAndSkipNoImageProducts(string outputRoot)
    {
        if (_productDb == null) return;

        var listingDir = Path.Combine(outputRoot, "listing_images");
        if (!Directory.Exists(listingDir)) return;

        var skipped = new List<string>();
        foreach (var p in _products.Where(p => p.IsSelected).ToList())
        {
            var gsDir = Path.Combine(listingDir, p.Code);
            if (!Directory.Exists(gsDir) || !Directory.GetFiles(gsDir).Any(IsImageFile))
            {
                _productDb.AddNoImageProduct(p.Code, p.Name, _currentSessionId);
                p.IsSelected = false;
                skipped.Add(p.Code);
            }
        }

        if (skipped.Count > 0)
        {
            Log($"이미지 없는 상품 {skipped.Count}개 스킵: {string.Join(", ", skipped)}");
            Log("수동으로 이미지 추가 후 '과거 결과 불러오기'로 이어서 처리 가능");
        }
    }

    #endregion

    #region ═══ 과거 결과 불러오기 ═══

    private void LoadPastResult_Click(object sender, RoutedEventArgs e)
    {
        if (_productDb == null) return;

        var sessions = _productDb.GetWorkSessions(50);
        if (sessions.Count == 0)
        {
            MessageBox.Show("저장된 작업 이력이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var items = sessions.Select(s =>
            $"[{s.SessionDate}] {Path.GetFileName(s.SourceFile)} ({s.ProductCount}개) — {s.Status}" +
            (string.IsNullOrEmpty(s.OutputRoot) ? "" : $"\n  {s.OutputRoot}")).ToList();

        var listBox = new ListBox
        {
            FontSize = 11,
            FontFamily = new System.Windows.Media.FontFamily("Consolas, 'D2Coding'"),
            Height = 300,
        };
        foreach (var item in items) listBox.Items.Add(item);
        listBox.SelectedIndex = 0;

        var dialog = new Window
        {
            Title = "과거 작업 결과 불러오기",
            Width = 600,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Content = new StackPanel
            {
                Margin = new Thickness(12),
                Children =
                {
                    new TextBlock { Text = "불러올 작업을 선택하세요:", FontSize = 12, Margin = new Thickness(0, 0, 0, 8) },
                    listBox,
                    new Button
                    {
                        Content = "불러오기",
                        Height = 32,
                        Margin = new Thickness(0, 8, 0, 0),
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x45, 0x7b, 0x9d)),
                        Foreground = System.Windows.Media.Brushes.White,
                    }
                }
            }
        };

        var loadButton = ((StackPanel)dialog.Content).Children[2] as Button;
        loadButton!.Click += (_, _) =>
        {
            if (listBox.SelectedIndex >= 0)
            {
                dialog.DialogResult = true;
                dialog.Close();
            }
        };

        if (dialog.ShowDialog() == true && listBox.SelectedIndex >= 0)
        {
            var session = sessions[listBox.SelectedIndex];
            if (!string.IsNullOrEmpty(session.OutputRoot) && Directory.Exists(session.OutputRoot))
            {
                _testOutputRoot = session.OutputRoot;
                _lastOutputRoot = session.OutputRoot;
                TryAutoLoadLatestV4Result();
                Log($"과거 결과 로드 완료: {session.SessionDate} — {Path.GetFileName(session.SourceFile)}");
                StatusText.Text = "과거 결과 로드 완료";
            }
            else if (!string.IsNullOrEmpty(session.ZipPath) && File.Exists(session.ZipPath))
            {
                LoadWorkspacePackageFromPath(session.ZipPath);
            }
            else
            {
                MessageBox.Show("결과 폴더 또는 ZIP 파일을 찾을 수 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    #endregion

    #region ═══ Cafe24 토큰 자동 리프레시 ═══

    private async Task AutoRefreshAllCafe24TokensAsync()
    {
        Log("Cafe24 토큰 자동 리프레시 시작...");
        try
        {
            await RefreshCafe24TokenSilentAsync(isBMarket: false);
        }
        catch (Exception ex) { Log($"홈런마켓 토큰 자동 리프레시 실패: {ex.Message}"); }

        try
        {
            await RefreshCafe24TokenSilentAsync(isBMarket: true);
        }
        catch (Exception ex) { Log($"준비몰 토큰 자동 리프레시 실패: {ex.Message}"); }
    }

    private async Task RefreshCafe24TokenSilentAsync(bool isBMarket)
    {
        var label = isBMarket ? "준비몰" : "홈런마켓";
        var store = new Cafe24ConfigStore(_v3Root, _legacyRoot);
        var tokenPath = isBMarket
            ? (string.IsNullOrWhiteSpace(SettingsBTokenPath.Text) ? _bMarketTokenPath : SettingsBTokenPath.Text.Trim())
            : GetHomeCafe24TokenPath();
        var state = isBMarket
            ? store.LoadTokenStateB(tokenPath)
            : store.LoadTokenState(tokenPath);
        var client = new Cafe24ApiClient();

        await Cafe24TokenRefreshSupport.TryRefreshAndSaveAsync(
            store, client, state, CancellationToken.None, msg => Log(msg), label);

        if (isBMarket)
        {
            _bMarketTokenPath = state.ConfigPath;
            Dispatcher.Invoke(() =>
            {
                SettingsBTokenPath.Text = state.ConfigPath;
                SettingsBTokenStatus.Text = $"자동 리프레시 ({DateTime.Now:HH:mm:ss})";
            });
        }
        else
        {
            Dispatcher.Invoke(() =>
            {
                SettingsTokenPath.Text = state.ConfigPath;
                SettingsTokenStatus.Text = $"자동 리프레시 ({DateTime.Now:HH:mm:ss})";
            });
        }
    }

    #endregion

    #region ═══ 업로드 이력 DB UI ═══

    private void UploadHistoryRefresh_Click(object sender, RoutedEventArgs e) => RefreshUploadHistoryGrid();
    private void UploadHistorySearch_Click(object sender, RoutedEventArgs e) => RefreshUploadHistoryGrid();

    private void RefreshUploadHistoryGrid()
    {
        if (_productDb == null) return;

        var dateFilter = UploadHistoryDateFilter.Text.Trim();
        var marketFilter = (UploadHistoryMarketFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (marketFilter == "전체") marketFilter = null;
        var gsFilter = UploadHistoryGsFilter.Text.Trim();

        var rows = _productDb.GetUploadHistory(
            string.IsNullOrEmpty(dateFilter) ? null : dateFilter,
            marketFilter,
            string.IsNullOrEmpty(gsFilter) ? null : gsFilter);

        UploadHistoryGrid.ItemsSource = rows;
    }

    private void UploadHistoryDelete_Click(object sender, RoutedEventArgs e)
    {
        if (UploadHistoryGrid.SelectedItem is not UploadHistoryRow row) return;
        if (_productDb == null) return;

        var confirm = MessageBox.Show($"이 업로드 이력을 삭제하시겠습니까?\n\n{row.GsCode} / {row.Market} / {row.UploadedAt}",
            "이력 삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        _productDb.DeleteUploadHistory(row.Id);
        RefreshUploadHistoryGrid();
        Log($"업로드 이력 삭제: {row.GsCode} / {row.Market}");
    }

    private void UploadHistoryExport_Click(object sender, RoutedEventArgs e)
    {
        if (_productDb == null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Excel|*.xlsx",
            FileName = $"upload_history_{DateTime.Now:yyyyMMdd}.xlsx",
        };
        if (dlg.ShowDialog() != true) return;

        var rows = _productDb.GetUploadHistory(limit: 10000);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("업로드이력");
        ws.Cell(1, 1).Value = "날짜";
        ws.Cell(1, 2).Value = "GS코드";
        ws.Cell(1, 3).Value = "상품명";
        ws.Cell(1, 4).Value = "마켓";
        ws.Cell(1, 5).Value = "상태";
        ws.Cell(1, 6).Value = "상품ID";
        ws.Cell(1, 7).Value = "오류";

        for (var i = 0; i < rows.Count; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].UploadedAt;
            ws.Cell(i + 2, 2).Value = rows[i].GsCode;
            ws.Cell(i + 2, 3).Value = rows[i].ProductName;
            ws.Cell(i + 2, 4).Value = rows[i].Market;
            ws.Cell(i + 2, 5).Value = rows[i].Status;
            ws.Cell(i + 2, 6).Value = rows[i].ProductId;
            ws.Cell(i + 2, 7).Value = rows[i].Error;
        }

        wb.SaveAs(dlg.FileName);
        Log($"업로드 이력 엑셀 내보내기 완료: {Path.GetFileName(dlg.FileName)} ({rows.Count}건)");
        Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
    }

    private void RecordUploadToDb(string gsCode, string productName, string market, string status,
        string? productId = null, string? error = null)
    {
        _productDb?.RecordUpload(gsCode, productName, market, status, productId, error, _sourcePath);
    }

    #endregion

    #region ═══ 키워드 편집기 ═══

    private sealed class KeywordEditorEntry
    {
        public string GsCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string MarketLabel { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public string Original { get; set; } = "";
        public string Modified { get; set; } = "";
    }

    private readonly List<KeywordEditorEntry> _keywordEditorEntries = new();

    private void KeywordEditorSave_Click(object sender, RoutedEventArgs e)
    {
        if (_keywordEditorEntries.Count == 0 || _testLlmResultFiles.Count == 0)
        {
            MessageBox.Show("편집할 데이터가 없습니다. V5 결과를 먼저 불러오세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var changed = _keywordEditorEntries.Where(entry => entry.Original != entry.Modified).ToList();
        if (changed.Count == 0)
        {
            MessageBox.Show("변경된 키워드가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var filePath = _testLlmResultFiles[0];
            using var wb = new XLWorkbook(filePath);

            foreach (var entry in changed)
            {
                foreach (var ws in wb.Worksheets)
                {
                    var headerRow = ws.FirstRowUsed();
                    if (headerRow == null) continue;
                    var headers = ReadHeaderColumns(headerRow);
                    if (!headers.TryGetValue(entry.ColumnName, out var col)) continue;

                    var gsCol = FindCol(headers, new[] { "자체 상품코드", "자체상품코드", "gs_code" });
                    if (gsCol <= 0) continue;

                    var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                    for (var r = headerRow.RowNumber() + 1; r <= lastRow; r++)
                    {
                        if (string.Equals(ws.Cell(r, gsCol).GetString().Trim(), entry.GsCode, StringComparison.OrdinalIgnoreCase))
                        {
                            ws.Cell(r, col).Value = entry.Modified;
                            break;
                        }
                    }
                }
            }

            wb.Save();
            Log($"키워드 편집 저장 완료: {changed.Count}건 변경 → {Path.GetFileName(filePath)}");
            KeywordEditorStatus.Text = $"저장 완료 ({changed.Count}건 변경)";
        }
        catch (Exception ex)
        {
            Log($"키워드 편집 저장 실패: {ex.Message}");
            MessageBox.Show(ex.Message, "저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void KeywordEditorAnalyze_Click(object sender, RoutedEventArgs e)
    {
        var changed = _keywordEditorEntries.Where(entry => entry.Original != entry.Modified).ToList();
        if (changed.Count == 0)
        {
            KeywordEditorAnalysisText.Text = "변경된 키워드가 없습니다.";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"총 {changed.Count}건 변경 감지");
        sb.AppendLine();

        foreach (var entry in changed)
        {
            var origWords = entry.Original.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var modWords = entry.Modified.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var added = modWords.Except(origWords).ToList();
            var removed = origWords.Except(modWords).ToList();

            sb.AppendLine($"[{entry.GsCode}] {entry.MarketLabel} / {entry.ColumnName}");
            sb.AppendLine($"  글자수: {entry.Original.Length} → {entry.Modified.Length} ({(entry.Modified.Length > entry.Original.Length ? "+" : "")}{entry.Modified.Length - entry.Original.Length})");
            sb.AppendLine($"  단어수: {origWords.Length} → {modWords.Length}");
            if (added.Count > 0) sb.AppendLine($"  추가: {string.Join(", ", added)}");
            if (removed.Count > 0) sb.AppendLine($"  삭제: {string.Join(", ", removed)}");

            var origFirst3 = string.Join(" ", origWords.Take(3));
            var modFirst3 = string.Join(" ", modWords.Take(3));
            if (origFirst3 != modFirst3)
                sb.AppendLine($"  순서변경: 앞3단어 [{origFirst3}] → [{modFirst3}]");
            sb.AppendLine();
        }

        var analysisJson = JsonSerializer.Serialize(changed.Select(c => new
        {
            c.GsCode,
            c.MarketLabel,
            c.ColumnName,
            OriginalLength = c.Original.Length,
            ModifiedLength = c.Modified.Length,
            OriginalWords = c.Original.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            ModifiedWords = c.Modified.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            Added = c.Modified.Split(' ', StringSplitOptions.RemoveEmptyEntries).Except(c.Original.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList(),
            Removed = c.Original.Split(' ', StringSplitOptions.RemoveEmptyEntries).Except(c.Modified.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList(),
            Original = c.Original,
            Modified = c.Modified,
        }), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        var feedbackDir = Path.Combine(_legacyRoot, "keyword_editor_feedback");
        Directory.CreateDirectory(feedbackDir);
        var feedbackPath = Path.Combine(feedbackDir, $"feedback_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(feedbackPath, analysisJson, Encoding.UTF8);

        KeywordEditorAnalysisText.Text = sb.ToString();
        Log($"키워드 변경 분석 저장: {Path.GetFileName(feedbackPath)}");

        Clipboard.SetText(analysisJson);
        Log("변경 분석 JSON이 클립보드에 복사됨 — Codex에 붙여넣어 프롬프트를 업데이트하세요.");
    }

    private void LoadKeywordEditorFromExcel(string excelPath)
    {
        _keywordEditorEntries.Clear();
        KeywordEditorPanel.Children.Clear();

        try
        {
            using var wb = WorkbookFileLoader.OpenReadOnly(excelPath);
            var targetColumns = new[]
            {
                ("홈런_네이버상품명", "홈런 네이버"),
                ("홈런_네이버태그", "홈런 네이버태그"),
                ("홈런_롯데ON상품명", "홈런 롯데ON"),
                ("홈런_롯데ON검색키워드", "홈런 롯데ON키워드"),
                ("홈런_공통마켓상품명", "홈런 11/ESM"),
                ("홈런_공통마켓검색키워드", "홈런 11/ESM키워드"),
                ("홈런_Cafe24검색어설정", "Cafe24 검색어설정"),
                ("홈런_Cafe24검색키워드", "Cafe24 키워드"),
                ("홈런_스마트스토어태그", "스마트스토어 태그"),
                ("홈런_스마트스토어검색키워드", "스마트스토어 키워드"),
                ("홈런_쿠팡검색키워드", "쿠팡 키워드"),
                ("홈런_ESM검색키워드", "ESM 키워드"),
                ("홈런_11번가검색키워드", "11번가 키워드"),
                ("1차키워드", "기본 키워드"),
                ("최종키워드2차", "최종 키워드"),
                ("검색키워드", "검색키워드"),
            };

            foreach (var ws in wb.Worksheets)
            {
                var headerRow = ws.FirstRowUsed();
                if (headerRow == null) continue;
                var headers = ReadHeaderColumns(headerRow);

                var gsCol = FindCol(headers, new[] { "자체 상품코드", "자체상품코드", "gs_code" });
                var nameCol = FindCol(headers, new[] { "상품명", "product_name" });
                if (gsCol <= 0) continue;

                var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                for (var r = headerRow.RowNumber() + 1; r <= lastRow; r++)
                {
                    var gsCode = ws.Cell(r, gsCol).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(gsCode)) continue;
                    var productName = nameCol > 0 ? ws.Cell(r, nameCol).GetString().Trim() : gsCode;

                    var productBorder = new Border
                    {
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)),
                        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xdd, 0xdd, 0xdd)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(12, 8, 12, 8),
                        Margin = new Thickness(0, 0, 0, 12),
                    };
                    var productStack = new StackPanel();
                    productStack.Children.Add(new TextBlock
                    {
                        Text = $"{gsCode} — {productName}",
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1a, 0x1a, 0x2e)),
                        Margin = new Thickness(0, 0, 0, 8),
                    });

                    foreach (var (colName, label) in targetColumns)
                    {
                        if (!headers.TryGetValue(colName, out var col)) continue;
                        var value = ws.Cell(r, col).GetString().Trim();
                        if (string.IsNullOrWhiteSpace(value)) continue;

                        var entry = new KeywordEditorEntry
                        {
                            GsCode = gsCode,
                            ProductName = productName,
                            MarketLabel = label,
                            ColumnName = colName,
                            Original = value,
                            Modified = value,
                        };
                        _keywordEditorEntries.Add(entry);

                        productStack.Children.Add(new TextBlock
                        {
                            Text = label,
                            FontSize = 11,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)),
                            Margin = new Thickness(0, 4, 0, 2),
                        });

                        var origBox = new TextBox
                        {
                            Text = value,
                            IsReadOnly = true,
                            FontSize = 11,
                            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xf8, 0xf9, 0xfa)),
                            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xee, 0xee, 0xee)),
                            Padding = new Thickness(6, 4, 6, 4),
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 2),
                        };

                        var modBox = new TextBox
                        {
                            Text = value,
                            FontSize = 11,
                            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6c, 0x5c, 0xe7)),
                            Padding = new Thickness(6, 4, 6, 4),
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 6),
                            AcceptsReturn = false,
                        };

                        var capturedEntry = entry;
                        modBox.TextChanged += (_, _) => capturedEntry.Modified = modBox.Text;

                        productStack.Children.Add(new TextBlock { Text = "원본:", FontSize = 10, Foreground = System.Windows.Media.Brushes.Gray });
                        productStack.Children.Add(origBox);
                        productStack.Children.Add(new TextBlock { Text = "수정:", FontSize = 10, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6c, 0x5c, 0xe7)) });
                        productStack.Children.Add(modBox);
                    }

                    productBorder.Child = productStack;
                    KeywordEditorPanel.Children.Add(productBorder);
                }
            }

            KeywordEditorStatus.Text = $"{_keywordEditorEntries.Count}개 키워드 로드됨";
        }
        catch (Exception ex)
        {
            Log($"키워드 편집기 로드 실패: {ex.Message}");
        }
    }

    #endregion

    #region ═══ 배치 결과 Final 폴더 분리 ═══

    private string GetFinalResultDir(string outputRoot)
    {
        var finalDir = Path.Combine(outputRoot, "llm_result_v5_cli", "final");
        Directory.CreateDirectory(finalDir);
        return finalDir;
    }

    #endregion

    #region ═══ ZIP 자동 저장 ═══

    private void AutoSaveCompletedWorkZip()
    {
        try
        {
            if (string.IsNullOrEmpty(_testOutputRoot) || !Directory.Exists(_testOutputRoot))
                return;

            var zipDir = Path.Combine(_legacyRoot, "auto_zip");
            Directory.CreateDirectory(zipDir);
            var zipName = $"work_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            var zipPath = Path.Combine(zipDir, zipName);

            ZipFile.CreateFromDirectory(_testOutputRoot, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
            _productDb?.CompleteWorkSession(_currentSessionId, "COMPLETED", zipPath, _testOutputRoot);
            Log($"작업 결과 자동 ZIP 저장: {zipName}");
        }
        catch (Exception ex)
        {
            Log($"ZIP 자동 저장 실패: {ex.Message}");
        }
    }

    #endregion
}

#region ═══ 데이터 모델 ═══

public class ProductItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private DateTime? _lastProcessedAt;
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime? LastProcessedAt
    {
        get => _lastProcessedAt;
        set
        {
            if (_lastProcessedAt == value) return;
            _lastProcessedAt = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastProcessedAt)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HistoryText)));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    // 이력이 있으면 "(MM/dd HH:mm)" 형태로 표시, 없으면 ""
    public string HistoryText => LastProcessedAt.HasValue
        ? LastProcessedAt.Value.ToString("(완료 MM/dd HH:mm)")
        : "";

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class PriceRow : INotifyPropertyChanged
{
    private bool _isChecked = true;
    private decimal _additionalAmount;

    public bool IsChecked
    {
        get => _isChecked;
        set { if (_isChecked == value) return; _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
    }
    public string GsCode { get; set; } = "";
    public string OptionName { get; set; } = "";
    public decimal SupplyPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal AdditionalAmount
    {
        get => _additionalAmount;
        set { if (_additionalAmount == value) return; _additionalAmount = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AdditionalAmount))); }
    }
    public decimal ConsumerPrice { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public class UploadResultRow
{
    public string Gs { get; set; } = "";
    public string ProductNo { get; set; } = "";
    public string Status { get; set; } = "";
    public string MainImage { get; set; } = "";
    public string AddCount { get; set; } = "";
    public string PriceStatus { get; set; } = "";
}

public class ImageThumbnailItem : INotifyPropertyChanged
{
    private bool _isMain;
    private bool _isAdditional;
    private bool _isMainB;

    public int Index { get; set; }
    public int DisplayNumber { get; set; }
    public string FilePath { get; set; } = "";
    public BitmapImage? Thumbnail { get; set; }

    public bool IsMain
    {
        get => _isMain;
        set
        {
            if (_isMain == value) return;
            _isMain = value;
            OnPropertyChanged(nameof(IsMain));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }
    }

    public bool IsAdditional
    {
        get => _isAdditional;
        set
        {
            if (_isAdditional == value) return;
            _isAdditional = value;
            OnPropertyChanged(nameof(IsAdditional));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }
    }

    public bool IsMainB
    {
        get => _isMainB;
        set
        {
            if (_isMainB == value) return;
            _isMainB = value;
            OnPropertyChanged(nameof(IsMainB));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }
    }

    public string StatusText => IsMain && IsMainB ? $"#{DisplayNumber} A+B대표"
        : IsMain ? $"#{DisplayNumber} A대표"
        : IsMainB ? $"#{DisplayNumber} B대표"
        : IsAdditional ? $"#{DisplayNumber} 추가"
        : $"#{DisplayNumber}";
    public string StatusColor => IsMain || IsMainB ? "#2196F3" : IsAdditional ? "#4CAF50" : "#888";
    public string StatusTextB => IsMainB ? $"#{DisplayNumber} B대표"
        : IsAdditional ? $"#{DisplayNumber} 추가"
        : $"#{DisplayNumber}";
    public string StatusColorB => IsMainB ? "#FF9800" : IsAdditional ? "#4CAF50" : "#888";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class WorkspacePackageListItem
{
    public string PackagePath { get; init; } = "";
    public string FileName => Path.GetFileName(PackagePath);
    public DateTimeOffset CreatedAt { get; init; }
    public string DisplayTime => CreatedAt == default
        ? File.GetLastWriteTime(PackagePath).ToString("MM/dd HH:mm")
        : CreatedAt.ToString("MM/dd HH:mm");
    public string SourceFileName { get; init; } = "";
    public int ProductCount { get; init; }
    public string RepresentativeResultFile { get; init; } = "";
    public List<string> SelectedCodes { get; init; } = new();
    public string DisplayCodes => FormatDisplayCodes(SelectedCodes);

    public static WorkspacePackageListItem? TryRead(string packagePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var entry = archive.GetEntry("manifest.json");
            if (entry is null)
                return new WorkspacePackageListItem
                {
                    PackagePath = packagePath,
                    CreatedAt = new DateTimeOffset(File.GetLastWriteTime(packagePath)),
                    SelectedCodes = DiscoverCodesFromArchive(archive),
                };

            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var manifest = JsonSerializer.Deserialize<WorkspacePackageManifest>(reader.ReadToEnd(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var selectedCodes = manifest?.SelectedCodes is { Count: > 0 }
                ? manifest.SelectedCodes
                : DiscoverCodesFromArchive(archive);
            return new WorkspacePackageListItem
            {
                PackagePath = packagePath,
                CreatedAt = manifest?.CreatedAt ?? new DateTimeOffset(File.GetLastWriteTime(packagePath)),
                SourceFileName = manifest?.SourceFileName ?? "",
                ProductCount = manifest?.ProductCount ?? 0,
                RepresentativeResultFile = manifest?.RepresentativeResultFile ?? "",
                SelectedCodes = selectedCodes,
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<string> DiscoverCodesFromArchive(ZipArchive archive)
        => archive.Entries
            .SelectMany(entry => Regex.Matches(entry.FullName, @"GS\d{7}[A-Z]?", RegexOptions.IgnoreCase)
                .Cast<Match>()
                .Select(match => match.Value.ToUpperInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();

    private static string FormatDisplayCodes(IReadOnlyList<string> codes)
    {
        if (codes.Count == 0)
            return "-";

        var visible = codes.Take(3).ToList();
        var text = string.Join(", ", visible);
        return codes.Count > visible.Count ? $"{text} 외 {codes.Count - visible.Count}개" : text;
    }
}

public sealed class WorkspaceResumeState
{
    [JsonPropertyName("saved_at")]
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;

    [JsonPropertyName("workspace_root")]
    public string WorkspaceRoot { get; set; } = "";

    [JsonPropertyName("package_path")]
    public string PackagePath { get; set; } = "";

    [JsonPropertyName("source_file_name")]
    public string SourceFileName { get; set; } = "";

    [JsonPropertyName("result_file")]
    public string ResultFile { get; set; } = "";

    [JsonPropertyName("product_count")]
    public int ProductCount { get; set; }

    [JsonPropertyName("selected_codes")]
    public List<string> SelectedCodes { get; set; } = new();

    [JsonPropertyName("image_selections_path")]
    public string ImageSelectionsPath { get; set; } = "";
}

#endregion
