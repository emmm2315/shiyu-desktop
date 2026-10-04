using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskSprout
{
    // Test-only partial: compiled into DialogLayerCheck.exe, never into the widget.
    internal sealed partial class MainForm
    {
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get {CreateParams value=base.CreateParams;value.ExStyle|=0x08000000|0x00000080;return value;}
        }
        [DllImport("user32.dll",EntryPoint="SetWindowPos",SetLastError=true)]
        private static extern bool LayerSetWindowPos(IntPtr window,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
        internal void LayerPrepare(bool pinned)
        {
            core.Settings.AlwaysOnTop=pinned;
            TopMost=false;
            if(pinned) TopMost=true;
            StartPosition=FormStartPosition.Manual;
            Location=new Point(-24000,-24000);
            // No-activation fixture startup can leave a cached managed pin value
            // before Windows has placed the existing HWND in its requested band.
            // Establish the real test precondition without activating or moving it.
            IntPtr insertAfter=new IntPtr(pinned ? -1 : -2); // TOPMOST / NOTOPMOST
            if(!LayerSetWindowPos(Handle,insertAfter,0,0,0,0,0x0013)) // NOSIZE|NOMOVE|NOACTIVATE
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        internal WidgetDialogBase LayerCreateDialog(string kind)
        {
            if(kind=="priority") return new PriorityDialog(core,DateTime.Today,false);
            if(kind=="duration") return new DurationDialog(600,false);
            if(kind=="pomodoro") return new PomodoroDialog(25,5,15,4,false,false);
            return new EntryDialog("Isolated layer test",DateTime.Today,false,"plan",false);
        }
        internal DialogResult LayerShowOwnedDialog(Form dialog) { return ShowOwnedDialog(dialog); }
        internal int LayerPaintHitCount()
        {
            using(Bitmap bitmap=new Bitmap(ClientSize.Width,ClientSize.Height))
            using(Graphics graphics=Graphics.FromImage(bitmap)) {
                graphics.ScaleTransform(scale,scale);
                PaintScene(graphics);
            }
            return hits.Count;
        }
        internal bool LayerBlocksWidgetAction()
        {
            bool called=false;
            RunWidgetAction(delegate{called=true;});
            return !called;
        }
        internal PomodoroDialog LayerEditPomodoro(Action<PomodoroDialog> callback)
        {
            PomodoroDialog captured=null;
            bool timedOut=false;
            Action capture=delegate {
                PomodoroDialog pending=activeDialog as PomodoroDialog;
                if(pending==null || pending.IsDisposed || captured!=null) return;
                captured=pending;
                Action queue=delegate {pending.BeginInvoke((Action)delegate{callback(pending);});};
                if(pending.Visible) queue();
                else pending.Shown+=delegate{queue();};
            };
            PaintEventHandler painted=delegate{capture();};
            Paint+=painted;
            using(System.Windows.Forms.Timer fallback=new System.Windows.Forms.Timer())
            using(System.Windows.Forms.Timer timeout=new System.Windows.Forms.Timer()) {
                fallback.Interval=40;
                fallback.Tick+=delegate{capture();};
                timeout.Interval=4000;
                timeout.Tick+=delegate {
                    timedOut=true;timeout.Stop();
                    if(activeDialog!=null && !activeDialog.IsDisposed) {
                        activeDialog.DialogResult=DialogResult.Cancel;activeDialog.Close();
                    }
                };
                try {fallback.Start();timeout.Start();EditPomodoro();}
                finally {Paint-=painted;fallback.Stop();timeout.Stop();}
            }
            if(timedOut) throw new TimeoutException("Actual EditPomodoro automation timed out.");
            if(captured==null) throw new InvalidOperationException("Actual EditPomodoro did not create a captured PomodoroDialog.");
            return captured;
        }
        internal bool LayerPomodoroSettingsMatch(int focus,int shortBreak,int longBreak,int cycle,bool automatic)
        {
            WidgetSettings settings=core.Settings;
            return settings.FocusMinutes==focus && settings.ShortBreakMinutes==shortBreak &&
                settings.LongBreakMinutes==longBreak && settings.SessionsBeforeLongBreak==cycle &&
                settings.AutoAdvance==automatic && core.Pomodoro.DurationSeconds==focus*60;
        }
        internal bool LayerSettingPinned { get { return core.Settings.AlwaysOnTop; } }
        internal string LayerStatePath { get { return core.StatePath; } }
        internal void LayerClose()
        {
            // Preview SaveQuietly does not write a state file.
            exiting=true;
            Close();
        }
    }

    internal sealed class LayerNoActivateDialog : Form
    {
        internal LayerNoActivateDialog()
        {
            Text="Isolated native ring check";
            ClientSize=new Size(320,200);
            ShowInTaskbar=false;
            StartPosition=FormStartPosition.Manual;
            Location=new Point(-24000,-24000);
        }
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams
        {
            get {CreateParams value=base.CreateParams;value.ExStyle|=0x08000000|0x00000080;return value;}
        }
    }
    internal static class DialogLayerCheck
    {
        private const int ExStyle=-20;
        private const int TopmostStyle=0x00000008;
        private const uint OwnerWindow=4;
        private static int passed,failed,skipped;
        private delegate bool EnumWindowCallback(IntPtr window,IntPtr data);
        [DllImport("user32.dll",SetLastError=true)] private static extern bool EnumWindows(EnumWindowCallback callback,IntPtr data);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window,uint command);
        [DllImport("user32.dll",EntryPoint="GetWindowLongW",SetLastError=true)] private static extern int GetWindowLong(IntPtr window,int index);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window,IntPtr region);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
        [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region,int x,int y);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        private static void Assert(bool condition,string message)
        {
            if(!condition) throw new InvalidOperationException(message);
        }
        private static bool NativeTopmost(IntPtr handle)
        {
            return (GetWindowLong(handle,ExStyle)&TopmostStyle)!=0;
        }
        private static void AssertOffscreen(Form form,string name)
        {
            Rectangle desktop=SystemInformation.VirtualScreen;
            Assert(!desktop.IntersectsWith(form.Bounds),name+" must stay outside the entire visible desktop.");
        }
        private static void AssertAboveOwner(IntPtr dialog,IntPtr owner)
        {
            int position=0,dialogPosition=-1,ownerPosition=-1;
            // Enumerate only handles and order: no titles, pixels or contents of other windows.
            EnumWindowCallback callback=delegate(IntPtr window,IntPtr data) {
                if(window==dialog) dialogPosition=position;
                if(window==owner) ownerPosition=position;
                position++;
                return true;
            };
            Assert(EnumWindows(callback,IntPtr.Zero),"EnumWindows failed: "+Marshal.GetLastWin32Error());
            Assert(dialogPosition>=0 && ownerPosition>=0,"Both test windows must occur in the native Z order.");
            Assert(dialogPosition<ownerPosition,"Modal dialog must be ahead of its owner in native Z order: dialog="+dialogPosition+", owner="+ownerPosition);
        }
        private static NumericUpDown PomodoroField(PomodoroDialog dialog,string name)
        {
            foreach(Control control in dialog.Controls) {
                NumericUpDown field=control as NumericUpDown;
                if(field!=null && field.AccessibleName==name) return field;
            }
            throw new InvalidOperationException("Missing actual Pomodoro input: "+name);
        }
        private static void FillPomodoro(PomodoroDialog dialog,int focus,int shortBreak,int longBreak,int cycle,bool automatic)
        {
            PomodoroField(dialog,"专注时长，分钟").Value=focus;
            PomodoroField(dialog,"短休息时长，分钟").Value=shortBreak;
            PomodoroField(dialog,"长休息时长，分钟").Value=longBreak;
            PomodoroField(dialog,"长休息间隔，轮").Value=cycle;
            bool found=false;
            foreach(Control control in dialog.Controls) {
                CheckBox check=control as CheckBox;
                if(check!=null) {check.Checked=automatic;found=true;}
            }
            Assert(found,"Actual Pomodoro automatic-stage checkbox must exist.");
        }
        private static void ClickDialogButton(Form dialog,bool save)
        {
            Button button=(save ? dialog.AcceptButton : dialog.CancelButton) as Button;
            Assert(button!=null && button.Visible && button.Enabled,"Actual modal button must be visible and enabled.");
            button.PerformClick();
        }
        private static void AssertPomodoroResult(PomodoroDialog dialog)
        {
            Assert(dialog.FocusMinutes==30 && dialog.ShortMinutes==7 && dialog.LongMinutes==20 &&
                dialog.CycleLength==3 && dialog.AutoAdvance,"Actual save button must return all edited Pomodoro values.");
        }
        private static void CheckCase(MainForm owner,string kind,bool pinned)
        {
            owner.LayerPrepare(pinned);
            Assert(owner.Visible && IsWindowVisible(owner.Handle),"Test owner must be a real visible native window.");
            Assert(owner.Enabled && IsWindowEnabled(owner.Handle),"Owner must start enabled.");
            Assert(NativeTopmost(owner.Handle)==pinned,"Owner must start with the requested native topmost style. Requested="+pinned+", managed="+owner.TopMost+", nativeEx="+GetWindowLong(owner.Handle,ExStyle).ToString("X"));
            AssertOffscreen(owner,"Owner");
            Assert(GetForegroundWindow()!=owner.Handle,"Isolated owner must not become the foreground window.");
            IntPtr originalForeground=GetForegroundWindow();
            bool inspected=false;
            Exception modalError=null;
            WidgetDialogBase dialog=owner.LayerCreateDialog(kind);
            using(dialog)
            using(System.Windows.Forms.Timer timeout=new System.Windows.Forms.Timer())
            {
                dialog.RenderingPreview=true;
                dialog.StartPosition=FormStartPosition.Manual;
                dialog.Location=new Point(-24000,-24000);
                timeout.Interval=4000;
                timeout.Tick+=delegate {
                    timeout.Stop();
                    if(!inspected) modalError=new TimeoutException("Modal inspection did not run within four seconds.");
                    dialog.DialogResult=DialogResult.Cancel;
                    dialog.Close();
                };
                dialog.Shown+=delegate {
                    dialog.BeginInvoke((Action)delegate {
                        try {
                            Assert(dialog.Visible && IsWindowVisible(dialog.Handle),"Dialog must be a real visible native window.");
                            Assert(dialog.Modal,"Dialog must be shown with the modal ShowDialog path.");
                            Assert(dialog.Owner==owner,"Managed Owner must be the test main form.");
                            Assert(GetWindow(dialog.Handle,OwnerWindow)==owner.Handle,"Native GW_OWNER must be the test main form.");
                            Assert(dialog.TopMost==pinned && NativeTopmost(dialog.Handle)==pinned,
                                "Dialog's managed and native topmost flags must match the original owner.");
                            Assert(!IsWindowEnabled(owner.Handle),"Modal owner must be natively disabled.");
                            AssertAboveOwner(dialog.Handle,owner.Handle);
                            Assert(owner.LayerPaintHitCount()==0,"Repainting during modal editing must not recreate main-form interactive hit areas.");
                            Assert(owner.LayerBlocksWidgetAction(),"RunWidgetAction must not invoke another action while a modal dialog is open.");
                            Assert(owner.LayerPaintHitCount()==0,"Blocked widget actions must leave modal main-form hit areas empty.");
                            Assert(owner.LayerSettingPinned==pinned,"Opening a dialog must not rewrite the saved pin preference.");
                            AssertOffscreen(dialog,"Dialog");
                            IntPtr foreground=GetForegroundWindow();
                            Assert(foreground!=dialog.Handle && foreground!=owner.Handle,"Preview test windows must not take foreground activation.");
                            Assert(foreground==originalForeground,"Preview modal show must leave the original foreground window unchanged.");
                            inspected=true;
                            if(kind=="pomodoro") {
                                FillPomodoro((PomodoroDialog)dialog,30,7,20,3,true);
                                ClickDialogButton(dialog,true);
                                Assert(dialog.DialogResult==DialogResult.OK,"The actual Pomodoro save button must return OK.");
                                AssertPomodoroResult((PomodoroDialog)dialog);
                            }
                        } catch(Exception error) { modalError=error; }
                        finally {
                            timeout.Stop();
                            if(dialog.DialogResult==DialogResult.None) {
                                dialog.DialogResult=DialogResult.Cancel;dialog.Close();
                            }
                        }
                    });
                };
                timeout.Start();
                DialogResult result=owner.LayerShowOwnedDialog(dialog);
                timeout.Stop();
                if(modalError!=null) throw new InvalidOperationException("Native modal assertions failed for "+kind+" pinned="+pinned,modalError);
                Assert(inspected,"The Shown/BeginInvoke modal assertion path must run.");
                Assert(result==(kind=="pomodoro" ? DialogResult.OK : DialogResult.Cancel),"Actual save or automated close must return the expected result through the real helper.");
                Assert(!dialog.Visible,"The closed dialog must be hidden when the owned-dialog helper returns.");
            }
            Assert(dialog.IsDisposed,"The caller using scope must dispose its closed dialog.");
            Assert(owner.LayerPaintHitCount()>0,"Normal interactive hit areas must return after modal close.");
            Assert(GetForegroundWindow()==originalForeground,"Modal close must leave the original foreground window unchanged.");
            Assert(owner.Enabled && IsWindowEnabled(owner.Handle),"Owner must be enabled again after modal close.");
            Assert(owner.TopMost==pinned && NativeTopmost(owner.Handle)==pinned,
                "The exact original topmost state must be restored after modal close.");
            Assert(owner.LayerSettingPinned==pinned,"The saved pin setting must remain unchanged after modal close.");
            AssertOffscreen(owner,"Restored owner");
            Assert(!File.Exists(owner.LayerStatePath) && !File.Exists(owner.LayerStatePath+".bak"),
                "Preview test must not create or update any state file.");
        }

        private static void CheckActualPomodoroSaveCancel(MainForm owner)
        {
            owner.LayerPrepare(true);
            IntPtr foreground=GetForegroundWindow();
            for(int step=0;step<2;step++) {
                bool save=step==0;
                bool inspected=false;
                Exception modalError=null;
                PomodoroDialog result=owner.LayerEditPomodoro(delegate(PomodoroDialog dialog) {
                    try {
                        Assert(dialog.RenderingPreview,"Actual edit dialogs must inherit the test preview mode.");
                        AssertOffscreen(dialog,"Actual Pomodoro edit");
                        Assert(dialog.Modal && !IsWindowEnabled(owner.Handle),"Actual edit must disable its owner through ShowDialog.");
                        Assert(GetWindow(dialog.Handle,OwnerWindow)==owner.Handle,"Actual edit must preserve native owner.");
                        AssertAboveOwner(dialog.Handle,owner.Handle);
                        Assert(owner.LayerBlocksWidgetAction(),"Actual modal edit must block competing widget actions.");
                        Assert(owner.LayerPaintHitCount()==0,"Actual edit backdrop must expose no interactive hit areas.");
                        if(!save) {
                            Assert(PomodoroField(dialog,"专注时长，分钟").Value==30 &&
                                PomodoroField(dialog,"短休息时长，分钟").Value==7 &&
                                PomodoroField(dialog,"长休息时长，分钟").Value==20 &&
                                PomodoroField(dialog,"长休息间隔，轮").Value==3,
                                "Reopened actual edit must refill values saved in the prior edit.");
                        }
                        FillPomodoro(dialog,save ? 30 : 45,save ? 7 : 9,save ? 20 : 25,save ? 3 : 6,save);
                        ClickDialogButton(dialog,save);
                        Assert(dialog.DialogResult==(save ? DialogResult.OK : DialogResult.Cancel),"Actual save/cancel buttons must return their assigned result.");
                        if(save) AssertPomodoroResult(dialog);
                        Assert(GetForegroundWindow()==foreground,"Actual preview edit must preserve foreground activation.");
                        inspected=true;
                    } catch(Exception error) {modalError=error;}
                    finally {
                        if(!dialog.IsDisposed && dialog.DialogResult==DialogResult.None) {
                            dialog.DialogResult=DialogResult.Cancel;dialog.Close();
                        }
                    }
                });
                if(modalError!=null) throw new InvalidOperationException("Actual Pomodoro step "+step+" failed.",modalError);
                Assert(inspected && result.IsDisposed,"Both actual edits must be inspected and disposed by the existing caller.");
                Assert(owner.LayerPomodoroSettingsMatch(30,7,20,3,true),
                    "Actual save must apply settings and reset the timer; cancel must keep those saved values unchanged.");
                Assert(owner.Enabled && IsWindowEnabled(owner.Handle) && owner.TopMost && NativeTopmost(owner.Handle),
                    "Owner must be enabled and remain pinned after either actual edit.");
                Assert(owner.LayerPaintHitCount()>0,"Normal main buttons must return after either edit.");
                Assert(GetForegroundWindow()==foreground,"Actual edit return must preserve the original foreground.");
                AssertOffscreen(owner,"Actual edit owner");
            }
        }
        private static bool CheckNativeRing(MainForm owner)
        {
            owner.LayerPrepare(true);
            IntPtr foreground=GetForegroundWindow();
            bool inspected=false,nativeAvailable=false;
            Exception modalError=null;
            using(LayerNoActivateDialog dialog=new LayerNoActivateDialog())
            using(ModalGlassFrame frame=new ModalGlassFrame(dialog,false,true))
            using(System.Windows.Forms.Timer timeout=new System.Windows.Forms.Timer()) {
                timeout.Interval=4000;
                timeout.Tick+=delegate {
                    timeout.Stop();modalError=new TimeoutException("Native ring inspection timed out.");
                    dialog.DialogResult=DialogResult.Cancel;dialog.Close();
                };
                dialog.Shown+=delegate {
                    dialog.BeginInvoke((Action)delegate {
                        try {
                            const float bitmapScale=1.5F;
                            using(Bitmap bitmap=ModalGlassFrame.RenderRing(dialog.Size,bitmapScale,false)) {
                                int padding=ModalGlassFrame.PaddingForScale(bitmapScale);
                                Assert(bitmap.GetPixel(bitmap.Width/2,bitmap.Height/2).A==0,"Bitmap ring must leave the full input center transparent.");
                                byte alpha=bitmap.GetPixel(bitmap.Width/2,padding/2).A;
                                Assert(alpha>0 && alpha<=200,"Bitmap rim must retain a partial alpha value.");
                            }
                            AssertOffscreen(dialog,"Native ring input");
                            frame.PresentAttached();frame.Update();
                            nativeAvailable=frame.IsGlassActive && frame.Visible;
                            if(!nativeAvailable) {
                                Console.WriteLine("SKIP native glass frame: compositor/backend declined; bitmap alpha checks passed.");
                                inspected=true;return;
                            }
                            Assert(frame.Owner==dialog && GetWindow(frame.Handle,OwnerWindow)==dialog.Handle,"Native ring HWND must be owned by the input dialog.");
                            Assert(frame.TopMost && NativeTopmost(frame.Handle),"Native ring must inherit the pinned input's topmost band.");
                            Assert((GetWindowLong(frame.Handle,ExStyle)&0x08000000)!=0,"Native ring must retain WS_EX_NOACTIVATE.");
                            AssertAboveOwner(frame.Handle,dialog.Handle);
                            AssertOffscreen(frame,"Native ring frame");
                            Assert(frame.Region!=null,"Native ring must have a cut-out region.");
                            int centerX=frame.Width/2,centerY=frame.Height/2,rimY=Math.Max(1,frame.RingPadding/2);
                            Assert(!frame.Region.IsVisible(centerX,centerY) && frame.Region.IsVisible(centerX,rimY),
                                "Managed window region must exclude the input center and include the outside rim.");
                            IntPtr nativeRegion=CreateRectRgn(0,0,0,0);
                            Assert(nativeRegion!=IntPtr.Zero,"Native region handle allocation must succeed.");
                            try {
                                Assert(GetWindowRgn(frame.Handle,nativeRegion)>0,"Actual frame HWND must have the configured cut-out region.");
                                Assert(!PtInRegion(nativeRegion,centerX,centerY) && PtInRegion(nativeRegion,centerX,rimY),
                                    "Actual native window region must exclude center and include the rim.");
                            } finally {DeleteObject(nativeRegion);}
                            Assert(GetForegroundWindow()==foreground,"Presenting a native glass rim must not change foreground activation.");
                            frame.Hide();Assert(!frame.Visible,"Native frame Hide must work independently.");
                            frame.PresentAttached();Assert(frame.Visible && frame.IsGlassActive,"Native frame must present again after Hide.");
                            Assert(GetForegroundWindow()==foreground,"Re-presenting the native rim must remain nonactivating.");
                            inspected=true;
                        } catch(Exception error) {modalError=error;}
                        finally {
                            timeout.Stop();dialog.DialogResult=DialogResult.Cancel;dialog.Close();
                        }
                    });
                };
                timeout.Start();
                Assert(owner.LayerShowOwnedDialog(dialog)==DialogResult.Cancel,"Native ring input must return through the real modal helper.");
                timeout.Stop();
                if(modalError!=null) throw new InvalidOperationException("Native glass-frame checks failed.",modalError);
                Assert(inspected,"Native ring modal must reach its deferred inspection.");
                Assert(frame.IsDisposed || !frame.Visible,"Closing the input must hide or dispose its ring.");
                dialog.Dispose();Assert(frame.IsDisposed,"Disposing the input must automatically dispose its attached ring.");
            }
            Assert(owner.Enabled && IsWindowEnabled(owner.Handle) && owner.TopMost && NativeTopmost(owner.Handle),
                "Native ring close must restore an enabled pinned owner.");
            Assert(GetForegroundWindow()==foreground,"Native ring lifecycle must preserve foreground activation.");
            AssertOffscreen(owner,"Native ring owner");
            return nativeAvailable;
        }
        [STAThread]
        private static int Main()
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            string isolatedPath=Path.Combine(Path.GetTempPath(),"DeskSprout-layer-check-"+Guid.NewGuid().ToString("N")+".json");
            Assert(!File.Exists(isolatedPath),"The generated isolated state path must not already exist.");
            using(System.Threading.Timer watchdog=new System.Threading.Timer(delegate {
                Console.Error.WriteLine("FAIL global timeout: the isolated test did not exit within 30 seconds.");
                Environment.Exit(2);
            },null,30000,System.Threading.Timeout.Infinite))
            using(MainForm owner=new MainForm(true,isolatedPath))
            {
                owner.LayerPrepare(true);
                owner.Shown+=delegate {
                    owner.BeginInvoke((Action)delegate {
                        try {
                            foreach(bool pinned in new[]{true,false})
                            foreach(string kind in new[]{"priority","duration","pomodoro","entry"}) {
                                string name=kind+" / originalTopmost="+pinned;
                                try { CheckCase(owner,kind,pinned); passed++; Console.WriteLine("PASS "+name); }
                                catch(Exception error) { failed++; Console.Error.WriteLine("FAIL "+name+Environment.NewLine+error); }
                            }
                            try {CheckActualPomodoroSaveCancel(owner);passed++;Console.WriteLine("PASS actual Pomodoro save then cancel");}
                            catch(Exception error) {failed++;Console.Error.WriteLine("FAIL actual Pomodoro save then cancel"+Environment.NewLine+error);}
                            try {if(CheckNativeRing(owner)) {passed++;Console.WriteLine("PASS native glass ring window");} else skipped++;}
                            catch(Exception error) {failed++;Console.Error.WriteLine("FAIL native glass ring window"+Environment.NewLine+error);}
                        } finally { owner.LayerClose(); }
                    });
                };
                Application.Run(owner);
            }
            // Actual ResetPomodoro persists only this generated isolated state path.
            foreach(string testFile in new[]{isolatedPath,isolatedPath+".bak"}) if(File.Exists(testFile)) File.Delete(testFile);
            Assert(!File.Exists(isolatedPath) && !File.Exists(isolatedPath+".bak"),"No isolated state or backup may be left behind.");
            Console.WriteLine("Dialog layer checks: "+passed+" passed, "+failed+" failed, "+skipped+" skipped.");
            return failed==0 ? 0 : 1;
        }
    }
}
