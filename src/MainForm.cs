using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeskSprout
{
    internal sealed class Hit
    {
        public RectangleF Bounds;
        public string Hint;
        public Action Action;
        public Hit(RectangleF bounds,string hint,Action action) {Bounds=bounds;Hint=hint;Action=action;}
    }

    internal sealed partial class MainForm : Form
    {
        private readonly WidgetCore core;
        private readonly bool preview;
        private readonly List<Hit> hits=new List<Hit>();
        private readonly ToolTip tip=new ToolTip();
        private readonly System.Windows.Forms.Timer pulse=new System.Windows.Forms.Timer();
        private NotifyIcon tray;
        private ToastForm toast;
        private string notificationHistory="";
        private bool glassActive;
        private Bitmap glassFrame;
        private Palette colors;
        private float scale=1;
        private int page, filter, scroll, hover=-1, keyboard=-1;
        private bool exiting, dragging;
        private Point dragStart, windowStart;
        private string lastTip="";
        private DateTime lastDay=DateTime.Today;
        private int lastAgendaMinute=DateTime.Now.Hour*60+DateTime.Now.Minute;
        private DateTime saveFailureAt=DateTime.MinValue;
        private bool hotkeyShow,hotkeyAdd;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h,int id,uint mods,uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h,int id);
#if DIAGNOSTICS
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window,uint flags);
#endif

        public MainForm(bool forPreview,string statePath=null)
        {
            preview=forPreview;
            core=new WidgetCore(statePath ?? (forPreview ? Path.Combine(Path.GetTempPath(),"DeskSprout-preview-"+Guid.NewGuid()+".json") : null));
            Text="时屿 · 桌面组件"; FormBorderStyle=FormBorderStyle.None;
            AutoScaleMode=AutoScaleMode.None; StartPosition=FormStartPosition.Manual;
            ShowInTaskbar=false; DoubleBuffered=true; KeyPreview=true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint,true);
            using(Graphics g=CreateGraphics()) scale=g.DpiX/96f;
            colors=Palette.Create(ResolveDark()); BackColor=colors.Background;
            Icon=Art.AppIcon(); page=Math.Max(0,Math.Min(2,core.Settings.SelectedTab));
            TopMost=core.Settings.AlwaysOnTop;
            ResizeWidget(); RestorePosition();
            if(!preview) {
                CreateTray(); SystemEvents.UserPreferenceChanged+=SystemThemeChanged;
                pulse.Interval=1000; pulse.Tick+=OnPulse; pulse.Start();
                Shown+=delegate { if(!String.IsNullOrEmpty(core.LoadWarning))Notify("已恢复组件数据",core.LoadWarning,false);SaveQuietly(); OnPulse(this,EventArgs.Empty); };
            }
        }
        private bool ResolveDark()
        {
            return core.Settings.ThemeMode=="dark" || (core.Settings.ThemeMode!="light" && Palette.SystemDark());
        }
        private void SystemThemeChanged(object sender,UserPreferenceChangedEventArgs e)
        {
            if(IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)delegate {ApplyTheme();}); } catch(InvalidOperationException) { }
        }
        private void ApplyTheme()
        {
            colors=Palette.Create(ResolveDark()); BackColor=colors.Background; UpdateBackdrop();Invalidate();
        }
        private void UpdateBackdrop()
        {
            glassActive=!preview && IsHandleCreated && NativeBackdrop.Apply(this,core.Settings.GlassEnabled,ResolveDark(),core.Settings.GlassStyle=="clear");
            if(colors!=null) {
                colors=glassActive?Palette.ClearGlass(ResolveDark()):Palette.Create(ResolveDark());
            }
            DoubleBuffered=!glassActive;
            SetStyle(ControlStyles.Opaque,glassActive);
            if(glassActive) {Region previous=Region;Region=null;if(previous!=null)previous.Dispose();}
            else {
                using(GraphicsPath p=Art.Rounded(new RectangleF(0,0,ClientSize.Width,ClientSize.Height),18*scale)) {
                    Region previous=Region;Region=new Region(p);if(previous!=null)previous.Dispose();
                }
            }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if(!preview) {
                hotkeyShow=RegisterHotKey(Handle,1,0x4003,0x20); // Ctrl + Alt + Space, no repeats
                hotkeyAdd=RegisterHotKey(Handle,2,0x4003,0x4e); // Ctrl + Alt + N
            }
            UpdateBackdrop();
        }
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x0312) { if(FocusOpenDialog())return; if(m.WParam.ToInt32()==1) ToggleVisible(); else if(m.WParam.ToInt32()==2) {Reveal(); EditEntry(null);} return; }
            if(m.Msg==0x8001) {Reveal();return;}
            if(m.Msg==0x8002) {ExitApp();return;}
            if(m.Msg==0x8003) {m.Result=new IntPtr(1201);return;}
            if(m.Msg==0x001A && colors!=null) {ApplyTheme();}
            if(m.Msg==0x031e) {UpdateBackdrop();Invalidate();}
            base.WndProc(ref m);
        }
        private void ResizeWidget()
        {
            int width=core.Settings.Compact ? 286 : 340;
            int height=core.Settings.Compact ? 86 : 460;
            ClientSize=new Size((int)Math.Round(width*scale),(int)Math.Round(height*scale));
            using(GraphicsPath p=Art.Rounded(new RectangleF(0,0,ClientSize.Width,ClientSize.Height),18*scale)) {
                Region previous=Region; Region=new Region(p); if(previous!=null) previous.Dispose();
            }
            hits.Clear(); hover=keyboard=-1; Invalidate();
            UpdateBackdrop();
        }
        private void RestorePosition()
        {
            Rectangle area=Screen.PrimaryScreen.WorkingArea;
            if(core.Settings.HasWindowPosition) {
                Point saved=new Point(core.Settings.WindowX,core.Settings.WindowY);
                area=Screen.FromPoint(saved).WorkingArea;
                Location=new Point(Math.Max(area.Left,Math.Min(saved.X,area.Right-Width)),Math.Max(area.Top,Math.Min(saved.Y,area.Bottom-Height)));
            } else Location=new Point(area.Right-Width-24,area.Bottom-Height-24);
        }
        private void KeepOnScreen()
        {
            Rectangle area=Screen.FromRectangle(Bounds).WorkingArea;
            Location=new Point(Math.Max(area.Left,Math.Min(Left,area.Right-Width)),Math.Max(area.Top,Math.Min(Top,area.Bottom-Height)));
        }
        private void SavePosition()
        {
            core.Settings.HasWindowPosition=true; core.Settings.WindowX=Left; core.Settings.WindowY=Top;
            SaveQuietly();
        }
        private void SaveQuietly()
        {
            if(preview) return;
            try {core.Save();}
            catch(Exception ex) {
                Program.Log(ex);
                if(DateTime.Now-saveFailureAt>TimeSpan.FromMinutes(5)) {
                    saveFailureAt=DateTime.Now;
                    Notify("保存遇到问题","请检查本机磁盘空间和数据目录权限。当前会话仍可继续使用。",false);
                }
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if(glassActive) {
                if(glassFrame==null || glassFrame.Size!=ClientSize) {
                    if(glassFrame!=null)glassFrame.Dispose();
                    glassFrame=new Bitmap(ClientSize.Width,ClientSize.Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                }
                using(Graphics g=Graphics.FromImage(glassFrame)) {
                    g.Clear(Color.Transparent);g.ScaleTransform(scale,scale);PaintScene(g);
                }
                if(!NativeBackdrop.Present(e.Graphics,glassFrame)) {
                    core.Settings.GlassEnabled=false;UpdateBackdrop();Invalidate();
                }
            } else {e.Graphics.ScaleTransform(scale,scale);PaintScene(e.Graphics);}
        }
        protected override void OnPaintBackground(PaintEventArgs e) {if(!glassActive)base.OnPaintBackground(e);}
        private void PaintScene(Graphics g)
        {
            g.SmoothingMode=SmoothingMode.AntiAlias; g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Art.TransparentText=glassActive;
            hits.Clear();
            int width=core.Settings.Compact?286:340, height=core.Settings.Compact?86:460;
            if(glassActive)Art.GlassShell(g,new RectangleF(0,0,width,height),18,colors.Dark,core.Settings.GlassTint);
            else {Art.Fill(g,new RectangleF(0,0,width,height),18,colors.Background);Art.Outline(g,new RectangleF(.5f,.5f,width-1,height-1),18,colors.Line,1);}
            // Give the editing window a quiet backdrop. A clear glass widget's
            // timer and agenda must not compete with the dialog's reading area.
            if(activeDialog!=null && !activeDialog.IsDisposed) {
                Art.Text(g,"时屿",18,16,width-36,24,17,colors.Text,FontStyle.Bold,StringAlignment.Near);
                Art.Text(g,"编辑中",18,height-40,width-36,22,10,colors.Muted,FontStyle.Regular,StringAlignment.Center);
                Art.TransparentText=false;
                return;
            }
            if(core.Settings.Compact) PaintCompact(g); else {PaintHeader(g);PaintTabs(g);if(page==2)PaintPlanner(g);else PaintTimer(g);PaintFooter(g);}
            if(keyboard>=0 && keyboard<hits.Count) {
                RectangleF r=hits[keyboard].Bounds; r.Inflate(-2,-2); Art.Outline(g,r,8,colors.Accent,1.2f);
            }
            PaintPriorityGlint(g,width,height);
            Art.TransparentText=false;
        }
        private void HitArea(RectangleF r,string hint,Action action) {hits.Add(new Hit(r,hint,action));}
        private bool Hot(RectangleF r)
        {
            return hover>=0 && hover<hits.Count && hits[hover].Bounds==r;
        }
        private void IconButton(Graphics g,string icon,RectangleF r,string hint,Action action,bool active)
        {
            int index=hits.Count;
            if(active || hover==index) Art.Fill(g,r,8,colors.AccentSoft);
            float iconSize=Math.Min(22,Math.Min(r.Width,r.Height)-4);
            Art.Icon(g,icon,new RectangleF(r.X+(r.Width-iconSize)/2,r.Y+(r.Height-iconSize)/2,iconSize,iconSize),active?colors.Accent:colors.Muted);
            HitArea(r,hint,action);
        }
        private void Button(Graphics g,string title,RectangleF r,Action action,bool primary)
        {
            Color bg=colors.Glass?(primary?colors.AccentSoft:colors.Surface):(primary?colors.Accent:colors.Surface);
            if(hover==hits.Count) bg=colors.Glass?Color.FromArgb(Math.Min(112,bg.A+22),255,255,255):(primary?Blend(bg,colors.Text,.10f):colors.AccentSoft);
            Art.Fill(g,r,12,bg);
            if(colors.Glass)Art.Outline(g,new RectangleF(r.X+.5f,r.Y+.5f,r.Width-1,r.Height-1),12,Color.FromArgb(primary?125:70,255,255,255),1);
            Art.Text(g,title,r.X,r.Y,r.Width,r.Height,13,colors.Glass?colors.Text:primary?colors.White:colors.Text,FontStyle.Bold,StringAlignment.Center);
            HitArea(r,title,action);
        }
        private static Color Blend(Color a,Color b,float t)
        {
            return Color.FromArgb((int)(a.R*(1-t)+b.R*t),(int)(a.G*(1-t)+b.G*t),(int)(a.B*(1-t)+b.B*t));
        }
        private void PaintHeader(Graphics g)
        {
            Art.Fill(g,new RectangleF(18,18,30,30),10,colors.AccentSoft);
            Art.Icon(g,"leaf",new RectangleF(21,20,25,25),colors.Accent);
            Art.Text(g,"时屿",56,15,86,23,17,colors.Text,FontStyle.Bold,StringAlignment.Near);
            Art.Text(g,DateTime.Now.ToString("M月d日  dddd",CultureInfo.GetCultureInfo("zh-CN")),56,38,154,15,10,colors.Muted,FontStyle.Regular,StringAlignment.Near);
            PaintPriorityBadge(g);
            IconButton(g,"pin",new RectangleF(222,20,28,28),"置顶 / 取消置顶",TogglePin,core.Settings.AlwaysOnTop);
            IconButton(g,"fold",new RectangleF(255,20,28,28),"收起成迷你条",ToggleCompact,false);
            IconButton(g,"close",new RectangleF(288,20,28,28),"隐藏到托盘 · Ctrl+Alt+Space 唤回",delegate{Hide();},false);
        }
        private void PaintTabs(Graphics g)
        {
            Art.Fill(g,new RectangleF(18,68,304,34),11,colors.Surface);
            string[] labels={"计时器","番茄钟","日程计划"};
            for(int i=0;i<3;i++) {
                int selected=i; RectangleF r=new RectangleF(21+i*100,71,98,28);
                if(page==i) Art.Fill(g,r,9,colors.Glass?colors.AccentSoft:colors.Background);
                Art.Text(g,labels[i],r.X,r.Y,r.Width,r.Height,12,page==i?colors.Accent:colors.Muted,page==i?FontStyle.Bold:FontStyle.Regular,StringAlignment.Center);
                HitArea(r,labels[i],delegate{SelectPage(selected);});
                bool running=i==0?core.Countdown.Status==TimerStatus.Running:i==1 && core.Pomodoro.Status==TimerStatus.Running;
                if(running) using(Brush b=new SolidBrush(colors.Accent))g.FillEllipse(b,r.Right-13,r.Y+12,4,4);
            }
        }
        private void SelectPage(int selected)
        {
            if(selected==2 && core.Settings.Compact){core.Settings.Compact=false;ResizeWidget();KeepOnScreen();}
            page=selected; core.Settings.SelectedTab=page; scroll=0; keyboard=hover=-1; SaveQuietly();Invalidate();
        }
        private bool FocusPage {get{return page==1;}}
        private TimerStatus CurrentStatus {get{return FocusPage?core.Pomodoro.Status:core.Countdown.Status;}}
        private double CurrentRemaining {get{return FocusPage?core.Pomodoro.RemainingSeconds:core.Countdown.RemainingSeconds;}}
        private double PhaseSeconds
        {
            get {
                if(!FocusPage)return core.Countdown.DurationSeconds;
                return core.Pomodoro.DurationSeconds;
            }
        }
        private string PhaseLabel
        {
            get {return core.Pomodoro.Phase==PomodoroPhase.Focus?"专注时间":core.Pomodoro.Phase==PomodoroPhase.ShortBreak?"短休息":"长休息";}
        }
        private static string LabelFor(PomodoroPhase phase)
        {
            return phase==PomodoroPhase.Focus?"专注时间":phase==PomodoroPhase.ShortBreak?"短休息":"长休息";
        }
        private string LinkedTaskId
        {
            get {return (core.Pomodoro.Status==TimerStatus.Running || core.Pomodoro.Status==TimerStatus.Paused) && core.Pomodoro.Phase==PomodoroPhase.Focus?core.Pomodoro.ActiveTaskId:core.Settings.FocusTaskId;}
        }
        private void PaintTimer(Graphics g)
        {
            if(FocusPage) {
                PlanItem linked=core.Items.FirstOrDefault(i=>i.Id==LinkedTaskId);
                RectangleF task=new RectangleF(24,111,292,28);
                Art.Fill(g,task,8,colors.Surface);
                Art.Text(g,linked==null?"＋ 选择这次专注的任务": "专注于  "+linked.Title,35,111,263,28,11,colors.Muted,FontStyle.Regular,StringAlignment.Near);
                HitArea(task,"选择关联任务",ShowTaskMenu);
            } else Art.Text(g,"留一点时间，给眼前的事",24,113,292,24,11,colors.Muted,FontStyle.Regular,StringAlignment.Center);
            RectangleF ring=new RectangleF(91,148,158,158);
            using(Pen track=new Pen(colors.Line,5))g.DrawEllipse(track,ring);
            double fraction=Math.Max(0,Math.Min(1,CurrentRemaining/Math.Max(1,PhaseSeconds)));
            if(fraction>0) using(Pen pen=new Pen(colors.Accent,5)) {
                pen.StartCap=LineCap.Round;pen.EndCap=LineCap.Round;
                g.DrawArc(pen,ring,-90,(float)Math.Max(.1,359.99*fraction));
            }
            Art.Text(g,FocusPage?PhaseLabel:"倒计时",101,171,138,22,11,colors.Muted,FontStyle.Regular,StringAlignment.Center);
            string time=FormatTime(CurrentRemaining);
            Art.Text(g,time,88,199,164,49,time.Length>5?32:42,colors.Text,FontStyle.Regular,StringAlignment.Center);
            string status=CurrentStatus==TimerStatus.Running?"正在进行":CurrentStatus==TimerStatus.Paused?"已暂停":CurrentStatus==TimerStatus.Completed?(FocusPage?"完成 · 下一段"+LabelFor(core.Pomodoro.NextPhase):"时间到 · 再来一次"):FocusPage?"准备开始":"点击设置时长";
            Art.Text(g,status,100,251,140,23,10,colors.Muted,FontStyle.Regular,StringAlignment.Center);
            if(!FocusPage) HitArea(new RectangleF(96,174,148,109),"设置小时、分钟、秒",EditDuration);
            if(FocusPage) {
                int total=Math.Min(12,core.Settings.SessionsBeforeLongBreak);
                float dotStart=170-(total*13-5)/2f;
                for(int i=0;i<total;i++)using(Brush b=new SolidBrush(i<core.Pomodoro.FocusSessionsInCycle?colors.Accent:colors.Line))g.FillEllipse(b,dotStart+i*13,317,8,8);
                Art.Text(g,"今日  "+core.TodayStats.FocusSessions+" 个番茄   ·   "+core.TodayStats.FocusMinutes+" 分钟",24,333,292,22,11,colors.Muted,FontStyle.Regular,StringAlignment.Center);
            } else {
                int[] presets={300,900,1800};string[] labels={"5 分钟","15 分钟","30 分钟"};
                for(int i=0;i<3;i++) {
                    int seconds=presets[i]; RectangleF r=new RectangleF(24+i*76,324,69,29);
                    Art.Fill(g,r,9,core.Countdown.DurationSeconds==seconds?colors.AccentSoft:colors.Surface);
                    Art.Text(g,labels[i],r.X,r.Y,r.Width,r.Height,11,core.Countdown.DurationSeconds==seconds?colors.Accent:colors.Muted,FontStyle.Regular,StringAlignment.Center);
                    HitArea(r,"设为 "+labels[i],delegate{core.ConfigureCountdown(seconds);SaveQuietly();Invalidate();});
                }
                IconButton(g,"gear",new RectangleF(254,324,61,29),"自定义时长",EditDuration,false);
            }
            string primary=CurrentStatus==TimerStatus.Running?"暂停":CurrentStatus==TimerStatus.Paused?"继续":FocusPage?"开始"+(CurrentStatus==TimerStatus.Completed?LabelFor(core.Pomodoro.NextPhase):PhaseLabel):"开始计时";
            Button(g,primary,new RectangleF(24,367,234,40),ToggleCurrent,true);
            IconButton(g,"reset",new RectangleF(269,367,47,40),"重置当前计时",ResetCurrent,false);
            if(FocusPage) {
                Art.Text(g,core.Settings.AutoAdvance?"结束后自动进入下一段":"每段结束后，手动开始下一段",24,416,258,20,10,colors.Muted,FontStyle.Regular,StringAlignment.Near);
                IconButton(g,"next",new RectangleF(284,412,32,28),"跳过当前阶段（不计入专注统计）",delegate{core.SkipPomodoro();SaveQuietly();Invalidate();},false);
            } else {
                PlanItem next=NextSchedule();
                Art.Text(g,next==null?"不赶时间，也不辜负时间。":"下一项  "+ItemWhen(next)+"  "+next.Title,24,416,292,20,10,colors.Muted,FontStyle.Regular,StringAlignment.Near);
            }
        }
        private void PaintFooter(Graphics g)
        {
            using(Pen p=new Pen(colors.Line))g.DrawLine(p,18,441,322,441);
            PaintExtrasFooter(g);
            string sound=core.Settings.SoundEnabled?"声音开启":"静音";
            Art.Text(g,sound,224,443,50,15,9,colors.Muted,FontStyle.Regular,StringAlignment.Far);
            HitArea(new RectangleF(220,441,56,19),"切换提醒声音",delegate{core.Settings.SoundEnabled=!core.Settings.SoundEnabled;SaveQuietly();Invalidate();});
            IconButton(g,"gear",new RectangleF(284,442,28,18),"设置和快捷键",ShowSettings,false);
        }
        private void PaintPlans(Graphics g)
        {
            string[] names={"今天","全部","完成"};
            for(int i=0;i<3;i++) {
                int index=i;RectangleF r=new RectangleF(24+i*62,117,56,26);
                if(filter==i) Art.Fill(g,r,8,colors.AccentSoft);
                Art.Text(g,names[i],r.X,r.Y,r.Width,r.Height,11,filter==i?colors.Accent:colors.Muted,FontStyle.Regular,StringAlignment.Center);
                HitArea(r,names[i],delegate{filter=index;scroll=0;keyboard=hover=-1;Invalidate();});
            }
            IconButton(g,"plus",new RectangleF(282,115,34,29),"添加日程或计划 · Ctrl+Alt+N",delegate{EditEntry(null);},false);
            List<PlanItem> items=VisibleItems();
            scroll=Math.Max(0,Math.Min(scroll,Math.Max(0,items.Count-5)));
            if(items.Count==0) {
                Art.Fill(g,new RectangleF(130,217,80,80),26,colors.Surface);
                Art.Icon(g,"leaf",new RectangleF(148,234,44,44),colors.Accent);
                Art.Text(g,filter==2?"完成的事，会在这里留下痕迹":"今天想做些什么？",32,309,276,25,13,colors.Text,FontStyle.Regular,StringAlignment.Center);
                Art.Text(g,"点右上角 ＋，写下第一项计划",32,336,276,21,10,colors.Muted,FontStyle.Regular,StringAlignment.Center);
            }
            for(int row=0;row<Math.Min(5,items.Count-scroll);row++) {
                PlanItem item=items[scroll+row]; DateTime occurrence=OccurrenceDate(item);bool done=core.IsCompletedOn(item,occurrence);float y=153+row*52;
                RectangleF panel=new RectangleF(24,y,292,47); Art.Fill(g,panel,10,colors.Surface);
                RectangleF check=new RectangleF(29,y+9,30,30);
                using(Pen p=new Pen(done?colors.Accent:colors.Line,1.4f))g.DrawEllipse(p,check.X+6,check.Y+6,18,18);
                if(done)Art.Icon(g,"check",new RectangleF(check.X+5,check.Y+5,20,20),colors.Accent);
                HitArea(check,done?"恢复未完成":"标记完成",delegate{core.SetCompleted(item.Id,!core.IsCompletedOn(item,occurrence),occurrence);SaveQuietly();Invalidate();});
                Art.Text(g,item.Title,64,y+6,214,19,12,done?colors.Muted:colors.Text,done?FontStyle.Strikeout:FontStyle.Regular,StringAlignment.Near);
                string meta=ItemWhen(item)+(item.Kind==PlanKind.Schedule?"  ·  日程":"  ·  计划")+RepeatLabel(item);
                Art.Text(g,meta,64,y+26,213,14,9,colors.Muted,FontStyle.Regular,StringAlignment.Near);
                HitArea(new RectangleF(61,y+3,217,41),"编辑 “"+item.Title+"”",delegate{EditEntry(item);});
                IconButton(g,"dots",new RectangleF(280,y+10,29,28),"更多操作",delegate{ShowItemMenu(item);},false);
            }
            if(items.Count>5) {
                float track=258;float thumb=track*5/items.Count;
                Art.Fill(g,new RectangleF(325,154+((float)scroll/(items.Count-5))*(track-thumb),3,thumb),1.5f,colors.Line);
            }
            int completed=core.Items.Count(i=>core.IsDueOn(i,DateTime.Today)&&core.IsCompletedOn(i,DateTime.Today));
            int today=core.Items.Count(i=>core.IsDueOn(i,DateTime.Today));
            Art.Text(g,"今日计划  "+completed+" / "+today+" 完成",24,417,230,19,10,colors.Muted,FontStyle.Regular,StringAlignment.Near);
            if(items.Count>5)Art.Text(g,(scroll+1)+"–"+Math.Min(scroll+5,items.Count)+" / "+items.Count,252,417,64,19,9,colors.Muted,FontStyle.Regular,StringAlignment.Far);
        }
        private string RepeatLabel(PlanItem item)
        {
            return item.Repeat==RepeatKind.Daily?"  ·  每天":item.Repeat==RepeatKind.Weekly?"  ·  每周":"";
        }
        private List<PlanItem> VisibleItems()
        {
            DateTime today=DateTime.Today;
            IEnumerable<PlanItem> list=core.Items;
            if(filter==0)list=list.Where(i=>core.IsDueOn(i,today) || (i.Repeat==RepeatKind.None && i.Date.Date<today && !i.Completed));
            else if(filter==1)list=list.Where(i=>!core.IsCompletedOn(i,OccurrenceDate(i)));
            else list=list.Where(i=>core.IsCompletedOn(i,OccurrenceDate(i)));
            return list.OrderBy(i=>core.IsCompletedOn(i,OccurrenceDate(i))).ThenBy(i=>OccurrenceDate(i)).ThenBy(i=>i.MinuteOfDay??1440).ThenBy(i=>i.Title).ToList();
        }
        private DateTime OccurrenceDate(PlanItem item)
        {
            if(item.Repeat==RepeatKind.None)return item.Date.Date;
            DateTime date=item.Date.Date>DateTime.Today?item.Date.Date:DateTime.Today;
            if(item.Repeat==RepeatKind.Weekly)date=date.AddDays(((int)item.Date.DayOfWeek-(int)date.DayOfWeek+7)%7);
            return date;
        }
        private string ItemWhen(PlanItem item)
        {
            DateTime date=OccurrenceDate(item);
            string prefix=date.Date==DateTime.Today?"今天":date.Date==DateTime.Today.AddDays(1)?"明天":date.Date<DateTime.Today?date.ToString("M/d")+(item.Completed?"":" 已逾期"):date.ToString("M/d");
            return prefix+(item.MinuteOfDay.HasValue?" "+TimeSpan.FromMinutes(item.MinuteOfDay.Value).ToString(@"hh\:mm"):" · 无时刻");
        }
        private PlanItem NextSchedule()
        {
            int minute=DateTime.Now.Hour*60+DateTime.Now.Minute;
            return core.Items.Where(i=>i.MinuteOfDay.HasValue && core.IsDueOn(i,DateTime.Today) && !core.IsCompletedOn(i,DateTime.Today) && i.MinuteOfDay.Value>=minute).OrderBy(i=>i.MinuteOfDay).FirstOrDefault();
        }
        private void PaintCompact(Graphics g)
        {
            Art.Icon(g,"leaf",new RectangleF(16,13,22,22),colors.Accent);
            Art.Text(g,FocusPage?PhaseLabel:"倒计时",43,13,130,23,11,colors.Muted,FontStyle.Regular,StringAlignment.Near);
            IconButton(g,"expand",new RectangleF(208,10,28,28),"展开组件",ToggleCompact,false);
            IconButton(g,"close",new RectangleF(246,10,28,28),"隐藏到托盘",delegate{Hide();},false);
            Art.Text(g,FormatTime(CurrentRemaining),18,36,140,38,30,colors.Text,FontStyle.Regular,StringAlignment.Near);
            PlanItem task=core.Items.FirstOrDefault(i=>i.Id==LinkedTaskId);
            Art.Text(g,FocusPage&&task!=null?task.Title:CurrentStatus==TimerStatus.Running?"进行中":"留一点时间",151,46,82,21,9,colors.Muted,FontStyle.Regular,StringAlignment.Near);
            IconButton(g,CurrentStatus==TimerStatus.Running?"pause":"play",new RectangleF(240,42,32,32),CurrentStatus==TimerStatus.Running?"暂停":"开始 / 继续",ToggleCurrent,false);
            double fraction=Math.Max(0,Math.Min(1,CurrentRemaining/Math.Max(1,PhaseSeconds)));
            Art.Fill(g,new RectangleF(19,79,248,2),1,colors.Line);
            if(fraction>0)Art.Fill(g,new RectangleF(19,79,(float)(248*fraction),2),1,colors.Accent);
        }
        private static string FormatTime(double remaining)
        {
            int seconds=(int)Math.Ceiling(Math.Max(0,remaining));
            return seconds>=3600?(seconds/3600).ToString("00")+":"+((seconds/60)%60).ToString("00")+":"+(seconds%60).ToString("00"):(seconds/60).ToString("00")+":"+(seconds%60).ToString("00");
        }
        private void ToggleCurrent()
        {
            if(page==2)SelectPage(1);
            if(FocusPage) {
                if(core.Pomodoro.Status==TimerStatus.Running)core.PausePomodoro();
                else if(core.Pomodoro.Status==TimerStatus.Paused)core.ResumePomodoro();
                else {
                    bool newFocus=core.Pomodoro.Status==TimerStatus.Completed?core.Pomodoro.NextPhase==PomodoroPhase.Focus:core.Pomodoro.Phase==PomodoroPhase.Focus;
                    if(newFocus && !RecallNextStep())return;
                    core.StartPomodoro();
                }
            } else {
                if(core.Countdown.Status==TimerStatus.Running)core.PauseCountdown();
                else if(core.Countdown.Status==TimerStatus.Paused)core.ResumeCountdown();
                else core.StartCountdown();
            }
            pulse.Interval=1000;SaveQuietly();Invalidate();
        }
        private void ResetCurrent()
        {
            if(FocusPage)core.ResetPomodoro();else core.ResetCountdown();SaveQuietly();Invalidate();
        }
        private void EditDuration()
        {
            using(DurationDialog dialog=new DurationDialog(core.Countdown.DurationSeconds,colors.Dark))
                if(ShowOwnedDialog(dialog)==DialogResult.OK) {core.ConfigureCountdown(dialog.ResultSeconds);SaveQuietly();Invalidate();}
        }
        private void EditPomodoro()
        {
            WidgetSettings s=core.Settings;
            using(PomodoroDialog dialog=new PomodoroDialog(s.FocusMinutes,s.ShortBreakMinutes,s.LongBreakMinutes,s.SessionsBeforeLongBreak,s.AutoAdvance,colors.Dark))
                if(ShowOwnedDialog(dialog)==DialogResult.OK) {
                    s.FocusMinutes=dialog.FocusMinutes;s.ShortBreakMinutes=dialog.ShortMinutes;s.LongBreakMinutes=dialog.LongMinutes;s.SessionsBeforeLongBreak=dialog.CycleLength;s.AutoAdvance=dialog.AutoAdvance;
                    core.ResetPomodoro();SaveQuietly();Invalidate();
                }
        }
        private void EditEntry(PlanItem item)
        {
            DateTime date=item==null?(page==2?SelectedScheduleDate:DateTime.Today):item.Date;
            if(item!=null && item.MinuteOfDay.HasValue)date=date.Date.AddMinutes(item.MinuteOfDay.Value);
            string repeat=item==null?"none":item.Repeat==RepeatKind.Daily?"daily":item.Repeat==RepeatKind.Weekly?"weekly":"none";
            using(EntryDialog dialog=new EntryDialog(item==null?"":item.Title,date,item!=null && item.MinuteOfDay.HasValue,item!=null && item.Kind==PlanKind.Schedule?"schedule":"plan",colors.Dark,repeat,item==null || item.ReminderEnabled,item==null?null:item.EstimatedMinutes))
                if(ShowOwnedDialog(dialog)==DialogResult.OK) {
                    int? minute=dialog.HasTime?(int?)(dialog.EntryDate.Hour*60+dialog.EntryDate.Minute):null;
                    PlanKind kind=dialog.EntryKind=="schedule"?PlanKind.Schedule:PlanKind.Plan;
                    RepeatKind recurrence=dialog.Repeat=="daily"?RepeatKind.Daily:dialog.Repeat=="weekly"?RepeatKind.Weekly:RepeatKind.None;
                    PlanItem updated=item;
                    if(item==null)updated=core.AddItem(dialog.EntryTitle,dialog.EntryDate.Date,minute,kind,recurrence,dialog.ReminderEnabled);
                    else core.UpdateItem(item.Id,dialog.EntryTitle,dialog.EntryDate.Date,minute,kind,recurrence,dialog.ReminderEnabled);
                    core.SetEstimatedMinutes(updated.Id,dialog.EstimatedMinutes);
                    SaveQuietly();scroll=0;Invalidate();
                }
        }
        private void ToggleCompact()
        {
            core.Settings.Compact=!core.Settings.Compact;if(page==2 && core.Settings.Compact)SelectPage(1);
            ResizeWidget();KeepOnScreen();SavePosition();
        }
        private void TogglePin() {core.Settings.AlwaysOnTop=!core.Settings.AlwaysOnTop;TopMost=core.Settings.AlwaysOnTop;SaveQuietly();Invalidate();}
        private void ShowTaskMenu()
        {
            ContextMenuStrip menu=NewMenu();
            if(core.Pomodoro.Status==TimerStatus.Running || core.Pomodoro.Status==TimerStatus.Paused) {ToolStripMenuItem note=new ToolStripMenuItem("任务变更将在下一段专注生效");note.Enabled=false;menu.Items.Add(note);}
            AddMenu(menu,"自由专注",delegate{core.Settings.FocusTaskId=null;SaveQuietly();Invalidate();},String.IsNullOrEmpty(core.Settings.FocusTaskId));
            List<PlanItem> items=core.Items.Where(i=>!core.IsCompletedOn(i,DateTime.Today)).Take(20).ToList();
            foreach(PlanItem item in items) {
                PlanItem selected=item;AddMenu(menu,item.Title,delegate{core.Settings.FocusTaskId=selected.Id;SaveQuietly();Invalidate();},core.Settings.FocusTaskId==item.Id);
            }
            menu.Items.Add(new ToolStripSeparator());AddMenu(menu,"＋ 新建计划",delegate{EditEntry(null);},false);ShowPopup(menu);
        }
        private void ShowItemMenu(PlanItem item)
        {
            ContextMenuStrip menu=NewMenu();
            AddMenu(menu,"编辑",delegate{EditEntry(item);},false);
            AddMenu(menu,"任务说明与练习…",delegate{ShowTaskDetail(item);},false);
            AddMenu(menu,"记下下一步…",delegate{EditNextStep(item.Id);},false);
            AddMenu(menu,"专注于这个任务",delegate{core.Settings.FocusTaskId=item.Id;SelectPage(1);},false);
            if(item.FocusSessions>0) {ToolStripMenuItem record=new ToolStripMenuItem("已专注 "+item.FocusSessions+" 个番茄 · "+item.FocusMinutes+" 分钟");record.Enabled=false;menu.Items.Add(record);}
            DateTime occurrence=page==2?ScheduleOccurrenceDate(item):OccurrenceDate(item);
            AddMenu(menu,core.IsCompletedOn(item,occurrence)?"恢复未完成":"标记完成",delegate{core.SetCompleted(item.Id,!core.IsCompletedOn(item,occurrence),occurrence);SaveQuietly();CelebratePrioritiesIfReady();Invalidate();},false);
            menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu,"删除…",delegate{
                if(ConfirmRemoval(item.Title)) {core.RemoveItem(item.Id);SaveQuietly();Invalidate();}
            },false);ShowPopup(menu);
        }
        private ContextMenuStrip NewMenu()
        {
            ContextMenuStrip menu=new ContextMenuStrip();menu.BackColor=colors.Background;menu.ForeColor=colors.Text;menu.Font=new Font("Microsoft YaHei UI",9);
            menu.Closed+=delegate{BeginInvoke((Action)delegate{menu.Dispose();});};return menu;
        }
        private static void AddMenu(ContextMenuStrip menu,string title,Action action,bool check)
        {
            ToolStripMenuItem item=new ToolStripMenuItem(title);item.Checked=check;item.Click+=delegate{action();};menu.Items.Add(item);
        }
        private void ShowPopup(ContextMenuStrip menu) {menu.Show(this,PointToClient(Cursor.Position));}
        private void ShowSettings()
        {
            ContextMenuStrip menu=NewMenu();
            AddMenu(menu,"小趣味与学习任务包…",ShowExtrasMenu,false);
            menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu,"番茄时长与休息…",EditPomodoro,false);
            AddMenu(menu,"提示音",delegate{core.Settings.SoundEnabled=!core.Settings.SoundEnabled;SaveQuietly();Invalidate();},core.Settings.SoundEnabled);
            AddMenu(menu,"窗口置顶",TogglePin,core.Settings.AlwaysOnTop);
            AddMenu(menu,NativeBackdrop.SupportsClearGlass?"通透玻璃":"通透玻璃（此系统回退为毛玻璃）",delegate{SetGlassStyle("clear");},core.Settings.GlassEnabled && core.Settings.GlassStyle=="clear");
            AddMenu(menu,"柔和毛玻璃",delegate{SetGlassStyle("acrylic");},core.Settings.GlassEnabled && core.Settings.GlassStyle=="acrylic");
            AddMenu(menu,"实色背景",delegate{core.Settings.GlassEnabled=false;UpdateBackdrop();SaveQuietly();Invalidate();},!core.Settings.GlassEnabled);
            menu.Items.Add(new ToolStripSeparator());
            int[] tints={4,16,48};string[] tintNames={"透明度：更通透","透明度：通透（默认）","透明度：文字更清晰"};
            for(int i=0;i<tints.Length;i++){int tint=tints[i];AddMenu(menu,tintNames[i],delegate{core.Settings.GlassTint=tint;SaveQuietly();Invalidate();},core.Settings.GlassTint==tint);}
            menu.Items.Add(new ToolStripSeparator());
            string[] values={"system","light","dark"};string[] labels={"跟随系统主题","浅色主题","深色主题"};
            for(int i=0;i<3;i++) {string v=values[i];AddMenu(menu,labels[i],delegate{core.Settings.ThemeMode=v;ApplyTheme();SaveQuietly();},core.Settings.ThemeMode==v);}
            menu.Items.Add(new ToolStripSeparator());AddMenu(menu,"快捷键与说明…",ShowHelp,false);
            AddMenu(menu,"退出组件",ExitApp,false);ShowPopup(menu);
        }
        private void SetGlassStyle(string style) {core.Settings.GlassEnabled=true;core.Settings.GlassStyle=style;UpdateBackdrop();SaveQuietly();Invalidate();}
        private void ShowHelp()
        {
            ShowInformation("时屿 · 桌面的一小角\n\nCtrl + Alt + Space    显示 / 隐藏\nCtrl + Alt + N          快速新建计划\nCtrl + 1 / 2 / 3        切换功能\nSpace                     开始 / 暂停\nCtrl + R                  重置计时\nTab / Enter             选择 / 执行按钮\nEsc                         隐藏到托盘\n\n拖动顶部移动窗口；贴近屏幕边缘会吸附。\n× 隐藏到托盘，托盘右键可彻底退出。\n\n日程到点提醒；每天 / 每周按起始日期重复。\n每段番茄结束后默认手动继续，可在设置中切换。\n睡眠期间无法提示，唤醒后会补发已到点的提醒。\n\n快捷键状态："+(hotkeyShow?"唤回可用":"唤回被其他软件占用")+"，"+(hotkeyAdd?"新建可用":"新建被其他软件占用")+"\n\n数据保存在：\n%LOCALAPPDATA%\\DeskSprout","使用说明");
        }
        private void CreateTray()
        {
            tray=new NotifyIcon();tray.Icon=Icon;tray.Text="时屿 · 桌面组件";tray.Visible=true;
            tray.MouseDoubleClick+=delegate{Reveal();};tray.BalloonTipClicked+=delegate{Reveal();};
            ContextMenuStrip menu=new ContextMenuStrip();
            AddMenu(menu,"显示 / 隐藏组件",ToggleVisible,false);
            AddMenu(menu,"新建日程计划",delegate{RunWidgetAction(delegate{EditEntry(null);});},false);
            AddMenu(menu,"小趣味与学习任务包",delegate{RunWidgetAction(ShowExtrasMenu);},false);
            AddMenu(menu,"查看最近提醒",delegate{RunWidgetAction(delegate{ShowInformation(String.IsNullOrEmpty(notificationHistory)?"暂时没有提醒。":notificationHistory,"最近提醒");});},false);
            menu.Items.Add(new ToolStripSeparator());AddMenu(menu,"计时器",delegate{RunWidgetAction(delegate{SelectPage(0);});},false);
            AddMenu(menu,"番茄钟",delegate{RunWidgetAction(delegate{SelectPage(1);});},false);AddMenu(menu,"日程计划",delegate{RunWidgetAction(delegate{SelectPage(2);});},false);
            menu.Items.Add(new ToolStripSeparator());AddMenu(menu,"退出",ExitApp,false);tray.ContextMenuStrip=menu;
        }
        private void Reveal() {if(FocusOpenDialog())return;KeepOnScreen();Show();WindowState=FormWindowState.Normal;Activate();Invalidate();}
        private void ToggleVisible() {if(FocusOpenDialog())return;if(Visible)Hide();else Reveal();}
        private void ExitApp() {exiting=true;SavePosition();Close();}
        private void Notify(string title,string body,bool sound,Action clicked=null)
        {
            if(preview)return;
            notificationHistory=title+"\n\n"+body;
            if(toast!=null && !toast.IsDisposed)toast.Close();
            toast=new ToastForm(colors.Dark,title,body,delegate{
                RunWidgetAction(delegate{if(clicked!=null)clicked();else ShowInformation(notificationHistory,"提醒记录");});
            });
            toast.Present(Screen.FromRectangle(Bounds).WorkingArea);
            if(sound && core.Settings.SoundEnabled)SystemSounds.Asterisk.Play();
        }
        private void OnPulse(object sender,EventArgs e)
        {
            try {
                string finishedTask=core.Pomodoro.ActiveTaskId;
                TickResult result=core.Tick();
                List<string> messages=new List<string>();
                if(result.CountdownCompleted)messages.Add("倒计时结束，可以开始下一件事了。");
                if(result.PomodoroCompleted)messages.Add(result.CompletedPhase==PomodoroPhase.Focus?"专注完成，站起来放松一下。\n点提醒可留一句下次接着做的思路。":"休息结束，准备开始下一段专注。");
                if(result.PomodoroCompleted && core.Settings.AutoAdvance && core.Pomodoro.Phase==PomodoroPhase.Focus) {
                    string next=core.NextStepFor(core.Pomodoro.ActiveTaskId);
                    if(!String.IsNullOrEmpty(next))messages[messages.Count-1]+="\n接着上次："+next;
                }
                foreach(PlanItem item in result.Reminders)messages.Add(item.Title+" · "+ItemWhen(item));
                if(result.CountdownCompleted || result.PomodoroCompleted || result.Reminders.Count>0)SaveQuietly();
                if(messages.Count>0 && !String.IsNullOrEmpty(core.LastSaveError))messages.Add("本次数据暂未保存，请检查磁盘空间与目录权限。");
                Action completedAction=messages.Count==1 && result.PomodoroCompleted && result.CompletedPhase==PomodoroPhase.Focus?(Action)delegate{EditNextStep(finishedTask);}:null;
                if(messages.Count>0)Notify(messages.Count>1?"有 "+messages.Count+" 项提醒":result.Reminders.Count>0?"日程提醒":result.PomodoroCompleted?"番茄钟提醒":"计时结束",String.Join("\n",messages.ToArray()),true,completedAction);
                CelebratePrioritiesIfReady();CheckEveningSummary(messages.Count>0);
                bool running=core.Countdown.Status==TimerStatus.Running || core.Pomodoro.Status==TimerStatus.Running;
                string text=running?"时屿 · "+(core.Pomodoro.Status==TimerStatus.Running?LabelFor(core.Pomodoro.Phase)+" "+FormatTime(core.Pomodoro.RemainingSeconds):"倒计时 "+FormatTime(core.Countdown.RemainingSeconds)):"时屿 · 桌面组件";
                if(tray!=null && tray.Text!=text)tray.Text=text;
                if(lastDay!=DateTime.Today) {AdvancePlannerToday(lastDay);lastDay=DateTime.Today;scroll=0;Invalidate();}
                int minute=DateTime.Now.Hour*60+DateTime.Now.Minute;
                bool agendaMinuteChanged=lastAgendaMinute!=minute;lastAgendaMinute=minute;
                if(Visible && ((running && page!=2) || (agendaMinuteChanged && (page==2 || core.Extras.ImportantDays.Count>0)) || result.CountdownCompleted || result.PomodoroCompleted || result.Reminders.Count>0))Invalidate();
                // Timers use absolute deadlines: no animation loop and no state writes every second.
                pulse.Interval=running?1000:10000;
            } catch(Exception ex) {Program.Log(ex);}
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if(dragging) {Point pos=Cursor.Position;Location=new Point(windowStart.X+pos.X-dragStart.X,windowStart.Y+pos.Y-dragStart.Y);return;}
            PointF p=new PointF(e.X/scale,e.Y/scale);int index=hits.FindIndex(h=>h.Bounds.Contains(p));
            if(index!=hover) {hover=index;Cursor=index>=0?Cursors.Hand:Cursors.Default;string hint=index>=0?hits[index].Hint:"";if(hint!=lastTip){tip.SetToolTip(this,hint);lastTip=hint;}Invalidate();}
        }
        protected override void OnMouseLeave(EventArgs e) {base.OnMouseLeave(e);hover=-1;Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            PointF p=new PointF(e.X/scale,e.Y/scale);
            if(e.Button==MouseButtons.Right) {ShowSettings();return;}
            if(e.Button==MouseButtons.Left && p.Y<(core.Settings.Compact?38:61) && !hits.Any(h=>h.Bounds.Contains(p))) {
                dragging=true;dragStart=Cursor.Position;windowStart=Location;Capture=true;
            }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if(dragging) {dragging=false;Capture=false;Snap();SavePosition();return;}
            if(e.Button==MouseButtons.Left) {PointF p=new PointF(e.X/scale,e.Y/scale);Hit h=hits.FirstOrDefault(i=>i.Bounds.Contains(p));if(h!=null){keyboard=-1;h.Action();}}
        }
        private void Snap()
        {
            Rectangle area=Screen.FromRectangle(Bounds).WorkingArea;int snap=(int)(18*scale);
            int x=Left,y=Top;
            if(Math.Abs(Left-area.Left)<snap)x=area.Left+8;
            if(Math.Abs(Right-area.Right)<snap)x=area.Right-Width-8;
            if(Math.Abs(Top-area.Top)<snap)y=area.Top+8;
            if(Math.Abs(Bottom-area.Bottom)<snap)y=area.Bottom-Height-8;
            Location=new Point(x,y);KeepOnScreen();
        }
        protected override void OnMouseWheel(MouseEventArgs e) {base.OnMouseWheel(e);if(page==2)ScrollPlanner(e.Delta);}
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
        {
            if(activeDialog!=null) return base.ProcessCmdKey(ref msg,keyData);
            if(keyData==Keys.Escape){Hide();return true;}
            if(keyData==(Keys.Control|Keys.D1)){SelectPage(0);return true;}
            if(keyData==(Keys.Control|Keys.D2)){SelectPage(1);return true;}
            if(keyData==(Keys.Control|Keys.D3)){SelectPage(2);return true;}
            if(keyData==(Keys.Control|Keys.N)){EditEntry(null);return true;}
            if(keyData==(Keys.Control|Keys.J)){AddQuickNote();return true;}
            if(keyData==(Keys.Control|Keys.R)){if(page!=2)ResetCurrent();return true;}
            if(keyData==Keys.Space){if(page!=2)ToggleCurrent();return true;}
            if(keyData==Keys.Tab || keyData==(Keys.Shift|Keys.Tab)) {
                if(hits.Count>0){keyboard=keyboard<0?(keyData==Keys.Tab?0:hits.Count-1):(keyboard+(keyData==Keys.Tab?1:-1)+hits.Count)%hits.Count;Invalidate();}return true;
            }
            if(keyData==Keys.Enter && keyboard>=0 && keyboard<hits.Count){hits[keyboard].Action();return true;}
            return base.ProcessCmdKey(ref msg,keyData);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if(!exiting && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();return;}
            SavePosition();base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing) {
                SystemEvents.UserPreferenceChanged-=SystemThemeChanged;
                if(IsHandleCreated){UnregisterHotKey(Handle,1);UnregisterHotKey(Handle,2);}
                pulse.Stop();pulse.Dispose();tip.Dispose();
                celebrationTimer.Stop();celebrationTimer.Dispose();
                if(tray!=null){tray.Visible=false;if(tray.ContextMenuStrip!=null)tray.ContextMenuStrip.Dispose();tray.Dispose();}
                if(toast!=null && !toast.IsDisposed)toast.Close();
                if(glassFrame!=null)glassFrame.Dispose();
                if(Icon!=null)Icon.Dispose();
            }
            base.Dispose(disposing);
        }
        internal static void RenderSamples(string folder)
        {
            Directory.CreateDirectory(folder);
            using(MainForm form=new MainForm(true)) {
                form.core.AddItem("8086 · 画一张寄存器分类图",DateTime.Today,null,PlanKind.Plan);
                form.core.AddItem("产品讨论",DateTime.Today,14*60+30,PlanKind.Schedule);
                form.core.AddItem("散步，离开屏幕一会儿",DateTime.Today,18*60,PlanKind.Plan,RepeatKind.Daily);
                PlanItem first=form.core.Items[0];form.core.Settings.FocusTaskId=first.Id;
                form.core.SetEstimatedMinutes(first.Id,10);form.core.SetTaskDetail(first.Id,LearningPack.Tasks[0].Detail);
                form.core.SetNextStep(first.Id,LearningPack.Tasks[0].NextStep);
                form.core.AddItem("整理本周的想法",DateTime.Today,null,PlanKind.Plan);
                form.core.SetCompleted(form.core.Items[3].Id,true);
                form.core.AddItem("晨间计划",DateTime.Today,9*60,PlanKind.Schedule,RepeatKind.Daily);
                form.core.AddItem("下周的项目讨论",DateTime.Today.AddDays(3),11*60,PlanKind.Schedule);
                form.core.AddItem("每周复盘",DateTime.Today,20*60,PlanKind.Plan,RepeatKind.Weekly);
                form.core.SetPriorities(DateTime.Today,new[]{first.Id,form.core.Items[2].Id,form.core.Items[3].Id});
                form.core.TodayStats.FocusSessions=2;form.core.TodayStats.FocusSeconds=3000;
                form.core.AddQuickNote("MOV AH,AL后AX怎么变？\r\n明天先手算，再在调试器里验证。");
                form.core.AddImportantDay("寄存器练习复盘",DateTime.Today.AddDays(7),false);
                string[] themes={"light","dark"};
                foreach(string theme in themes) {
                    form.core.Settings.ThemeMode=theme;form.ApplyTheme();
                    form.glassActive=true;form.colors=Palette.ClearGlass(theme=="dark");
                    for(int i=0;i<3;i++) {
                        form.page=i;form.core.Settings.Compact=false;
                        using(Bitmap bmp=new Bitmap(680,920))using(Graphics g=Graphics.FromImage(bmp)) {
                            PreviewBackground(g,bmp.Size,theme=="dark");g.ScaleTransform(2,2);form.PaintScene(g);bmp.Save(Path.Combine(folder,theme+"-"+i+".png"),System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    form.page=2;
                    for(int mode=0;mode<3;mode++) {
                        form.plannerMode=mode;
                        using(Bitmap bmp=new Bitmap(680,920))using(Graphics g=Graphics.FromImage(bmp)) {
                            PreviewBackground(g,bmp.Size,theme=="dark");g.ScaleTransform(2,2);form.PaintScene(g);bmp.Save(Path.Combine(folder,theme+"-"+new[]{"calendar","daily","list"}[mode]+".png"),System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    form.plannerMode=0;form.SetPlannerDate(new DateTime(2026,8,31));
                    using(Bitmap bmp=new Bitmap(680,920))using(Graphics g=Graphics.FromImage(bmp)) {
                        PreviewBackground(g,bmp.Size,theme=="dark");g.ScaleTransform(2,2);form.PaintScene(g);bmp.Save(Path.Combine(folder,theme+"-calendar-six-weeks.png"),System.Drawing.Imaging.ImageFormat.Png);
                    }
                    form.SetPlannerDate(DateTime.Today);
                    form.page=1;form.core.Settings.Compact=true;
                    using(Bitmap bmp=new Bitmap(572,172))using(Graphics g=Graphics.FromImage(bmp)) {
                        PreviewBackground(g,bmp.Size,theme=="dark");g.ScaleTransform(2,2);form.PaintScene(g);bmp.Save(Path.Combine(folder,theme+"-compact.png"),System.Drawing.Imaging.ImageFormat.Png);
                    }
                    form.core.Settings.Compact=false;form.page=1;
                    using(Bitmap alpha=new Bitmap(340,460,System.Drawing.Imaging.PixelFormat.Format32bppPArgb))using(Graphics g=Graphics.FromImage(alpha)) {
                        g.Clear(Color.Transparent);form.PaintScene(g);
                        int bg=alpha.GetPixel(12,135).A,card=alpha.GetPixel(30,124).A,button=alpha.GetPixel(70,375).A;
                        if(bg>=90 || card>=150 || button>=200)throw new InvalidOperationException("玻璃背景/卡片透明度检查失败。");
                        int opaqueGlyphPixels=0;
                        for(int y=204;y<244;y++)for(int x=108;x<232;x++)if(alpha.GetPixel(x,y).A>=250)opaqueGlyphPixels++;
                        if(opaqueGlyphPixels<20)throw new InvalidOperationException("倒计时文字清晰度检查失败。");
                        alpha.Save(Path.Combine(folder,theme+"-transparent-layer.png"),System.Drawing.Imaging.ImageFormat.Png);
                        File.WriteAllText(Path.Combine(folder,theme+"-render-check.txt"),"BackgroundAlpha="+bg+"\nCardAlpha="+card+"\nButtonAlpha="+button+"\nOpaqueGlyphPixels="+opaqueGlyphPixels+"\nPASS transparent foreground layers and opaque timer glyphs.\n");
                    }
                    using(DurationDialog dialog=new DurationDialog(900,form.colors.Dark))SaveDialogImage(dialog,Path.Combine(folder,theme+"-duration.png"));
                    using(PomodoroDialog dialog=new PomodoroDialog(25,5,15,4,false,form.colors.Dark))SaveDialogImage(dialog,Path.Combine(folder,theme+"-pomodoro-settings.png"));
                    using(EntryDialog dialog=new EntryDialog("读完《设计心理学》第二章",DateTime.Today.AddHours(14),true,"plan",form.colors.Dark,"daily"))SaveDialogImage(dialog,Path.Combine(folder,theme+"-entry.png"));
                    using(LotteryDialog dialog=new LotteryDialog(form.core,form.colors.Dark))SaveDialogImage(dialog,Path.Combine(folder,theme+"-lottery.png"));
                    using(PriorityDialog dialog=new PriorityDialog(form.core,DateTime.Today,form.colors.Dark))SaveDialogImage(dialog,Path.Combine(folder,theme+"-priorities.png"));
                    using(TextEntryDialog dialog=new TextEntryDialog("明天接着做","8086 · 画一张寄存器分类图\n留一句具体下一步。",form.core.NextStepFor(first.Id),form.colors.Dark))SaveDialogImage(dialog,Path.Combine(folder,theme+"-next-step.png"));
                    using(ReflectionDialog dialog=new ReflectionDialog(form.core,DateTime.Today,form.colors.Dark))SaveDialogImage(dialog,Path.Combine(folder,theme+"-reflection.png"));
                    using(LearningPackDialog dialog=new LearningPackDialog(form.colors.Dark,LearningPack.All[1]))SaveDialogImage(dialog,Path.Combine(folder,theme+"-learning-pack.png"));
                    using(ReadNoteDialog dialog=new ReadNoteDialog("8086 · 寄存器练习","第2天 · 20分钟 · 先手算，再单步验证",LearningPack.Tasks[3].Detail,form.colors.Dark))SaveDialogImage(dialog,Path.Combine(folder,theme+"-exercise.png"));
                }
                foreach(string path in new[]{form.core.StatePath,form.core.StatePath+".bak"})if(File.Exists(path))File.Delete(path);
            }
            RenderLearningSamples(folder);
        }
        private static void RenderLearningSamples(string folder)
        {
            using(MainForm form=new MainForm(true)) {
                form.core.Import8086Registers(DateTime.Today);form.page=2;form.plannerMode=1;
                foreach(string theme in new[]{"light","dark"}) {
                    form.core.Settings.ThemeMode=theme;form.ApplyTheme();form.glassActive=true;form.colors=Palette.ClearGlass(theme=="dark");
                    for(int day=0;day<2;day++) {
                        form.SetPlannerDate(DateTime.Today.AddDays(day));
                        using(Bitmap bmp=new Bitmap(680,920))using(Graphics g=Graphics.FromImage(bmp)) {
                            PreviewBackground(g,bmp.Size,theme=="dark");g.ScaleTransform(2,2);form.PaintScene(g);
                            bmp.Save(Path.Combine(folder,theme+"-8086-day"+(day+1)+".png"),System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                }
                foreach(string path in new[]{form.core.StatePath,form.core.StatePath+".bak"})if(File.Exists(path))File.Delete(path);
            }
        }
        private static void PreviewBackground(Graphics g,Size size,bool dark)
        {
            Rectangle r=new Rectangle(Point.Empty,size);
            using(LinearGradientBrush brush=new LinearGradientBrush(r,dark?Color.FromArgb(60,79,111):Color.FromArgb(191,214,239),dark?Color.FromArgb(114,108,145):Color.FromArgb(244,218,230),40f))g.FillRectangle(brush,r);
            using(LinearGradientBrush glow=new LinearGradientBrush(r,Color.FromArgb(50,255,244,227),Color.Transparent,LinearGradientMode.Vertical))g.FillRectangle(glow,r);
        }
        private static void SaveDialogImage(Form dialog,string file)
        {
            WidgetDialogBase previewDialog=dialog as WidgetDialogBase;
            if(previewDialog!=null)previewDialog.RenderingPreview=true;
            dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-20000,-20000);
            dialog.Show();Application.DoEvents();
            using(Bitmap bitmap=new Bitmap(dialog.Width,dialog.Height)) {
                dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(file,System.Drawing.Imaging.ImageFormat.Png);
            }
            dialog.Hide();
        }
#if DIAGNOSTICS
        internal static void RunLiveCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            string statePath=Path.Combine(folder,"live-check-state.json");
            using(MainForm form=new MainForm(false,statePath))
            using(System.Windows.Forms.Timer probe=new System.Windows.Forms.Timer())
            using(System.Diagnostics.Process process=System.Diagnostics.Process.GetCurrentProcess()) {
                int step=0;TimeSpan cpu=TimeSpan.Zero;DateTime start=DateTime.MinValue;
                string report="";probe.Interval=1600;
                probe.Tick+=delegate {
                    try {
                        step++;
                        if(step==1) {
                            form.core.Settings.ThemeMode="light";form.ApplyTheme();form.SelectPage(1);
                        } else if(step==2 || step==4) {
                            IntPtr atCenter=WindowFromPoint(new Point(form.Left+form.Width/2,form.Top+form.Height/2));
                            if(GetAncestor(atCenter,2)==form.Handle) {
                                using(Bitmap bmp=new Bitmap(form.Width,form.Height))using(Graphics g=Graphics.FromImage(bmp)) {
                                    g.CopyFromScreen(form.Location,Point.Empty,bmp.Size);bmp.Save(Path.Combine(folder,step==2?"live-glass-light.png":"live-glass-dark.png"),System.Drawing.Imaging.ImageFormat.Png);
                                }
                            }
                            if(step==4){cpu=process.TotalProcessorTime;start=DateTime.UtcNow;}
                        } else if(step==3) {form.core.Settings.ThemeMode="dark";form.ApplyTheme();}
                        else if(step==10) {
                            process.Refresh();double wall=(DateTime.UtcNow-start).TotalSeconds;
                            report="GlassActive="+form.glassActive+Environment.NewLine+"WorkingSetMiB="+(process.WorkingSet64/1048576.0).ToString("F1",CultureInfo.InvariantCulture)+Environment.NewLine+"PrivateMiB="+(process.PrivateMemorySize64/1048576.0).ToString("F1",CultureInfo.InvariantCulture)+Environment.NewLine+"IdleCpuSingleCorePercent="+((process.TotalProcessorTime-cpu).TotalSeconds/wall*100).ToString("F2",CultureInfo.InvariantCulture)+Environment.NewLine;
                            File.WriteAllText(Path.Combine(folder,"runtime-check.txt"),report);probe.Stop();form.ExitApp();
                        }
                    }catch(Exception ex){File.WriteAllText(Path.Combine(folder,"runtime-check-error.txt"),ex.ToString());probe.Stop();form.ExitApp();}
                };
                form.Shown+=delegate{probe.Start();};Application.Run(form);
            }
            foreach(string path in new[]{statePath,statePath+".bak"})if(File.Exists(path))File.Delete(path);
        }
#endif
    }
}
