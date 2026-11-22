using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TransparentFolder;

/// <summary>
/// Registers and unregisters the "Transparent Folders" / "Cam Klasörler" virtual namespace node
/// in the Windows File Explorer navigation pane (sidebar) under HKCU without showing on the desktop.
/// </summary>
public static class ExplorerNavRegistrar
{
    public const string Clsid = "{7E4B2F91-8C3A-4D52-9B1E-3A6F4C8D2E10}";
    private const string FolderShortcutDelegateClsid = "{0E5AAE11-A475-4c5b-AB00-C66DE400274E}";

    private const string ClsidSubKey = @"Software\Classes\CLSID\" + Clsid;
    private const string DefaultIconSubKey = ClsidSubKey + @"\DefaultIcon";
    private const string InProcServer32SubKey = ClsidSubKey + @"\InProcServer32";
    private const string InstanceSubKey = ClsidSubKey + @"\Instance";
    private const string InitPropertyBagSubKey = InstanceSubKey + @"\InitPropertyBag";
    private const string ShellFolderSubKey = ClsidSubKey + @"\ShellFolder";

    private const string DesktopNameSpaceSubKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\" + Clsid;

    private const string HideDesktopIconsSubKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    public static string DisplayName =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)
            ? "Cam Klasörler"
            : "Transparent Folders";

    /// <summary>
    /// Registers the File Explorer navigation pane node pointing to the vault folder
    /// while hiding it from the desktop wallpaper via HideDesktopIcons\NewStartPanel.
    /// </summary>
    public static void EnsureRegistered()
    {
        string displayName = DisplayName;
        string vaultRoot = FolderStore.VaultRoot;
        string iconValue = $"{ShellNewRegistrar.IconPath},0";

        using (RegistryKey clsidKey = Registry.CurrentUser.CreateSubKey(ClsidSubKey, writable: true))
        {
            clsidKey.SetValue(null, displayName, RegistryValueKind.String);
            clsidKey.SetValue("System.IsPinnedToNameSpaceTree", 1, RegistryValueKind.DWord);
            clsidKey.SetValue("SortOrderIndex", 0x42, RegistryValueKind.DWord);
        }

        using (RegistryKey iconKey = Registry.CurrentUser.CreateSubKey(DefaultIconSubKey, writable: true))
        {
            iconKey.SetValue(null, iconValue, RegistryValueKind.String);
        }

        using (RegistryKey inProcKey = Registry.CurrentUser.CreateSubKey(InProcServer32SubKey, writable: true))
        {
            inProcKey.SetValue(null, @"%SYSTEMROOT%\system32\shell32.dll", RegistryValueKind.ExpandString);
            inProcKey.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
        }

        using (RegistryKey instanceKey = Registry.CurrentUser.CreateSubKey(InstanceSubKey, writable: true))
        {
            instanceKey.SetValue("CLSID", FolderShortcutDelegateClsid, RegistryValueKind.String);
        }

        using (RegistryKey bagKey = Registry.CurrentUser.CreateSubKey(InitPropertyBagSubKey, writable: true))
        {
            bagKey.SetValue("Attributes", 0x11, RegistryValueKind.DWord);
            bagKey.SetValue("TargetFolderPath", vaultRoot, RegistryValueKind.String);
        }

        using (RegistryKey shellFolderKey = Registry.CurrentUser.CreateSubKey(ShellFolderSubKey, writable: true))
        {
            shellFolderKey.SetValue("FolderValueFlags", 0x28, RegistryValueKind.DWord);
            shellFolderKey.SetValue("Attributes", unchecked((int)0xF080004D), RegistryValueKind.DWord);
        }

        using (RegistryKey nsKey = Registry.CurrentUser.CreateSubKey(DesktopNameSpaceSubKey, writable: true))
        {
            nsKey.SetValue(null, displayName, RegistryValueKind.String);
        }

        using (RegistryKey hideKey = Registry.CurrentUser.CreateSubKey(HideDesktopIconsSubKey, writable: true))
        {
            hideKey.SetValue(Clsid, 1, RegistryValueKind.DWord);
        }

        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Removes all HKCU registry entries for the File Explorer navigation pane node and notifies Explorer.
    /// </summary>
    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(ClsidSubKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(DesktopNameSpaceSubKey, throwOnMissingSubKey: false);

        using (RegistryKey? hideKey = Registry.CurrentUser.OpenSubKey(HideDesktopIconsSubKey, writable: true))
        {
            hideKey?.DeleteValue(Clsid, throwOnMissingValue: false);
        }

        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
