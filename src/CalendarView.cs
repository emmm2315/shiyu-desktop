using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace DeskSprout
{
    internal sealed partial class MainForm
    {
        private int plannerMode;
        private DateTime selectedDate=DateTime.Today;
        private DateTime calendarMonth=new DateTime(DateTime.Today.Year,DateTime.Today.Month,1);
        private DateTime SelectedScheduleDate { get { return plannerMode==2?DateTime.Today:selectedDate.Date; } }

        private DateTime ScheduleOccurrenceDate(PlanItem item)
        {
            if(page==2 && plannerMode==2 && item.Repeat==RepeatKind.None)return core.LocalNow.Date;
            return page==2 && plannerMode!=2 ? SelectedScheduleDate : OccurrenceDate(item);
        }
        private void SetPlannerDate(DateTime date)
        {
            selectedDate=date.Date;
            calendarMonth=new DateTime(date.Year,date.Month,1);
            scroll=0;keyboard=hover=-1;Invalidate();
        }
        private void SetSelectedDate(DateTime date) { SetPlannerDate(date); }
        private void AdvancePlannerToday(DateTime previousDay)
        {
            if(selectedDate.Date==previousDay.Date)SetPlannerDate(DateTime.Today);
        }
        private void SetPlannerMode(int mode)
        {
            plannerMode=mode;scroll=0;keyboard=hover=-1;Invalidate();
        }
        private void MovePlannerDay(int delta)
        {
            long day=selectedDate.Ticks/TimeSpan.TicksPerDay+delta;
            if(day>=0 && day<=DateTime.MaxValue.Ticks/TimeSpan.TicksPerDay)
                SetPlannerDate(new DateTime(day*TimeSpan.TicksPerDay));
        }
        private void MovePlannerMonth(int delta)
        {
            int month=(calendarMonth.Year-1)*12+calendarMonth.Month-1+delta;
            if(month<0 || month>=9999*12)return;
            int year=month/12+1, number=month%12+1;
            SetPlannerDate(new DateTime(year,number,Math.Min(selectedDate.Day,DateTime.DaysInMonth(year,number))));
        }
        internal static DateTime CalendarGridStart(DateTime month)
        {
            DateTime first=new DateTime(month.Year,month.Month,1);
            return first.AddDays(-(((int)first.DayOfWeek+6)%7));
        }
        private List<PlanItem> ItemsOnDate(DateTime date)
        {
            return core.Items.Where(i=>core.IsDueOn(i,date)).OrderBy(i=>i.MinuteOfDay.HasValue?0:1)
                .ThenBy(i=>i.MinuteOfDay??1440).ToList();
        }
        private void ScrollPlanner(int delta)
        {
            if(plannerMode==0 || delta==0)return;
            int count=plannerMode==1?ItemsOnDate(selectedDate).Count:VisibleItems().Count;
            scroll=Math.Max(0,Math.Min(scroll+(delta<0?1:-1),Math.Max(0,count-4)));
            keyboard=hover=-1;Invalidate();
        }
        private void PlannerArrow(Graphics g,RectangleF bounds,bool forward,Action action,string hint)
        {
            if(hover==hits.Count)Art.Fill(g,bounds,8,colors.AccentSoft);
            float x=bounds.X+bounds.Width/2, y=bounds.Y+bounds.Height/2;
            using(Pen pen=new Pen(colors.Muted,1.4f)) {
                float direction=forward?1:-1;
                g.DrawLines(pen,new[]{new PointF(x-direction*2,y-4),new PointF(x+direction*2,y),new PointF(x-direction*2,y+4)});
            }
            HitArea(bounds,hint,action);
        }
        private void PlannerTodayButton(Graphics g,float y)
        {
            RectangleF bounds=new RectangleF(260,y,56,26);
            if(hover==hits.Count)Art.Fill(g,bounds,8,colors.AccentSoft);
            Art.Text(g,"今天",bounds.X,bounds.Y,bounds.Width,bounds.Height,11,colors.Accent,FontStyle.Regular,StringAlignment.Center);
            HitArea(bounds,"回到今天",delegate{SetPlannerDate(DateTime.Today);});
        }
        private void PaintPlanner(Graphics g)
        {
            string[] labels={"月历","每日","清单"};
            for(int i=0;i<labels.Length;i++) {
                int mode=i;RectangleF bounds=new RectangleF(24+i*72,117,66,26);
                if(plannerMode==i || hover==hits.Count)Art.Fill(g,bounds,8,colors.AccentSoft);
                Art.Text(g,labels[i],bounds.X,bounds.Y,bounds.Width,bounds.Height,11,plannerMode==i?colors.Accent:colors.Muted,
                    plannerMode==i?FontStyle.Bold:FontStyle.Regular,StringAlignment.Center);
                HitArea(bounds,"查看"+labels[i],delegate{SetPlannerMode(mode);});
            }
            string addHint=plannerMode==2?"添加日程或计划":"为 "+selectedDate.ToString("M月d日")+" 添加日程或计划";
            IconButton(g,"plus",new RectangleF(282,115,34,29),addHint,delegate{EditEntry(null);},false);
            if(plannerMode==0)PaintMonthCalendar(g);
            else if(plannerMode==1)PaintDailyAgenda(g);
            else PaintPlannerList(g);
        }
        private void PaintMonthCalendar(Graphics g)
        {
            Art.Text(g,calendarMonth.ToString("yyyy年M月"),24,150,166,26,14,colors.Text,FontStyle.Bold,StringAlignment.Near);
            PlannerArrow(g,new RectangleF(196,150,26,26),false,delegate{MovePlannerMonth(-1);},"上个月");
            PlannerArrow(g,new RectangleF(226,150,26,26),true,delegate{MovePlannerMonth(1);},"下个月");
            PlannerTodayButton(g,150);
            string[] weekdays={"一","二","三","四","五","六","日"};
            for(int column=0;column<7;column++)
                Art.Text(g,weekdays[column],24+column*42,180,40,20,10,colors.Muted,FontStyle.Regular,StringAlignment.Center);
            DateTime start=CalendarGridStart(calendarMonth);
            for(int cell=0;cell<42;cell++) {
                long ticks=start.Ticks+(long)cell*TimeSpan.TicksPerDay;
                if(ticks>DateTime.MaxValue.Ticks)continue;
                DateTime date=new DateTime(ticks);
                int column=cell%7, row=cell/7;
                RectangleF bounds=new RectangleF(24+column*42,203+row*26,40,26);
                RectangleF badge=new RectangleF(bounds.X+5,bounds.Y+1,30,24);
                bool selected=date==selectedDate, today=date==DateTime.Today, current=date.Month==calendarMonth.Month;
                if(selected || hover==hits.Count)Art.Fill(g,badge,8,colors.AccentSoft);
                if(today)Art.Outline(g,badge,8,colors.Accent,1);
                Color number=selected?colors.Accent:current?colors.Text:Color.FromArgb(110,colors.Muted);
                Art.Text(g,date.Day.ToString(CultureInfo.InvariantCulture),bounds.X,bounds.Y-1,bounds.Width,21,11,number,
                    selected||today?FontStyle.Bold:FontStyle.Regular,StringAlignment.Center);
                List<PlanItem> items=ItemsOnDate(date);
                if(items.Count>0) {
                    int dots=Math.Min(3,items.Count);
                    bool allDone=items.All(i=>core.IsCompletedOn(i,date));
                    using(Brush brush=new SolidBrush(allDone?colors.Muted:colors.Accent)) {
                        float x=bounds.X+bounds.Width/2-(dots*5-2)/2f;
                        for(int dot=0;dot<dots;dot++)g.FillEllipse(brush,x+dot*5,bounds.Y+20,2.5f,2.5f);
                    }
                }
                HitArea(bounds,date.ToString("yyyy年M月d日")+" · "+items.Count+" 项",delegate{SetPlannerDate(date);});
            }
            using(Pen pen=new Pen(colors.Line))g.DrawLine(pen,24,361,316,361);
            List<PlanItem> selectedItems=ItemsOnDate(selectedDate);
            string label=selectedDate.ToString("M月d日 ddd",CultureInfo.GetCultureInfo("zh-CN"));
            Art.Text(g,label,24,366,182,20,11,colors.Text,FontStyle.Bold,StringAlignment.Near);
            RectangleF open=new RectangleF(230,365,86,24);
            if(hover==hits.Count)Art.Fill(g,open,8,colors.AccentSoft);
            Art.Text(g,"查看当日 ›",open.X,open.Y,open.Width,open.Height,10,colors.Accent,FontStyle.Regular,StringAlignment.Far);
            HitArea(open,"查看这一天的完整日程",delegate{SetPlannerMode(1);});
            if(selectedItems.Count==0)
                Art.Text(g,"这一天还没有安排，点 ＋ 添加",24,397,292,23,10,colors.Muted,FontStyle.Regular,StringAlignment.Near);
            for(int i=0;i<Math.Min(2,selectedItems.Count);i++) {
                PlanItem item=selectedItems[i];DateTime occurrence=selectedDate;float y=389+i*23;
                PaintPlannerCheck(g,item,occurrence,new RectangleF(21,y,24,23),true);
                string time=item.MinuteOfDay.HasValue?TimeSpan.FromMinutes(item.MinuteOfDay.Value).ToString(@"hh\:mm"):"待办";
                bool done=core.IsCompletedOn(item,occurrence);
                Art.Text(g,time,49,y,39,23,9,colors.Muted,FontStyle.Regular,StringAlignment.Near);
                Art.Text(g,item.Title,94,y,222,23,10,done?colors.Muted:colors.Text,done?FontStyle.Strikeout:FontStyle.Regular,StringAlignment.Near);
                HitArea(new RectangleF(46,y,270,23),"编辑 “"+item.Title+"”",delegate{EditEntry(item);});
            }
        }
        private void PaintDailyAgenda(Graphics g)
        {
            string format=selectedDate.Year==DateTime.Today.Year?"M月d日 ddd":"yyyy.M.d ddd";
            Art.Text(g,selectedDate.ToString(format,CultureInfo.GetCultureInfo("zh-CN")),24,150,169,27,13,colors.Text,FontStyle.Bold,StringAlignment.Near);
            PlannerArrow(g,new RectangleF(196,150,26,26),false,delegate{MovePlannerDay(-1);},"前一天");
            PlannerArrow(g,new RectangleF(226,150,26,26),true,delegate{MovePlannerDay(1);},"后一天");
            PlannerTodayButton(g,150);
            List<PlanItem> items=ItemsOnDate(selectedDate);
            scroll=Math.Max(0,Math.Min(scroll,Math.Max(0,items.Count-4)));
            int completed=items.Count(i=>core.IsCompletedOn(i,selectedDate));
            Art.Text(g,"完成 "+completed+" / "+items.Count+"   ·   按时间排列",24,182,292,19,10,colors.Muted,FontStyle.Regular,StringAlignment.Near);
            if(items.Count==0) { PaintPlannerEmpty(g,"给这一天留一点空间","点右上角 ＋，安排日程或待办");return; }
            int visible=Math.Min(4,items.Count-scroll);
            using(Pen pen=new Pen(colors.Line,1))g.DrawLine(pen,70,225,70,205+(visible-1)*53+32);
            for(int row=0;row<visible;row++) {
                PlanItem item=items[scroll+row];DateTime occurrence=selectedDate;float y=205+row*53;
                string time=item.MinuteOfDay.HasValue?TimeSpan.FromMinutes(item.MinuteOfDay.Value).ToString(@"hh\:mm"):"待办";
                Art.Text(g,time,24,y+6,38,21,10,colors.Muted,FontStyle.Regular,StringAlignment.Near);
                using(Brush brush=new SolidBrush(colors.Accent))g.FillEllipse(brush,67,y+18,6,6);
                RectangleF panel=new RectangleF(81,y,235,47);Art.Fill(g,panel,10,colors.Surface);
                PaintPlannerCheck(g,item,occurrence,new RectangleF(82,y+8,29,30),false);
                bool done=core.IsCompletedOn(item,occurrence);
                Art.Text(g,item.Title,114,y+5,167,21,11,done?colors.Muted:colors.Text,done?FontStyle.Strikeout:FontStyle.Regular,StringAlignment.Near);
                string meta=(item.Kind==PlanKind.Schedule?"日程":"计划")+(item.EstimatedMinutes.HasValue?" · "+item.EstimatedMinutes.Value+"分钟":"")+RepeatLabel(item);
                if(item.MinuteOfDay.HasValue && item.ReminderEnabled)meta+="  ·  提醒";
                Art.Text(g,meta,114,y+27,167,14,9,colors.Muted,FontStyle.Regular,StringAlignment.Near);
                HitArea(new RectangleF(112,y+2,168,43),"编辑 “"+item.Title+"”",delegate{EditEntry(item);});
                IconButton(g,"dots",new RectangleF(282,y+10,29,28),"更多操作",delegate{ShowItemMenu(item);},false);
            }
            PaintPlannerScroll(g,items.Count,205,206);
            Art.Text(g,items.Count>4?"滚动查看其他安排":"按照自己的节奏，慢慢来",24,419,228,18,9,colors.Muted,FontStyle.Regular,StringAlignment.Near);
            if(items.Count>4)Art.Text(g,(scroll+1)+"–"+Math.Min(scroll+4,items.Count)+" / "+items.Count,251,419,65,18,9,colors.Muted,FontStyle.Regular,StringAlignment.Far);
        }
        private void PaintPlannerCheck(Graphics g,PlanItem item,DateTime occurrence,RectangleF bounds,bool small)
        {
            bool done=core.IsCompletedOn(item,occurrence);float size=small?13:17;
            float x=bounds.X+(bounds.Width-size)/2,y=bounds.Y+(bounds.Height-size)/2;
            using(Pen pen=new Pen(done?colors.Accent:Color.FromArgb(145,colors.Muted),1.2f))g.DrawEllipse(pen,x,y,size,size);
            if(done)Art.Icon(g,"check",new RectangleF(x,y,size,size),colors.Accent);
            HitArea(bounds,done?"恢复这一天的未完成":"完成这一天的安排",delegate{
                DateTime completedOn=plannerMode==2 && item.Repeat==RepeatKind.None?core.LocalNow.Date:occurrence;
                core.SetCompleted(item.Id,!core.IsCompletedOn(item,occurrence),completedOn);SaveQuietly();CelebratePrioritiesIfReady();Invalidate();
            });
        }
        private void PaintPlannerList(Graphics g)
        {
            string[] labels={"今天","全部","完成"};
            for(int i=0;i<3;i++) {
                int value=i;RectangleF bounds=new RectangleF(24+i*62,152,56,24);
                if(filter==i || hover==hits.Count)Art.Fill(g,bounds,8,colors.AccentSoft);
                Art.Text(g,labels[i],bounds.X,bounds.Y,bounds.Width,bounds.Height,10,filter==i?colors.Accent:colors.Muted,FontStyle.Regular,StringAlignment.Center);
                HitArea(bounds,labels[i]+"的计划",delegate{filter=value;scroll=0;keyboard=hover=-1;Invalidate();});
            }
            List<PlanItem> items=VisibleItems();
            scroll=Math.Max(0,Math.Min(scroll,Math.Max(0,items.Count-4)));
            if(items.Count==0) { PaintPlannerEmpty(g,filter==2?"完成的事，会在这里留下痕迹":"今天想做些什么？","点右上角 ＋，写下第一项计划");return; }
            for(int row=0;row<Math.Min(4,items.Count-scroll);row++) {
                PlanItem item=items[scroll+row];DateTime occurrence=OccurrenceDate(item);float y=186+row*55;
                Art.Fill(g,new RectangleF(24,y,292,49),10,colors.Surface);
                PaintPlannerCheck(g,item,occurrence,new RectangleF(29,y+9,30,30),false);
                bool done=core.IsCompletedOn(item,occurrence);
                Art.Text(g,item.Title,64,y+6,213,21,12,done?colors.Muted:colors.Text,done?FontStyle.Strikeout:FontStyle.Regular,StringAlignment.Near);
                Art.Text(g,ItemWhen(item)+(item.Kind==PlanKind.Schedule?"  ·  日程":"  ·  计划")+RepeatLabel(item),64,y+28,213,14,9,colors.Muted,FontStyle.Regular,StringAlignment.Near);
                HitArea(new RectangleF(61,y+3,217,43),"编辑 “"+item.Title+"”",delegate{EditEntry(item);});
                IconButton(g,"dots",new RectangleF(282,y+10,29,28),"更多操作",delegate{ShowItemMenu(item);},false);
            }
            PaintPlannerScroll(g,items.Count,186,214);
            int completed=core.Items.Count(i=>core.IsDueOn(i,DateTime.Today)&&core.IsCompletedOn(i,DateTime.Today));
            int today=core.Items.Count(i=>core.IsDueOn(i,DateTime.Today));
            Art.Text(g,"今日计划  "+completed+" / "+today+" 完成",24,419,230,18,10,colors.Muted,FontStyle.Regular,StringAlignment.Near);
            if(items.Count>4)Art.Text(g,(scroll+1)+"–"+Math.Min(scroll+4,items.Count)+" / "+items.Count,252,419,64,18,9,colors.Muted,FontStyle.Regular,StringAlignment.Far);
        }
        private void PaintPlannerScroll(Graphics g,int count,float top,float height)
        {
            if(count<=4)return;
            float thumb=height*4/count;
            Art.Fill(g,new RectangleF(324,top+(float)scroll/(count-4)*(height-thumb),3,thumb),1.5f,colors.Line);
        }
        private void PaintPlannerEmpty(Graphics g,string title,string detail)
        {
            Art.Fill(g,new RectangleF(138,243,64,64),20,colors.Surface);
            Art.Icon(g,"leaf",new RectangleF(154,258,34,34),colors.Accent);
            Art.Text(g,title,26,326,288,23,12,colors.Text,FontStyle.Regular,StringAlignment.Center);
            Art.Text(g,detail,26,354,288,21,10,colors.Muted,FontStyle.Regular,StringAlignment.Center);
        }
    }
}
