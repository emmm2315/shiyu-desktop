using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DeskSprout
{
    internal sealed class TextEntryDialog : WidgetDialogBase
    {
        private readonly TextBox input;
        public string ResultText { get; private set; }
        public TextEntryDialog(string title,string hint,string initial,bool dark,int maxLength=500)
            : base(title,400,318,dark)
        {
            int limit=Math.Max(1,maxLength);
            Hint(hint);
            input=new TextBox { Multiline=true,AcceptsReturn=true,MaxLength=limit,ScrollBars=ScrollBars.Vertical,
                Bounds=new Rectangle(18,54,364,155),Text=initial??"",BorderStyle=BorderStyle.FixedSingle,
                BackColor=DialogStyle.Surface(dark),ForeColor=ForeColor,AccessibleName=title };
            Controls.Add(input);
            Label count=AddLabel("",18,214,364,22); count.ForeColor=DialogStyle.Muted(dark);
            EventHandler update=delegate { count.Text=input.Text.Length+" / "+limit+" · Ctrl + Enter 保存"; ErrorLabel.Text=""; };
            input.TextChanged+=update; update(this,EventArgs.Empty);
            EventHandler save=delegate {
                if(input.Text.Length>limit) { ErrorLabel.Text="最多可输入 "+limit+" 个字符。"; input.Focus(); return; }
                ResultText=input.Text.Trim(); DialogResult=DialogResult.OK; Close();
            };
            Buttons("保存",save);
            input.KeyDown+=delegate(object sender,KeyEventArgs e) {
                if(e.Control && e.KeyCode==Keys.Enter) { e.SuppressKeyPress=true; e.Handled=true; save(sender,EventArgs.Empty); }
            };
            Shown+=delegate { if(!RenderingPreview) { input.Focus(); input.SelectionStart=input.Text.Length; } };
            FinalizeLayout();
        }
    }

    internal sealed class PriorityDialog : WidgetDialogBase
    {
        private sealed class Choice
        {
            internal string Id,Title;
            public override string ToString() { return Title; }
        }
        private readonly CheckedListBox checklist;
        public List<string> SelectedIds { get; private set; }
        public PriorityDialog(WidgetCore core,DateTime day,bool dark)
            : base("每日三件事",400,336,dark)
        {
            day=day.Date;
            SelectedIds=new List<string>();
            Hint(day.ToString("M 月 d 日")+" · 最多选 3 件值得完成的事。\n已完成的计划也会保留，让今天的进展看得见。");
            checklist=new CheckedListBox { Bounds=new Rectangle(18,54,364,185),CheckOnClick=true,
                IntegralHeight=false,HorizontalScrollbar=true,BorderStyle=BorderStyle.FixedSingle,
                BackColor=DialogStyle.Surface(dark),ForeColor=ForeColor,AccessibleName="每日三件事候选计划" };
            Controls.Add(checklist);
            List<string> selected=core.ExtrasFor(day).PriorityTaskIds??new List<string>();
            List<PlanItem> candidates=new List<PlanItem>();
            foreach(PlanItem item in core.ActionablePlans(day)) {
                if(item.Kind==PlanKind.Plan && !candidates.Exists(delegate(PlanItem p){return p.Id==item.Id;})) candidates.Add(item);
            }
            foreach(PlanItem item in core.Items) {
                if(item.Kind!=PlanKind.Plan) continue;
                bool completedToday=item.Repeat==RepeatKind.None
                    ? item.Completed && item.CompletedDate.HasValue && item.CompletedDate.Value.Date==day
                    : core.IsDueOn(item,day) && core.IsCompletedOn(item,day);
                if((completedToday || selected.Contains(item.Id)) && !candidates.Exists(delegate(PlanItem p){return p.Id==item.Id;})) candidates.Add(item);
            }
            foreach(PlanItem item in candidates) {
                bool completed=core.IsCompletedOn(item,day);
                Choice choice=new Choice { Id=item.Id,Title=(completed ? "已完成 · " : "")+item.Title };
                checklist.Items.Add(choice,selected.Contains(item.Id));
            }
            checklist.ItemCheck+=delegate(object sender,ItemCheckEventArgs e) {
                int after=checklist.CheckedItems.Count+(e.NewValue==CheckState.Checked ? 1 : 0)-(e.CurrentValue==CheckState.Checked ? 1 : 0);
                if(after>3) { e.NewValue=e.CurrentValue; ErrorLabel.Text="最多选 3 件。先取消一项再添加。"; }
                else ErrorLabel.Text="";
            };
            if(checklist.Items.Count==0) {
                checklist.Visible=false;
                Label empty=AddLabel("这一天还没有可选计划。\n先添加计划，再挑出最重要的三件事。",18,54,364,185);
                empty.TextAlign=ContentAlignment.MiddleCenter; empty.ForeColor=DialogStyle.Muted(dark);
            }
            Buttons("保存",delegate {
                if(checklist.CheckedItems.Count>3) { ErrorLabel.Text="每日三件事最多可选 3 项。"; return; }
                List<string> result=new List<string>();
                foreach(Choice choice in checklist.CheckedItems) result.Add(choice.Id);
                SelectedIds=result; DialogResult=DialogResult.OK; Close();
            });
            Shown+=delegate { if(!RenderingPreview && checklist.Visible) checklist.Focus(); };
            FinalizeLayout();
        }
    }

    internal sealed class ImportantDayDialog : WidgetDialogBase
    {
        private readonly TextBox titleBox;
        private readonly DateTimePicker picker;
        private readonly CheckBox annual;
        public string TitleText { get; private set; }
        public DateTime DayDate { get; private set; }
        public bool Annual { get; private set; }
        public ImportantDayDialog(bool dark) : base("添加重要日期",368,273,dark)
        {
            Hint("给值得期待的日子留一个位置。\n纪念日可以设为每年重复。");
            AddLabel("名称",18,54,62,28);
            titleBox=new TextBox { MaxLength=120,Bounds=new Rectangle(88,54,262,28),BorderStyle=BorderStyle.FixedSingle,
                BackColor=DialogStyle.Surface(dark),ForeColor=ForeColor,AccessibleName="重要日期名称" }; Controls.Add(titleBox);
            AddLabel("日期",18,96,62,28);
            picker=new DateTimePicker { Format=DateTimePickerFormat.Custom,CustomFormat="yyyy 年 MM 月 dd 日",Value=DateTime.Today,
                Bounds=new Rectangle(88,96,262,28),CalendarMonthBackground=DialogStyle.Surface(dark),CalendarForeColor=ForeColor,AccessibleName="重要日期" };
            Controls.Add(picker);
            annual=Check("每年重复",false,88,138,262);
            titleBox.TextChanged+=delegate { ErrorLabel.Text=""; };
            Buttons("添加",delegate {
                string title=titleBox.Text.Trim();
                if(title.Length==0 || title.Length>120) { ErrorLabel.Text="名称需包含 1 至 120 个字符。"; titleBox.Focus(); return; }
                TitleText=title; DayDate=picker.Value.Date; Annual=annual.Checked; DialogResult=DialogResult.OK; Close();
            });
            Shown+=delegate { if(!RenderingPreview) titleBox.Focus(); };
            FinalizeLayout();
        }
    }

    internal sealed class ReflectionDialog : WidgetDialogBase
    {
        private readonly ComboBox moodBox;
        private readonly TextBox reflection;
        public string Mood { get; private set; }
        public string ReflectionText { get; private set; }
        public ReflectionDialog(WidgetCore core,DateTime date,bool dark) : base("轻量复盘",400,400,dark)
        {
            date=date.Date;
            Hint(date.ToString("yyyy 年 M 月 d 日")+" · 留下今天的一句话。\n写下收获、卡住的地方，或者明天的第一步。");
            DailyStats stats=core.StatsOn(date);
            AddLabel("完成 "+stats.FocusSessions+" 个番茄 · 专注 "+stats.FocusMinutes+" 分钟 · 完成 "+stats.CompletedPlans+" 项",18,54,364,28);
            string[] moods={"暂不记录","开心","平静","充实","疲惫","低落"};
            string currentMood=core.ExtrasFor(date).Mood??"";
            int selected=Array.IndexOf(moods,currentMood); if(selected<0) selected=0;
            AddLabel("心情",18,92,62,28);
            moodBox=Choices("今日心情",moods,selected,88,92,294);
            reflection=new TextBox { Multiline=true,AcceptsReturn=true,MaxLength=2000,ScrollBars=ScrollBars.Vertical,
                Bounds=new Rectangle(18,134,364,160),Text=core.ExtrasFor(date).Reflection??"",BorderStyle=BorderStyle.FixedSingle,
                BackColor=DialogStyle.Surface(dark),ForeColor=ForeColor,AccessibleName="复盘内容" }; Controls.Add(reflection);
            Label count=AddLabel("",18,298,364,20); count.ForeColor=DialogStyle.Muted(dark);
            EventHandler update=delegate { count.Text=reflection.Text.Length+" / 2000 · Ctrl + Enter 保存"; ErrorLabel.Text=""; };
            reflection.TextChanged+=update; update(this,EventArgs.Empty);
            EventHandler save=delegate {
                if(reflection.Text.Length>2000) { ErrorLabel.Text="复盘最多 2000 个字符。"; reflection.Focus(); return; }
                Mood=moodBox.SelectedIndex<=0 ? "" : moods[moodBox.SelectedIndex]; ReflectionText=reflection.Text.Trim();
                DialogResult=DialogResult.OK; Close();
            };
            Buttons("保存",save);
            reflection.KeyDown+=delegate(object sender,KeyEventArgs e) {
                if(e.Control && e.KeyCode==Keys.Enter) { e.SuppressKeyPress=true; e.Handled=true; save(sender,EventArgs.Empty); }
            };
            Shown+=delegate { if(!RenderingPreview) { reflection.Focus(); reflection.SelectionStart=reflection.Text.Length; } };
            FinalizeLayout();
        }
    }

    internal sealed class LearningPackDialog : WidgetDialogBase
    {
        private readonly DateTimePicker picker;
        public DateTime StartDate { get; private set; }
        public LearningPackDialog(bool dark,LearningPackDefinition pack=null) : base(pack==null?"8086 · 寄存器入门":pack.Title,410,269,dark)
        {
            if(pack==null)pack=LearningPack.All[0];
            Hint(pack.Summary);
            Label help=AddLabel("可调整计划日期。加入后，每天有一项概念学习\n和一项动手练习，完成一项就勾掉一项。",18,62,374,44);
            help.ForeColor=DialogStyle.Muted(dark);
            AddLabel("开始日期",18,124,82,28);
            picker=new DateTimePicker { Format=DateTimePickerFormat.Custom,CustomFormat="yyyy 年 MM 月 dd 日",Value=DateTime.Today,
                MaxDate=DateTimePicker.MaximumDateTime.Date.AddDays(-(pack.Days-1)),Bounds=new Rectangle(108,124,284,28),
                CalendarMonthBackground=DialogStyle.Surface(dark),CalendarForeColor=ForeColor,AccessibleName="学习包开始日期" }; Controls.Add(picker);
            Buttons("加入计划",delegate { StartDate=picker.Value.Date; DialogResult=DialogResult.OK; Close(); });
            Shown+=delegate { if(!RenderingPreview) picker.Focus(); };
            FinalizeLayout();
        }
    }
}
