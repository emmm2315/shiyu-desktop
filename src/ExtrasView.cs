using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace DeskSprout
{
    internal sealed partial class MainForm
    {
        private DateTime celebrationUntil=DateTime.MinValue;
        private readonly System.Windows.Forms.Timer celebrationTimer=new System.Windows.Forms.Timer();
        private void PaintPriorityBadge(Graphics g)
        {
            List<PlanItem> priorities=core.PrioritiesFor(core.LocalNow.Date);
            string label=priorities.Count==0?"三件事":"重点 "+priorities.Count(i=>core.IsCompletedOn(i,core.LocalNow.Date))+"/"+priorities.Count;
            RectangleF r=new RectangleF(146,17,64,23);Art.Fill(g,r,7,colors.Surface);
            Art.Text(g,label,r.X,r.Y,r.Width,r.Height,9,colors.Accent,FontStyle.Regular,StringAlignment.Center);
            HitArea(r,"挑选今天最重要的三件事",delegate{ShowPriorities(DateTime.Today);});
        }

        private void PaintPlant(Graphics g,RectangleF r,int sessions)
        {
            GraphicsState saved=g.Save();g.TranslateTransform(r.X,r.Y);g.ScaleTransform(r.Width/24,r.Height/24);
            Color leaf=colors.Dark?Color.FromArgb(198,224,209):Color.FromArgb(67,113,94);
            using(Pen stem=new Pen(leaf,1.5f)) {
                stem.StartCap=stem.EndCap=LineCap.Round;
                g.DrawArc(stem,5,18,14,3,0,180);
                if(sessions==0) {using(Brush seed=new SolidBrush(leaf))g.FillEllipse(seed,10,15,4,4);}
                else {
                    g.DrawLine(stem,12,18,12,7);
                    using(Brush b=new SolidBrush(leaf)) {
                        g.FillEllipse(b,5,10,8,4);g.FillEllipse(b,12,7,8,4);
                        if(sessions>=2)g.FillEllipse(b,4,4,8,5);
                        if(sessions>=3)g.FillEllipse(b,12,12,8,4);
                    }
                    if(sessions>=4) {
                        using(Brush petals=new SolidBrush(colors.Orange)) {
                            g.FillEllipse(petals,9,1,6,6);g.FillEllipse(petals,6,4,6,6);g.FillEllipse(petals,12,4,6,6);
                        }
                        using(Brush center=new SolidBrush(colors.Accent))g.FillEllipse(center,10,5,4,4);
                    }
                }
            }
            g.Restore(saved);
        }
        private void PaintExtrasFooter(Graphics g)
        {
            PaintPlant(g,new RectangleF(22,442,21,18),core.TodayStats.FocusSessions);
            HitArea(new RectangleF(20,442,24,18),"专注小植物 · 查看最近七天",ShowGarden);
            string text=core.TodayStats.FocusSessions==0?"小芽，等一段专注":"小芽 · 今日 "+core.TodayStats.FocusSessions+" 个番茄";
            if(core.Extras.ImportantDays.Count>0) {
                List<ImportantDay> days=core.Extras.ImportantDays.OrderBy(d=>core.NextImportantOccurrence(d,DateTime.Today)).ToList();
                ImportantDay day=days[(DateTime.Now.Hour*60+DateTime.Now.Minute)%days.Count];
                text=ImportantLabel(day);
            }
            Art.Text(g,text,47,443,135,15,9,colors.Muted,FontStyle.Regular,StringAlignment.Near);
            HitArea(new RectangleF(45,442,138,18),text,core.Extras.ImportantDays.Count>0?(Action)ShowImportantDays:ShowGarden);
            RectangleF extras=new RectangleF(185,441,27,19);
            if(hover==hits.Count)Art.Fill(g,extras,6,colors.AccentSoft);
            float cx=extras.X+13,cy=extras.Y+9;
            using(Pen pen=new Pen(colors.Accent,1.3f)) {
                g.DrawLines(pen,new[]{new PointF(cx,cy-6),new PointF(cx+2,cy-2),new PointF(cx+6,cy),new PointF(cx+2,cy+2),new PointF(cx,cy+6),new PointF(cx-2,cy+2),new PointF(cx-6,cy),new PointF(cx-2,cy-2),new PointF(cx,cy-6)});
            }
            HitArea(extras,"小趣味：抽签、三件事、随手记、学习任务包",ShowExtrasMenu);
        }
        private void PaintPriorityGlint(Graphics g,int width,int height)
        {
            if(DateTime.UtcNow>=celebrationUntil)return;
            Art.Outline(g,new RectangleF(2,2,width-4,height-4),17,Color.FromArgb(215,255,246,213),2);
        }
        private void CelebratePrioritiesIfReady()
        {
            DateTime today=core.LocalNow.Date;DailyExtraRecord day=core.ExtrasFor(today);
            List<PlanItem> items=core.PrioritiesFor(today);
            if(items.Count==0 || day.PriorityCelebrated || !items.All(i=>core.IsCompletedOn(i,today)))return;
            day.PriorityCelebrated=true;SaveQuietly();
            if(!Visible || preview)return;
            celebrationUntil=DateTime.UtcNow.AddMilliseconds(850);celebrationTimer.Interval=900;
            celebrationTimer.Stop();celebrationTimer.Tick-=EndPriorityCelebration;celebrationTimer.Tick+=EndPriorityCelebration;
            celebrationTimer.Start();Invalidate();
        }
        private void EndPriorityCelebration(object sender,EventArgs e) {celebrationTimer.Stop();celebrationUntil=DateTime.MinValue;Invalidate();}
        private void ShowExtrasMenu()
        {
            ContextMenuStrip menu=NewMenu();
            AddMenu(menu,"专注小植物 · 七天花园…",ShowGarden,false);
            AddMenu(menu,"下一步抽签…",ShowLottery,false);
            AddMenu(menu,"今日三件事…",delegate{ShowPriorities(DateTime.Today);},false);
            AddMenu(menu,"明天接着做…",delegate{EditNextStep(LinkedTaskId);},false);
            AddMenu(menu,"重要日倒数…",ShowImportantDays,false);
            AddMenu(menu,"随手记气泡…",ShowQuickNotes,false);
            AddMenu(menu,"一日小结…",delegate{ShowReflection(DateTime.Today);},false);
            menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu,"学习任务包 · 8086 / 操作系统 / 算法…",ShowLearningPack,false);
            ShowPopup(menu);
        }
        private void ShowGarden()
        {
            List<string> lines=new List<string>();
            for(int i=0;i<7;i++) {
                DateTime date=DateTime.Today.AddDays(-6+i);DailyStats stats=core.StatsOn(date);
                string stage=stats.FocusSessions==0?"种子":stats.FocusSessions==1?"发芽":stats.FocusSessions<4?"长叶":"开花";
                lines.Add(date.ToString("M/d ddd",CultureInfo.GetCultureInfo("zh-CN"))+"    "+stage+"    "+stats.FocusSessions+" 个番茄 · "+stats.FocusMinutes+" 分钟");
            }
            using(ReadNoteDialog dialog=new ReadNoteDialog("专注小植物","每完成一个番茄，小芽长一点。\n每天留下一株，休息日保留一粒种子。",String.Join("\r\n\r\n",lines.ToArray()),colors.Dark))ShowOwnedDialog(dialog);
        }
        private void ShowLottery()
        {
            using(LotteryDialog dialog=new LotteryDialog(core,colors.Dark))
                if(ShowOwnedDialog(dialog)==DialogResult.OK) {
                    bool active=core.Pomodoro.Status==TimerStatus.Running || core.Pomodoro.Status==TimerStatus.Paused;
                    core.Settings.FocusTaskId=dialog.SelectedTaskId;SelectPage(1);SaveQuietly();Invalidate();
                    if(active)Notify("下次专注任务已选","当前一段继续计入原任务，新选择在下一段专注生效。",false);
                }
        }
        private void ShowPriorities(DateTime day)
        {
            using(PriorityDialog dialog=new PriorityDialog(core,day,colors.Dark))
                if(ShowOwnedDialog(dialog)==DialogResult.OK) {core.SetPriorities(day,dialog.SelectedIds);SaveQuietly();CelebratePrioritiesIfReady();Invalidate();}
        }
        private void EditNextStep(string taskId)
        {
            PlanItem task=core.Items.FirstOrDefault(i=>i.Id==taskId);
            if(task==null)taskId=null;
            string hint=(task==null?"自由专注":task.Title)+"\n留一句具体下一步；下次开始专注时会显示。";
            using(TextEntryDialog dialog=new TextEntryDialog("明天接着做",hint,core.NextStepFor(taskId),colors.Dark))
                if(ShowOwnedDialog(dialog)==DialogResult.OK) {core.SetNextStep(taskId,dialog.ResultText);SaveQuietly();}
        }
        private bool RecallNextStep()
        {
            string id=core.Settings.FocusTaskId;string note=core.NextStepFor(id);
            if(String.IsNullOrEmpty(note))return true;
            PlanItem task=core.Items.FirstOrDefault(i=>i.Id==id);
            using(ReadNoteDialog dialog=new ReadNoteDialog("接着上次的思路",task==null?"自由专注":task.Title,note,colors.Dark,true))
                return ShowOwnedDialog(dialog)==DialogResult.OK;
        }
        private string ImportantLabel(ImportantDay day)
        {
            int count=(core.NextImportantOccurrence(day,DateTime.Today)-DateTime.Today).Days;
            return day.Title+(count==0?" · 就是今天":count>0?" · 还有 "+count+" 天":" · 已过 "+(-count)+" 天");
        }
        private void ShowImportantDays()
        {
            ContextMenuStrip menu=NewMenu();AddMenu(menu,"＋ 添加重要日期…",AddImportantDay,false);
            if(core.Extras.ImportantDays.Count>0)menu.Items.Add(new ToolStripSeparator());
            foreach(ImportantDay day in core.Extras.ImportantDays.OrderBy(d=>core.NextImportantOccurrence(d,DateTime.Today))) {
                ImportantDay selected=day;ToolStripMenuItem item=new ToolStripMenuItem(ImportantLabel(day));
                item.ToolTipText=day.Date.ToString("yyyy年M月d日")+(day.Annual?" · 每年":"");
                ToolStripMenuItem detail=new ToolStripMenuItem("查看日期");detail.Click+=delegate{ShowInformation(item.ToolTipText+"\n"+ImportantLabel(selected),selected.Title);};
                ToolStripMenuItem remove=new ToolStripMenuItem("删除");remove.Click+=delegate{core.RemoveImportantDay(selected.Id);SaveQuietly();Invalidate();};
                item.DropDownItems.Add(detail);item.DropDownItems.Add(remove);menu.Items.Add(item);
            }
            ShowPopup(menu);
        }
        private void AddImportantDay()
        {
            using(ImportantDayDialog dialog=new ImportantDayDialog(colors.Dark))
                if(ShowOwnedDialog(dialog)==DialogResult.OK) {core.AddImportantDay(dialog.TitleText,dialog.DayDate,dialog.Annual);SaveQuietly();Invalidate();}
        }
        private void AddQuickNote()
        {
            using(TextEntryDialog dialog=new TextEntryDialog("随手记气泡","先记下冒出的想法，稍后转为计划或日程。\nCtrl + J 也能打开这里。","",colors.Dark,2000))
                if(ShowOwnedDialog(dialog)==DialogResult.OK && !String.IsNullOrWhiteSpace(dialog.ResultText)) {core.AddQuickNote(dialog.ResultText);SaveQuietly();Invalidate();}
        }
        private void ShowQuickNotes()
        {
            ContextMenuStrip menu=NewMenu();AddMenu(menu,"＋ 记一个想法…",AddQuickNote,false);
            if(core.Extras.Notes.Count==0){ToolStripMenuItem empty=new ToolStripMenuItem("灵感会留在这里");empty.Enabled=false;menu.Items.Add(empty);}
            else {
                menu.Items.Add(new ToolStripSeparator());
                foreach(QuickNote note in core.Extras.Notes.OrderByDescending(n=>n.CreatedAtUtc)) {
                    QuickNote selected=note;string title=note.Text.Replace("\r"," ").Replace("\n"," ");if(title.Length>28)title=title.Substring(0,28)+"…";
                    ToolStripMenuItem item=new ToolStripMenuItem(title);item.ToolTipText=note.CreatedAtUtc.ToLocalTime().ToString("M/d HH:mm");
                    ToolStripMenuItem read=new ToolStripMenuItem("查看全文");read.Click+=delegate{using(ReadNoteDialog d=new ReadNoteDialog("随手记",selected.CreatedAtUtc.ToLocalTime().ToString("M月d日 HH:mm"),selected.Text,colors.Dark))ShowOwnedDialog(d);};
                    ToolStripMenuItem plan=new ToolStripMenuItem("转成计划…");plan.Click+=delegate{ConvertNote(selected,false);};
                    ToolStripMenuItem schedule=new ToolStripMenuItem("转成日程…");schedule.Click+=delegate{ConvertNote(selected,true);};
                    ToolStripMenuItem remove=new ToolStripMenuItem("删除");remove.Click+=delegate{core.RemoveQuickNote(selected.Id);SaveQuietly();Invalidate();};
                    item.DropDownItems.AddRange(new ToolStripItem[]{read,plan,schedule,remove});menu.Items.Add(item);
                }
            }
            ShowPopup(menu);
        }
        private void ConvertNote(QuickNote note,bool schedule)
        {
            string title=note.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"想法";
            if(title.Length>120)title=title.Substring(0,120);
            DateTime date=page==2?SelectedScheduleDate:DateTime.Today;
            using(EntryDialog dialog=new EntryDialog(title,date,schedule,schedule?"schedule":"plan",colors.Dark))
                if(ShowOwnedDialog(dialog)==DialogResult.OK) {
                    int? minute=dialog.HasTime?(int?)(dialog.EntryDate.Hour*60+dialog.EntryDate.Minute):null;
                    PlanKind kind=dialog.EntryKind=="schedule"?PlanKind.Schedule:PlanKind.Plan;
                    PlanItem item=core.ConvertQuickNote(note.Id,dialog.EntryDate.Date,kind,minute,dialog.EstimatedMinutes);
                    RepeatKind repeat=dialog.Repeat=="daily"?RepeatKind.Daily:dialog.Repeat=="weekly"?RepeatKind.Weekly:RepeatKind.None;
                    core.UpdateItem(item.Id,dialog.EntryTitle,dialog.EntryDate.Date,minute,kind,repeat,dialog.ReminderEnabled);
                    SaveQuietly();SetPlannerDate(dialog.EntryDate);SelectPage(2);SetPlannerMode(1);
                }
        }
        private void ShowReflection(DateTime date)
        {
            using(ReflectionDialog dialog=new ReflectionDialog(core,date,colors.Dark))
                if(ShowOwnedDialog(dialog)==DialogResult.OK) {core.SaveReflection(date,dialog.Mood,dialog.ReflectionText);SaveQuietly();}
        }
        private void CheckEveningSummary(bool otherNotice)
        {
            DateTime now=core.LocalNow;
            DailyExtraRecord record=core.ExtrasFor(now.Date);
            if(preview || otherNotice || now.Hour<21 || record.SummaryPrompted)return;
            DailyStats stats=core.StatsOn(now.Date);
            if((stats.CompletedPlans==0 && stats.FocusSessions==0) || !String.IsNullOrEmpty(record.Reflection))return;
            DateTime day=now.Date;
            record.SummaryPrompted=true;SaveQuietly();
            Notify("一天的小结","今天完成 "+stats.CompletedPlans+" 项 · 专注 "+stats.FocusMinutes+" 分钟。\n点这里留下一句收获和心情。",false,delegate{ShowReflection(day);});
        }
        private void ShowLearningPack()
        {
            ContextMenuStrip menu=NewMenu();
            foreach(LearningPackDefinition definition in LearningPack.All) {
                LearningPackDefinition pack=definition;ToolStripMenuItem item=new ToolStripMenuItem(pack.Title+" · "+pack.Days+"天");
                ToolStripMenuItem view=new ToolStripMenuItem("查看完整练习与自检");
                view.Click+=delegate{using(ReadNoteDialog dialog=new ReadNoteDialog(pack.Title,pack.Summary,LearningPack.PlanText(pack),colors.Dark))ShowOwnedDialog(dialog);};item.DropDownItems.Add(view);
                bool imported=core.Extras.ImportedLearningPacks.Contains(pack.Id);
                ToolStripMenuItem import=new ToolStripMenuItem(imported?"已加入计划（避免重复添加）":"选择开始日期，加入计划…");import.Enabled=!imported;
                import.Click+=delegate{
                    using(LearningPackDialog dialog=new LearningPackDialog(colors.Dark,pack))if(ShowOwnedDialog(dialog)==DialogResult.OK) {
                        int count=core.ImportLearningPack(pack.Id,dialog.StartDate);SaveQuietly();SetPlannerDate(dialog.StartDate);SelectPage(2);SetPlannerMode(1);
                        Notify(pack.Title+"已加入",count+" 项，"+pack.Days+"天，每天两项；条目的更多菜单可查看练习说明。",false);
                    }
                };item.DropDownItems.Add(import);menu.Items.Add(item);
            }
            ShowPopup(menu);
        }
        private void ShowTaskDetail(PlanItem item)
        {
            using(TextEntryDialog dialog=new TextEntryDialog("任务说明",item.Title+"\n可保存练习步骤、验收条件或随手记正文。",item.DetailNote,colors.Dark,4000))
                if(ShowOwnedDialog(dialog)==DialogResult.OK) {core.SetTaskDetail(item.Id,dialog.ResultText);SaveQuietly();}
        }
    }
    internal sealed class ReadNoteDialog : WidgetDialogBase
    {
        internal ReadNoteDialog(string title,string hint,string text,bool dark,bool proceed=false) : base(title,426,392,dark)
        {
            Hint(hint);
            TextBox content=new TextBox {Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Text=text??"",
                Bounds=new Rectangle(18,54,390,240),BorderStyle=BorderStyle.FixedSingle,BackColor=DialogStyle.Surface(dark),ForeColor=ForeColor,AccessibleName=title};
            Controls.Add(content);Buttons(proceed?"开始专注":"完成",delegate{DialogResult=DialogResult.OK;Close();});FinalizeLayout();
        }
    }
    internal sealed class LotteryDialog : WidgetDialogBase
    {
        private readonly WidgetCore core;
        private readonly Random random=new Random();
        private readonly CheckBox quick;
        private readonly Label title,meta;
        private readonly TextBox detail;
        private PlanItem picked;
        internal string SelectedTaskId {get;private set;}
        internal LotteryDialog(WidgetCore core,bool dark) : base("下一步抽签",400,340,dark)
        {
            this.core=core;Hint(core.Pomodoro.Status==TimerStatus.Running || core.Pomodoro.Status==TimerStatus.Paused?
                "当前专注继续进行，新任务在下一段生效。\n从今天和逾期的未完成计划里抽一项。":
                "从今天和逾期的未完成计划里抽一项。\n选中后关联到番茄钟，按自己的节奏开始。");
            quick=Check("只抽15分钟内的小事（需要填写预计时长）",false,18,54,364);
            title=AddLabel("",18,90,364,36);title.ForeColor=DialogStyle.Accent(dark);
            detail=new TextBox {Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Bounds=new Rectangle(18,148,364,103),
                BackColor=DialogStyle.Surface(dark),ForeColor=ForeColor,BorderStyle=BorderStyle.FixedSingle,AccessibleName="抽中计划说明"};Controls.Add(detail);
            meta=AddLabel("",18,127,364,18);meta.ForeColor=DialogStyle.Muted(dark);
            Button redraw=new Button {Text="再抽一次",Bounds=new Rectangle(18,292,86,32),FlatStyle=FlatStyle.Flat,
                BackColor=DialogStyle.Surface(dark),ForeColor=ForeColor,UseVisualStyleBackColor=false};Controls.Add(redraw);
            redraw.Click+=delegate{Draw();};quick.CheckedChanged+=delegate{Draw();};
            Buttons("选这项",delegate{if(picked==null){ErrorLabel.Text="没有可选项，先添加计划或调整筛选。";return;}SelectedTaskId=picked.Id;DialogResult=DialogResult.OK;Close();});
            Draw();FinalizeLayout();
        }
        private void Draw()
        {
            ErrorLabel.Text="";picked=core.DrawNextTask(quick.Checked,random);
            title.Text=picked==null?"暂时没有符合条件的计划":picked.Title;
            detail.Text=picked==null?"可先添加一项今天的计划；小事筛选会排除未填写预计时长的计划。":String.IsNullOrEmpty(picked.DetailNote)?"先做一个能落笔、能验证的小步骤。":picked.DetailNote;
            int count=core.ActionablePlans(core.LocalNow.Date,quick.Checked).Count;
            meta.Text=picked==null?"0 项候选":(picked.EstimatedMinutes.HasValue?"预计 "+picked.EstimatedMinutes+" 分钟":"时长未估")+" · "+count+" 项候选";
        }
    }
}
