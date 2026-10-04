using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeskSprout
{
    internal static class DialogStyle
    {
        internal static Color Background(bool dark) { return SystemInformation.HighContrast ? SystemColors.Window : dark ? Color.FromArgb(54,59,67) : Color.FromArgb(244,247,251); }
        internal static Color Surface(bool dark) { return SystemInformation.HighContrast ? SystemColors.Window : dark ? Color.FromArgb(70,76,86) : Color.FromArgb(255,255,255); }
        internal static Color Text(bool dark) { return SystemInformation.HighContrast ? SystemColors.WindowText : dark ? Color.FromArgb(242,246,251) : Color.FromArgb(43,53,69); }
        internal static Color Muted(bool dark) { return SystemInformation.HighContrast ? SystemColors.WindowText : dark ? Color.FromArgb(197,207,222) : Color.FromArgb(100,115,137); }
        internal static Color Accent(bool dark) { return SystemInformation.HighContrast ? SystemColors.Highlight : dark ? Color.FromArgb(218,227,241) : Color.FromArgb(83,103,133); }
        internal static Color Line(bool dark) { return SystemInformation.HighContrast ? SystemColors.WindowText : dark ? Color.FromArgb(111,122,139) : Color.FromArgb(209,221,239); }
    }

    internal abstract class WidgetDialogBase : Form
    {
        private readonly Font ownedFont;
        protected readonly bool Dark;
        protected readonly Label ErrorLabel;
        internal bool RenderingPreview;
        protected override bool ShowWithoutActivation {get{return RenderingPreview;}}
        protected override void OnHandleCreated(EventArgs e) {base.OnHandleCreated(e);NativeBackdrop.Apply(this,false,Dark);}
        protected WidgetDialogBase(string title, int width, int height, bool dark)
        {
            Dark=dark;
            ownedFont=new Font("Microsoft YaHei UI",9F,FontStyle.Regular,GraphicsUnit.Point);
            Font=ownedFont;
            AutoScaleMode=AutoScaleMode.None; DoubleBuffered=true;
            Text=title; ClientSize=new Size(width,height);
            BackColor=DialogStyle.Background(dark); ForeColor=DialogStyle.Text(dark);
            FormBorderStyle=FormBorderStyle.FixedDialog; StartPosition=FormStartPosition.CenterParent;
            MaximizeBox=false; MinimizeBox=false; ShowInTaskbar=false;
            ErrorLabel=AddLabel("",18,height-82,width-36,28);
            ErrorLabel.ForeColor=dark ? Color.FromArgb(235,175,143) : Color.FromArgb(165,85,53);
            ErrorLabel.AccessibleName="输入提示";
        }
        // Native input controls keep ordinary opaque painting. The soft shell is
        // drawn here without enabling per-pixel alpha on their GDI window surface.
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if(SystemInformation.HighContrast || ClientSize.Width<2 || ClientSize.Height<2) return;
            RectangleF bounds=new RectangleF(0,0,ClientSize.Width,ClientSize.Height);
            using(LinearGradientBrush sheen=new LinearGradientBrush(bounds,
                Color.FromArgb(Dark ? 30 : 100,255,255,255),Color.FromArgb(0,255,255,255),LinearGradientMode.Vertical))
                e.Graphics.FillRectangle(sheen,bounds);
            using(LinearGradientBrush rim=new LinearGradientBrush(bounds,
                Color.FromArgb(Dark ? 92 : 190,255,255,255),Color.FromArgb(Dark ? 28 : 80,255,255,255),LinearGradientMode.Vertical))
            using(Pen edge=new Pen(rim,1))
                e.Graphics.DrawRectangle(edge,.5F,.5F,ClientSize.Width-1F,ClientSize.Height-1F);
            using(Pen line=new Pen(Color.FromArgb(Dark ? 25 : 60,255,255,255),1))
                e.Graphics.DrawLine(line,18,50,ClientSize.Width-18,50);
        }
        protected Label AddLabel(string text,int x,int y,int w,int h)
        {
            Label label=new Label { Text=text,Bounds=new Rectangle(x,y,w,h),TextAlign=ContentAlignment.MiddleLeft,ForeColor=ForeColor,BackColor=Color.Transparent };
            Controls.Add(label); return label;
        }
        protected void Hint(string text)
        {
            Label label=AddLabel(text,18,10,ClientSize.Width-36,36); label.ForeColor=DialogStyle.Muted(Dark);
        }
        protected NumericUpDown Number(string name,int min,int max,int value,int x,int y,int width)
        {
            NumericUpDown number=new NumericUpDown { Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,value)),
                Bounds=new Rectangle(x,y,width,28),TextAlign=HorizontalAlignment.Center,
                BackColor=DialogStyle.Surface(Dark),ForeColor=ForeColor,BorderStyle=BorderStyle.FixedSingle,AccessibleName=name };
            Controls.Add(number); return number;
        }
        protected ComboBox Choices(string name,string[] items,int selected,int x,int y,int width)
        {
            ComboBox combo=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Bounds=new Rectangle(x,y,width,28),
                BackColor=DialogStyle.Surface(Dark),ForeColor=ForeColor,FlatStyle=FlatStyle.Flat,AccessibleName=name };
            combo.Items.AddRange(items); combo.SelectedIndex=selected; Controls.Add(combo); return combo;
        }
        protected CheckBox Check(string text,bool value,int x,int y,int width)
        {
            CheckBox check=new CheckBox { Text=text,Checked=value,Bounds=new Rectangle(x,y,width,28),ForeColor=ForeColor,BackColor=Color.Transparent };
            Controls.Add(check); return check;
        }
        protected void Buttons(string text,EventHandler save)
        {
            Button accept=new Button { Text=text,Bounds=new Rectangle(ClientSize.Width-104,ClientSize.Height-48,86,32),
                FlatStyle=FlatStyle.Flat,BackColor=DialogStyle.Accent(Dark),ForeColor=SystemInformation.HighContrast ? SystemColors.HighlightText : Dark ? Color.FromArgb(41,54,75) : Color.White,UseVisualStyleBackColor=false };
            accept.FlatAppearance.BorderSize=0; accept.Click+=save;
            Button cancel=new Button { Text="取消",Bounds=new Rectangle(ClientSize.Width-198,ClientSize.Height-48,86,32),
                FlatStyle=FlatStyle.Flat,BackColor=DialogStyle.Surface(Dark),ForeColor=ForeColor,DialogResult=DialogResult.Cancel,UseVisualStyleBackColor=false };
            cancel.FlatAppearance.BorderColor=DialogStyle.Line(Dark);
            Controls.Add(cancel); Controls.Add(accept); AcceptButton=accept; CancelButton=cancel;
        }
        protected void FinalizeLayout()
        {
            float factor;
            using(Graphics graphics=CreateGraphics()) factor=graphics.DpiX/96F;
            if(Math.Abs(factor-1F)<0.01F) return;
            SuspendLayout();
            Size original=ClientSize;
            foreach(Control control in Controls) {
                Rectangle bounds=control.Bounds;
                control.Bounds=new Rectangle((int)Math.Round(bounds.X*factor),(int)Math.Round(bounds.Y*factor),
                    (int)Math.Round(bounds.Width*factor),(int)Math.Round(bounds.Height*factor));
            }
            ClientSize=new Size((int)Math.Round(original.Width*factor),(int)Math.Round(original.Height*factor));
            ResumeLayout(false);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing); if(disposing) ownedFont.Dispose();
        }
    }

    internal sealed class DurationDialog : WidgetDialogBase
    {
        private readonly NumericUpDown hours,minutes,seconds;
        public int ResultSeconds { get; private set; }
        public DurationDialog(int totalSeconds,bool dark) : base("自定义计时",346,211,dark)
        {
            int initial=Math.Max(1,Math.Min(359999,totalSeconds)); ResultSeconds=initial;
            Hint("留一小段时间，专心做一件事。\n最长可设置 99 小时 59 分 59 秒。");
            AddLabel("小时",18,54,94,22); AddLabel("分钟",122,54,94,22); AddLabel("秒",226,54,102,22);
            hours=Number("小时",0,99,initial/3600,18,80,94);
            minutes=Number("分钟",0,59,initial/60%60,122,80,94);
            seconds=Number("秒",0,59,initial%60,226,80,102);
            EventHandler clear=delegate { ErrorLabel.Text=""; }; hours.ValueChanged+=clear; minutes.ValueChanged+=clear; seconds.ValueChanged+=clear;
            Buttons("应用",delegate {
                int result=Decimal.ToInt32(hours.Value)*3600+Decimal.ToInt32(minutes.Value)*60+Decimal.ToInt32(seconds.Value);
                if(result==0) { ErrorLabel.Text="请设置大于 0 秒的时长。"; seconds.Focus(); return; }
                ResultSeconds=result; DialogResult=DialogResult.OK; Close();
            });
            Shown+=delegate { if(!RenderingPreview){minutes.Focus(); minutes.Select(0,minutes.Text.Length);} };
            FinalizeLayout();
        }
    }

    internal sealed class PomodoroDialog : WidgetDialogBase
    {
        private readonly NumericUpDown focus,shortBreak,longBreak,cycle;
        private readonly CheckBox automatic;
        public int FocusMinutes { get; private set; }
        public int ShortMinutes { get; private set; }
        public int LongMinutes { get; private set; }
        public int CycleLength { get; private set; }
        public bool AutoAdvance { get; private set; }
        public PomodoroDialog(int focusMinutes,int shortMinutes,int longMinutes,int cycleLength,bool autoAdvance,bool dark)
            : base("番茄钟设置",346,345,dark)
        {
            Hint("找到自己的节奏。完成一轮专注后短休息，\n每隔指定轮数，安排一次长休息。");
            AddLabel("专注 / 分钟",18,54,114,28); focus=Number("专注时长，分钟",1,180,focusMinutes,140,54,188);
            AddLabel("短休息 / 分钟",18,96,114,28); shortBreak=Number("短休息时长，分钟",1,60,shortMinutes,140,96,188);
            AddLabel("长休息 / 分钟",18,138,114,28); longBreak=Number("长休息时长，分钟",1,120,longMinutes,140,138,188);
            AddLabel("长休息 / 轮",18,180,114,28); cycle=Number("长休息间隔，轮",1,12,cycleLength,140,180,188);
            automatic=Check("结束后自动进入下一阶段",autoAdvance,18,224,310);
            Buttons("保存",delegate {
                FocusMinutes=Decimal.ToInt32(focus.Value); ShortMinutes=Decimal.ToInt32(shortBreak.Value);
                LongMinutes=Decimal.ToInt32(longBreak.Value); CycleLength=Decimal.ToInt32(cycle.Value);
                AutoAdvance=automatic.Checked; DialogResult=DialogResult.OK; Close();
            });
            Shown+=delegate { if(!RenderingPreview){focus.Focus(); focus.Select(0,focus.Text.Length);} };
            FinalizeLayout();
        }
    }

    internal sealed class EntryDialog : WidgetDialogBase
    {
        private readonly TextBox titleBox;
        private readonly DateTimePicker dayPicker,timePicker;
        private readonly CheckBox timed,reminder;
        private readonly ComboBox kindBox,repeatBox;
        private readonly NumericUpDown estimate;
        public string EntryTitle { get; private set; }
        public DateTime EntryDate { get; private set; }
        public bool HasTime { get; private set; }
        public string EntryKind { get; private set; }
        public string Repeat { get; private set; }
        public bool ReminderEnabled { get; private set; }
        public int? EstimatedMinutes { get; private set; }
        public EntryDialog(string title,DateTime date,bool hasTime,string kind,bool dark,string repeat="none",bool reminderEnabled=true,int? estimatedMinutes=null)
            : base(String.IsNullOrEmpty(title) ? "添加计划 / 日程" : "编辑计划 / 日程",368,439,dark)
        {
            Hint("记下下一步，让桌面替你留心。\n计划用来完成，日程用来安排时间。");
            AddLabel("标题",18,54,62,28);
            titleBox=new TextBox { Text=title??"",MaxLength=120,Bounds=new Rectangle(88,54,262,28),
                BorderStyle=BorderStyle.FixedSingle,BackColor=DialogStyle.Surface(dark),ForeColor=ForeColor,AccessibleName="标题" };
            Controls.Add(titleBox);
            AddLabel("类型",18,96,62,28);
            kindBox=Choices("类型",new string[]{"计划 · 待完成","日程 · 安排时间"},kind=="schedule" ? 1 : 0,88,96,262);
            DateTime initial=date<DateTimePicker.MinimumDateTime || date>DateTimePicker.MaximumDateTime ? DateTime.Today : date;
            AddLabel("日期",18,138,62,28);
            dayPicker=new DateTimePicker { Format=DateTimePickerFormat.Custom,CustomFormat="yyyy 年 MM 月 dd 日",Value=initial.Date,
                Bounds=new Rectangle(88,138,262,28),CalendarMonthBackground=DialogStyle.Surface(dark),CalendarForeColor=ForeColor,AccessibleName="日期" };
            Controls.Add(dayPicker);
            AddLabel("时间",18,180,62,28); timed=Check("指定时间",hasTime,88,180,113);
            timePicker=new DateTimePicker { Format=DateTimePickerFormat.Custom,CustomFormat="HH:mm",ShowUpDown=true,Value=initial,
                Bounds=new Rectangle(211,180,139,28),Enabled=hasTime,AccessibleName="时间" }; Controls.Add(timePicker);
            AddLabel("重复",18,222,62,28);
            repeatBox=Choices("重复频率",new string[]{"不重复","每天","每周同一天"},repeat=="daily" ? 1 : repeat=="weekly" ? 2 : 0,88,222,262);
            AddLabel("预计分钟",18,264,62,28);
            estimate=Number("预计时长，分钟；0 表示未估",0,480,estimatedMinutes??0,88,264,162);
            AddLabel("0 = 未估",260,264,90,28).ForeColor=DialogStyle.Muted(dark);
            reminder=Check("到时提醒",reminderEnabled,88,306,262); reminder.Enabled=hasTime;
            timed.CheckedChanged+=delegate { timePicker.Enabled=timed.Checked; reminder.Enabled=timed.Checked; };
            titleBox.TextChanged+=delegate { ErrorLabel.Text=""; };
            Buttons("保存",delegate {
                string entryTitle=titleBox.Text.Trim();
                if(entryTitle.Length==0) { ErrorLabel.Text="请先写一个标题。"; titleBox.Focus(); return; }
                if(entryTitle.Length>120) { ErrorLabel.Text="标题最多 120 个字符。"; titleBox.Focus(); return; }
                EntryTitle=entryTitle; HasTime=timed.Checked; EntryDate=dayPicker.Value.Date;
                if(HasTime) EntryDate=EntryDate.AddHours(timePicker.Value.Hour).AddMinutes(timePicker.Value.Minute);
                EntryKind=kindBox.SelectedIndex==1 ? "schedule" : "plan";
                Repeat=repeatBox.SelectedIndex==1 ? "daily" : repeatBox.SelectedIndex==2 ? "weekly" : "none";
                ReminderEnabled=HasTime && reminder.Checked;
                EstimatedMinutes=estimate.Value>0 ? (int?)Decimal.ToInt32(estimate.Value) : null;
                DialogResult=DialogResult.OK; Close();
            });
            Shown+=delegate { if(!RenderingPreview){titleBox.Focus(); titleBox.SelectionStart=titleBox.Text.Length;} };
            FinalizeLayout();
        }
    }
}
