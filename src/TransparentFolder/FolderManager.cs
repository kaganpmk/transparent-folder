using System.Globalization;
using Microsoft.Web.WebView2.Core;

namespace TransparentFolder;

internal sealed class FolderManager : ApplicationContext
{
    private readonly Control _uiHost;
    private readonly Dictionary<string, FolderForm> _forms = new(StringComparer.OrdinalIgnoreCase);
    private readonly DesktopTriggerWatcher _desktopWatcher;
    private FolderForm? _standbyForm;
    private Task<CoreWebView2Environment>? _sharedEnvTask;

    public FolderManager()
    {
        _uiHost = new Control();
        _uiHost.CreateControl();
        _ = _uiHost.Handle;

        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        }

        ShellNewRegistrar.EnsureRegistered();

        State = FolderStore.Load();
        FolderStore.ApplyAutoStart(State.AutoStart);

        _desktopWatcher = new DesktopTriggerWatcher(
            SynchronizationContext.Current,
            cleanupOrphansOnStart: true,
            autoStart: true);
        _desktopWatcher.NewFolderTriggered += OnDesktopNewFolderTriggered;

        foreach (FolderData folder in State.Folders)
        {
            SpawnFolderForm(folder);
        }

        WarmUpStandbyForm();
    }

    public AppState State { get; }

    public Task<CoreWebView2Environment> GetSharedEnvironmentAsync()
    {
        _sharedEnvTask ??= CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: FolderStore.WebViewDataDir);
        return _sharedEnvTask;
    }

    public void SaveState()
    {
        FolderStore.Save(State);
    }

    public void UpdateGlobalSettings(int? animationDurationMs, int? hoverDelayMs)
    {
        if (animationDurationMs.HasValue)
        {
            State.AnimationDurationMs = Math.Clamp(animationDurationMs.Value, 120, 650);
        }

        if (hoverDelayMs.HasValue)
        {
            State.HoverDelayMs = Math.Clamp(hoverDelayMs.Value, 150, 1000);
        }

        SaveState();
        foreach (FolderForm form in _forms.Values)
        {
            form.RefreshSettings();
        }
    }

    public IEnumerable<Point> GetOtherFolderPositions(string excludeFolderId)
    {
        foreach (FolderData folder in State.Folders)
        {
            if (!folder.Id.Equals(excludeFolderId, StringComparison.OrdinalIgnoreCase))
            {
                yield return new Point(folder.X, folder.Y);
            }
        }
    }

    public void CreateNewFolder()
    {
        Rectangle wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int baseX = 64;
        int baseY = 64;

        // Find a non-overlapping spot on desktop
        for (int attempt = 0; attempt < 30; attempt++)
        {
            int col = attempt % 6;
            int row = attempt / 6;
            int candidateX = wa.Left + 64 + col * 124;
            int candidateY = wa.Top + 64 + row * 136;

            bool occupied = State.Folders.Any(f =>
                Math.Abs(f.X - candidateX) < 80 && Math.Abs(f.Y - candidateY) < 80);

            if (!occupied)
            {
                baseX = candidateX;
                baseY = candidateY;
                break;
            }
        }

        CreateNewFolderNear(baseX, baseY);
    }

    private static bool IsTurkish =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase);

    public void CreateNewFolderNear(int x, int y)
    {
        int index = State.Folders.Count + 1;
        string defaultName = IsTurkish ? $"Klasör {index}" : $"Folder {index}";
        CreateNewFolderAt(defaultName, x, y, openForRename: true);
    }

    public void CreateNewFolderAt(string name, int x, int y, bool openForRename = false)
    {
        string normalizedName = string.IsNullOrWhiteSpace(name)
            ? (IsTurkish ? $"Klasör {State.Folders.Count + 1}" : $"Folder {State.Folders.Count + 1}")
            : name.Trim();

        var folder = new FolderData
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = normalizedName,
            AccentColor = "#a9f1fe",
            PreviewGrid = 2
        };

        // Ensure vault directory exists
        FolderStore.GetFolderStoragePath(folder);

        Point nonOverlapping = DesktopIconHelper.FindNonOverlappingPosition(
            new Point(x, y),
            new Size(FolderForm.CollapsedWidth, FolderForm.CollapsedHeight),
            GetOtherFolderPositions(folder.Id));

        folder.X = nonOverlapping.X;
        folder.Y = nonOverlapping.Y;

        State.Folders.Add(folder);
        SaveState();

        if (_standbyForm is { IsDisposed: false })
        {
            FolderForm form = _standbyForm;
            _standbyForm = null;
            _forms[folder.Id] = form;
            form.ActivateFromStandby(folder, openForRename);
            WarmUpStandbyForm();
        }
        else
        {
            FolderForm form = SpawnFolderForm(folder);
            if (openForRename)
            {
                form.OpenForRename();
            }
        }
    }

    public void DeleteFolder(string folderId, bool restoreItemsToDesktop, bool sendToRecycleBin = false)
    {
        if (_forms.TryGetValue(folderId, out FolderForm? form))
        {
            _forms.Remove(folderId);
            form.Close();
            form.Dispose();
        }

        FolderData? folder = State.Folders.FirstOrDefault(
            f => f.Id.Equals(folderId, StringComparison.OrdinalIgnoreCase));

        if (folder is not null)
        {
            State.Folders.RemoveAll(f => f.Id.Equals(folderId, StringComparison.OrdinalIgnoreCase));
            SaveState();
            FileOperations.DeleteFolderStorage(folder, restoreItemsToDesktop, sendToRecycleBin);
        }
        else
        {
            SaveState();
        }
    }

    public void CollapseOtherFolders(string activeFolderId)
    {
        foreach ((string id, FolderForm form) in _forms)
        {
            if (!id.Equals(activeFolderId, StringComparison.OrdinalIgnoreCase))
            {
                form.ForceCollapseIfOpen();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _standbyForm?.Dispose();
            _standbyForm = null;
            _desktopWatcher.NewFolderTriggered -= OnDesktopNewFolderTriggered;
            _desktopWatcher.Dispose();
            _uiHost.Dispose();
        }

        base.Dispose(disposing);
    }

    private void WarmUpStandbyForm()
    {
        if (_standbyForm is { IsDisposed: false })
        {
            return;
        }

        _standbyForm = new FolderForm(this, null, isStandby: true);
        _standbyForm.Show();
    }

    private void OnDesktopNewFolderTriggered(string name, Point cursorPos)
    {
        if (_uiHost.IsDisposed)
        {
            return;
        }

        if (_uiHost.InvokeRequired)
        {
            try
            {
                _uiHost.BeginInvoke(() => OnDesktopNewFolderTriggered(name, cursorPos));
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        CreateNewFolderAt(
            name,
            cursorPos.X - FolderForm.CollapsedWidth / 2,
            cursorPos.Y - FolderForm.CollapsedHeight / 2,
            openForRename: true);
    }

    private FolderForm SpawnFolderForm(FolderData folder)
    {
        if (_forms.TryGetValue(folder.Id, out FolderForm? existing))
        {
            return existing;
        }

        var form = new FolderForm(this, folder);
        _forms[folder.Id] = form;
        form.Show();
        return form;
    }
}
