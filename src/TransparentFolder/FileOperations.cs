using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace TransparentFolder;

internal static class FileOperations
{
    internal const uint SHCNE_CREATE = 0x00000002;
    internal const uint SHCNE_DELETE = 0x00000004;
    internal const uint SHCNE_RMDIR = 0x00000010;
    internal const uint SHCNE_UPDATEDIR = 0x00001000;
    internal const uint SHCNE_UPDATEITEM = 0x00002000;
    internal const uint SHCNE_ASSOCCHANGED = 0x08000000;

    internal const uint SHCNF_IDLIST = 0x0000;
    internal const uint SHCNF_PATHW = 0x0005;
    internal const uint SHCNF_FLUSH = 0x1000;
    internal const uint SHCNF_FLUSHNOWAIT = 0x2000;

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern void SHChangeNotify(uint wEventId, uint uFlags, string? dwItem1, IntPtr dwItem2);

    [DllImport("shell32.dll")]
    internal static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation64(ref SHFILEOPSTRUCT64 lpFileOp);

    [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation32(ref SHFILEOPSTRUCT32 lpFileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT64
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT32
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    public static bool SendToRecycleBin(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return false;
        }

        IntPtr pFrom = IntPtr.Zero;
        try
        {
            // SHFileOperationW expects double-null-terminated string list
            pFrom = Marshal.StringToHGlobalUni(fullPath + "\0\0");
            ushort flags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI;

            int result;
            bool aborted;
            if (IntPtr.Size == 8)
            {
                var op = new SHFILEOPSTRUCT64
                {
                    hwnd = IntPtr.Zero,
                    wFunc = FO_DELETE,
                    pFrom = pFrom,
                    pTo = IntPtr.Zero,
                    fFlags = flags,
                    fAnyOperationsAborted = false,
                    hNameMappings = IntPtr.Zero,
                    lpszProgressTitle = IntPtr.Zero
                };
                result = SHFileOperation64(ref op);
                aborted = op.fAnyOperationsAborted;
            }
            else
            {
                var op = new SHFILEOPSTRUCT32
                {
                    hwnd = IntPtr.Zero,
                    wFunc = FO_DELETE,
                    pFrom = pFrom,
                    pTo = IntPtr.Zero,
                    fFlags = flags,
                    fAnyOperationsAborted = false,
                    hNameMappings = IntPtr.Zero,
                    lpszProgressTitle = IntPtr.Zero
                };
                result = SHFileOperation32(ref op);
                aborted = op.fAnyOperationsAborted;
            }

            if (result == 0 && !aborted)
            {
                SHChangeNotify(SHCNE_DELETE, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, fullPath, IntPtr.Zero);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (pFrom != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pFrom);
            }
        }
    }
    public static List<FolderItemDto> ListFolderItems(FolderData folder)
    {
        string folderDir = FolderStore.GetFolderStoragePath(folder);
        var entries = new List<FolderItemDto>();

        if (!Directory.Exists(folderDir))
        {
            return entries;
        }

        foreach (string dirPath in Directory.EnumerateDirectories(folderDir))
        {
            string dirName = Path.GetFileName(dirPath);
            if (string.IsNullOrEmpty(dirName))
            {
                continue;
            }

            entries.Add(new FolderItemDto
            {
                Id = dirName,
                Name = dirName,
                Path = dirPath,
                Extension = string.Empty,
                IsDirectory = true,
                IconUrl = IconExtractor.GetIconUrl(dirPath)
            });
        }

        foreach (string filePath in Directory.EnumerateFiles(folderDir))
        {
            string fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName) ||
                fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string ext = Path.GetExtension(fileName);
            string displayName = ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                                 ext.Equals(".url", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(fileName)
                : fileName;

            entries.Add(new FolderItemDto
            {
                Id = fileName,
                Name = displayName,
                Path = filePath,
                Extension = ext.ToLowerInvariant(),
                IsDirectory = false,
                IconUrl = IconExtractor.GetIconUrl(filePath)
            });
        }

        // Apply custom order, then alphabetical for newly added items
        var orderMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < folder.ItemOrder.Count; i++)
        {
            orderMap.TryAdd(folder.ItemOrder[i], i);
        }

        entries.Sort((a, b) =>
        {
            bool aHas = orderMap.TryGetValue(a.Id, out int aIdx);
            bool bHas = orderMap.TryGetValue(b.Id, out int bIdx);
            if (aHas && bHas) return aIdx.CompareTo(bIdx);
            if (aHas) return -1;
            if (bHas) return 1;
            return StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name);
        });

        return entries;
    }

    public static (int movedCount, List<string> errors) MoveIntoFolder(FolderData folder, IEnumerable<string> sourcePaths)
    {
        string folderDir = FolderStore.GetFolderStoragePath(folder);
        string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string normalizedFolderDir = Path.GetFullPath(folderDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string folderPrefix = normalizedFolderDir.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedFolderDir
            : normalizedFolderDir + Path.DirectorySeparatorChar;

        int moved = 0;
        var errors = new List<string>();
        var elevatedDeleteQueue = new List<string>();

        foreach (string rawPath in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            string sourcePath = Path.GetFullPath(rawPath);
            string normalizedSource = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string sourcePrefix = normalizedSource.EndsWith(Path.DirectorySeparatorChar)
                ? normalizedSource
                : normalizedSource + Path.DirectorySeparatorChar;

            // Skip if item is the folder itself, already inside this folder, or an ancestor of this folder
            if (string.Equals(normalizedSource, normalizedFolderDir, StringComparison.OrdinalIgnoreCase) ||
                normalizedSource.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase) ||
                normalizedFolderDir.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (File.Exists(sourcePath))
                {
                    int queuedBefore = elevatedDeleteQueue.Count;
                    string fileName = Path.GetFileName(sourcePath);
                    string destPath = GetUniquePath(folderDir, fileName, isDirectory: false);
                    MoveFileStrict(sourcePath, destPath, elevatedDeleteQueue);
                    if (elevatedDeleteQueue.Count == queuedBefore)
                    {
                        SHChangeNotify(SHCNE_DELETE, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, sourcePath, IntPtr.Zero);
                    }
                    moved++;
                }
                else if (Directory.Exists(sourcePath))
                {
                    string dirName = Path.GetFileName(normalizedSource);
                    string destPath = GetUniquePath(folderDir, dirName, isDirectory: true);
                    MoveDirectoryStrict(sourcePath, destPath);
                    SHChangeNotify(SHCNE_DELETE, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, sourcePath, IntPtr.Zero);
                    moved++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(sourcePath)}: {ex.Message}");
            }
        }

        // If any files came from Public Desktop (requiring admin rights to delete original),
        // trigger a single elevated cleanup so they disappear from the desktop as requested.
        if (elevatedDeleteQueue.Count > 0)
        {
            TryElevatedDeleteFiles(elevatedDeleteQueue);
            foreach (string deletedPath in elevatedDeleteQueue)
            {
                if (!File.Exists(deletedPath))
                {
                    SHChangeNotify(SHCNE_DELETE, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, deletedPath, IntPtr.Zero);
                }
            }

            string publicDesktopDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!string.IsNullOrWhiteSpace(publicDesktopDir))
            {
                SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, publicDesktopDir, IntPtr.Zero);
            }
        }

        if (moved > 0 && !string.IsNullOrWhiteSpace(desktopDir))
        {
            SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, desktopDir, IntPtr.Zero);
        }

        return (moved, errors);
    }

    public static bool MoveItemOutToDesktop(FolderData folder, string itemId, out string? movedFileName, out string? error)
    {
        movedFileName = null;
        error = null;
        try
        {
            string folderDir = FolderStore.GetFolderStoragePath(folder);
            string sourcePath = Path.Combine(folderDir, itemId);
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            if (File.Exists(sourcePath))
            {
                string destPath = GetUniquePath(desktopDir, Path.GetFileName(sourcePath), isDirectory: false);
                MoveFileStrict(sourcePath, destPath, null);
                RefreshDesktopShellIcon(destPath, desktopDir);
                movedFileName = Path.GetFileName(destPath);
                return true;
            }

            if (Directory.Exists(sourcePath))
            {
                string destPath = GetUniquePath(desktopDir, Path.GetFileName(sourcePath), isDirectory: true);
                MoveDirectoryStrict(sourcePath, destPath);
                RefreshDesktopShellIcon(destPath, desktopDir);
                movedFileName = Path.GetFileName(destPath);
                return true;
            }

            error = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)
                ? "Öğe bulunamadı."
                : "Item not found.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool RestoreItemToDesktop(FolderData folder, string itemId, out string? error)
        => MoveItemOutToDesktop(folder, itemId, out _, out error);

    public static void RestoreAllToDesktop(FolderData folder)
    {
        string folderDir = FolderStore.GetFolderStoragePath(folder);
        if (!Directory.Exists(folderDir))
        {
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(folderDir).ToList())
        {
            string name = Path.GetFileName(entry);
            if (!string.IsNullOrEmpty(name) &&
                !name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                MoveItemOutToDesktop(folder, name, out _, out _);
            }
        }
    }

    public static bool DeleteItemPermanently(FolderData folder, string itemId, out string? error)
    {
        error = null;
        try
        {
            string folderDir = FolderStore.GetFolderStoragePath(folder);
            string target = Path.Combine(folderDir, itemId);

            if (File.Exists(target))
            {
                File.Delete(target);
                return true;
            }

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
                return true;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void DeleteFolderStorage(FolderData folder, bool restoreItemsFirst, bool sendToRecycleBin = false)
    {
        if (restoreItemsFirst)
        {
            RestoreAllToDesktop(folder);
        }

        try
        {
            string folderDir = FolderStore.GetFolderStoragePath(folder);

            if (Directory.Exists(folderDir))
            {
                if (sendToRecycleBin)
                {
                    SendToRecycleBin(folderDir);
                }
                else
                {
                    Directory.Delete(folderDir, recursive: true);
                    SHChangeNotify(SHCNE_DELETE, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, folderDir, IntPtr.Zero);
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    public static void LaunchItem(string fullPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fullPath,
                WorkingDirectory = Path.GetDirectoryName(fullPath) ?? string.Empty,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore launch failures
        }
    }

    public static void OpenInExplorer(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{directoryPath}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore
        }
    }

    private static void RefreshDesktopShellIcon(string destPath, string desktopDir)
    {
        SHChangeNotify(SHCNE_CREATE, SHCNF_PATHW | SHCNF_FLUSH, destPath, IntPtr.Zero);
        SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSH, destPath, IntPtr.Zero);
        SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSH, desktopDir, IntPtr.Zero);
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST | SHCNF_FLUSHNOWAIT, IntPtr.Zero, IntPtr.Zero);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ie4uinit.exe",
                Arguments = "-show",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch
        {
            // Ignore shell icon cache refresh errors
        }
    }

    private static void MoveFileStrict(string sourcePath, string destPath, List<string>? elevatedDeleteQueue)
    {
        try
        {
            File.Move(sourcePath, destPath);
        }
        catch (UnauthorizedAccessException) when (elevatedDeleteQueue is not null)
        {
            // Likely Public Desktop shortcut: copy into vault, queue elevated delete of original
            File.Copy(sourcePath, destPath, overwrite: false);
            elevatedDeleteQueue.Add(sourcePath);
        }
        catch (IOException)
        {
            // Cross-volume move fallback
            File.Copy(sourcePath, destPath, overwrite: false);
            try
            {
                File.Delete(sourcePath);
            }
            catch (UnauthorizedAccessException) when (elevatedDeleteQueue is not null)
            {
                elevatedDeleteQueue.Add(sourcePath);
            }
        }
    }

    private static void MoveDirectoryStrict(string sourceDir, string destDir)
    {
        try
        {
            Directory.Move(sourceDir, destDir);
        }
        catch (IOException)
        {
            CopyDirectoryRecursive(sourceDir, destDir);
            Directory.Delete(sourceDir, recursive: true);
        }
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

    private static void TryElevatedDeleteFiles(List<string> paths)
    {
        try
        {
            string script = string.Join("; ", paths.Select(p => $"Remove-Item -LiteralPath '{p.Replace("'", "''")}' -Force -ErrorAction SilentlyContinue"));
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using Process? proc = Process.Start(psi);
            proc?.WaitForExit(10000);
        }
        catch
        {
            // User declined UAC or delete failed
        }
    }

    private static string GetUniquePath(string targetDir, string fileName, bool isDirectory)
    {
        string candidate = Path.Combine(targetDir, fileName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        string baseName = isDirectory ? fileName : Path.GetFileNameWithoutExtension(fileName);
        string ext = isDirectory ? string.Empty : Path.GetExtension(fileName);

        for (int i = 2; i < 1000; i++)
        {
            string next = Path.Combine(targetDir, $"{baseName} ({i}){ext}");
            if (!File.Exists(next) && !Directory.Exists(next))
            {
                return next;
            }
        }

        return Path.Combine(targetDir, $"{baseName}_{Guid.NewGuid():N}{ext}");
    }
}
