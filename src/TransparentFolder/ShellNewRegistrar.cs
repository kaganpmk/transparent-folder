using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TransparentFolder;

/// <summary>
/// Registers and unregisters the glass folder (.tfolder) entry in the Windows Explorer
/// right-click "New" context menu under HKCU without requiring UAC elevation.
/// </summary>
public static class ShellNewRegistrar
{
    public const string FileExtension = ".tfolder";
    public const string ProgId = "TransparentFolder.GlassFolder";

    private const string LegacyFileExtension = ".dfolder";
    private const string LegacyProgId = "DesktopFolders.CamKlasor";

    private const string ExtensionSubKey = @"Software\Classes\" + FileExtension;
    private const string ShellNewSubKey = ExtensionSubKey + @"\ShellNew";
    private const string ProgIdSubKey = @"Software\Classes\" + ProgId;
    private const string DefaultIconSubKey = ProgIdSubKey + @"\DefaultIcon";

    private const string LegacyExtensionSubKey = @"Software\Classes\" + LegacyFileExtension;
    private const string LegacyProgIdSubKey = @"Software\Classes\" + LegacyProgId;

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    public static string DisplayName =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)
            ? "Cam Klasör"
            : "Transparent Folder";

    public static string IconDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TransparentFolder");

    public static string IconPath => Path.Combine(IconDirectory, "transparent-folder.ico");

    /// <summary>
    /// Ensures the glass folder icon file exists and the HKCU ShellNew registration is applied,
    /// cleans legacy DesktopFolders ShellNew entries, and notifies Explorer to refresh.
    /// </summary>
    public static void EnsureRegistered()
    {
        RemoveLegacyRegistration();

        string icoPath = IconPath;
        EnsureIconFile(icoPath);

        using (RegistryKey extKey = Registry.CurrentUser.CreateSubKey(ExtensionSubKey, writable: true))
        {
            extKey.SetValue(null, ProgId, RegistryValueKind.String);
        }

        using (RegistryKey shellNewKey = Registry.CurrentUser.CreateSubKey(ShellNewSubKey, writable: true))
        {
            shellNewKey.SetValue("NullFile", string.Empty, RegistryValueKind.String);
        }

        using (RegistryKey progIdKey = Registry.CurrentUser.CreateSubKey(ProgIdSubKey, writable: true))
        {
            progIdKey.SetValue(null, DisplayName, RegistryValueKind.String);
        }

        using (RegistryKey defaultIconKey = Registry.CurrentUser.CreateSubKey(DefaultIconSubKey, writable: true))
        {
            defaultIconKey.SetValue(null, $"{icoPath},0", RegistryValueKind.String);
        }

        NotifyShellAssociationChanged();
    }

    /// <summary>
    /// Removes the HKCU ShellNew registration for .tfolder (and any legacy .dfolder keys)
    /// and refreshes Explorer.
    /// </summary>
    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(ExtensionSubKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(ProgIdSubKey, throwOnMissingSubKey: false);
        RemoveLegacyRegistration();

        try
        {
            if (File.Exists(IconPath))
            {
                File.Delete(IconPath);
            }
        }
        catch (IOException)
        {
            // Explorer may still hold a read handle on the cached icon file.
        }
        catch (UnauthorizedAccessException)
        {
        }

        NotifyShellAssociationChanged();
    }

    private static void RemoveLegacyRegistration()
    {
        Registry.CurrentUser.DeleteSubKeyTree(LegacyExtensionSubKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(LegacyProgIdSubKey, throwOnMissingSubKey: false);
    }

    private static void NotifyShellAssociationChanged()
    {
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    private static void EnsureIconFile(string icoPath)
    {
        string? dir = Path.GetDirectoryName(icoPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        byte[] icoBytes = BuildMultiResolutionIco(32, 64);

        try
        {
            if (File.Exists(icoPath))
            {
                byte[] existing = File.ReadAllBytes(icoPath);
                if (existing.AsSpan().SequenceEqual(icoBytes))
                {
                    return;
                }
            }

            File.WriteAllBytes(icoPath, icoBytes);
        }
        catch (IOException) when (File.Exists(icoPath))
        {
            // Keep existing icon if Explorer temporarily locks it.
        }
    }

    private static byte[] BuildMultiResolutionIco(params int[] sizes)
    {
        var frames = new List<(int Size, byte[] PngBytes)>(sizes.Length);
        foreach (int size in sizes)
        {
            frames.Add((size, RenderGlassFolderPng(size)));
        }

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // ICONDIR header (6 bytes)
        writer.Write((ushort)0); // Reserved
        writer.Write((ushort)1); // Type: 1 = Icon
        writer.Write((ushort)frames.Count);

        uint offset = (uint)(6 + (16 * frames.Count));

        // ICONDIRENTRY records (16 bytes each)
        foreach ((int size, byte[] pngBytes) in frames)
        {
            byte dimension = size >= 256 ? (byte)0 : (byte)size;
            writer.Write(dimension); // Width
            writer.Write(dimension); // Height
            writer.Write((byte)0);   // ColorCount (0 when >= 8bpp / PNG)
            writer.Write((byte)0);   // Reserved
            writer.Write((ushort)1); // Planes
            writer.Write((ushort)32);// BitCount
            writer.Write((uint)pngBytes.Length);
            writer.Write(offset);

            offset += (uint)pngBytes.Length;
        }

        // PNG payloads
        foreach ((_, byte[] pngBytes) in frames)
        {
            writer.Write(pngBytes);
        }

        writer.Flush();
        return ms.ToArray();
    }

    private static byte[] RenderGlassFolderPng(int size)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        float s = size / 32f;
        var cardRect = new RectangleF(2f * s, 2f * s, 28f * s, 28f * s);
        float radius = 7f * s;

        using GraphicsPath cardPath = CreateRoundedRect(cardRect, radius);

        // Dark glass base (#0d0f14)
        using (var bgBrush = new SolidBrush(Color.FromArgb(245, 13, 15, 20)))
        {
            g.FillPath(bgBrush, cardPath);
        }

        // Subtle top glass sheen
        var sheenRect = new RectangleF(cardRect.X, cardRect.Y, cardRect.Width, cardRect.Height * 0.55f);
        using (var sheenBrush = new LinearGradientBrush(
            sheenRect,
            Color.FromArgb(26, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255),
            LinearGradientMode.Vertical))
        {
            GraphicsState state = g.Save();
            g.SetClip(cardPath);
            g.FillRectangle(sheenBrush, sheenRect);
            g.Restore(state);
        }

        // Thin ice-blue border (#a9f1fe)
        using (var borderPen = new Pen(Color.FromArgb(225, 169, 241, 254), 1.35f * s))
        {
            borderPen.Alignment = PenAlignment.Center;
            g.DrawPath(borderPen, cardPath);
        }

        // 2x2 mini ice-blue app dots (#a9f1fe)
        float dotSize = 4.6f * s;
        float gap = 3.4f * s;
        float start = (size - (dotSize * 2f + gap)) / 2f;
        float[] coords = { start, start + dotSize + gap };

        using var dotBrush = new SolidBrush(Color.FromArgb(245, 169, 241, 254));
        foreach (float x in coords)
        {
            foreach (float y in coords)
            {
                g.FillEllipse(dotBrush, x, y, dotSize, dotSize);
            }
        }

        using var pngStream = new MemoryStream();
        bmp.Save(pngStream, ImageFormat.Png);
        return pngStream.ToArray();
    }

    private static GraphicsPath CreateRoundedRect(RectangleF bounds, float radius)
    {
        float diameter = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
