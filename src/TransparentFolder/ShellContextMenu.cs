using System.Runtime.InteropServices;

namespace TransparentFolder;

public static class ShellContextMenu
{
    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    private const uint CmdFirst = 1;
    private const uint CmdLast = 0x7FFF;

    private const uint CMF_NORMAL = 0x00000000;
    private const uint CMF_EXPLORE = 0x00000004;

    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    private const uint CMIC_MASK_UNICODE = 0x00004000;
    private const uint CMIC_MASK_PTINVOKE = 0x20000000;

    private const int SW_SHOWNORMAL = 1;

    private const int WM_NULL = 0x0000;
    private const int WM_DRAWITEM = 0x002B;
    private const int WM_MEASUREITEM = 0x002C;
    private const int WM_INITMENUPOPUP = 0x0117;
    private const int WM_MENUCHAR = 0x0120;

    private const int WS_POPUP = unchecked((int)0x80000000);

    public static bool ShowForPath(IntPtr ownerHwnd, string filePath, Point screenPoint)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch
        {
            return false;
        }

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return false;
        }

        IntPtr pidl = IntPtr.Zero;
        IShellFolder? parentFolder = null;
        object? contextMenuObj = null;
        IntPtr hMenu = IntPtr.Zero;
        MenuMessageWindow? menuWindow = null;

        try
        {
            int hr = SHParseDisplayName(fullPath, IntPtr.Zero, out pidl, 0, out _);
            if (hr < 0 || pidl == IntPtr.Zero)
            {
                return false;
            }

            hr = SHBindToParent(pidl, IID_IShellFolder, out parentFolder, out IntPtr pidlChild);
            if (hr < 0 || parentFolder is null || pidlChild == IntPtr.Zero)
            {
                return false;
            }

            IntPtr[] childPidls = { pidlChild };
            hr = parentFolder.GetUIObjectOf(
                ownerHwnd,
                1,
                childPidls,
                IID_IContextMenu,
                IntPtr.Zero,
                out contextMenuObj);

            if (hr < 0 || contextMenuObj is not IContextMenu contextMenu)
            {
                return false;
            }

            hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero)
            {
                return false;
            }

            uint queryFlags = CMF_NORMAL | CMF_EXPLORE;
            hr = contextMenu.QueryContextMenu(hMenu, 0, CmdFirst, CmdLast, queryFlags);
            if (hr < 0)
            {
                return false;
            }

            IContextMenu2? contextMenu2 = contextMenuObj as IContextMenu2;
            IContextMenu3? contextMenu3 = contextMenuObj as IContextMenu3;

            menuWindow = new MenuMessageWindow(contextMenu2, contextMenu3);
            menuWindow.Create(ownerHwnd, screenPoint);

            NativeMethods.SetForegroundWindow(menuWindow.Handle);

            uint cmd = TrackPopupMenuEx(
                hMenu,
                TPM_LEFTALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD,
                screenPoint.X,
                screenPoint.Y,
                menuWindow.Handle,
                IntPtr.Zero);

            PostMessage(menuWindow.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (cmd >= CmdFirst && cmd <= CmdLast)
            {
                IntPtr verbOffset = new((int)(cmd - CmdFirst));
                var invokeInfo = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                    hwnd = ownerHwnd,
                    lpVerb = verbOffset,
                    lpParameters = IntPtr.Zero,
                    lpDirectory = IntPtr.Zero,
                    nShow = SW_SHOWNORMAL,
                    dwHotKey = 0,
                    hIcon = IntPtr.Zero,
                    lpTitle = IntPtr.Zero,
                    lpVerbW = verbOffset,
                    lpParametersW = IntPtr.Zero,
                    lpDirectoryW = IntPtr.Zero,
                    lpTitleW = IntPtr.Zero,
                    ptInvoke = new NativeMethods.POINT { X = screenPoint.X, Y = screenPoint.Y }
                };

                contextMenu.InvokeCommand(ref invokeInfo);
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (menuWindow is not null)
            {
                menuWindow.DestroyHandle();
            }

            if (hMenu != IntPtr.Zero)
            {
                DestroyMenu(hMenu);
            }

            if (contextMenuObj is not null)
            {
                Marshal.ReleaseComObject(contextMenuObj);
            }

            if (parentFolder is not null)
            {
                Marshal.ReleaseComObject(parentFolder);
            }

            if (pidl != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
    }

    private sealed class MenuMessageWindow : NativeWindow
    {
        private readonly IContextMenu2? _contextMenu2;
        private readonly IContextMenu3? _contextMenu3;

        public MenuMessageWindow(IContextMenu2? contextMenu2, IContextMenu3? contextMenu3)
        {
            _contextMenu2 = contextMenu2;
            _contextMenu3 = contextMenu3;
        }

        public void Create(IntPtr ownerHwnd, Point location)
        {
            var cp = new CreateParams
            {
                Style = WS_POPUP,
                ExStyle = NativeMethods.WS_EX_TOOLWINDOW,
                Parent = ownerHwnd,
                X = location.X,
                Y = location.Y,
                Width = 0,
                Height = 0
            };
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg is WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR)
            {
                if (_contextMenu3 is not null)
                {
                    int hr = _contextMenu3.HandleMenuMsg2(
                        (uint)m.Msg,
                        m.WParam,
                        m.LParam,
                        out IntPtr result);

                    if (hr >= 0)
                    {
                        m.Result = result;
                        return;
                    }
                }
                else if (_contextMenu2 is not null)
                {
                    int hr = _contextMenu2.HandleMenuMsg((uint)m.Msg, m.WParam, m.LParam);
                    if (hr >= 0)
                    {
                        m.Result = IntPtr.Zero;
                        return;
                    }
                }
            }

            base.WndProc(ref m);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public NativeMethods.POINT ptInvoke;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    private interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(
            IntPtr hwnd,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
            ref uint pchEaten,
            out IntPtr ppidl,
            ref uint pdwAttributes);

        [PreserveSig]
        int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);

        [PreserveSig]
        int BindToObject(
            IntPtr pidl,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IntPtr ppv);

        [PreserveSig]
        int BindToStorage(
            IntPtr pidl,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IntPtr ppv);

        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

        [PreserveSig]
        int CreateViewObject(
            IntPtr hwndOwner,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IntPtr ppv);

        [PreserveSig]
        int GetAttributesOf(
            uint cidl,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            ref uint rgfInOut);

        [PreserveSig]
        int GetUIObjectOf(
            IntPtr hwndOwner,
            uint cidl,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            IntPtr rgfReserved,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

        [PreserveSig]
        int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);

        [PreserveSig]
        int SetNameOf(
            IntPtr hwnd,
            IntPtr pidl,
            [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            uint uFlags,
            out IntPtr ppidlOut);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(
            IntPtr hmenu,
            uint indexMenu,
            uint idCmdFirst,
            uint idCmdLast,
            uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

        [PreserveSig]
        int GetCommandString(
            UIntPtr idCmd,
            uint uType,
            IntPtr pReserved,
            IntPtr pszName,
            uint cchMax);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    private interface IContextMenu2
    {
        [PreserveSig]
        int QueryContextMenu(
            IntPtr hmenu,
            uint indexMenu,
            uint idCmdFirst,
            uint idCmdLast,
            uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

        [PreserveSig]
        int GetCommandString(
            UIntPtr idCmd,
            uint uType,
            IntPtr pReserved,
            IntPtr pszName,
            uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    private interface IContextMenu3
    {
        [PreserveSig]
        int QueryContextMenu(
            IntPtr hmenu,
            uint indexMenu,
            uint idCmdFirst,
            uint idCmdLast,
            uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

        [PreserveSig]
        int GetCommandString(
            UIntPtr idCmd,
            uint uType,
            IntPtr pReserved,
            IntPtr pszName,
            uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);

        [PreserveSig]
        int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHBindToParent(
        IntPtr pidl,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellFolder ppv,
        out IntPtr ppidlLast);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(
        IntPtr hmenu,
        uint fuFlags,
        int x,
        int y,
        IntPtr hwnd,
        IntPtr lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
}
