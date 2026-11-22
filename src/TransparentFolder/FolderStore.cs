using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;

namespace TransparentFolder;

internal static class FolderStore
{
    private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "TransparentFolder";
    private const string LegacyRunValueName = "DesktopFolders";

    private static bool IsTurkish =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly object SaveLock = new();

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string DataRoot
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TransparentFolder");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string StateFilePath => Path.Combine(DataRoot, "folders.json");

    public static string VaultRoot
    {
        get
        {
            string dir = Path.Combine(DataRoot, "vault");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string IconsCacheDir
    {
        get
        {
            string dir = Path.Combine(DataRoot, "icons");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string CustomIconsDir
    {
        get
        {
            string dir = Path.Combine(DataRoot, "custom-icons");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string WebViewDataDir
    {
        get
        {
            string dir = Path.Combine(DataRoot, "WebView2");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string GetFolderStoragePath(FolderData folder)
        => GetFolderStoragePath(folder, out _);

    internal static string GetFolderStoragePath(FolderData folder, out bool migrated)
    {
        migrated = false;

        string resolvedDir;
        if (!string.IsNullOrWhiteSpace(folder.StorageDirName))
        {
            resolvedDir = Path.Combine(VaultRoot, folder.StorageDirName);
            Directory.CreateDirectory(resolvedDir);
        }
        else
        {
            string legacyIdDir = Path.Combine(VaultRoot, SanitizeId(folder.Id));
            bool legacyExists = Directory.Exists(legacyIdDir);
            string desiredBase = !string.IsNullOrWhiteSpace(folder.Name)
                ? SanitizeFolderName(folder.Name)
                : (IsTurkish ? "İsimsiz" : "Unnamed");

            string targetName = GetUniqueVaultDirName(
                desiredBase,
                currentFullPath: legacyExists ? legacyIdDir : null);
            string targetDir = Path.Combine(VaultRoot, targetName);

            if (legacyExists)
            {
                if (TryMoveDirectoryCaseSafe(legacyIdDir, targetDir))
                {
                    folder.StorageDirName = targetName;
                    migrated = true;
                    resolvedDir = targetDir;
                }
                else
                {
                    resolvedDir = legacyIdDir;
                }
            }
            else
            {
                Directory.CreateDirectory(targetDir);
                folder.StorageDirName = targetName;
                migrated = true;
                resolvedDir = targetDir;
            }
        }

        if (!string.IsNullOrWhiteSpace(folder.StoragePath))
        {
            if (MigrateDesktopStorageToVault(folder, resolvedDir))
            {
                migrated = true;
            }
        }

        return resolvedDir;
    }

    public static void RenameFolderStorage(FolderData folder, string newName)
    {
        string trimmed = (newName ?? string.Empty).Trim();
        folder.Name = trimmed;

        if (trimmed.Length == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(folder.StorageDirName))
        {
            GetFolderStoragePath(folder, out _);
            return;
        }

        string currentDir = Path.Combine(VaultRoot, folder.StorageDirName);
        bool currentExists = Directory.Exists(currentDir);
        string desiredBase = SanitizeFolderName(trimmed);
        string targetName = GetUniqueVaultDirName(
            desiredBase,
            currentFullPath: currentExists ? currentDir : null);
        string targetDir = Path.Combine(VaultRoot, targetName);

        if (string.Equals(folder.StorageDirName, targetName, StringComparison.Ordinal))
        {
            if (!currentExists)
            {
                Directory.CreateDirectory(targetDir);
            }
            return;
        }

        if (currentExists)
        {
            if (TryMoveDirectoryCaseSafe(currentDir, targetDir))
            {
                folder.StorageDirName = targetName;
            }
        }
        else
        {
            Directory.CreateDirectory(targetDir);
            folder.StorageDirName = targetName;
        }
    }

    private static string GetUniqueVaultDirName(string desiredBase, string? currentFullPath = null)
    {
        string? normalizedCurrent = string.IsNullOrEmpty(currentFullPath)
            ? null
            : Path.GetFullPath(currentFullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        bool IsAvailable(string candidateName)
        {
            string candidatePath = Path.GetFullPath(Path.Combine(VaultRoot, candidateName))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (normalizedCurrent is not null &&
                string.Equals(candidatePath, normalizedCurrent, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !Directory.Exists(candidatePath) && !File.Exists(candidatePath);
        }

        if (IsAvailable(desiredBase))
        {
            return desiredBase;
        }

        for (int i = 2; i < 1000; i++)
        {
            string next = $"{desiredBase} ({i})";
            if (IsAvailable(next))
            {
                return next;
            }
        }

        return $"{desiredBase}_{Guid.NewGuid():N}";
    }

    private static bool TryMoveDirectoryCaseSafe(string sourceDir, string destDir)
    {
        try
        {
            string fullSource = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullDest = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(fullSource, fullDest, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(fullSource, fullDest, StringComparison.OrdinalIgnoreCase))
            {
                string parentDir = Path.GetDirectoryName(fullDest) ?? VaultRoot;
                string tempDir = Path.Combine(parentDir, $"__rename_{Guid.NewGuid():N}");
                Directory.Move(fullSource, tempDir);
                Directory.Move(tempDir, fullDest);
                return true;
            }

            Directory.Move(fullSource, fullDest);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool MigrateDesktopStorageToVault(FolderData folder, string targetVaultDir)
    {
        if (string.IsNullOrWhiteSpace(folder.StoragePath))
        {
            return false;
        }

        try
        {
            string desktopDir = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string desktopPrefix = desktopDir + Path.DirectorySeparatorChar;
            string currentPath = Path.GetFullPath(folder.StoragePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string vaultDir = Path.GetFullPath(targetVaultDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(currentPath, vaultDir, StringComparison.OrdinalIgnoreCase))
            {
                folder.StoragePath = string.Empty;
                return true;
            }

            bool isOnDesktop = currentPath.StartsWith(desktopPrefix, StringComparison.OrdinalIgnoreCase);
            if (!isOnDesktop)
            {
                if (!Directory.Exists(currentPath))
                {
                    folder.StoragePath = string.Empty;
                    return true;
                }

                return false;
            }

            if (Directory.Exists(currentPath))
            {
                foreach (string filePath in Directory.EnumerateFiles(currentPath).ToList())
                {
                    try
                    {
                        string fileName = Path.GetFileName(filePath);
                        string destFile = GetUniqueEntryPath(vaultDir, fileName, isDirectory: false);
                        try
                        {
                            File.Move(filePath, destFile);
                        }
                        catch (IOException)
                        {
                            File.Copy(filePath, destFile, overwrite: false);
                            File.Delete(filePath);
                        }
                    }
                    catch
                    {
                        // Continue migrating remaining files
                    }
                }

                foreach (string subDirPath in Directory.EnumerateDirectories(currentPath).ToList())
                {
                    try
                    {
                        string dirName = Path.GetFileName(subDirPath);
                        string destSubDir = GetUniqueEntryPath(vaultDir, dirName, isDirectory: true);
                        try
                        {
                            Directory.Move(subDirPath, destSubDir);
                        }
                        catch (IOException)
                        {
                            CopyDirectoryRecursive(subDirPath, destSubDir);
                            Directory.Delete(subDirPath, recursive: true);
                        }
                    }
                    catch
                    {
                        // Continue migrating remaining directories
                    }
                }

                if (!Directory.EnumerateFileSystemEntries(currentPath).Any())
                {
                    FileAttributes attrs = File.GetAttributes(currentPath);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(currentPath, attrs & ~FileAttributes.ReadOnly);
                    }

                    Directory.Delete(currentPath, recursive: false);
                    FileOperations.SHChangeNotify(
                        FileOperations.SHCNE_RMDIR,
                        FileOperations.SHCNF_PATHW | FileOperations.SHCNF_FLUSHNOWAIT,
                        currentPath,
                        IntPtr.Zero);
                    FileOperations.SHChangeNotify(
                        FileOperations.SHCNE_UPDATEDIR,
                        FileOperations.SHCNF_PATHW | FileOperations.SHCNF_FLUSHNOWAIT,
                        desktopDir,
                        IntPtr.Zero);
                }
            }

            if (!Directory.Exists(currentPath))
            {
                folder.StoragePath = string.Empty;
                return true;
            }
        }
        catch
        {
            // Ignore transient migration failures and retry on next access
        }

        return false;
    }

    private static string GetUniqueEntryPath(string parentDir, string entryName, bool isDirectory)
    {
        string candidate = Path.Combine(parentDir, entryName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        string baseName = isDirectory ? entryName : Path.GetFileNameWithoutExtension(entryName);
        string ext = isDirectory ? string.Empty : Path.GetExtension(entryName);

        for (int i = 2; i < 1000; i++)
        {
            string next = Path.Combine(parentDir, $"{baseName} ({i}){ext}");
            if (!File.Exists(next) && !Directory.Exists(next))
            {
                return next;
            }
        }

        return Path.Combine(parentDir, $"{baseName}_{Guid.NewGuid():N}{ext}");
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.EnumerateFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string subDir in Directory.EnumerateDirectories(sourceDir))
        {
            CopyDirectoryRecursive(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
        }
    }

    private static readonly object WebExtractLock = new();
    private static string? _resolvedWebDir;

    public static string ResolveWebDirectory()
    {
        if (!string.IsNullOrEmpty(_resolvedWebDir))
        {
            return _resolvedWebDir;
        }

        lock (WebExtractLock)
        {
            if (!string.IsNullOrEmpty(_resolvedWebDir))
            {
                return _resolvedWebDir;
            }

            string baseDir = AppContext.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "web"),
#if DEBUG
                Path.GetFullPath(Path.Combine(baseDir, "..", "web")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "web")),
#endif
            };

            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "index.html")))
                {
                    _resolvedWebDir = candidate;
                    return _resolvedWebDir;
                }
            }

            _resolvedWebDir = ExtractEmbeddedWebAssets();
            return _resolvedWebDir;
        }
    }

    private static string ExtractEmbeddedWebAssets()
    {
        string webDir = Path.Combine(DataRoot, "web");
        Directory.CreateDirectory(webDir);

        Assembly assembly = Assembly.GetExecutingAssembly();
        string[] files = { "index.html", "styles.css", "app.js" };

        foreach (string fileName in files)
        {
            string resourceName = $"TransparentFolder.web.{fileName}";
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                throw new InvalidOperationException($"Missing embedded web resource: {resourceName}");
            }

            string destPath = Path.Combine(webDir, fileName);
            using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            stream.CopyTo(fileStream);
        }

        return webDir;
    }

    public static AppState Load()
    {
        lock (SaveLock)
        {
            MigrateLegacyDataIfNeeded();

            try
            {
                if (File.Exists(StateFilePath))
                {
                    string json = File.ReadAllText(StateFilePath);
                    AppState? state = JsonSerializer.Deserialize<AppState>(json, JsonOptions);
                    if (state is not null)
                    {
                        state.Folders ??= new List<FolderData>();
                        state.HoverDelayMs = Math.Clamp(state.HoverDelayMs, 150, 1000);
                        state.AnimationDurationMs = Math.Clamp(state.AnimationDurationMs, 120, 650);
                        state.GlassOpacity = Math.Clamp(state.GlassOpacity, 50, 98);

                        bool migrated = false;
                        foreach (FolderData folder in state.Folders)
                        {
                            GetFolderStoragePath(folder, out bool folderMigrated);
                            if (folderMigrated)
                            {
                                migrated = true;
                            }
                        }

                        if (migrated)
                        {
                            Save(state);
                        }

                        return state;
                    }
                }
            }
            catch
            {
                // Fall back to fresh default state
            }

            var initial = new AppState
            {
                AutoStart = true,
                HoverDelayMs = 400,
                AnimationDurationMs = 400,
                GlassOpacity = 90,
                Folders = new List<FolderData>()
            };
            Save(initial);
            return initial;
        }
    }

    public static void Save(AppState state)
    {
        lock (SaveLock)
        {
            try
            {
                string json = JsonSerializer.Serialize(state, JsonOptions);
                string temp = StateFilePath + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, StateFilePath, overwrite: true);
            }
            catch
            {
                // Ignore transient file lock issues
            }
        }
    }

    public static void ApplyAutoStart(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true);
            if (key is null)
            {
                return;
            }

            key.DeleteValue(LegacyRunValueName, throwOnMissingValue: false);

            if (enabled)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue(RunValueName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Ignore registry errors
        }
    }

    private static void MigrateLegacyDataIfNeeded()
    {
        try
        {
            string newStatePath = StateFilePath;
            if (File.Exists(newStatePath))
            {
                return;
            }

            string legacyRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopFolders");
            string legacyStatePath = Path.Combine(legacyRoot, "folders.json");
            if (!File.Exists(legacyStatePath))
            {
                return;
            }

            string legacyVault = Path.Combine(legacyRoot, "vault");
            if (Directory.Exists(legacyVault))
            {
                CopyDirectoryMerge(legacyVault, VaultRoot);
            }

            string legacyCustomIcons = Path.Combine(legacyRoot, "custom-icons");
            if (Directory.Exists(legacyCustomIcons))
            {
                CopyDirectoryMerge(legacyCustomIcons, CustomIconsDir);
            }

            File.Copy(legacyStatePath, newStatePath, overwrite: false);
        }
        catch
        {
            // Ignore migration failures and continue with available state
        }
    }

    private static void CopyDirectoryMerge(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.EnumerateFiles(sourceDir))
        {
            try
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                if (!File.Exists(destFile))
                {
                    File.Copy(file, destFile, overwrite: false);
                }
            }
            catch
            {
                // Continue copying remaining files
            }
        }

        foreach (string subDir in Directory.EnumerateDirectories(sourceDir))
        {
            try
            {
                CopyDirectoryMerge(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
            }
            catch
            {
                // Continue copying remaining subdirectories
            }
        }
    }

    public static string SanitizeFolderName(string? name)
    {
        string defaultName = IsTurkish ? "Yeni Klasör" : "New Folder";
        if (string.IsNullOrWhiteSpace(name))
        {
            return defaultName;
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return defaultName;
        }

        if (ReservedDeviceNames.Contains(cleaned))
        {
            return $"{cleaned}_";
        }

        return cleaned;
    }

    private static string SanitizeId(string id)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(id.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "default" : cleaned;
    }
}
