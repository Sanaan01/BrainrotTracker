using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;

namespace Brainrot.UI
{
    internal static class ProcessIconProvider
    {
        private static readonly Dictionary<string, ImageSource> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly object Gate = new();

        private static readonly Dictionary<string, string?> ExecutablePathCache =
            new(StringComparer.OrdinalIgnoreCase);

        public static ImageSource GetIcon(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return GetFallbackIcon();
            }

            lock (Gate)
            {
                if (Cache.TryGetValue(processName, out var cached))
                {
                    return cached;
                }
            }

            var icon = TryLoadIcon(processName) ?? GetFallbackIcon();

            lock (Gate)
            {
                Cache[processName] = icon;
            }

            return icon;
        }

        private static ImageSource? TryLoadIcon(string processName)
        {
            // 1) Active process (preferred, freshest)
            var fromRunning = TryLoadIconFromRunningProcess(processName);
            if (fromRunning != null)
            {
                return fromRunning;
            }

            // 2) Installed app (non-running)
            var fromInstalled = TryLoadIconFromInstalled(processName);
            if (fromInstalled != null)
            {
                return fromInstalled;
            }

            return null;
        }

        private static ImageSource? TryLoadIconFromRunningProcess(string processName)
        {
            try
            {
                var process = Process.GetProcessesByName(processName)
                    .FirstOrDefault();

                if (process == null)
                    return null;

                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch
                {
                    // Some system processes may not expose MainModule; ignore.
                }

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return null;

                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null)
                    return null;

                using var bitmap = icon.ToBitmap();
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;

                var image = new BitmapImage
                {
                    DecodePixelWidth = 32,
                    DecodePixelHeight = 32,
                    CreateOptions = BitmapCreateOptions.IgnoreImageCache
                };
                image.SetSource(ms.AsRandomAccessStream());
                return image;
            }
            catch
            {
                // If we can't resolve the icon, fall back to a placeholder.
                return null;
            }
        }

        private static ImageSource? TryLoadIconFromInstalled(string processName)
        {
            try
            {
                var path = ResolveExecutablePath(processName);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return null;
                }

                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null)
                {
                    return null;
                }

                using var bitmap = icon.ToBitmap();
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;

                var image = new BitmapImage
                {
                    DecodePixelWidth = 32,
                    DecodePixelHeight = 32,
                    CreateOptions = BitmapCreateOptions.IgnoreImageCache
                };
                image.SetSource(ms.AsRandomAccessStream());
                return image;
            }
            catch
            {
                return null;
            }
        }

        private static ImageSource GetFallbackIcon()
        {
            return new BitmapImage(new Uri("ms-appx:///Assets/Square44x44Logo.targetsize-24_altform-unplated.png"));
        }

        private static string? ResolveExecutablePath(string processName)
        {
            lock (Gate)
            {
                if (ExecutablePathCache.TryGetValue(processName, out var cached))
                {
                    return cached;
                }
            }

            var exeName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName
                : $"{processName}.exe";

            // Try registry (App Paths) first - reliable for Office and many installers.
            var regPath = GetExePathFromRegistry(exeName);
            if (!string.IsNullOrWhiteSpace(regPath) && File.Exists(regPath))
            {
                CachePath(processName, regPath);
                return regPath;
            }

            var candidateDirectories = GetCandidateDirectories()
                .Where(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d))
                .ToList();

            foreach (var dir in candidateDirectories)
            {
                // First try direct path at the directory root.
                var directPath = Path.Combine(dir, exeName);
                if (File.Exists(directPath))
                {
                    CachePath(processName, directPath);
                    return directPath;
                }

                // Then a shallow search through subdirectories (bounded).
                try
                {
                    var match = Directory.EnumerateFiles(dir, exeName, SearchOption.AllDirectories)
                        .FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(match))
                    {
                        CachePath(processName, match);
                        return match;
                    }
                }
                catch
                {
                    // Ignore access issues and keep searching other directories.
                }
            }

            CachePath(processName, null);
            return null;
        }

        private static string? GetExePathFromRegistry(string exeName)
        {
            try
            {
                string keyPath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}";

                foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
                {
                    using var key = hive.OpenSubKey(keyPath);
                    if (key == null)
                        continue;

                    var defaultValue = key.GetValue(string.Empty) as string;
                    if (!string.IsNullOrWhiteSpace(defaultValue) && File.Exists(defaultValue))
                    {
                        return defaultValue;
                    }
                }
            }
            catch
            {
                // Ignore registry access issues.
            }

            return null;
        }

        private static void CachePath(string processName, string? path)
        {
            lock (Gate)
            {
                ExecutablePathCache[processName] = path;
            }
        }

        private static IEnumerable<string> GetCandidateDirectories()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
                yield return programFiles;

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
                yield return programFilesX86;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
                yield return Path.Combine(localAppData, "Programs");

            var programData = Environment.GetEnvironmentVariable("ProgramData");
            if (!string.IsNullOrWhiteSpace(programData))
                yield return programData;

            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windowsDir))
                yield return windowsDir;
        }
    }
}
