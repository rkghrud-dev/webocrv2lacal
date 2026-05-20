using System;
using System.IO;

namespace KeywordOcr.App.Services;

internal static class PathDefaults
{
    public static string ExportRoot
    {
        get
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
                desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
            return Path.Combine(desktop, "EXPORT");
        }
    }
}
