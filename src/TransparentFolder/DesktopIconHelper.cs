using System.Runtime.InteropServices;

namespace TransparentFolder;

public static class DesktopIconHelper
{
    private const int LVM_FIRST = 0x1000;
    private const int LVM_GETITEMCOUNT = LVM_FIRST + 4;
    private const int LVM_SETITEMPOSITION = LVM_FIRST + 15;
    private const int LVM_GETITEMPOSITION = LVM_FIRST + 16;
    private const int LVM_SETITEMPOSITION32 = LVM_FIRST + 49; // 0x1031
    private const int LVM_GETITEMTEXTW = LVM_FIRST + 115;

    private const uint LVIF_TEXT = 0x0001;
    private const int MaxTextChars = 260;

    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;

    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;

    private const double MinCenterDistance = 88.0;

    public static IntPtr FindDesktopListView()
    {
        IntPtr progman = FindWindow("Progman", "Program Manager");
        if (progman == IntPtr.Zero)
        {
            progman = FindWindow("Progman", null);
        }

        if (progman != IntPtr.Zero)
        {
            IntPtr shellView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                IntPtr listView = FindListViewInShellView(shellView);
                if (listView != IntPtr.Zero)
                {
                    return listView;
                }
            }
        }

        IntPtr foundListView = IntPtr.Zero;
        EnumWindows((topHwnd, _) =>
        {
            IntPtr shellView = FindWindowEx(topHwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                IntPtr listView = FindListViewInShellView(shellView);
                if (listView != IntPtr.Zero)
                {
                    foundListView = listView;
                    return false;
                }
            }

            return true;
        }, IntPtr.Zero);

        if (foundListView != IntPtr.Zero)
        {
            return foundListView;
        }

        // Windows 11 24H2 fallback where WorkerW/SHELLDLL_DefView can sit under Progman
        if (progman != IntPtr.Zero)
        {
            EnumChildWindows(progman, (childHwnd, _) =>
            {
                string cls = NativeMethods.GetWindowClassName(childHwnd);
                if (string.Equals(cls, "SHELLDLL_DefView", StringComparison.Ordinal))
                {
                    IntPtr listView = FindListViewInShellView(childHwnd);
                    if (listView != IntPtr.Zero)
                    {
                        foundListView = listView;
                        return false;
                    }
                }

                return true;
            }, IntPtr.Zero);
        }

        return foundListView;
    }

    public static List<(string Name, Point Position)> GetDesktopIconPositions()
    {
        var results = new List<(string Name, Point Position)>();
        IntPtr hListView = FindDesktopListView();
        if (hListView == IntPtr.Zero)
        {
            return results;
        }

        int count = (int)SendMessage(hListView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
        if (count <= 0)
        {
            return results;
        }

        NativeMethods.GetWindowThreadProcessId(hListView, out uint processId);
        if (processId == 0)
        {
            return results;
        }

        IntPtr hProcess = OpenProcess(
            PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE,
            false,
            processId);

        if (hProcess == IntPtr.Zero)
        {
            return results;
        }

        int lvItemSize = Marshal.SizeOf<LVITEMW>();
        int pointSize = Marshal.SizeOf<NativeMethods.POINT>();
        int textByteCount = MaxTextChars * sizeof(char);

        IntPtr remoteLvItem = IntPtr.Zero;
        IntPtr remoteText = IntPtr.Zero;
        IntPtr remotePoint = IntPtr.Zero;
        IntPtr localLvItemPtr = IntPtr.Zero;

        try
        {
            remoteLvItem = VirtualAllocEx(
                hProcess,
                IntPtr.Zero,
                (UIntPtr)lvItemSize,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_READWRITE);

            remoteText = VirtualAllocEx(
                hProcess,
                IntPtr.Zero,
                (UIntPtr)textByteCount,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_READWRITE);

            remotePoint = VirtualAllocEx(
                hProcess,
                IntPtr.Zero,
                (UIntPtr)pointSize,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_READWRITE);

            if (remoteLvItem == IntPtr.Zero || remoteText == IntPtr.Zero || remotePoint == IntPtr.Zero)
            {
                return results;
            }

            localLvItemPtr = Marshal.AllocHGlobal(lvItemSize);
            byte[] textBuffer = new byte[textByteCount];
            byte[] pointBuffer = new byte[pointSize];

            for (int i = 0; i < count; i++)
            {
                var lvItem = new LVITEMW
                {
                    mask = LVIF_TEXT,
                    iItem = i,
                    iSubItem = 0,
                    pszText = remoteText,
                    cchTextMax = MaxTextChars
                };

                Marshal.StructureToPtr(lvItem, localLvItemPtr, false);
                if (!WriteProcessMemory(hProcess, remoteLvItem, localLvItemPtr, (UIntPtr)lvItemSize, out _))
                {
                    continue;
                }

                int charsCopied = (int)SendMessage(hListView, LVM_GETITEMTEXTW, new IntPtr(i), remoteLvItem);
                Array.Clear(textBuffer, 0, textBuffer.Length);
                string name = string.Empty;

                if (charsCopied > 0 &&
                    ReadProcessMemory(hProcess, remoteText, textBuffer, (UIntPtr)textByteCount, out _))
                {
                    int boundedChars = Math.Min(charsCopied, MaxTextChars - 1);
                    string rawText = System.Text.Encoding.Unicode.GetString(textBuffer, 0, boundedChars * 2);
                    int nullIdx = rawText.IndexOf('\0');
                    name = nullIdx >= 0 ? rawText[..nullIdx] : rawText;
                }

                IntPtr posResult = SendMessage(hListView, LVM_GETITEMPOSITION, new IntPtr(i), remotePoint);
                if (posResult == IntPtr.Zero ||
                    !ReadProcessMemory(hProcess, remotePoint, pointBuffer, (UIntPtr)pointSize, out _))
                {
                    continue;
                }

                var pt = MemoryMarshal.Read<NativeMethods.POINT>(pointBuffer);
                ClientToScreen(hListView, ref pt);
                results.Add((name, new Point(pt.X, pt.Y)));
            }
        }
        finally
        {
            if (localLvItemPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(localLvItemPtr);
            }

            if (remotePoint != IntPtr.Zero)
            {
                VirtualFreeEx(hProcess, remotePoint, UIntPtr.Zero, MEM_RELEASE);
            }

            if (remoteText != IntPtr.Zero)
            {
                VirtualFreeEx(hProcess, remoteText, UIntPtr.Zero, MEM_RELEASE);
            }

            if (remoteLvItem != IntPtr.Zero)
            {
                VirtualFreeEx(hProcess, remoteLvItem, UIntPtr.Zero, MEM_RELEASE);
            }

            CloseHandle(hProcess);
        }

        return results;
    }

    public static bool TryGetItemPosition(string nameOrFileName, out Point position)
    {
        position = Point.Empty;
        if (string.IsNullOrWhiteSpace(nameOrFileName))
        {
            return false;
        }

        List<(string Name, Point Position)> items = GetDesktopIconPositions();
        foreach ((string name, Point pos) in items)
        {
            if (string.Equals(name, nameOrFileName, StringComparison.OrdinalIgnoreCase))
            {
                position = pos;
                return true;
            }
        }

        string targetWithoutExt = Path.GetFileNameWithoutExtension(nameOrFileName);
        if (!string.IsNullOrWhiteSpace(targetWithoutExt))
        {
            foreach ((string name, Point pos) in items)
            {
                if (string.Equals(name, targetWithoutExt, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileNameWithoutExtension(name), targetWithoutExt, StringComparison.OrdinalIgnoreCase))
                {
                    position = pos;
                    return true;
                }
            }
        }

        return false;
    }

    public static async Task<bool> SetDesktopItemPositionAsync(string fileName, Point screenDropPoint)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        IntPtr lvHwnd = FindDesktopListView();
        if (lvHwnd == IntPtr.Zero)
        {
            return false;
        }

        var clientPt = new NativeMethods.POINT
        {
            X = screenDropPoint.X,
            Y = screenDropPoint.Y
        };

        if (!ScreenToClient(lvHwnd, ref clientPt))
        {
            return false;
        }

        int targetX = clientPt.X - 36;
        int targetY = clientPt.Y - 36;
        if (GetClientRect(lvHwnd, out NativeMethods.RECT clientRect) &&
            clientRect.Right > clientRect.Left &&
            clientRect.Bottom > clientRect.Top)
        {
            int maxX = Math.Max(0, clientRect.Right - 72);
            int maxY = Math.Max(0, clientRect.Bottom - 72);
            targetX = Math.Clamp(targetX, 0, maxX);
            targetY = Math.Clamp(targetY, 0, maxY);
        }
        else
        {
            targetX = Math.Max(0, targetX);
            targetY = Math.Max(0, targetY);
        }

        string? targetNoExt = Path.GetFileNameWithoutExtension(fileName);
        IntPtr lParam = MakeLParam(targetX, targetY);

        for (int attempt = 0; attempt < 15; attempt++)
        {
            int index = FindDesktopItemIndex(lvHwnd, name => IsMatchingName(name, fileName, targetNoExt));
            if (index >= 0)
            {
                SendMessage(lvHwnd, LVM_SETITEMPOSITION, new IntPtr(index), lParam);
                return true;
            }

            if (attempt < 14)
            {
                await Task.Delay(60).ConfigureAwait(false);
            }
        }

        return false;
    }

    public static bool TryGetRecycleBinRect(out Rectangle rect)
    {
        foreach ((string name, Point pos) in GetDesktopIconPositions())
        {
            if (name.Equals("Geri Dönüşüm Kutusu", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Recycle Bin", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Geri Dönüşüm", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Recycle Bin", StringComparison.OrdinalIgnoreCase))
            {
                rect = new Rectangle(pos.X - 16, pos.Y - 16, 112, 112);
                return true;
            }
        }

        rect = Rectangle.Empty;
        return false;
    }

    public static bool IsDesktopItemVisibleOnScreen(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return false;
        }

        Rectangle virtualScreen = SystemInformation.VirtualScreen;
        foreach ((string name, Point pos) in GetDesktopIconPositions())
        {
            if (string.Equals(name, folderName, StringComparison.OrdinalIgnoreCase))
            {
                return virtualScreen.Contains(pos);
            }
        }

        return false;
    }

    public static bool HideDesktopItemOffscreen(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return false;
        }

        IntPtr lvHwnd = FindDesktopListView();
        if (lvHwnd == IntPtr.Zero)
        {
            return false;
        }

        int index = FindDesktopItemIndex(
            lvHwnd,
            name => string.Equals(name, folderName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(lvHwnd, out uint processId);
        if (processId == 0)
        {
            return false;
        }

        IntPtr hProcess = OpenProcess(
            PROCESS_VM_OPERATION | PROCESS_VM_WRITE,
            false,
            processId);
        if (hProcess == IntPtr.Zero)
        {
            return false;
        }

        int offX = SystemInformation.VirtualScreen.Right + 4000;
        int offY = SystemInformation.VirtualScreen.Bottom + 4000;
        int pointSize = Marshal.SizeOf<NativeMethods.POINT>();

        IntPtr remotePt = IntPtr.Zero;
        IntPtr localPt = IntPtr.Zero;

        try
        {
            remotePt = VirtualAllocEx(
                hProcess,
                IntPtr.Zero,
                (UIntPtr)pointSize,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_READWRITE);
            if (remotePt == IntPtr.Zero)
            {
                return false;
            }

            localPt = Marshal.AllocHGlobal(pointSize);
            Marshal.StructureToPtr(new NativeMethods.POINT { X = offX, Y = offY }, localPt, false);

            if (!WriteProcessMemory(hProcess, remotePt, localPt, (UIntPtr)pointSize, out _))
            {
                return false;
            }

            SendMessage(lvHwnd, LVM_SETITEMPOSITION32, new IntPtr(index), remotePt);
            return true;
        }
        finally
        {
            if (localPt != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(localPt);
            }

            if (remotePt != IntPtr.Zero)
            {
                VirtualFreeEx(hProcess, remotePt, UIntPtr.Zero, MEM_RELEASE);
            }

            CloseHandle(hProcess);
        }
    }

    private static int FindDesktopItemIndex(IntPtr hListView, Func<string, bool> predicate)
    {
        int count = (int)SendMessage(hListView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
        if (count <= 0)
        {
            return -1;
        }

        NativeMethods.GetWindowThreadProcessId(hListView, out uint processId);
        if (processId == 0)
        {
            return -1;
        }

        IntPtr hProcess = OpenProcess(
            PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE,
            false,
            processId);

        if (hProcess == IntPtr.Zero)
        {
            return -1;
        }

        int lvItemSize = Marshal.SizeOf<LVITEMW>();
        int textByteCount = MaxTextChars * sizeof(char);

        IntPtr remoteLvItem = IntPtr.Zero;
        IntPtr remoteText = IntPtr.Zero;
        IntPtr localLvItemPtr = IntPtr.Zero;

        try
        {
            remoteLvItem = VirtualAllocEx(
                hProcess,
                IntPtr.Zero,
                (UIntPtr)lvItemSize,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_READWRITE);

            remoteText = VirtualAllocEx(
                hProcess,
                IntPtr.Zero,
                (UIntPtr)textByteCount,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_READWRITE);

            if (remoteLvItem == IntPtr.Zero || remoteText == IntPtr.Zero)
            {
                return -1;
            }

            localLvItemPtr = Marshal.AllocHGlobal(lvItemSize);
            byte[] textBuffer = new byte[textByteCount];

            for (int i = 0; i < count; i++)
            {
                var lvItem = new LVITEMW
                {
                    mask = LVIF_TEXT,
                    iItem = i,
                    iSubItem = 0,
                    pszText = remoteText,
                    cchTextMax = MaxTextChars
                };

                Marshal.StructureToPtr(lvItem, localLvItemPtr, false);
                if (!WriteProcessMemory(hProcess, remoteLvItem, localLvItemPtr, (UIntPtr)lvItemSize, out _))
                {
                    continue;
                }

                int charsCopied = (int)SendMessage(hListView, LVM_GETITEMTEXTW, new IntPtr(i), remoteLvItem);
                if (charsCopied <= 0)
                {
                    continue;
                }

                Array.Clear(textBuffer, 0, textBuffer.Length);
                if (!ReadProcessMemory(hProcess, remoteText, textBuffer, (UIntPtr)textByteCount, out _))
                {
                    continue;
                }

                int boundedChars = Math.Min(charsCopied, MaxTextChars - 1);
                string rawText = System.Text.Encoding.Unicode.GetString(textBuffer, 0, boundedChars * 2);
                int nullIdx = rawText.IndexOf('\0');
                string name = nullIdx >= 0 ? rawText[..nullIdx] : rawText;

                if (predicate(name))
                {
                    return i;
                }
            }

            return -1;
        }
        finally
        {
            if (localLvItemPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(localLvItemPtr);
            }

            if (remoteText != IntPtr.Zero)
            {
                VirtualFreeEx(hProcess, remoteText, UIntPtr.Zero, MEM_RELEASE);
            }

            if (remoteLvItem != IntPtr.Zero)
            {
                VirtualFreeEx(hProcess, remoteLvItem, UIntPtr.Zero, MEM_RELEASE);
            }

            CloseHandle(hProcess);
        }
    }

    public static Point FindNonOverlappingPosition(
        Point desired,
        Size size,
        IEnumerable<Point> otherFolderPoints,
        string? ignoreDesktopItemName = null)
    {
        Rectangle workArea = Screen.FromPoint(desired).WorkingArea;
        int maxX = Math.Max(workArea.Left, workArea.Right - size.Width);
        int maxY = Math.Max(workArea.Top, workArea.Bottom - size.Height);

        Point clampedDesired = new(
            Math.Clamp(desired.X, workArea.Left, maxX),
            Math.Clamp(desired.Y, workArea.Top, maxY));

        var obstacleCenters = new List<(double X, double Y)>();
        double halfW = size.Width / 2.0;
        double halfH = size.Height / 2.0;

        if (otherFolderPoints is not null)
        {
            foreach (Point other in otherFolderPoints)
            {
                obstacleCenters.Add((other.X + halfW, other.Y + halfH));
            }
        }

        string? ignoreNoExt = string.IsNullOrWhiteSpace(ignoreDesktopItemName)
            ? null
            : Path.GetFileNameWithoutExtension(ignoreDesktopItemName);

        foreach ((string name, Point pos) in GetDesktopIconPositions())
        {
            if (ignoreDesktopItemName is not null && IsMatchingName(name, ignoreDesktopItemName, ignoreNoExt))
            {
                continue;
            }

            // Ignore icons placed offscreen (e.g., hidden real folders at -2500, -2500)
            if (pos.X < workArea.Left - 128 || pos.Y < workArea.Top - 128 ||
                pos.X > workArea.Right + 128 || pos.Y > workArea.Bottom + 128)
            {
                continue;
            }

            obstacleCenters.Add((pos.X + 38.0, pos.Y + 38.0));
        }

        const double minDistSq = MinCenterDistance * MinCenterDistance;

        if (GetMinDistanceSq(clampedDesired, halfW, halfH, obstacleCenters) >= minDistSq)
        {
            return clampedDesired;
        }

        const int step = 22;
        int maxSpan = Math.Max(workArea.Width, workArea.Height);
        int maxRing = (maxSpan / step) + 2;

        Point bestFallback = clampedDesired;
        double bestFallbackDistSq = GetMinDistanceSq(clampedDesired, halfW, halfH, obstacleCenters);

        for (int ring = 1; ring <= maxRing; ring++)
        {
            var ringCandidates = new List<(Point Pt, long DesiredDistSq)>();

            for (int ix = -ring; ix <= ring; ix++)
            {
                for (int iy = -ring; iy <= ring; iy++)
                {
                    if (Math.Max(Math.Abs(ix), Math.Abs(iy)) != ring)
                    {
                        continue;
                    }

                    int x = clampedDesired.X + ix * step;
                    int y = clampedDesired.Y + iy * step;

                    if (x < workArea.Left || x > maxX || y < workArea.Top || y > maxY)
                    {
                        continue;
                    }

                    long dx = x - clampedDesired.X;
                    long dy = y - clampedDesired.Y;
                    ringCandidates.Add((new Point(x, y), dx * dx + dy * dy));
                }
            }

            if (ringCandidates.Count == 0)
            {
                continue;
            }

            ringCandidates.Sort((a, b) => a.DesiredDistSq.CompareTo(b.DesiredDistSq));

            foreach ((Point candidate, _) in ringCandidates)
            {
                double minObsDistSq = GetMinDistanceSq(candidate, halfW, halfH, obstacleCenters);
                if (minObsDistSq >= minDistSq)
                {
                    return candidate;
                }

                if (minObsDistSq > bestFallbackDistSq)
                {
                    bestFallbackDistSq = minObsDistSq;
                    bestFallback = candidate;
                }
            }
        }

        return bestFallback;
    }

    private static double GetMinDistanceSq(
        Point candidate,
        double halfW,
        double halfH,
        List<(double X, double Y)> obstacleCenters)
    {
        if (obstacleCenters.Count == 0)
        {
            return double.MaxValue;
        }

        double cx = candidate.X + halfW;
        double cy = candidate.Y + halfH;
        double minSq = double.MaxValue;

        foreach ((double ox, double oy) in obstacleCenters)
        {
            double dx = cx - ox;
            double dy = cy - oy;
            double dSq = dx * dx + dy * dy;
            if (dSq < minSq)
            {
                minSq = dSq;
            }
        }

        return minSq;
    }

    private static bool IsMatchingName(string iconName, string target, string? targetNoExt)
    {
        if (string.Equals(iconName, target, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(targetNoExt))
        {
            if (string.Equals(iconName, targetNoExt, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileNameWithoutExtension(iconName), targetNoExt, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IntPtr FindListViewInShellView(IntPtr shellView)
    {
        IntPtr listView = FindWindowEx(shellView, IntPtr.Zero, "SysListView32", "FolderView");
        if (listView == IntPtr.Zero)
        {
            listView = FindWindowEx(shellView, IntPtr.Zero, "SysListView32", null);
        }

        return listView;
    }

    private static IntPtr MakeLParam(int low, int high)
    {
        int packed = ((high & 0xFFFF) << 16) | (low & 0xFFFF);
        return new IntPtr(packed);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LVITEMW
    {
        public uint mask;
        public int iItem;
        public int iSubItem;
        public uint state;
        public uint stateMask;
        public IntPtr pszText;
        public int cchTextMax;
        public int iImage;
        public IntPtr lParam;
        public int iIndent;
        public int iGroupId;
        public uint cColumns;
        public IntPtr puColumns;
        public IntPtr piColFmt;
        public int iGroup;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref NativeMethods.POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref NativeMethods.POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeMethods.RECT lpRect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, UIntPtr nSize, out UIntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, UIntPtr nSize, out UIntPtr lpNumberOfBytesRead);
}
