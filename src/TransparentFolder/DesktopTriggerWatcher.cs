using System.Collections.Concurrent;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;

namespace TransparentFolder;

/// <summary>
/// Watches the user's Desktop directory for newly created .tfolder trigger files
/// (from Explorer's Right-Click -> New menu), immediately cancels
/// Explorer's inline rename Edit box so no rename error dialog appears, deletes
/// the temporary trigger file with instant Shell notification, and raises
/// <see cref="NewFolderTriggered"/> on the UI thread.
/// </summary>
public sealed class DesktopTriggerWatcher : IDisposable
{
    private const string TriggerFilter = "*.tfolder";
    private const int InlineRenameDetectDelayMs = 10;
    private const int InlineRenameRecheckDelayMs = 25;
    private const int MaxDeleteAttempts = 12;
    private const int DeleteRetryDelayMs = 40;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int VK_ESCAPE = 0x1B;

    private static readonly TimeSpan StartupOrphanMinAge = TimeSpan.FromSeconds(3);

    private readonly string _desktopPath;
    private readonly SynchronizationContext? _uiContext;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, byte> _inFlightPaths = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private int _disposed;

    public DesktopTriggerWatcher(
        SynchronizationContext? uiContext = null,
        bool cleanupOrphansOnStart = true,
        bool autoStart = true)
    {
        _desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _uiContext = uiContext ?? SynchronizationContext.Current;

        if (autoStart)
        {
            Start();
        }

        if (cleanupOrphansOnStart)
        {
            CleanupOrphanedFiles(StartupOrphanMinAge);
        }
    }

    private static string DefaultFolderName =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)
            ? "Yeni Klasör"
            : "New Folder";

    /// <summary>
    /// Raised on the UI thread when a .tfolder trigger file is detected and cleaned up.
    /// Parameters: default folder name and desktop screen coordinates.
    /// </summary>
    public event Action<string, Point>? NewFolderTriggered;

    /// <summary>
    /// Gets the watched desktop directory path.
    /// </summary>
    public string DesktopPath => _desktopPath;

    /// <summary>
    /// Gets whether the underlying FileSystemWatcher is currently active.
    /// </summary>
    public bool IsWatching => _watcher is { EnableRaisingEvents: true } && Volatile.Read(ref _disposed) == 0;

    /// <summary>
    /// Starts watching the desktop directory for *.tfolder files.
    /// </summary>
    public void Start()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(DesktopTriggerWatcher));
        }

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(_desktopPath) || !Directory.Exists(_desktopPath))
        {
            return;
        }

        var watcher = new FileSystemWatcher(_desktopPath, TriggerFilter)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
            InternalBufferSize = 32 * 1024
        };

        watcher.Created += OnTriggerFileCreated;
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    /// <summary>
    /// Stops watching without disposing the instance.
    /// </summary>
    public void Stop()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
        }
    }

    /// <summary>
    /// Deletes leftover orphaned *.tfolder files from the desktop without interfering with
    /// newly created trigger files currently being processed.
    /// </summary>
    /// <param name="minimumAge">
    /// Optional minimum file age before deletion. Pass <see cref="TimeSpan.Zero"/> (or null)
    /// to remove all untracked *.tfolder files immediately.
    /// </param>
    /// <returns>The number of orphaned files removed.</returns>
    public int CleanupOrphanedFiles(TimeSpan? minimumAge = null)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            string.IsNullOrWhiteSpace(_desktopPath) ||
            !Directory.Exists(_desktopPath))
        {
            return 0;
        }

        TimeSpan threshold = minimumAge ?? TimeSpan.Zero;
        DateTime nowUtc = DateTime.UtcNow;
        int removedCount = 0;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_desktopPath, TriggerFilter, SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }

        foreach (string filePath in files)
        {
            if (_inFlightPaths.ContainsKey(filePath))
            {
                continue;
            }

            try
            {
                if (threshold > TimeSpan.Zero)
                {
                    DateTime createdUtc = File.GetCreationTimeUtc(filePath);
                    if (nowUtc - createdUtc < threshold)
                    {
                        continue;
                    }
                }

                File.Delete(filePath);
                FileOperations.SHChangeNotify(
                    FileOperations.SHCNE_DELETE,
                    FileOperations.SHCNF_PATHW | FileOperations.SHCNF_FLUSHNOWAIT,
                    filePath,
                    IntPtr.Zero);
                removedCount++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removedCount;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cts.Cancel();

        FileSystemWatcher? watcher = Interlocked.Exchange(ref _watcher, null);
        if (watcher is not null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnTriggerFileCreated;
            watcher.Dispose();
        }

        _cts.Dispose();
    }

    private void OnTriggerFileCreated(object sender, FileSystemEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrWhiteSpace(e.FullPath))
        {
            return;
        }

        if (!_inFlightPaths.TryAdd(e.FullPath, 0))
        {
            return;
        }

        Point cursorPosition = CaptureCursorPosition();
        _ = ProcessTriggerCreationAsync(e.FullPath, cursorPosition, _cts.Token);
    }

    private async Task ProcessTriggerCreationAsync(string fullPath, Point cursorPosition, CancellationToken cancellationToken)
    {
        try
        {
            // Wait ~30ms for Explorer to open its inline rename Edit box, then cancel it with Escape
            await Task.Delay(InlineRenameDetectDelayMs, cancellationToken).ConfigureAwait(false);

            if (!CancelDesktopInlineRenameIfActive())
            {
                await Task.Delay(InlineRenameRecheckDelayMs, cancellationToken).ConfigureAwait(false);
                CancelDesktopInlineRenameIfActive();
            }

            Point suggestedPos = ResolveTriggerPosition(fullPath, cursorPosition);

            await TryDeleteWithRetriesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            FileOperations.SHChangeNotify(
                FileOperations.SHCNE_DELETE,
                FileOperations.SHCNF_PATHW | FileOperations.SHCNF_FLUSHNOWAIT,
                fullPath,
                IntPtr.Zero);

            if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            DispatchTriggered(DefaultFolderName, suggestedPos);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Keep background FileSystemWatcher handler resilient.
        }
        finally
        {
            _inFlightPaths.TryRemove(fullPath, out _);
        }
    }

    private static bool CancelDesktopInlineRenameIfActive()
    {
        IntPtr editHwnd = IntPtr.Zero;

        IntPtr lvHwnd = DesktopIconHelper.FindDesktopListView();
        if (lvHwnd != IntPtr.Zero)
        {
            editHwnd = FindWindowEx(lvHwnd, IntPtr.Zero, "Edit", null);
        }

        if (editHwnd == IntPtr.Zero)
        {
            editHwnd = FindFocusedDesktopEditControl();
        }

        if (editHwnd == IntPtr.Zero)
        {
            return false;
        }

        SendMessage(editHwnd, WM_KEYDOWN, (IntPtr)VK_ESCAPE, IntPtr.Zero);
        SendMessage(editHwnd, WM_KEYUP, (IntPtr)VK_ESCAPE, IntPtr.Zero);
        return true;
    }

    private static IntPtr FindFocusedDesktopEditControl()
    {
        var gui = new NativeMethods.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
        };

        if (!NativeMethods.GetGUIThreadInfo(0, ref gui))
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            uint fgThreadId = NativeMethods.GetWindowThreadProcessId(fg, out _);
            if (fgThreadId == 0 || !NativeMethods.GetGUIThreadInfo(fgThreadId, ref gui))
            {
                return IntPtr.Zero;
            }
        }

        IntPtr focusHwnd = gui.hwndFocus;
        if (focusHwnd == IntPtr.Zero ||
            !NativeMethods.GetWindowClassName(focusHwnd).Equals("Edit", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        IntPtr current = focusHwnd;
        for (int depth = 0; depth < 6; depth++)
        {
            IntPtr parent = NativeMethods.GetParent(current);
            if (parent == IntPtr.Zero)
            {
                parent = NativeMethods.GetAncestor(current, NativeMethods.GA_PARENT);
            }

            if (parent == IntPtr.Zero || parent == current)
            {
                break;
            }

            string parentClass = NativeMethods.GetWindowClassName(parent);
            if (parentClass.Equals("SysListView32", StringComparison.OrdinalIgnoreCase) ||
                parentClass.Equals("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase))
            {
                return focusHwnd;
            }

            current = parent;
        }

        return IntPtr.Zero;
    }

    private static async Task TryDeleteWithRetriesAsync(string fullPath, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MaxDeleteAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(fullPath))
            {
                return;
            }

            try
            {
                File.Delete(fullPath);
                return;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            await Task.Delay(DeleteRetryDelayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private void DispatchTriggered(string folderName, Point cursorPosition)
    {
        Action<string, Point>? handler = NewFolderTriggered;
        if (handler is null)
        {
            return;
        }

        if (_uiContext is not null)
        {
            _uiContext.Post(_ =>
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    NewFolderTriggered?.Invoke(folderName, cursorPosition);
                }
            }, null);
            return;
        }

        Form? openForm = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
        if (openForm is { IsDisposed: false, IsHandleCreated: true })
        {
            try
            {
                openForm.BeginInvoke(() =>
                {
                    if (Volatile.Read(ref _disposed) == 0)
                    {
                        NewFolderTriggered?.Invoke(folderName, cursorPosition);
                    }
                });
                return;
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        handler.Invoke(folderName, cursorPosition);
    }

    private static Point ResolveTriggerPosition(string filePath, Point fallbackCursorPos)
    {
        try
        {
            string fileName = Path.GetFileName(filePath);
            if (!string.IsNullOrWhiteSpace(fileName) &&
                DesktopIconHelper.TryGetItemPosition(fileName, out Point iconPos))
            {
                return iconPos;
            }

            string nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            if (!string.IsNullOrWhiteSpace(nameWithoutExt) &&
                DesktopIconHelper.TryGetItemPosition(nameWithoutExt, out iconPos))
            {
                return iconPos;
            }
        }
        catch
        {
            // Fall back to cursor position if Explorer icon query fails
        }

        return fallbackCursorPos;
    }

    private static Point CaptureCursorPosition()
    {
        if (NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
        {
            return new Point(pt.X, pt.Y);
        }

        return Cursor.Position;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
}
