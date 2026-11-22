using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TransparentFolder;

internal sealed class FolderForm : Form
{
    public const int CollapsedWidth = 104;
    public const int CollapsedHeight = 116;
    public const int ExpandedWidth = 408;
    public const int ExpandedHeight = 446;

    private readonly FolderManager _manager;
    private FolderData _folder;
    private readonly WebView2 _webView;
    private readonly System.Windows.Forms.Timer _collapseTimer;
    private readonly System.Windows.Forms.Timer _focusTitleTimer;
    private readonly System.Windows.Forms.Timer _winDGuardTimer;
    private readonly System.Windows.Forms.Timer _dragTimer;
    private readonly System.Windows.Forms.Timer _dragOutTimer;
    private readonly System.Windows.Forms.Timer _vaultDebounceTimer;
    private FileSystemWatcher? _vaultWatcher;
    private readonly SynchronizationContext? _uiSyncContext;

    private bool _isExpanded;
    private int _pinMode;
    private bool _isCollapsing;
    private bool _isExpanding;
    private bool _suppressDeactivate;
    private bool _webViewReady;
    private bool _uiReady;
    private bool _pendingOpenForRename;
    private bool _isDragging;
    private bool _isOverTrash;
    private Rectangle? _cachedRecycleBinRect;
    private string? _pendingDragOutItemId;
    private Point _dragCursorStart;
    private Point _dragFormStart;

    public FolderForm(FolderManager manager, FolderData? folder, bool isStandby = false)
    {
        _manager = manager;
        IsStandby = isStandby;
        _folder = isStandby
            ? new FolderData { Id = "__standby__" }
            : (folder ?? throw new ArgumentNullException(nameof(folder)));
        _uiSyncContext = SynchronizationContext.Current;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(1, 2, 3);
        TransparencyKey = Color.FromArgb(1, 2, 3);
        Text = FormatWindowTitle(_folder.Name);

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.Transparent
        };
        Controls.Add(_webView);

        _collapseTimer = new System.Windows.Forms.Timer { Interval = 240 };
        _collapseTimer.Tick += (_, _) => CompleteCollapse();

        _focusTitleTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _focusTitleTimer.Tick += (_, _) =>
        {
            _focusTitleTimer.Stop();
            if (IsDisposed || !IsHandleCreated || !_isExpanded || !IsPinned || _isCollapsing)
            {
                return;
            }

            NativeMethods.ForceForegroundWindow(Handle);
            _webView.Focus();
            PostToWeb(new { type = "host.focusTitle" });
        };

        _winDGuardTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _winDGuardTimer.Tick += (_, _) => EnsureDesktopVisibility();

        _dragTimer = new System.Windows.Forms.Timer { Interval = 14 };
        _dragTimer.Tick += OnDragTick;

        _dragOutTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _dragOutTimer.Tick += OnDragOutTick;

        _vaultDebounceTimer = new System.Windows.Forms.Timer { Interval = 220 };
        _vaultDebounceTimer.Tick += (_, _) =>
        {
            _vaultDebounceTimer.Stop();
            PushFolderData();
        };

        if (IsStandby)
        {
            Opacity = 0;
            Enabled = false;
            Bounds = new Rectangle(-32000, -32000, CollapsedWidth, CollapsedHeight);
        }
        else
        {
            UpdateWatcherPath();
            Bounds = ComputeCollapsedBounds();
        }
    }

    public bool IsStandby { get; private set; }

    public string FolderId => _folder.Id;

    public bool IsExpanded => _isExpanded;

    public int PinMode => _pinMode;

    private bool IsPinned => _pinMode > 0;

    protected override bool ShowWithoutActivation => !(_isExpanded && _pinMode == 1);

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            if (IsStandby)
            {
                cp.ExStyle |= NativeMethods.WS_EX_TRANSPARENT;
            }

            if (!(_isExpanded && IsPinned))
            {
                cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE;
            }

            return cp;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (!IsStandby)
        {
            ApplyBoundsAndZOrder(expanded: false, pinMode: 0);
        }

        _winDGuardTimer.Start();
        _ = InitializeWebViewAsync();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_isExpanded && !IsStandby)
        {
            PinToBottom();
        }

        if (!IsStandby)
        {
            _manager.SyncOverlapAndZOrder();
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_suppressDeactivate)
        {
            return;
        }

        if (_isExpanded && _pinMode == 1 && !_isCollapsing && !_isExpanding)
        {
            RequestCollapse();
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_WINDOWPOSCHANGING)
        {
            if ((!_isExpanded && !_isExpanding) || (_isExpanded && _pinMode == 2))
            {
                var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(m.LParam);
                if ((pos.flags & NativeMethods.SWP_NOZORDER) == 0 && pos.hwndInsertAfter != NativeMethods.HWND_BOTTOM)
                {
                    pos.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
                    Marshal.StructureToPtr(pos, m.LParam, fDeleteOld: false);
                }
            }
        }
        else if (m.Msg == NativeMethods.WM_MOUSEACTIVATE && !(_isExpanded && IsPinned))
        {
            m.Result = (IntPtr)NativeMethods.MA_NOACTIVATE;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_vaultWatcher is not null)
            {
                _vaultWatcher.EnableRaisingEvents = false;
                _vaultWatcher.Dispose();
                _vaultWatcher = null;
            }

            _collapseTimer.Dispose();
            _focusTitleTimer.Dispose();
            _winDGuardTimer.Dispose();
            _dragTimer.Dispose();
            _dragOutTimer.Dispose();
            _vaultDebounceTimer.Dispose();
            _webView.Dispose();
        }

        base.Dispose(disposing);
    }

    public void ActivateFromStandby(FolderData folder, bool openForRename)
    {
        _folder = folder;
        IsStandby = false;
        Enabled = true;
        if (IsHandleCreated)
        {
            NativeMethods.SetTransparent(Handle, transparent: false);
        }

        Text = FormatWindowTitle(_folder.Name);
        UpdateWatcherPath();
        ApplyBoundsAndZOrder(expanded: false, pinMode: 0);
        Opacity = 1.0;
        if (!Visible)
        {
            Show();
        }

        PushFolderData();
        _manager.SyncOverlapAndZOrder();
        if (openForRename)
        {
            OpenForRename();
        }
    }

    public void SetClickThroughOverlay(bool passThrough)
    {
        if (IsStandby || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (passThrough)
        {
            NativeMethods.SetTransparent(Handle, true);
            PostToWeb(new { type = "host.cancelHover" });
        }
        else
        {
            NativeMethods.SetTransparent(Handle, false);
        }
    }

    public void PushToVeryBottom()
    {
        if (!IsStandby && !IsDisposed && IsHandleCreated && !_isExpanded)
        {
            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HWND_BOTTOM,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    public void RefreshSettings()
    {
        RunOnUiThread(PushFolderData);
    }

    public void ForceCollapseIfOpen()
    {
        RunOnUiThread(() =>
        {
            if (_pinMode == 2)
            {
                return;
            }

            if (_isExpanded && !_isCollapsing)
            {
                RequestCollapse();
            }
        });
    }

    public void OpenForRename()
    {
        RunOnUiThread(() =>
        {
            if (IsDisposed)
            {
                return;
            }

            if (!_webViewReady || !_uiReady)
            {
                _pendingOpenForRename = true;
                return;
            }

            _pendingOpenForRename = false;
            OpenForRenameCore();
        });
    }

    private void OpenForRenameCore()
    {
        if (!_isExpanded)
        {
            Expand(pinned: true, focusTitle: true);
            return;
        }

        if (_pinMode == 0)
        {
            PinCurrentExpanded(focusTitle: true);
            return;
        }

        NativeMethods.SetNoActivate(Handle, noActivate: false);
        if (_pinMode == 1)
        {
            NativeMethods.ForceForegroundWindow(Handle);
            Activate();
        }

        _webView.Focus();
        PostToWeb(new
        {
            type = "host.state",
            expanded = true,
            pinned = IsPinned,
            pinMode = _pinMode,
            focusTitle = true
        });
        ScheduleDelayedTitleFocus();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            CoreWebView2Environment env = await _manager.GetSharedEnvironmentAsync();
            if (IsDisposed)
            {
                return;
            }

            await _webView.EnsureCoreWebView2Async(env);
            if (IsDisposed || _webView.CoreWebView2 is null)
            {
                return;
            }

            _webView.AllowExternalDrop = true;
            CoreWebView2 core = _webView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;

            string webDir = FolderStore.ResolveWebDirectory();
            Directory.CreateDirectory(webDir);
            core.SetVirtualHostNameToFolderMapping(
                "folders.local",
                webDir,
                CoreWebView2HostResourceAccessKind.Allow);

            core.SetVirtualHostNameToFolderMapping(
                "icons.local",
                FolderStore.IconsCacheDir,
                CoreWebView2HostResourceAccessKind.Allow);

            core.SetVirtualHostNameToFolderMapping(
                "customicons.local",
                FolderStore.CustomIconsDir,
                CoreWebView2HostResourceAccessKind.Allow);

            core.WebMessageReceived += OnWebMessageReceived;
            core.NavigationCompleted += OnNavigationCompleted;
            _webViewReady = true;
            core.Navigate("https://folders.local/index.html");
        }
        catch (Exception ex)
        {
            LogDiagnostic($"WebView2 init error ({_folder.Id}): {ex}");
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            LogDiagnostic($"WebView2 navigation failed ({_folder.Id}): {e.WebErrorStatus}");
        }
    }

    private static bool IsTurkish =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase);

    private static void LogDiagnostic(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        try
        {
            string logPath = Path.Combine(FolderStore.DataRoot, "transparentfolder.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Ignore logging failures
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            string msgType = GetString(root, "type") ?? string.Empty;
            if (IsStandby && msgType != "folder.ready")
            {
                return;
            }

            switch (msgType)
            {
                case "folder.ready":
                    _uiReady = true;
                    if (!IsStandby)
                    {
                        PushFolderData();
                        if (_pendingOpenForRename)
                        {
                            _pendingOpenForRename = false;
                            OpenForRenameCore();
                        }
                    }
                    break;

                case "folder.expand":
                case "folder.readyToExpand":
                {
                    bool pinned = GetBool(root, "pinned") ?? false;
                    Expand(pinned);
                    break;
                }

                case "folder.pin":
                    if (_pinMode == 0)
                    {
                        SetPinMode(1);
                    }
                    break;

                case "folder.setPinMode":
                {
                    int mode = GetInt(root, "mode") ?? 0;
                    SetPinMode(mode);
                    break;
                }

                case "folder.requestCollapse":
                    RequestCollapse();
                    break;

                case "folder.collapsed":
                    CompleteCollapse();
                    break;

                case "folder.dragStart":
                    StartWindowDrag();
                    break;

                case "folder.dropFiles":
                    HandleDroppedFiles(e.AdditionalObjects);
                    break;

                case "folder.launchItem":
                {
                    string? itemPath = GetString(root, "path");
                    if (!string.IsNullOrWhiteSpace(itemPath))
                    {
                        FileOperations.LaunchItem(itemPath);
                        if (_pinMode != 2)
                        {
                            RequestCollapse();
                        }
                    }

                    break;
                }

                case "folder.restoreItem":
                {
                    string? itemId = GetString(root, "itemId");
                    if (!string.IsNullOrWhiteSpace(itemId))
                    {
                        FileOperations.RestoreItemToDesktop(_folder, itemId, out _);
                        _folder.ItemOrder.RemoveAll(x => x.Equals(itemId, StringComparison.OrdinalIgnoreCase));
                        _manager.SaveState();
                        PushFolderData();
                    }

                    break;
                }

                case "folder.deleteItem":
                {
                    string? itemId = GetString(root, "itemId");
                    if (!string.IsNullOrWhiteSpace(itemId))
                    {
                        FileOperations.DeleteItemPermanently(_folder, itemId, out _);
                        _folder.ItemOrder.RemoveAll(x => x.Equals(itemId, StringComparison.OrdinalIgnoreCase));
                        _manager.SaveState();
                        PushFolderData();
                    }

                    break;
                }

                case "folder.reorderItems":
                {
                    if (root.TryGetProperty("order", out JsonElement orderEl) &&
                        orderEl.ValueKind == JsonValueKind.Array)
                    {
                        _folder.ItemOrder = orderEl.EnumerateArray()
                            .Where(x => x.ValueKind == JsonValueKind.String)
                            .Select(x => x.GetString()!)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .ToList();
                        _manager.SaveState();
                        PushFolderData();
                    }

                    break;
                }

                case "folder.updateMeta":
                {
                    string? name = GetString(root, "name");
                    string? accent = GetString(root, "accentColor");
                    int? grid = GetInt(root, "previewGrid");

                    if (name is not null)
                    {
                        string trimmedName = name.Trim();
                        if (!string.Equals(trimmedName, _folder.Name, StringComparison.Ordinal))
                        {
                            FolderStore.RenameFolderStorage(_folder, trimmedName);
                            Text = FormatWindowTitle(_folder.Name);
                            UpdateWatcherPath();
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(accent))
                    {
                        _folder.AccentColor = accent.Trim();
                    }

                    if (grid is 2 or 3)
                    {
                        _folder.PreviewGrid = grid.Value;
                    }

                    _manager.SaveState();
                    PushFolderData();
                    break;
                }

                case "folder.createNew":
                {
                    _manager.CreateNewFolderNear(_folder.X + 124, _folder.Y);
                    break;
                }

                case "folder.openInExplorer":
                {
                    FileOperations.OpenInExplorer(FolderStore.GetFolderStoragePath(_folder));
                    break;
                }

                case "folder.addFilesDialog":
                {
                    OpenAddFilesDialog();
                    break;
                }

                case "folder.showShellContextMenu":
                {
                    string? path = GetString(root, "path");
                    int screenX = GetInt(root, "screenX") ?? Cursor.Position.X;
                    int screenY = GetInt(root, "screenY") ?? Cursor.Position.Y;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        _suppressDeactivate = true;
                        try
                        {
                            ShellContextMenu.ShowForPath(Handle, path, new Point(screenX, screenY));
                        }
                        finally
                        {
                            _suppressDeactivate = false;
                            PushFolderData();
                        }
                    }

                    break;
                }

                case "folder.pickCustomIcon":
                {
                    PickCustomIcon();
                    break;
                }

                case "folder.clearCustomIcon":
                {
                    _folder.CustomIconFile = null;
                    _manager.SaveState();
                    PushFolderData();
                    break;
                }

                case "folder.updateGlobalSettings":
                {
                    int? animMs = GetInt(root, "animationDurationMs");
                    int? hoverMs = GetInt(root, "hoverDelayMs");
                    _manager.UpdateGlobalSettings(animMs, hoverMs);
                    break;
                }

                case "folder.beginDragOutToDesktop":
                {
                    string? itemId = GetString(root, "itemId");
                    if (!string.IsNullOrWhiteSpace(itemId))
                    {
                        BeginDragOutToDesktop(itemId);
                    }

                    break;
                }

                case "folder.deleteFolder":
                {
                    bool restoreItems = GetBool(root, "restoreItems") ?? true;
                    _manager.DeleteFolder(_folder.Id, restoreItems);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebMessage error: {ex}");
        }
    }

    private void HandleDroppedFiles(IReadOnlyList<object>? additionalObjects)
    {
        if (additionalObjects is null || additionalObjects.Count == 0)
        {
            return;
        }

        var paths = new List<string>();
        foreach (object obj in additionalObjects)
        {
            if (obj is CoreWebView2File file && !string.IsNullOrWhiteSpace(file.Path))
            {
                paths.Add(file.Path);
            }
        }

        if (paths.Count == 0)
        {
            return;
        }

        (int movedCount, List<string> errors) = FileOperations.MoveIntoFolder(_folder, paths);
        PushFolderData();
        PostToWeb(new
        {
            type = "host.dropResult",
            movedCount,
            errors
        });
    }

    private void OpenAddFilesDialog()
    {
        _suppressDeactivate = true;
        try
        {
            using var dialog = new OpenFileDialog
            {
                Title = IsTurkish
                    ? $"{_folder.Name} klasörüne eklenecek dosyaları seç"
                    : $"Select files to add to {_folder.Name}",
                Multiselect = true,
                DereferenceLinks = false,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Filter = IsTurkish
                    ? "Tüm Dosyalar (*.*)|*.*|Kısayollar (*.lnk;*.url;*.exe)|*.lnk;*.url;*.exe"
                    : "All Files (*.*)|*.*|Shortcuts (*.lnk;*.url;*.exe)|*.lnk;*.url;*.exe"
            };

            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.FileNames.Length > 0)
            {
                FileOperations.MoveIntoFolder(_folder, dialog.FileNames);
                PushFolderData();
            }
        }
        finally
        {
            _suppressDeactivate = false;
        }
    }

    private void PickCustomIcon()
    {
        _suppressDeactivate = true;
        try
        {
            using var dialog = new OpenFileDialog
            {
                Title = IsTurkish
                    ? $"{_folder.Name} için özel ikon seç"
                    : $"Choose custom icon for {_folder.Name}",
                Multiselect = false,
                DereferenceLinks = true,
                Filter = IsTurkish
                    ? "İkon veya Resim (*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.webp;*.exe)|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.webp;*.exe|Tüm Dosyalar (*.*)|*.*"
                    : "Icon or Image (*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.webp;*.exe)|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.webp;*.exe|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.FileName))
            {
                string? savedFile = IconExtractor.SaveCustomFolderIcon(
                    _folder.Id,
                    dialog.FileName,
                    FolderStore.CustomIconsDir);

                if (!string.IsNullOrWhiteSpace(savedFile))
                {
                    _folder.CustomIconFile = savedFile;
                    _manager.SaveState();
                    PushFolderData();
                }
            }
        }
        finally
        {
            _suppressDeactivate = false;
        }
    }

    private void BeginDragOutToDesktop(string itemId)
    {
        _pendingDragOutItemId = itemId;
        _dragOutTimer.Stop();
        _dragOutTimer.Start();
    }

    private void OnDragOutTick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pendingDragOutItemId))
        {
            _dragOutTimer.Stop();
            return;
        }

        bool leftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
        if (leftDown)
        {
            return;
        }

        string itemId = _pendingDragOutItemId;
        _pendingDragOutItemId = null;
        _dragOutTimer.Stop();

        if (NativeMethods.GetCursorPos(out NativeMethods.POINT pt) && !Bounds.Contains(pt.X, pt.Y))
        {
            if (DesktopIconHelper.TryGetRecycleBinRect(out Rectangle rb) && rb.Contains(pt.X, pt.Y))
            {
                string itemPath = Path.Combine(FolderStore.GetFolderStoragePath(_folder), itemId);
                if (FileOperations.SendToRecycleBin(itemPath))
                {
                    _folder.ItemOrder.RemoveAll(x => x.Equals(itemId, StringComparison.OrdinalIgnoreCase));
                    _manager.SaveState();
                    PushFolderData();
                }
            }
            else if (FileOperations.MoveItemOutToDesktop(_folder, itemId, out string? movedFileName, out _))
            {
                if (!string.IsNullOrWhiteSpace(movedFileName))
                {
                    _ = DesktopIconHelper.SetDesktopItemPositionAsync(movedFileName, new Point(pt.X, pt.Y));
                }

                _folder.ItemOrder.RemoveAll(x => x.Equals(itemId, StringComparison.OrdinalIgnoreCase));
                _manager.SaveState();
                PushFolderData();
            }
        }
    }

    private void UpdateWatcherPath()
    {
        if (IsStandby)
        {
            return;
        }

        try
        {
            string storageDir = FolderStore.GetFolderStoragePath(_folder);
            if (_vaultWatcher is null)
            {
                _vaultWatcher = new FileSystemWatcher(storageDir)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                _vaultWatcher.Created += OnVaultChanged;
                _vaultWatcher.Deleted += OnVaultChanged;
                _vaultWatcher.Renamed += OnVaultChanged;
                return;
            }

            if (!string.Equals(_vaultWatcher.Path, storageDir, StringComparison.OrdinalIgnoreCase))
            {
                _vaultWatcher.EnableRaisingEvents = false;
                _vaultWatcher.Path = storageDir;
                _vaultWatcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic($"Watcher path update failed ({_folder.Id}): {ex.Message}");
        }
    }

    private void Expand(bool pinned, bool focusTitle = false)
    {
        if (_isDragging || IsStandby || IsDisposed || !_webViewReady || !_uiReady)
        {
            return;
        }

        _collapseTimer.Stop();
        _isCollapsing = false;

        if (_isExpanded)
        {
            if (pinned && _pinMode == 0)
            {
                PinCurrentExpanded(focusTitle);
            }
            else if (focusTitle)
            {
                if (IsPinned)
                {
                    NativeMethods.SetNoActivate(Handle, noActivate: false);
                    if (_pinMode == 1)
                    {
                        NativeMethods.ForceForegroundWindow(Handle);
                        Activate();
                    }

                    _webView.Focus();
                    ScheduleDelayedTitleFocus();
                }

                PostToWeb(new
                {
                    type = "host.state",
                    expanded = true,
                    pinned = IsPinned,
                    pinMode = _pinMode,
                    focusTitle = true
                });
            }

            return;
        }

        // Collapse other folders so screen stays clean
        _manager.CollapseOtherFolders(_folder.Id);

        _isExpanding = true;
        _isExpanded = true;
        _pinMode = pinned ? 1 : 0;

        try
        {
            Rectangle collapsedRect = ComputeCollapsedBounds();
            Rectangle expandedRect = ComputeExpandedBounds();

            // Calculate exact morph origin relative to expanded window so animation springs from icon center
            int originX = Math.Clamp((collapsedRect.X + collapsedRect.Width / 2) - expandedRect.X, 24, expandedRect.Width - 24);
            int originY = Math.Clamp((collapsedRect.Y + collapsedRect.Height / 2) - expandedRect.Y, 24, expandedRect.Height - 24);

            NativeMethods.SetNoActivate(Handle, noActivate: !pinned);
            ApplyBoundsAndZOrder(expanded: true, pinMode: _pinMode, explicitRect: expandedRect);

            if (pinned)
            {
                NativeMethods.ForceForegroundWindow(Handle);
                Activate();
                _webView.Focus();
            }

            _manager.SyncOverlapAndZOrder();

            PostToWeb(new
            {
                type = "host.state",
                expanded = true,
                pinned,
                pinMode = _pinMode,
                morphOrigin = $"{originX}px {originY}px",
                focusTitle
            });

            if (pinned && focusTitle)
            {
                ScheduleDelayedTitleFocus();
            }
        }
        finally
        {
            _isExpanding = false;
        }
    }

    private void PinCurrentExpanded(bool focusTitle = false)
    {
        if (!_isExpanded || _pinMode != 0)
        {
            return;
        }

        _isExpanding = true;
        _pinMode = 1;
        try
        {
            NativeMethods.SetNoActivate(Handle, noActivate: false);
            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HWND_TOP,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            NativeMethods.ForceForegroundWindow(Handle);
            Activate();
            _webView.Focus();
            _manager.SyncOverlapAndZOrder();
            PostToWeb(new
            {
                type = "host.state",
                expanded = true,
                pinned = true,
                pinMode = _pinMode,
                focusTitle
            });

            if (focusTitle)
            {
                ScheduleDelayedTitleFocus();
            }
        }
        finally
        {
            _isExpanding = false;
        }
    }

    private void SetPinMode(int mode)
    {
        if (!_isExpanded || _isCollapsing)
        {
            return;
        }

        switch (mode)
        {
            case 0:
                _pinMode = 0;
                RequestCollapse();
                break;

            case 1:
                _pinMode = 1;
                NativeMethods.SetNoActivate(Handle, noActivate: false);
                NativeMethods.SetWindowPos(
                    Handle,
                    NativeMethods.HWND_TOP,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
                NativeMethods.ForceForegroundWindow(Handle);
                Activate();
                _webView.Focus();
                PostPinState();
                _manager.SyncOverlapAndZOrder();
                break;

            case 2:
                _pinMode = 2;
                NativeMethods.SetNoActivate(Handle, noActivate: false);
                NativeMethods.SetWindowPos(
                    Handle,
                    NativeMethods.HWND_BOTTOM,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                PostPinState();
                _manager.SyncOverlapAndZOrder();
                break;
        }
    }

    private void PostPinState()
    {
        PostToWeb(new
        {
            type = "host.pinState",
            pinMode = _pinMode,
            pinned = IsPinned
        });
    }

    private void ScheduleDelayedTitleFocus()
    {
        _focusTitleTimer.Stop();
        _focusTitleTimer.Start();
    }

    private void RequestCollapse()
    {
        if (!_isExpanded || _isCollapsing)
        {
            return;
        }

        _focusTitleTimer.Stop();
        _isCollapsing = true;
        _pinMode = 0;
        PostToWeb(new
        {
            type = "host.state",
            expanded = false,
            pinned = false,
            pinMode = 0
        });
        _collapseTimer.Stop();
        _collapseTimer.Interval = Math.Max(220, _manager.State.AnimationDurationMs + 140);
        _collapseTimer.Start();
    }

    private void CompleteCollapse()
    {
        _collapseTimer.Stop();
        _focusTitleTimer.Stop();
        bool needsResize = IsHandleCreated && (Width != CollapsedWidth || Height != CollapsedHeight);
        if (!_isExpanded && !_isCollapsing && !needsResize)
        {
            return;
        }

        _isCollapsing = false;
        _isExpanded = false;
        _pinMode = 0;

        NativeMethods.SetNoActivate(Handle, noActivate: true);
        ApplyBoundsAndZOrder(expanded: false, pinMode: 0);
        PostToWeb(new { type = "host.collapsedReady" });
        _manager.SyncOverlapAndZOrder();
    }

    private void StartWindowDrag()
    {
        if (_isExpanded || _isDragging)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
        {
            return;
        }

        _isDragging = true;
        _isOverTrash = false;
        _cachedRecycleBinRect = DesktopIconHelper.TryGetRecycleBinRect(out Rectangle rb) ? rb : null;
        _dragCursorStart = new Point(pt.X, pt.Y);
        _dragFormStart = Location;
        _dragTimer.Start();
    }

    private void OnDragTick(object? sender, EventArgs e)
    {
        if (!_isDragging)
        {
            _dragTimer.Stop();
            return;
        }

        bool leftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
        bool hasCursor = NativeMethods.GetCursorPos(out NativeMethods.POINT pt);
        if (!leftDown)
        {
            _isDragging = false;
            _dragTimer.Stop();

            bool droppedOnTrash = _isOverTrash ||
                (hasCursor && _cachedRecycleBinRect?.Contains(pt.X, pt.Y) == true);

            if (droppedOnTrash)
            {
                _isOverTrash = false;
                _cachedRecycleBinRect = null;
                PostToWeb(new { type = "host.dragOverTrash", overTrash = false });
                _manager.DeleteFolder(_folder.Id, restoreItemsToDesktop: false, sendToRecycleBin: true);
                return;
            }

            if (_isOverTrash)
            {
                _isOverTrash = false;
                PostToWeb(new { type = "host.dragOverTrash", overTrash = false });
            }

            _cachedRecycleBinRect = null;

            Point snapped = DesktopIconHelper.FindNonOverlappingPosition(
                Location,
                Size,
                _manager.GetOtherFolderPositions(_folder.Id));

            if (snapped != Location)
            {
                NativeMethods.SetWindowPos(
                    Handle,
                    NativeMethods.HWND_BOTTOM,
                    snapped.X,
                    snapped.Y,
                    CollapsedWidth,
                    CollapsedHeight,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE);
            }

            _folder.X = snapped.X;
            _folder.Y = snapped.Y;
            _manager.SaveState();
            PostToWeb(new { type = "host.dragEnded" });
            _manager.SyncOverlapAndZOrder();
            return;
        }

        if (hasCursor)
        {
            bool over = _cachedRecycleBinRect.HasValue && _cachedRecycleBinRect.Value.Contains(pt.X, pt.Y);
            if (over != _isOverTrash)
            {
                _isOverTrash = over;
                PostToWeb(new { type = "host.dragOverTrash", overTrash = over });
            }

            int dx = pt.X - _dragCursorStart.X;
            int dy = pt.Y - _dragCursorStart.Y;
            Rectangle wa = Screen.FromPoint(new Point(pt.X, pt.Y)).WorkingArea;
            int newX = Math.Clamp(_dragFormStart.X + dx, wa.Left, wa.Right - CollapsedWidth);
            int newY = Math.Clamp(_dragFormStart.Y + dy, wa.Top, wa.Bottom - CollapsedHeight);

            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HWND_BOTTOM,
                newX,
                newY,
                CollapsedWidth,
                CollapsedHeight,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE);
        }
    }

    private Rectangle ComputeCollapsedBounds()
    {
        Screen screen = Screen.FromPoint(new Point(_folder.X + CollapsedWidth / 2, _folder.Y + CollapsedHeight / 2));
        Rectangle wa = screen.WorkingArea;

        int x = Math.Clamp(_folder.X, wa.Left, Math.Max(wa.Left, wa.Right - CollapsedWidth));
        int y = Math.Clamp(_folder.Y, wa.Top, Math.Max(wa.Top, wa.Bottom - CollapsedHeight));
        return new Rectangle(x, y, CollapsedWidth, CollapsedHeight);
    }

    private Rectangle ComputeExpandedBounds()
    {
        Rectangle collapsed = ComputeCollapsedBounds();
        Screen screen = Screen.FromRectangle(collapsed);
        Rectangle wa = screen.WorkingArea;

        int w = Math.Min(ExpandedWidth, wa.Width - 16);
        int h = Math.Min(ExpandedHeight, wa.Height - 16);

        // Center expanded panel around the collapsed icon, clamped cleanly inside working area
        int idealX = collapsed.X + (collapsed.Width - w) / 2;
        int idealY = collapsed.Y + (collapsed.Height - h) / 2;

        int x = Math.Clamp(idealX, wa.Left + 8, Math.Max(wa.Left + 8, wa.Right - w - 8));
        int y = Math.Clamp(idealY, wa.Top + 8, Math.Max(wa.Top + 8, wa.Bottom - h - 8));
        return new Rectangle(x, y, w, h);
    }

    private void ApplyBoundsAndZOrder(bool expanded, int pinMode, Rectangle? explicitRect = null)
    {
        Rectangle rect = explicitRect ?? (expanded ? ComputeExpandedBounds() : ComputeCollapsedBounds());
        if (!IsHandleCreated)
        {
            Bounds = rect;
            return;
        }

        IntPtr insertAfter = expanded
            ? (pinMode == 2
                ? NativeMethods.HWND_BOTTOM
                : (pinMode == 1 ? NativeMethods.HWND_TOP : NativeMethods.HWND_TOPMOST))
            : NativeMethods.HWND_BOTTOM;

        uint flags = (expanded && pinMode == 1)
            ? NativeMethods.SWP_SHOWWINDOW
            : NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;

        NativeMethods.SetWindowPos(
            Handle,
            insertAfter,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            flags);
    }

    private void EnsureDesktopVisibility()
    {
        if (IsStandby || _isExpanding || _isDragging)
        {
            return;
        }

        if (_isExpanded && _pinMode != 2)
        {
            return;
        }

        if (!_isExpanded && !_isCollapsing && IsHandleCreated && (Width != CollapsedWidth || Height != CollapsedHeight))
        {
            ApplyBoundsAndZOrder(expanded: false, pinMode: 0);
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        if (!Visible)
        {
            Show();
        }

        PinToBottom();
        if (_isExpanded && _pinMode == 2)
        {
            _manager.SyncOverlapAndZOrder();
        }
    }

    private void PinToBottom()
    {
        if (!IsHandleCreated || _isExpanding || (_isExpanded && _pinMode != 2))
        {
            return;
        }

        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_BOTTOM,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void OnVaultChanged(object sender, FileSystemEventArgs e)
    {
        RunOnUiThread(() =>
        {
            _vaultDebounceTimer.Stop();
            _vaultDebounceTimer.Start();
        });
    }

    private void PushFolderData()
    {
        if (IsStandby || !_webViewReady || !_uiReady)
        {
            return;
        }

        string customIconUrl = string.IsNullOrWhiteSpace(_folder.CustomIconFile)
            ? string.Empty
            : $"https://customicons.local/{_folder.CustomIconFile}";

        List<FolderItemDto> items = FileOperations.ListFolderItems(_folder);
        PostToWeb(new
        {
            type = "host.data",
            locale = IsTurkish ? "tr" : "en",
            folder = new
            {
                id = _folder.Id,
                name = _folder.Name,
                accentColor = _folder.AccentColor,
                previewGrid = _folder.PreviewGrid,
                customIconUrl
            },
            settings = new
            {
                hoverDelayMs = _manager.State.HoverDelayMs,
                animationDurationMs = _manager.State.AnimationDurationMs,
                glassOpacity = _manager.State.GlassOpacity
            },
            items
        });
    }

    private void PostToWeb(object payload)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            RunOnUiThread(() => PostToWeb(payload));
            return;
        }

        if (!_webViewReady || _webView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            string json = JsonSerializer.Serialize(payload);
            _webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch
        {
            // Ignore transient post failures
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (IsHandleCreated)
        {
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(action);
                }
                catch (ObjectDisposedException)
                {
                    // Form is closing.
                }
            }
            else
            {
                action();
            }

            return;
        }

        if (_uiSyncContext is not null)
        {
            _uiSyncContext.Post(_ =>
            {
                if (!IsDisposed)
                {
                    action();
                }
            }, null);
        }
        else
        {
            action();
        }
    }

    private static string FormatWindowTitle(string? folderName)
        => string.IsNullOrWhiteSpace(folderName) ? "TransparentFolder" : $"TransparentFolder - {folderName}";

    private static string? GetString(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static bool? GetBool(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out JsonElement el) && (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False)
            ? el.GetBoolean()
            : null;

    private static int? GetInt(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out JsonElement el) && el.TryGetInt32(out int val)
            ? val
            : null;
}
