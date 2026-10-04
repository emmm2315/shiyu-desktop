using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeskSprout
{
    // Documented Windows 11 per-pixel glass and optional Desktop Acrylic blur.
    internal static class NativeBackdrop
    {
        private const int BackdropAttribute = 38;
        private const int RedirectionAlphaAttribute = 39;
        private static readonly int Build = ReadWindowsBuild();
        internal static bool IsSupported { get { return Environment.OSVersion.Platform == PlatformID.Win32NT && Build >= 22621; } }
        internal static bool SupportsClearGlass { get { return IsSupported && Build >= 26100; } }

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left, Right, Top, Bottom;
            public Margins(int value) { Left = Right = Top = Bottom = value; }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, ImageSize;
            public int XPelsPerMeter, YPelsPerMeter;
            public uint ColorsUsed, ColorsImportant;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo { public BitmapInfoHeader Header; public uint Color; }

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
        [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);

        private static int ReadWindowsBuild()
        {
            int result = Environment.OSVersion.Version.Build;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion"))
                {
                    int detected;
                    if (key != null && Int32.TryParse(Convert.ToString(key.GetValue("CurrentBuildNumber")), out detected)) result = detected;
                }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.IO.IOException) { }
            return result;
        }
        private static int Set(IntPtr window, int attribute, int value)
        {
            return DwmSetWindowAttribute(window, attribute, ref value, sizeof(int));
        }
        private static void Reset(IntPtr window)
        {
            Set(window, BackdropAttribute, 1); // DWMSBT_NONE
            if (Build >= 26100) Set(window, RedirectionAlphaAttribute, 0);
            Margins margins = new Margins(0);
            DwmExtendFrameIntoClientArea(window, ref margins);
        }

        // Returns true only when a composition mode was accepted. Call after handle
        // creation, on theme/size changes, and WM_DWMCOMPOSITIONCHANGED (0x031E).
        // True does not override Windows transparency, battery saver, or contrast policy.
        internal static bool Apply(Form form, bool enabled, bool dark, bool clear = false)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated || !IsSupported) return false;
            IntPtr window = form.Handle;
            try
            {
                Set(window, 20, dark ? 1 : 0); // DWMWA_USE_IMMERSIVE_DARK_MODE
                Set(window, 33, 2); // DWMWCP_ROUND
                if (!enabled || SystemInformation.HighContrast) { Reset(window); return false; }
                bool composition;
                if (DwmIsCompositionEnabled(out composition) < 0 || !composition) { Reset(window); return false; }
                Margins margins = new Margins(-1);
                if (DwmExtendFrameIntoClientArea(window, ref margins) < 0)
                {
                    Reset(window); return false;
                }
                // Clear per-pixel glass has no Acrylic material or inactive tint.
                if (clear && SupportsClearGlass && Set(window, BackdropAttribute, 1) >= 0 &&
                    Set(window, RedirectionAlphaAttribute, 1) >= 0) return true;
                if (Set(window, BackdropAttribute, 3) < 0) { Reset(window); return false; }
                // All pixels supplied by Present are premultiplied BGRA. Older
                // Windows 11 versions use alpha through the extended glass frame.
                if (Build >= 26100) Set(window, RedirectionAlphaAttribute, 1);
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        // Copy a physical-pixel PArgb32 frame without destroying its alpha channel.
        // Render the source on transparent black; keep text/buttons opaque. Call this
        // on the unscaled PaintEventArgs.Graphics with WinForms double buffering off.
        // A top-down DIB and SRCCOPY avoid GetHbitmap's background flattening.
        internal static bool Present(Graphics target, Bitmap source)
        {
            if (target == null || source == null) return false;
            if (source.PixelFormat != PixelFormat.Format32bppPArgb)
                throw new ArgumentException("The backdrop frame must use Format32bppPArgb.", "source");
            IntPtr targetDc = IntPtr.Zero, memoryDc = IntPtr.Zero, dib = IntPtr.Zero, original = IntPtr.Zero;
            BitmapData pixels = null;
            try
            {
                targetDc = target.GetHdc();
                memoryDc = CreateCompatibleDC(targetDc);
                if (memoryDc == IntPtr.Zero) return false;
                BitmapInfo info = new BitmapInfo();
                info.Header.Size = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader));
                info.Header.Width = source.Width; info.Header.Height = -source.Height;
                info.Header.Planes = 1; info.Header.BitCount = 32; // BI_RGB
                IntPtr destination;
                dib = CreateDIBSection(targetDc, ref info, 0, out destination, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero || destination == IntPtr.Zero) return false;
                original = SelectObject(memoryDc, dib);
                if (original == IntPtr.Zero || original == new IntPtr(-1)) return false;
                pixels = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                int rowBytes = checked(source.Width * 4);
                byte[] row = new byte[rowBytes];
                for (int y = 0; y < source.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(pixels.Scan0, y * pixels.Stride), row, 0, rowBytes);
                    Marshal.Copy(row, 0, IntPtr.Add(destination, y * rowBytes), rowBytes);
                }
                return BitBlt(targetDc, 0, 0, source.Width, source.Height, memoryDc, 0, 0, 0x00CC0020); // SRCCOPY
            }
            finally
            {
                if (pixels != null) source.UnlockBits(pixels);
                if (memoryDc != IntPtr.Zero && original != IntPtr.Zero && original != new IntPtr(-1)) SelectObject(memoryDc, original);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                if (dib != IntPtr.Zero) DeleteObject(dib);
                if (targetDc != IntPtr.Zero) target.ReleaseHdc(targetDc);
            }
        }
    }
}