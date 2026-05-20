using System;
using System.IO;

namespace KeywordOcr.App.Services;

internal static class DesktopKeyStore
{
    public static string DirectoryPath =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KEYWORDOCR_KEY_DIR"))
            ? Environment.GetEnvironmentVariable("KEYWORDOCR_KEY_DIR")!
            : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBOCR_KEY_ROOT"))
                ? Environment.GetEnvironmentVariable("WEBOCR_KEY_ROOT")!
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop", "key");

    public static string GetPath(string fileName) => Path.Combine(DirectoryPath, fileName);
}
