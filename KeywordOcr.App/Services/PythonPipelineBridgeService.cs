using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KeywordOcr.App.Services;

public sealed record PythonPipelineBridgeResult(string OutputRoot, string OutputFile);
public sealed record PythonFreeKeywordV4Result(string AnalysisFile, string KeywordResultFile);

public sealed class PythonPipelineBridgeService
{
    private readonly string _workspaceRoot;
    private readonly string _legacyRoot;

    public PythonPipelineBridgeService(string workspaceRoot, string legacyRoot)
    {
        _workspaceRoot = workspaceRoot;
        _legacyRoot = legacyRoot;
    }

    public async Task<PythonPipelineBridgeResult> RunPipelineAsync(
        string sourcePath,
        ListingImageSettings listingSettings,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        string phase = "full",
        string exportRoot = "",
        string model = "",
        int chunkSize = 10,
        string keywordVersion = "3.0")
    {
        var scriptPath = ResolveScriptPath();

        var psi = new ProcessStartInfo
        {
            FileName = "py",
            WorkingDirectory = _legacyRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-3");
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("--legacy-root");
        psi.ArgumentList.Add(_legacyRoot);
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(sourcePath);
        AddArg(psi, "--make-listing", ToBoolText(listingSettings.MakeListing));
        AddArg(psi, "--listing-size", listingSettings.ListingSize.ToString());
        AddArg(psi, "--listing-pad", listingSettings.ListingPad.ToString());
        AddArg(psi, "--listing-max", listingSettings.ListingMax.ToString());
        AddArg(psi, "--logo-path", listingSettings.LogoPath ?? string.Empty);
        AddArg(psi, "--logo-ratio", listingSettings.LogoRatio.ToString());
        AddArg(psi, "--logo-opacity", listingSettings.LogoOpacity.ToString());
        AddArg(psi, "--logo-pos", listingSettings.LogoPosition ?? "tr");
        AddArg(psi, "--use-auto-contrast", ToBoolText(listingSettings.UseAutoContrast));
        AddArg(psi, "--use-sharpen", ToBoolText(listingSettings.UseSharpen));
        AddArg(psi, "--use-small-rotate", ToBoolText(listingSettings.UseSmallRotate));
        AddArg(psi, "--rotate-zoom", listingSettings.RotateZoom.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArg(psi, "--ultra-angle-deg", listingSettings.UltraAngleDeg.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArg(psi, "--ultra-translate-px", listingSettings.UltraTranslatePx.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArg(psi, "--ultra-scale-pct", listingSettings.UltraScalePct.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArg(psi, "--trim-tol", listingSettings.TrimTolerance.ToString());
        AddArg(psi, "--jpeg-q-min", listingSettings.JpegQualityMin.ToString());
        AddArg(psi, "--jpeg-q-max", listingSettings.JpegQualityMax.ToString());
        AddArg(psi, "--flip-lr", ToBoolText(listingSettings.FlipLeftRight));
        if (!string.IsNullOrEmpty(listingSettings.LogoPathB))
            AddArg(psi, "--logo-path-b", listingSettings.LogoPathB);
        AddArg(psi, "--img-tag", listingSettings.ImgTag ?? string.Empty);
        if (!string.IsNullOrEmpty(listingSettings.ImgTagB))
            AddArg(psi, "--img-tag-b", listingSettings.ImgTagB);
        AddArg(psi, "--a-name-min", listingSettings.ANameMin.ToString());
        AddArg(psi, "--a-name-max", listingSettings.ANameMax.ToString());
        AddArg(psi, "--b-name-min", listingSettings.BNameMin.ToString());
        AddArg(psi, "--b-name-max", listingSettings.BNameMax.ToString());
        AddArg(psi, "--a-tag-count", listingSettings.ATagCount.ToString());
        AddArg(psi, "--b-tag-count", listingSettings.BTagCount.ToString());
        AddArg(psi, "--phase", phase);
        if (!string.IsNullOrEmpty(exportRoot))
            AddArg(psi, "--export-root", exportRoot);
        if (!string.IsNullOrEmpty(model))
            AddArg(psi, "--model", model);
        AddArg(psi, "--chunk-size", chunkSize.ToString());
        AddArg(psi, "--keyword-version", string.IsNullOrWhiteSpace(keywordVersion) ? "3.0" : keywordVersion.Trim());
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var outputDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorBuffer = new StringBuilder();
        PythonPipelineBridgeResult? result = null;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                outputDone.TrySetResult(true);
                return;
            }

            if (e.Data.StartsWith("__RESULT__", StringComparison.Ordinal))
            {
                var payload = e.Data[10..];
                var parsed = JsonSerializer.Deserialize<ResultPayload>(payload);
                if (parsed is not null)
                {
                    result = new PythonPipelineBridgeResult(
                        parsed.output_root ?? string.Empty,
                        parsed.output_file ?? string.Empty);
                }
                return;
            }

            progress?.Report(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                errorDone.TrySetResult(true);
                return;
            }

            errorBuffer.AppendLine(e.Data);
            progress?.Report(e.Data);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Python 프로세스를 시작하지 못했습니다.");
        }

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
                    ? "Python 파이프라인 실행에 실패했습니다."
                    : detail);
        }

        // phase=images 이면 OutputFile이 빈 문자열이므로 OutputRoot만 체크
        if (result is null || string.IsNullOrWhiteSpace(result.OutputRoot))
        {
            throw new InvalidDataException("Python 파이프라인 결과를 읽지 못했습니다.");
        }

        return result;
    }

    public async Task<PythonFreeKeywordV4Result> RunFreeKeywordV4Async(
        string imageRoot,
        string uploadFile,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageRoot))
            throw new ArgumentException("이미지 루트가 비어 있습니다.", nameof(imageRoot));
        if (string.IsNullOrWhiteSpace(uploadFile) || !File.Exists(uploadFile))
            throw new FileNotFoundException("V5 키워드 생성용 업로드 엑셀을 찾지 못했습니다.", uploadFile);

        var psi = new ProcessStartInfo
        {
            FileName = "py",
            WorkingDirectory = _legacyRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-3");
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add("app.services.free_keyword_analysis");
        AddArg(psi, "--image-root", imageRoot);
        AddArg(psi, "--upload-file", uploadFile);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var outputDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorBuffer = new StringBuilder();
        FreeKeywordPayload? payload = null;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                outputDone.TrySetResult(true);
                return;
            }

            var line = e.Data.Trim();
            if (line.StartsWith("{", StringComparison.Ordinal))
            {
                try { payload = JsonSerializer.Deserialize<FreeKeywordPayload>(line); }
                catch { progress?.Report(line); }
                return;
            }

            progress?.Report(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                errorDone.TrySetResult(true);
                return;
            }

            errorBuffer.AppendLine(e.Data);
            progress?.Report(e.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException("Python V5 키워드 프로세스를 시작하지 못했습니다.");

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
                    ? "V4 로컬 키워드 생성에 실패했습니다."
                    : detail);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.output_file))
            throw new InvalidDataException("V4 로컬 키워드 결과를 읽지 못했습니다.");

        return new PythonFreeKeywordV4Result(
            payload.output_file ?? string.Empty,
            payload.keyword_result_file ?? string.Empty);
    }

    private static void AddArg(ProcessStartInfo psi, string name, string value)
    {
        psi.ArgumentList.Add(name);
        psi.ArgumentList.Add(value);
    }

    private static string ToBoolText(bool value) => value ? "true" : "false";

    private string ResolveScriptPath()
    {
        var candidates = new[]
        {
            // v3 Bridge
            Path.Combine(_workspaceRoot, "KeywordOcr.App", "Bridge", "run_pipeline_bridge.py"),
            Path.Combine(AppContext.BaseDirectory, "Bridge", "run_pipeline_bridge.py"),
            // v2 Bridge (fallback)
            Path.Combine(_legacyRoot, "keywordocr-v2", "KeywordOcrV2.App", "Bridge", "run_pipeline_bridge.py"),
            // v3 in legacy root
            Path.Combine(_legacyRoot, "keywordocr-v3", "KeywordOcr.App", "Bridge", "run_pipeline_bridge.py"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("파이프라인 브리지 스크립트를 찾지 못했습니다.", candidates[0]);
    }

    private sealed class ResultPayload
    {
        public string? output_root { get; set; }
        public string? output_file { get; set; }
    }

    private sealed class FreeKeywordPayload
    {
        public string? output_file { get; set; }
        public string? keyword_result_file { get; set; }
    }
}
