using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using Microsoft.Win32;
using System.Windows.Forms;

// ---------------------------------------------------------------------------
// WebOCR 설치 관리자 (per-user, 관리자 권한 불필요)
//   인자 없음        -> GUI: 상태 감지 후 설치 / 업데이트 / 복구 / 제거 선택
//   --install        -> GUI 진행표시줄로 설치/업데이트
//   --uninstall      -> 제거 (Windows "앱 및 기능"의 제거가 이걸 호출)
//   --silent         -> 확인/완료창 없이 진행 (--install / --uninstall과 함께)
// ---------------------------------------------------------------------------

const string AppName = "WebOCR";
const string Publisher = "leo & ash";
const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WebOCR";

static string LocalAppDataPath(params string[] parts)
{
    var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return Path.Combine(new[] { root }.Concat(parts).ToArray());
}

static string InstallRoot() => LocalAppDataPath("Programs", "WebOCR");
static string DataRoot() => LocalAppDataPath("WebOCR");
static string UninstallerDir() => Path.Combine(DataRoot(), "uninstall");
static string UninstallerPath() => Path.Combine(UninstallerDir(), "WebOCR_Uninstall.exe");
static string LauncherPath() => Path.Combine(InstallRoot(), "WebOCR.exe");
static string KeyMakerPath() => Path.Combine(InstallRoot(), "MarketKeyMaker.exe");

static string DesktopShortcut() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WebOCR.lnk");
static string StartMenuFolder() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WebOCR");
static string LegacyStartMenuShortcut() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WebOCR.lnk");

// 번들(설치본)에 들어있는 버전 = 임베디드 payload.zip 안의 WebOCR/VERSION.txt
static string ReadBundledVersion()
{
    try
    {
        using var zs = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
        if (zs is null) return "0.0.0";
        using var za = new ZipArchive(zs, ZipArchiveMode.Read);
        var entry = za.GetEntry("WebOCR/VERSION.txt");
        if (entry is null) return "0.0.0";
        using var r = new StreamReader(entry.Open());
        return r.ReadToEnd().Trim();
    }
    catch
    {
        return "0.0.0";
    }
}

static string? ReadInstalledVersion()
{
    var p = Path.Combine(InstallRoot(), "VERSION.txt");
    return File.Exists(p) ? File.ReadAllText(p).Trim() : null;
}

// 설치 폴더 안에서 실행 중인 WebOCR / python 프로세스 종료 (파일 잠금 해제)
static void KillRunningInstances()
{
    var installRoot = InstallRoot();
    foreach (var p in Process.GetProcesses())
    {
        try
        {
            var path = p.MainModule?.FileName;
            if (!string.IsNullOrEmpty(path) &&
                path.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
            }
        }
        catch { /* 접근 불가 프로세스 무시 */ }
    }
    try
    {
        var pidFile = Path.Combine(DataRoot(), "webocr.pid");
        if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid))
        {
            try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch { }
        }
    }
    catch { }
}

static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string? arguments = null, string? description = null)
{
    var shellType = Type.GetTypeFromProgID("WScript.Shell");
    if (shellType is null) return;
    dynamic shell = Activator.CreateInstance(shellType)!;
    dynamic sc = shell.CreateShortcut(shortcutPath);
    sc.TargetPath = targetPath;
    sc.WorkingDirectory = workingDir;
    sc.IconLocation = targetPath;
    if (arguments is not null) sc.Arguments = arguments;
    sc.Description = description ?? "WebOCR";
    sc.Save();
}

static void CreateAllShortcuts()
{
    var installRoot = InstallRoot();
    CreateShortcut(DesktopShortcut(), LauncherPath(), installRoot, description: "WebOCR 실행");

    Directory.CreateDirectory(StartMenuFolder());
    CreateShortcut(Path.Combine(StartMenuFolder(), "WebOCR.lnk"), LauncherPath(), installRoot, description: "WebOCR 실행");
    if (File.Exists(KeyMakerPath()))
    {
        CreateShortcut(Path.Combine(StartMenuFolder(), "WebOCR API 키 생성기.lnk"), KeyMakerPath(), installRoot,
            description: "WebOCR API 키 파일 생성");
    }
    CreateShortcut(Path.Combine(StartMenuFolder(), "WebOCR 제거.lnk"), UninstallerPath(), UninstallerDir(),
        arguments: "--uninstall", description: "WebOCR 제거");
}

static void RemoveAllShortcuts()
{
    try { File.Delete(DesktopShortcut()); } catch { }
    try { File.Delete(LegacyStartMenuShortcut()); } catch { }
    try { if (Directory.Exists(StartMenuFolder())) Directory.Delete(StartMenuFolder(), recursive: true); } catch { }
}

static long DirectorySizeKb(string root)
{
    long bytes = 0;
    try
    {
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try { bytes += new FileInfo(f).Length; } catch { }
        }
    }
    catch { }
    return bytes / 1024;
}

static void RegisterUninstall(string version)
{
    var installRoot = InstallRoot();
    using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
    key.SetValue("DisplayName", AppName);
    key.SetValue("DisplayVersion", version);
    key.SetValue("Publisher", Publisher);
    key.SetValue("DisplayIcon", LauncherPath());
    key.SetValue("InstallLocation", installRoot);
    key.SetValue("UninstallString", $"\"{UninstallerPath()}\" --uninstall");
    key.SetValue("QuietUninstallString", $"\"{UninstallerPath()}\" --uninstall --silent");
    key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
    key.SetValue("EstimatedSize", (int)DirectorySizeKb(installRoot), RegistryValueKind.DWord);
    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
}

static void UnregisterUninstall()
{
    try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false); } catch { }
}

// 제거기는 설치 폴더를 통째로 지워야 하므로, 설치 폴더 밖(데이터 폴더)에 자기 자신을 복사해 둔다.
static void InstallUninstaller()
{
    Directory.CreateDirectory(UninstallerDir());
    File.Copy(Environment.ProcessPath!, UninstallerPath(), overwrite: true);
}

// 진행 표시줄과 함께 설치/업데이트. (progress: 0~100, 상태 메시지)
static void DoInstall(IProgress<(int pct, string msg)>? progress)
{
    void Report(int p, string m) => progress?.Report((p, m));

    var installRoot = InstallRoot();
    var dataRoot = DataRoot();
    Directory.CreateDirectory(installRoot);
    Directory.CreateDirectory(dataRoot);

    Report(2, "실행 중인 WebOCR 종료 중...");
    KillRunningInstances();

    var tempZip = Path.Combine(Path.GetTempPath(), "webocr_payload_" + Guid.NewGuid().ToString("N") + ".zip");
    try
    {
        Report(5, "설치 파일 준비 중...");
        using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
                           ?? throw new InvalidOperationException("payload.zip resource not found."))
        using (var output = File.Create(tempZip))
        {
            input.CopyTo(output);
        }

        Report(12, "기존 파일 정리 중...");
        if (Directory.Exists(installRoot))
        {
            foreach (var item in Directory.EnumerateFileSystemEntries(installRoot))
            {
                try
                {
                    if (Directory.Exists(item)) Directory.Delete(item, recursive: true);
                    else File.Delete(item);
                }
                catch { }
            }
        }

        // payload.zip 의 엔트리는 "WebOCR/..." 접두사. 접두사를 떼고 installRoot 로 직접 추출.
        using (var za = ZipFile.OpenRead(tempZip))
        {
            var entries = za.Entries.Where(e => e.FullName.StartsWith("WebOCR/", StringComparison.Ordinal)).ToList();
            int total = Math.Max(1, entries.Count);
            int done = 0;
            foreach (var e in entries)
            {
                var rel = e.FullName.Substring("WebOCR/".Length);
                if (rel.Length == 0) { done++; continue; }
                var target = Path.Combine(installRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (e.FullName.EndsWith("/", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(target);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    e.ExtractToFile(target, overwrite: true);
                }
                done++;
                if ((done & 0x3F) == 0 || done == total)
                {
                    int pct = 15 + (int)(75.0 * done / total);
                    Report(pct, $"파일 설치 중... ({done:N0}/{total:N0})");
                }
            }
        }

        Report(92, "바로가기 생성 중...");
        var version = ReadInstalledVersion() ?? ReadBundledVersion();
        InstallUninstaller();
        CreateAllShortcuts();

        Report(96, "Windows 등록 중...");
        RegisterUninstall(version);

        Report(100, "완료");
    }
    finally
    {
        try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
    }
}

static void DoUninstall(bool deleteData)
{
    KillRunningInstances();
    RemoveAllShortcuts();

    try { if (Directory.Exists(InstallRoot())) Directory.Delete(InstallRoot(), recursive: true); } catch { }
    UnregisterUninstall();

    // 실행 중인 제거기 자신(과 옵션에 따라 데이터 폴더)은 분리된 cmd 로 지연 삭제
    var targets = new List<string> { deleteData ? DataRoot() : UninstallerDir() };
    var sb = new StringBuilder("/c timeout /t 2 /nobreak >nul");
    foreach (var t in targets) sb.Append($" & rmdir /s /q \"{t}\"");
    try
    {
        Process.Start(new ProcessStartInfo("cmd.exe", sb.ToString())
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
    }
    catch { }
}

static void ShowError(Exception ex, string phase)
{
    var logRoot = LocalAppDataPath("WebOCR", "logs");
    Directory.CreateDirectory(logRoot);
    var logPath = Path.Combine(logRoot, "setup_error.log");
    File.WriteAllText(logPath, $"[{phase}]\n{ex}");
    MessageBox.Show($"WebOCR {phase} 중 오류가 발생했습니다.\n\n{logPath}", "WebOCR Setup",
        MessageBoxButtons.OK, MessageBoxIcon.Error);
    try { Process.Start(new ProcessStartInfo("notepad.exe", logPath) { UseShellExecute = true }); } catch { }
}

// 진행 표시줄을 띄우고 백그라운드에서 설치 실행. 오류는 throw.
static void RunInstallWithProgress(string titleVerb)
{
    using var form = new ProgressForm($"WebOCR {titleVerb}", DoInstall);
    form.ShowDialog();
    if (form.Error is not null) throw form.Error;
}

// ---------------------------------------------------------------------------
// 진입점
// ---------------------------------------------------------------------------
var argList = args.Select(a => a.ToLowerInvariant()).ToList();
bool silent = argList.Contains("--silent");

// 설치관리자 중복 실행 방지
using var mutex = new System.Threading.Mutex(true, "WebOCR_Setup_SingleInstance", out bool isNew);
if (!isNew)
{
    if (!silent)
    {
        MessageBox.Show("WebOCR 설치 관리자가 이미 실행 중입니다.", "WebOCR Setup",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    return;
}

ApplicationConfiguration.Initialize();

try
{
    if (argList.Contains("--uninstall"))
    {
        bool deleteData = false;
        if (!silent)
        {
            using var dlg = new UninstallDialog();
            if (dlg.ShowDialog() != DialogResult.OK) return;
            deleteData = dlg.DeleteData;
        }
        DoUninstall(deleteData);
        if (!silent)
        {
            MessageBox.Show("WebOCR 제거가 완료되었습니다.", "WebOCR Setup",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        return;
    }

    if (argList.Contains("--install"))
    {
        if (silent) DoInstall(null);
        else RunInstallWithProgress("설치");
        if (!silent)
        {
            MessageBox.Show("WebOCR 설치가 완료되었습니다.", "WebOCR Setup",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        return;
    }

    // 인자 없음 -> 상태 감지 GUI
    using var main = new SetupDialog(ReadBundledVersion(), ReadInstalledVersion());
    if (main.ShowDialog() != DialogResult.OK) return;

    if (main.Action == SetupAction.Uninstall)
    {
        using var dlg = new UninstallDialog();
        if (dlg.ShowDialog() != DialogResult.OK) return;
        DoUninstall(dlg.DeleteData);
        MessageBox.Show("WebOCR 제거가 완료되었습니다.", "WebOCR Setup",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
    }

    var verb = main.Action switch
    {
        SetupAction.Update => "업데이트",
        SetupAction.Repair => "복구",
        _ => "설치",
    };
    RunInstallWithProgress(verb);

    var run = MessageBox.Show($"WebOCR {verb}가 완료되었습니다.\n\n지금 실행할까요?", "WebOCR Setup",
        MessageBoxButtons.YesNo, MessageBoxIcon.Information);
    if (run == DialogResult.Yes)
    {
        try { Process.Start(new ProcessStartInfo(LauncherPath()) { UseShellExecute = true, WorkingDirectory = InstallRoot() }); } catch { }
    }
}
catch (Exception ex)
{
    ShowError(ex, "설치/제거");
}

// ---------------------------------------------------------------------------
// UI
// ---------------------------------------------------------------------------
enum SetupAction { Install, Update, Repair, Uninstall }

static class VersionUtil
{
    public static Version Parse(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text) && Version.TryParse(text.Trim(), out var v))
        {
            return v;
        }
        return new Version(0, 0);
    }
}

class SetupDialog : Form
{
    public SetupAction Action { get; private set; } = SetupAction.Install;

    public SetupDialog(string bundled, string? installed)
    {
        Text = $"WebOCR 설치 관리자 (v{bundled})";
        Icon = Program_AppIcon.Value;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(430, 205);
        Font = new Font("Segoe UI", 9F);

        Controls.Add(new Label
        {
            Text = "WebOCR",
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            Location = new Point(20, 18),
            AutoSize = true,
        });

        var bv = VersionUtil.Parse(bundled);
        var iv = installed is null ? null : (Version?)VersionUtil.Parse(installed);

        string status;
        var buttons = new List<(string text, SetupAction action)>();
        if (iv is null)
        {
            status = $"이 컴퓨터에 설치되어 있지 않습니다.\n설치할 버전: v{bundled}";
            buttons.Add(("설치", SetupAction.Install));
        }
        else if (bv > iv)
        {
            status = $"설치됨: v{installed}\n새 버전으로 업데이트: v{bundled}";
            buttons.Add(("업데이트", SetupAction.Update));
            buttons.Add(("제거", SetupAction.Uninstall));
        }
        else if (bv == iv)
        {
            status = $"이미 최신 버전(v{installed})이 설치되어 있습니다.";
            buttons.Add(("복구(재설치)", SetupAction.Repair));
            buttons.Add(("제거", SetupAction.Uninstall));
        }
        else
        {
            status = $"설치됨: v{installed}\n번들 버전(v{bundled})으로 재설치(다운그레이드)";
            buttons.Add(("재설치", SetupAction.Repair));
            buttons.Add(("제거", SetupAction.Uninstall));
        }

        Controls.Add(new Label
        {
            Text = status,
            Location = new Point(22, 62),
            Size = new Size(386, 70),
        });

        int x = 430 - 20 - 110;
        var cancel = new Button { Text = "취소", Size = new Size(100, 32), Location = new Point(x, 155) };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(cancel);
        CancelButton = cancel;
        x -= 110;

        bool primary = true;
        foreach (var (text, action) in buttons)
        {
            var btn = new Button { Text = text, Size = new Size(100, 32), Location = new Point(x, 155) };
            var captured = action;
            btn.Click += (_, _) => { Action = captured; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(btn);
            if (primary) { AcceptButton = btn; primary = false; }
            x -= 110;
        }
    }
}

class UninstallDialog : Form
{
    private readonly CheckBox _chk;
    public bool DeleteData => _chk.Checked;

    public UninstallDialog()
    {
        Text = "WebOCR 제거";
        Icon = Program_AppIcon.Value;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(430, 180);
        Font = new Font("Segoe UI", 9F);

        Controls.Add(new Label
        {
            Text = "WebOCR 를 제거합니다.\n실행 중인 WebOCR 은 자동으로 종료됩니다.",
            Location = new Point(22, 22),
            Size = new Size(386, 50),
        });

        _chk = new CheckBox
        {
            Text = "사용자 데이터(키 · 카테고리DB · 업로드/내보내기)도 모두 삭제",
            Location = new Point(22, 76),
            Size = new Size(390, 40),
            Checked = false,
        };
        Controls.Add(_chk);

        var ok = new Button { Text = "제거", Size = new Size(100, 32), Location = new Point(200, 132) };
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        Controls.Add(ok);
        AcceptButton = ok;

        var cancel = new Button { Text = "취소", Size = new Size(100, 32), Location = new Point(310, 132) };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(cancel);
        CancelButton = cancel;
    }
}

class ProgressForm : Form
{
    private readonly ProgressBar _bar;
    private readonly Label _lbl;
    private readonly Action<IProgress<(int, string)>> _work;
    public Exception? Error { get; private set; }

    public ProgressForm(string title, Action<IProgress<(int, string)>> work)
    {
        _work = work;
        Text = title;
        Icon = Program_AppIcon.Value;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ClientSize = new Size(430, 120);
        Font = new Font("Segoe UI", 9F);

        _lbl = new Label
        {
            Text = "준비 중...",
            Location = new Point(22, 22),
            Size = new Size(386, 40),
        };
        Controls.Add(_lbl);

        _bar = new ProgressBar
        {
            Location = new Point(22, 70),
            Size = new Size(386, 24),
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
        };
        Controls.Add(_bar);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var progress = new Progress<(int pct, string msg)>(t =>
        {
            _bar.Value = Math.Min(100, Math.Max(0, t.pct));
            _lbl.Text = t.msg;
        });
        Task.Run(() =>
        {
            try { _work(progress); }
            catch (Exception ex) { Error = ex; }
            finally { BeginInvoke(() => { DialogResult = DialogResult.OK; Close(); }); }
        });
    }
}

static class Program_AppIcon
{
    public static readonly Icon? Value = TryLoad();
    private static Icon? TryLoad()
    {
        try { return Icon.ExtractAssociatedIcon(Environment.ProcessPath!); }
        catch { return null; }
    }
}
