using System.Diagnostics;
using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Text;

static string LocalAppDataPath(params string[] parts)
{
    var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return Path.Combine(new[] { root }.Concat(parts).ToArray());
}

static int FindPort(int start = 5556, int end = 5576)
{
    for (var port = start; port <= end; port++)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Parse("127.0.0.1"), port);
            listener.Start();
            return port;
        }
        catch
        {
            // Try the next port.
        }
        finally
        {
            listener?.Stop();
        }
    }

    throw new InvalidOperationException($"No available localhost port found in range {start}-{end}.");
}

static void CopyMissingTree(string sourceRoot, string targetRoot)
{
    if (!Directory.Exists(sourceRoot))
    {
        return;
    }

    foreach (var sourcePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
    {
        var rel = Path.GetRelativePath(sourceRoot, sourcePath);
        var targetPath = Path.Combine(targetRoot, rel);
        if (File.Exists(targetPath))
        {
            continue;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath, overwrite: false);
    }
}

static void ShowError(string message)
{
    var logRoot = LocalAppDataPath("WebOCR", "logs");
    Directory.CreateDirectory(logRoot);
    var logPath = Path.Combine(logRoot, "launcher_error.log");
    File.WriteAllText(logPath, message, Encoding.UTF8);
    Process.Start(new ProcessStartInfo("notepad.exe", logPath) { UseShellExecute = true });
}

static async Task<bool> WaitForServerAsync(int port, int timeoutMs = 15000)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            using var response = await http.GetAsync($"http://127.0.0.1:{port}/api/pm/status");
            if (response.IsSuccessStatusCode)
            {
                return true;
            }
        }
        catch
        {
            // Server is still starting.
        }
        await Task.Delay(300);
    }
    return false;
}

try
{
    var installRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var webRoot = Path.Combine(installRoot, "webocrcludev2");
    var scriptPath = Path.Combine(webRoot, "scripts", "local_api_server.py");
    var pythonw = Path.Combine(installRoot, "runtime", "python", "pythonw.exe");
    var python = File.Exists(pythonw) ? pythonw : Path.Combine(installRoot, "runtime", "python", "python.exe");

    if (!File.Exists(python))
    {
        throw new FileNotFoundException("Bundled Python runtime was not found.", python);
    }
    if (!File.Exists(scriptPath))
    {
        throw new FileNotFoundException("WebOCR local server script was not found.", scriptPath);
    }

    var dataRoot = LocalAppDataPath("WebOCR");
    var keyRoot = Path.Combine(dataRoot, "key");
    var productManagerRoot = Path.Combine(dataRoot, "ProductManager");
    foreach (var dir in new[]
             {
                 dataRoot,
                 keyRoot,
                 productManagerRoot,
                 Path.Combine(webRoot, "data", "uploads"),
                 Path.Combine(webRoot, "data", "exports"),
                 Path.Combine(webRoot, "data", "jobs"),
                 Path.Combine(webRoot, "data", "seeds"),
                 Path.Combine(webRoot, "data", "logos"),
                 Path.Combine(webRoot, "data", "emergency"),
                 Path.Combine(webRoot, "data", "market_keys"),
             })
    {
        Directory.CreateDirectory(dir);
    }

    CopyMissingTree(Path.Combine(installRoot, "defaults", "key"), keyRoot);
    CopyMissingTree(Path.Combine(installRoot, "ProductManager"), productManagerRoot);

    var port = FindPort();
    Directory.CreateDirectory(Path.Combine(dataRoot, "logs"));

    var start = new ProcessStartInfo
    {
        FileName = python,
        WorkingDirectory = webRoot,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    start.ArgumentList.Add(scriptPath);
    start.ArgumentList.Add("--port");
    start.ArgumentList.Add(port.ToString());
    start.ArgumentList.Add("--host");
    start.ArgumentList.Add("127.0.0.1");
    start.Environment["WEBOCR_KEY_ROOT"] = keyRoot;
    start.Environment["KEYWORDOCR_KEY_DIR"] = keyRoot;
    start.Environment["WEBOCR_ORIGINAL_KEY_ROOT"] = keyRoot;
    start.Environment["WEBOCR_PRODUCT_MANAGER_ROOT"] = productManagerRoot;
    start.Environment["PYTHONUTF8"] = "1";
    start.Environment["PYTHONIOENCODING"] = "utf-8";
    start.Environment["PYTHONNOUSERSITE"] = "1";

    var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start WebOCR local server.");
    File.WriteAllText(Path.Combine(dataRoot, "webocr.pid"), process.Id.ToString(), Encoding.ASCII);
    WaitForServerAsync(port).GetAwaiter().GetResult();
    Process.Start(new ProcessStartInfo($"http://localhost:{port}/index.html") { UseShellExecute = true });
}
catch (Exception ex)
{
    ShowError(ex.ToString());
}
