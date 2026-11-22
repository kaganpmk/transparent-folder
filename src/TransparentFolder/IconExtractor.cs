using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TransparentFolder;

public static class IconExtractor
{
    private static readonly Guid ShellItemImageFactoryGuid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".webp"
    };

    private static readonly HashSet<string> SupportedIconBinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ico", ".exe"
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfields0;
        public uint dsBitfields1;
        public uint dsBitfields2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
    private static extern int GetObject(IntPtr hObject, int nCount, out DIBSECTION lpObject);

    public static void ClearIconCache()
    {
        try
        {
            string cacheDir = FolderStore.IconsCacheDir;
            if (!Directory.Exists(cacheDir))
            {
                return;
            }

            foreach (string pngPath in Directory.EnumerateFiles(cacheDir, "*.png"))
            {
                try
                {
                    File.Delete(pngPath);
                }
                catch
                {
                    // Ignore locked cache files
                }
            }
        }
        catch
        {
            // Ignore cache directory errors
        }
    }

    public static string? SaveCustomFolderIcon(string folderId, string sourceImagePath, string customIconsDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folderId) ||
                string.IsNullOrWhiteSpace(sourceImagePath) ||
                string.IsNullOrWhiteSpace(customIconsDir) ||
                !File.Exists(sourceImagePath))
            {
                return null;
            }

            string ext = Path.GetExtension(sourceImagePath);
            if (!SupportedImageExtensions.Contains(ext) && !SupportedIconBinaryExtensions.Contains(ext))
            {
                return null;
            }

            Directory.CreateDirectory(customIconsDir);

            using Image? sourceImage = LoadCustomIconSource(sourceImagePath, ext);
            if (sourceImage is null || sourceImage.Width <= 0 || sourceImage.Height <= 0)
            {
                return null;
            }

            using var output = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(output))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                float scale = Math.Min(128f / sourceImage.Width, 128f / sourceImage.Height);
                int drawWidth = Math.Max(1, (int)Math.Round(sourceImage.Width * scale));
                int drawHeight = Math.Max(1, (int)Math.Round(sourceImage.Height * scale));
                int drawX = (128 - drawWidth) / 2;
                int drawY = (128 - drawHeight) / 2;

                using var attrs = new ImageAttributes();
                attrs.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(
                    sourceImage,
                    new Rectangle(drawX, drawY, drawWidth, drawHeight),
                    0,
                    0,
                    sourceImage.Width,
                    sourceImage.Height,
                    GraphicsUnit.Pixel,
                    attrs);
            }

            string safeFolderId = SanitizeFileSegment(folderId);
            string fileName = $"{safeFolderId}_{DateTime.UtcNow.Ticks}.png";
            string destPath = Path.Combine(customIconsDir, fileName);
            output.Save(destPath, ImageFormat.Png);
            return fileName;
        }
        catch
        {
            return null;
        }
    }

    private static Image? LoadCustomIconSource(string sourceImagePath, string ext)
    {
        if (SupportedIconBinaryExtensions.Contains(ext))
        {
            Bitmap? extracted = ExtractBestBitmap(sourceImagePath, 128);
            if (extracted is not null)
            {
                return extracted;
            }

            if (ext.Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var icon = new Icon(sourceImagePath, new Size(128, 128));
                return icon.ToBitmap();
            }

            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(sourceImagePath);
            using Image loaded = Image.FromStream(stream);
            return new Bitmap(loaded);
        }
        catch
        {
            // Fallback for formats such as .webp when GDI+ lacks a built-in decoder
            return ExtractBestBitmap(sourceImagePath, 128);
        }
    }

    private static string SanitizeFileSegment(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(value.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "folder" : cleaned;
    }

    public static string GetIconUrl(string filePath)
    {
        try
        {
            string stamp = GetFileStamp(filePath);
            string hash = ComputeShortHash($"{filePath.ToLowerInvariant()}|{stamp}");
            string fileName = $"{hash}.png";
            string cachePath = Path.Combine(FolderStore.IconsCacheDir, fileName);

            if (!File.Exists(cachePath))
            {
                using Bitmap? bmp = ExtractBestBitmap(filePath, 128);
                if (bmp is not null)
                {
                    bmp.Save(cachePath, ImageFormat.Png);
                }
                else
                {
                    return string.Empty;
                }
            }

            return $"https://icons.local/{fileName}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static Bitmap? ExtractBestBitmap(string filePath, int size)
    {
        Bitmap? shellBmp = TryExtractViaShellFactory(filePath, size);
        if (shellBmp is not null)
        {
            return shellBmp;
        }

        try
        {
            if (File.Exists(filePath))
            {
                using Icon? icon = Icon.ExtractAssociatedIcon(filePath);
                if (icon is not null)
                {
                    return icon.ToBitmap();
                }
            }
        }
        catch
        {
            // Ignore fallback error
        }

        return null;
    }

    private static Bitmap? TryExtractViaShellFactory(string filePath, int size)
    {
        NativeMethods.IShellItemImageFactory? factory = null;
        IntPtr hBitmap = IntPtr.Zero;

        try
        {
            int hr = NativeMethods.SHCreateItemFromParsingName(
                filePath,
                IntPtr.Zero,
                ShellItemImageFactoryGuid,
                out factory);

            if (hr != 0 || factory is null)
            {
                return null;
            }

            var requested = new NativeMethods.SIZE(size, size);
            hr = factory.GetImage(
                requested,
                NativeMethods.SIIGBF.SIIGBF_RESIZETOFIT | NativeMethods.SIIGBF.SIIGBF_BIGGERSIZEOK,
                out hBitmap);

            if (hr != 0 || hBitmap == IntPtr.Zero)
            {
                hr = factory.GetImage(
                    requested,
                    NativeMethods.SIIGBF.SIIGBF_ICONONLY,
                    out hBitmap);
            }

            if (hr != 0 || hBitmap == IntPtr.Zero)
            {
                return null;
            }

            return CreateAlphaPreservingBitmap(hBitmap);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(hBitmap);
            }

            if (factory is not null)
            {
                Marshal.ReleaseComObject(factory);
            }
        }
    }

    private static Bitmap? CreateAlphaPreservingBitmap(IntPtr hBitmap)
    {
        int bytesRead = GetObject(hBitmap, Marshal.SizeOf<DIBSECTION>(), out DIBSECTION ds);
        if (bytesRead == 0 ||
            ds.dsBm.bmWidth <= 0 ||
            ds.dsBm.bmHeight == 0)
        {
            return null;
        }

        if (ds.dsBm.bmBitsPixel == 32 && ds.dsBm.bmBits != IntPtr.Zero)
        {
            int width = ds.dsBm.bmWidth;
            int height = Math.Abs(ds.dsBm.bmHeight);
            int stride = Math.Abs(ds.dsBm.bmWidthBytes);
            if (stride < width * 4)
            {
                stride = width * 4;
            }

            bool hasBmih = bytesRead >= Marshal.SizeOf<DIBSECTION>() && ds.dsBmih.biSize >= 40;
            // Windows DIBs are bottom-up when biHeight > 0, top-down when biHeight < 0
            bool isBottomUp = hasBmih ? ds.dsBmih.biHeight > 0 : ds.dsBm.bmHeight > 0;

            int byteCount = stride * height;
            byte[] raw = new byte[byteCount];
            Marshal.Copy(ds.dsBm.bmBits, raw, 0, byteCount);

            bool hasAlpha = false;
            bool isStraightAlpha = false;
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x * 4;
                    byte b = raw[idx];
                    byte g = raw[idx + 1];
                    byte r = raw[idx + 2];
                    byte a = raw[idx + 3];

                    if (a != 0)
                    {
                        hasAlpha = true;
                        if (b > a || g > a || r > a)
                        {
                            isStraightAlpha = true;
                        }
                    }
                }
            }

            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = result.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                int rowCopyBytes = Math.Min(width * 4, Math.Min(stride, data.Stride));
                for (int y = 0; y < height; y++)
                {
                    int srcY = isBottomUp ? (height - 1 - y) : y;
                    int srcRow = srcY * stride;
                    IntPtr dstRow = data.Scan0 + y * data.Stride;

                    if (!hasAlpha)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            raw[srcRow + x * 4 + 3] = 255;
                        }
                    }
                    else if (!isStraightAlpha)
                    {
                        // Un-premultiply PARGB pixels to straight ARGB so PNG edges stay crisp without dark halos
                        for (int x = 0; x < width; x++)
                        {
                            int idx = srcRow + x * 4;
                            byte a = raw[idx + 3];
                            if (a == 0)
                            {
                                raw[idx] = 0;
                                raw[idx + 1] = 0;
                                raw[idx + 2] = 0;
                            }
                            else if (a < 255)
                            {
                                raw[idx] = (byte)Math.Min(255, (raw[idx] * 255 + (a >> 1)) / a);
                                raw[idx + 1] = (byte)Math.Min(255, (raw[idx + 1] * 255 + (a >> 1)) / a);
                                raw[idx + 2] = (byte)Math.Min(255, (raw[idx + 2] * 255 + (a >> 1)) / a);
                            }
                        }
                    }

                    Marshal.Copy(raw, srcRow, dstRow, rowCopyBytes);
                }
            }
            finally
            {
                result.UnlockBits(data);
            }

            return result;
        }

        return Image.FromHbitmap(hBitmap);
    }

    private static string GetFileStamp(string filePath)
    {
        try
        {
            if (Directory.Exists(filePath))
            {
                return Directory.GetLastWriteTimeUtc(filePath).Ticks.ToString();
            }

            if (File.Exists(filePath))
            {
                var info = new FileInfo(filePath);
                return $"{info.Length}_{info.LastWriteTimeUtc.Ticks}";
            }
        }
        catch
        {
            // Ignore
        }

        return "0";
    }

    private static string ComputeShortHash(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
