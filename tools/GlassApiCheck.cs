using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DeskSprout;

internal static class GlassApiCheck
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    private static readonly List<string> Report = new List<string>();
    private static void Check(bool condition, string message)
    {
        Report.Add((condition ? "PASS " : "FAIL ") + message);
        if (!condition) throw new InvalidOperationException(message);
    }
    private static int Backdrop(Form form)
    {
        int value;
        int result = DwmGetWindowAttribute(form.Handle, 38, out value, sizeof(int));
        Check(result >= 0, "Read system backdrop attribute HRESULT=" + result);
        return value;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        string path = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/previews-v1.1/native-api-check.txt");
        int status = 0;
        try
        {
            Report.Add("NativeBackdrop.IsSupported=" + NativeBackdrop.IsSupported);
            Report.Add("NativeBackdrop.SupportsClearGlass=" + NativeBackdrop.SupportsClearGlass);
            Report.Add("HighContrast=" + SystemInformation.HighContrast);
            using (Form form = new Form())
            {
                form.FormBorderStyle = FormBorderStyle.None;
                form.ShowInTaskbar = false;
                form.ClientSize = new Size(340, 460);
                IntPtr ownHiddenHandle = form.Handle;
                Check(ownHiddenHandle != IntPtr.Zero && !form.Visible, "Own form handle exists and is hidden");
                bool clear = NativeBackdrop.Apply(form, true, false, true);
                Report.Add("ClearLightAccepted=" + clear);
                if (NativeBackdrop.SupportsClearGlass && !SystemInformation.HighContrast)
                {
                    Check(clear, "Clear glass API accepted");
                    Check(Backdrop(form) == 1, "Clear glass uses DWMSBT_NONE");
                    Check(NativeBackdrop.Apply(form, true, true, true), "Dark clear glass API accepted");
                    Check(Backdrop(form) == 1, "Dark clear glass uses DWMSBT_NONE");
                }
                else Report.Add("SKIP Explicit clear glass assertions: unsupported system or high contrast");
                bool acrylic = NativeBackdrop.Apply(form, true, false, false);
                Report.Add("AcrylicAccepted=" + acrylic);
                if (NativeBackdrop.IsSupported && !SystemInformation.HighContrast)
                {
                    Check(acrylic, "Acrylic API accepted");
                    Check(Backdrop(form) == 3, "Acrylic uses DWMSBT_TRANSIENTWINDOW");
                }
                Check(!NativeBackdrop.Apply(form, false, false, true), "Disabling returns false");
                Check(!form.Visible, "Form remained hidden throughout");
            }
            Report.Add("PASS Native API modes; no visible window, screenshot, or user state access");
        }
        catch (Exception ex)
        {
            status = 1;
            Report.Add("ERROR " + ex.GetType().Name + ": " + ex.Message);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllLines(path, Report);
        foreach (string line in Report) Console.WriteLine(line);
        return status;
    }
}