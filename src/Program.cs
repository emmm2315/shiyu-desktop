using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DeskSprout
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
        [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SendMessageTimeout(IntPtr h,uint m,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
        [STAThread]
        private static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 2 && args[0] == "--render") {
                MainForm.RenderSamples(Path.GetFullPath(args[1])); return;
            }
            if(args.Length>=2 && args[0]=="--export-learning-plan") {LearningPack.Export(args.Length>2?args[2]:LearningPack.Id,Path.GetFullPath(args[1]));return;}
#if DIAGNOSTICS
            if (args.Length >= 2 && args[0] == "--verify-live") {
                MainForm.RunLiveCheck(Path.GetFullPath(args[1])); return;
            }
#endif
            bool first;
            using (Mutex mutex = new Mutex(true, "Local\\DeskSprout.Widget", out first)) {
                if (!first) {
                    IntPtr previous=FindWindow(null,"时屿 · 桌面组件");
                    if(previous!=IntPtr.Zero) {
                        IntPtr version;SendMessageTimeout(previous,0x8003,IntPtr.Zero,IntPtr.Zero,2,600,out version);
                        if(version.ToInt64()<1201)MessageBox.Show("旧版本正在运行。\n请右键托盘中的时屿图标，选择“退出”，再重新打开新版。\n\n已有日程和正在运行的计时会自动恢复。","打开新版时屿",MessageBoxButtons.OK,MessageBoxIcon.Information);
                        else PostMessage(previous,0x8001,IntPtr.Zero,IntPtr.Zero);
                    }
                    return;
                }
                Application.ThreadException += delegate(object sender,ThreadExceptionEventArgs e) { Log(e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender,UnhandledExceptionEventArgs e) { Log(e.ExceptionObject as Exception); };
                try { using(MainForm form=new MainForm(false)) Application.Run(form); }
                catch(Exception ex) {Log(ex); MessageBox.Show("组件启动失败，请查看本机数据目录中的 error.log。\n"+ex.Message,"时屿",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            }
        }
        internal static void Log(Exception ex)
        {
            try {
                string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskSprout");
                Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir,"error.log"),DateTime.Now.ToString("s")+" "+ex+Environment.NewLine);
            } catch { }
        }
    }
}
