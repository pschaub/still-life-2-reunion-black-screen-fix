using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace StillLife2Patcher.Core;

public sealed record SaveEntry(string Path, DateTime Modified, long Bytes, Inspection? Inspection, string? Error);
public sealed record DiscoveryResult(IReadOnlyList<SaveEntry> Saves, IReadOnlyList<string> Installations, IReadOnlyList<string> Warnings);

public static class Discovery
{
    static bool GameName(string s) => Regex.IsMatch(s, @"still[\s_-]*life[\s_-]*2", RegexOptions.IgnoreCase);
    public static IReadOnlyList<string> InstallationRoots(List<string> warnings)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path) { if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) roots.Add(System.IO.Path.GetFullPath(path)); }
        var steam = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var sk = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (sk?.GetValue("SteamPath") is string sp) steam.Add(sp);
                foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                foreach (string key in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\GOG.com\Games", @"SOFTWARE\WOW6432Node\GOG.com\Games" })
                {
                    using var parent = hive.OpenSubKey(key); if (parent is null) continue;
                    foreach (var sub in parent.GetSubKeyNames())
                    {
                        using var entry = parent.OpenSubKey(sub);
                        string name = (entry?.GetValue("DisplayName") ?? entry?.GetValue("gameName")) as string ?? "";
                        if (GameName(name)) Add((entry?.GetValue("InstallLocation") ?? entry?.GetValue("path")) as string);
                    }
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { warnings.Add(e.Message); }
        }
        steam.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        foreach (var home in steam.ToArray())
        {
            try
            {
                string vdf = System.IO.Path.Combine(home, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\"")) steam.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { warnings.Add(e.Message); }
        }
        foreach (var home in steam)
        {
            string apps = System.IO.Path.Combine(home, "steamapps");
            try
            {
                if (!Directory.Exists(apps)) continue;
                foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    string text = File.ReadAllText(manifest);
                    var name = Regex.Match(text, "\"name\"\\s*\"([^\"]+)\"");
                    var dir = Regex.Match(text, "\"installdir\"\\s*\"([^\"]+)\"");
                    if (name.Success && dir.Success && GameName(name.Groups[1].Value))
                    {
                        string common = System.IO.Path.GetFullPath(System.IO.Path.Combine(apps, "common")) + System.IO.Path.DirectorySeparatorChar;
                        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(common, dir.Groups[1].Value));
                        if (path.StartsWith(common, StringComparison.OrdinalIgnoreCase)) Add(path);
                    }
                }
                string commonDir = System.IO.Path.Combine(apps, "common");
                if (Directory.Exists(commonDir)) foreach (var dir in Directory.EnumerateDirectories(commonDir)) if (GameName(System.IO.Path.GetFileName(dir))) Add(dir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { warnings.Add(e.Message); }
        }
        foreach (var parent in new[] { @"C:\GOG Games", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy", "Games"), System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") })
        {
            try { if (Directory.Exists(parent)) foreach (var dir in Directory.EnumerateDirectories(parent)) if (GameName(System.IO.Path.GetFileName(dir))) Add(dir); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { warnings.Add(e.Message); }
        }
        return roots.Order().ToArray();
    }
    public static DiscoveryResult Scan(string? selectedFolder, bool autoDetect, CancellationToken token, IProgress<(int Done, int Total)>? progress = null)
    {
        var warnings = new List<string>(); var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installs = autoDetect ? InstallationRoots(warnings) : [];
        foreach (var root in installs) roots.Add(root);
        if (!string.IsNullOrWhiteSpace(selectedFolder)) roots.Add(System.IO.Path.GetFullPath(selectedFolder));
        if (autoDetect)
        foreach (string name in new[] { "Still Life 2", "StillLife2" })
        {
            roots.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), name));
            roots.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", name));
        }
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            dirs.Add(root); dirs.Add(System.IO.Path.Combine(root, "EXTDataSave"));
            if (System.IO.Path.IsPathRooted(root) && root.Length > 3)
            {
                string vs = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VirtualStore", root[3..]);
                dirs.Add(System.IO.Path.Combine(vs, "EXTDataSave"));
            }
        }
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirs)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(dir) || (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var path in Directory.EnumerateFiles(dir, "*.sav", SearchOption.TopDirectoryOnly))
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                    paths.Add(System.IO.Path.GetFullPath(path));
                    if (paths.Count >= 500) { warnings.Add("Save scan limited to 500 files. Select a specific folder to inspect more."); break; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { warnings.Add(e.Message); }
            if (paths.Count >= 500) break;
        }
        var saves = new List<SaveEntry>();
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length > 64 * 1024 * 1024) saves.Add(new(path, fi.LastWriteTime, fi.Length, null, "File exceeds 64 MiB scan limit."));
                else saves.Add(new(path, fi.LastWriteTime, fi.Length, SaveRepair.Inspect(File.ReadAllBytes(path)), null));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { saves.Add(new(path, DateTime.MinValue, 0, null, e.Message)); }
            progress?.Report((saves.Count, paths.Count));
        }
        return new(saves.OrderByDescending(x => x.Inspection?.Compatible == true).ThenByDescending(x => x.Modified).ToArray(), installs, warnings);
    }
}
