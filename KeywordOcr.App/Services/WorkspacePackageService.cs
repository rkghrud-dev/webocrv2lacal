using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KeywordOcr.App.Services;

public sealed class WorkspacePackageManifest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("app")]
    public string App { get; set; } = "KeywordOCR";

    [JsonPropertyName("program_version")]
    public string ProgramVersion { get; set; } = "v4";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [JsonPropertyName("source_file_name")]
    public string SourceFileName { get; set; } = "";

    [JsonPropertyName("product_count")]
    public int ProductCount { get; set; }

    [JsonPropertyName("selected_codes")]
    public List<string> SelectedCodes { get; set; } = new();

    [JsonPropertyName("representative_result_file")]
    public string RepresentativeResultFile { get; set; } = "";

    [JsonPropertyName("upload_workbook")]
    public string UploadWorkbook { get; set; } = "";

    [JsonPropertyName("latest_v4_result_file")]
    public string LatestV4ResultFile { get; set; } = "";

    [JsonPropertyName("image_selections")]
    public string ImageSelections { get; set; } = "";

    [JsonPropertyName("workspace_folder_name")]
    public string WorkspaceFolderName { get; set; } = "";

    [JsonPropertyName("included_file_count")]
    public int IncludedFileCount { get; set; }

    [JsonPropertyName("excluded_file_count")]
    public int ExcludedFileCount { get; set; }
}

public sealed record WorkspacePackageSaveResult(
    string PackagePath,
    WorkspacePackageManifest Manifest,
    int IncludedFileCount,
    int ExcludedFileCount);

public sealed record WorkspacePackageRestoreResult(
    string RestoredFolder,
    WorkspacePackageManifest Manifest,
    string? UploadWorkbookPath,
    string? LatestV4ResultPath,
    string? ImageSelectionsPath);

public static class WorkspacePackageService
{
    private const string ManifestEntryName = "manifest.json";
    private const string ReadmeEntryName = "README.txt";
    private const string WorkspacePrefix = "workspace/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] SensitiveNameFragments =
    {
        "token",
        "cookie",
        "secret",
        "password",
        "credential",
        "apikey",
        "api_key",
        "anthropic_api_key",
        "client_secret",
        "access_token",
        "refresh_token",
    };

    private static readonly HashSet<string> SensitiveExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env",
        "app_settings.json",
        "cafe24_upload_config.txt",
        "cafe24_token.txt",
        "cafe24_token.json",
        "cafe24_token_jb.json",
        "anthropic_api_key.txt",
    };

    public static string BuildDefaultPackageFileName(int productCount, DateTimeOffset? now = null)
    {
        var stamp = (now ?? DateTimeOffset.Now).ToString("yyyyMMdd_HHmmss");
        var countPart = productCount > 0 ? $"_{productCount}개" : "";
        return $"KeywordOCRV4_작업보관_{stamp}{countPart}.zip";
    }

    public static WorkspacePackageSaveResult CreatePackage(
        string workspaceRoot,
        string packagePath,
        string? sourceFilePath = null,
        string? representativeResultFile = null,
        int productCount = 0,
        string programVersion = "v4",
        IEnumerable<string>? selectedCodes = null)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            throw new DirectoryNotFoundException($"작업 폴더를 찾을 수 없습니다: {workspaceRoot}");
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("저장할 ZIP 파일 경로가 비어 있습니다.", nameof(packagePath));

        var rootFullPath = Path.GetFullPath(workspaceRoot);
        var packageFullPath = Path.GetFullPath(packagePath);
        var packageDir = Path.GetDirectoryName(packageFullPath);
        if (!string.IsNullOrWhiteSpace(packageDir))
            Directory.CreateDirectory(packageDir);
        if (File.Exists(packageFullPath))
            File.Delete(packageFullPath);

        var uploadWorkbook = FindLatestUploadWorkbook(rootFullPath);
        var latestV4Result = FindLatestV4Result(rootFullPath);
        var imageSelections = FindRelativeFile(rootFullPath, "image_selections.json");
        var representative = ResolveRepresentativeResult(rootFullPath, representativeResultFile, latestV4Result, uploadWorkbook);

        var files = Directory.EnumerateFiles(rootFullPath, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(rootFullPath, path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var included = new List<string>();
        var excluded = 0;
        foreach (var file in files)
        {
            if (string.Equals(Path.GetFullPath(file), packageFullPath, StringComparison.OrdinalIgnoreCase)
                || ShouldExclude(file, rootFullPath))
            {
                excluded++;
                continue;
            }
            included.Add(file);
        }

        var manifest = new WorkspacePackageManifest
        {
            App = "KeywordOCR",
            ProgramVersion = programVersion,
            SourceFileName = string.IsNullOrWhiteSpace(sourceFilePath) ? "" : Path.GetFileName(sourceFilePath),
            ProductCount = productCount,
            SelectedCodes = NormalizeSelectedCodes(selectedCodes),
            RepresentativeResultFile = ToRelativeZipPath(rootFullPath, representative),
            UploadWorkbook = ToRelativeZipPath(rootFullPath, uploadWorkbook),
            LatestV4ResultFile = ToRelativeZipPath(rootFullPath, latestV4Result),
            ImageSelections = ToRelativeZipPath(rootFullPath, imageSelections),
            WorkspaceFolderName = Path.GetFileName(rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            IncludedFileCount = included.Count,
            ExcludedFileCount = excluded,
        };

        using var archive = ZipFile.Open(packageFullPath, ZipArchiveMode.Create, Encoding.UTF8);
        WriteTextEntry(archive, ManifestEntryName, JsonSerializer.Serialize(manifest, JsonOptions));
        WriteTextEntry(archive, ReadmeEntryName, BuildReadme(manifest));

        foreach (var file in included)
        {
            var rel = Path.GetRelativePath(rootFullPath, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, WorkspacePrefix + rel, CompressionLevel.Optimal);
        }

        return new WorkspacePackageSaveResult(packageFullPath, manifest, included.Count, excluded);
    }

    public static WorkspacePackageRestoreResult RestorePackage(string packagePath, string exportRoot)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            throw new FileNotFoundException("작업 패키지 ZIP 파일을 찾을 수 없습니다.", packagePath);
        if (string.IsNullOrWhiteSpace(exportRoot))
            throw new ArgumentException("복원할 EXPORT 경로가 비어 있습니다.", nameof(exportRoot));

        Directory.CreateDirectory(exportRoot);
        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = archive.GetEntry(ManifestEntryName)
            ?? throw new InvalidDataException("작업 패키지에 manifest.json이 없습니다.");

        WorkspacePackageManifest manifest;
        using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            var json = reader.ReadToEnd();
            manifest = JsonSerializer.Deserialize<WorkspacePackageManifest>(json, JsonOptions)
                ?? throw new InvalidDataException("manifest.json을 읽을 수 없습니다.");
        }

        if (!archive.Entries.Any(entry => entry.FullName.StartsWith(WorkspacePrefix, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("작업 패키지에 workspace 폴더가 없습니다.");

        var targetName = "복원된_" + SanitizeFileName(
            string.IsNullOrWhiteSpace(manifest.WorkspaceFolderName)
                ? Path.GetFileNameWithoutExtension(packagePath)
                : manifest.WorkspaceFolderName);
        var targetRoot = GetUniqueDirectory(Path.Combine(exportRoot, targetName));
        Directory.CreateDirectory(targetRoot);
        var targetRootFull = Path.GetFullPath(targetRoot);

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(WorkspacePrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = entry.FullName[WorkspacePrefix.Length..];
            if (string.IsNullOrWhiteSpace(relative))
                continue;

            var destination = Path.GetFullPath(Path.Combine(
                targetRootFull,
                relative.Replace('/', Path.DirectorySeparatorChar)));

            if (!IsPathInside(destination, targetRootFull))
                throw new InvalidDataException($"안전하지 않은 ZIP 경로가 포함되어 있습니다: {entry.FullName}");

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            var dir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            entry.ExtractToFile(destination, overwrite: false);
        }

        var uploadWorkbook = ResolveManifestFile(targetRootFull, manifest.UploadWorkbook)
            ?? FindLatestUploadWorkbook(targetRootFull);
        var latestV4Result = ResolveManifestFile(targetRootFull, manifest.LatestV4ResultFile)
            ?? FindLatestV4Result(targetRootFull);
        var imageSelections = ResolveManifestFile(targetRootFull, manifest.ImageSelections)
            ?? FindImageSelections(targetRootFull);

        return new WorkspacePackageRestoreResult(
            targetRootFull,
            manifest,
            uploadWorkbook,
            latestV4Result,
            imageSelections);
    }

    public static string? FindLatestUploadWorkbook(string workspaceRoot)
        => FindLatestFile(workspaceRoot, "업로드용_*.xlsx");

    public static string? FindLatestV4Result(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            return null;

        return Directory.EnumerateFiles(workspaceRoot, "*.xlsx", SearchOption.AllDirectories)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return !name.StartsWith("~$", StringComparison.Ordinal)
                       && (name.Contains("_llm_v5_cli", StringComparison.OrdinalIgnoreCase)
                           || name.Contains("_llm_v4_cli", StringComparison.OrdinalIgnoreCase)
                           || name.Contains("_llm_v4_local", StringComparison.OrdinalIgnoreCase));
            })
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string? FindLatestFile(string workspaceRoot, string pattern)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            return null;

        return Directory.EnumerateFiles(workspaceRoot, pattern, SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public static string? FindImageSelections(string workspaceRoot)
        => FindRelativeFile(workspaceRoot, "image_selections.json");

    private static string? FindRelativeFile(string workspaceRoot, string fileName)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            return null;

        return Directory.EnumerateFiles(workspaceRoot, fileName, SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string? ResolveRepresentativeResult(
        string workspaceRoot,
        string? representativeResultFile,
        string? latestV4Result,
        string? uploadWorkbook)
    {
        if (!string.IsNullOrWhiteSpace(representativeResultFile)
            && File.Exists(representativeResultFile)
            && IsPathInside(Path.GetFullPath(representativeResultFile), workspaceRoot))
        {
            return representativeResultFile;
        }

        return latestV4Result ?? uploadWorkbook;
    }

    private static string ToRelativeZipPath(string rootFullPath, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return "";
        var full = Path.GetFullPath(filePath);
        return IsPathInside(full, rootFullPath)
            ? Path.GetRelativePath(rootFullPath, full).Replace('\\', '/')
            : "";
    }

    private static string? ResolveManifestFile(string rootFullPath, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var destination = Path.GetFullPath(Path.Combine(
            rootFullPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

        return IsPathInside(destination, rootFullPath) && File.Exists(destination)
            ? destination
            : null;
    }

    private static bool ShouldExclude(string filePath, string rootFullPath)
    {
        var name = Path.GetFileName(filePath);
        if (name.StartsWith("~$", StringComparison.Ordinal))
            return true;
        if (SensitiveExactNames.Contains(name))
            return true;

        var relative = Path.GetRelativePath(rootFullPath, filePath).Replace('\\', '/');
        if (relative.Split('/').Any(part =>
                string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase)
                || string.Equals(part, "__pycache__", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var lowerName = name.ToLowerInvariant();
        var lowerRelative = relative.ToLowerInvariant();
        return SensitiveNameFragments.Any(fragment =>
            lowerName.Contains(fragment, StringComparison.Ordinal)
            || lowerRelative.Contains(fragment, StringComparison.Ordinal));
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.Write(content);
    }

    private static string BuildReadme(WorkspacePackageManifest manifest)
        => $"""
           KeywordOCR V5 작업 패키지

           생성일: {manifest.CreatedAt:yyyy-MM-dd HH:mm:ss zzz}
           원본 CSV/Excel: {manifest.SourceFileName}
           상품 수: {manifest.ProductCount}
           상품코드: {FormatCodesForReadme(manifest.SelectedCodes)}
           대표 결과 파일: {manifest.RepresentativeResultFile}

           사용 방법:
           1. KeywordOCR V5에서 '작업 패키지 불러오기'를 선택합니다.
           2. 이 ZIP 파일을 선택하면 Desktop\EXPORT 아래에 작업 폴더가 복원됩니다.
           3. 복원 후 업로드용 엑셀, 이미지 선택, V5 결과 파일은 프로그램이 자동으로 연결합니다.

           보안:
           API 키, Cafe24 토큰, 로그인 쿠키, 개인 설정 파일, 임시 잠금 파일은 패키지에서 제외됩니다.
           """;

    private static List<string> NormalizeSelectedCodes(IEnumerable<string>? selectedCodes)
    {
        if (selectedCodes is null)
            return new List<string>();

        return selectedCodes
            .Select(code => (code ?? "").Trim().ToUpperInvariant())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatCodesForReadme(IReadOnlyCollection<string>? codes)
        => codes is { Count: > 0 } ? string.Join(", ", codes) : "-";

    private static string SanitizeFileName(string value)
    {
        var invalid = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        var sanitized = Regex.Replace(value, $"[{invalid}]+", "_").Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "KeywordOCRV4" : sanitized;
    }

    private static string GetUniqueDirectory(string path)
    {
        if (!Directory.Exists(path))
            return path;

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{path}_{i}";
            if (!Directory.Exists(candidate))
                return candidate;
        }

        return $"{path}_{DateTimeOffset.Now:yyyyMMdd_HHmmss}";
    }

    private static bool IsPathInside(string fullPath, string rootFullPath)
    {
        var root = rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var path = fullPath;
        if (string.Equals(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
