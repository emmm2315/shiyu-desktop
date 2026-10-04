using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace DeskSprout
{
    // Test-only partial helpers; this file is compiled only by /main:DeskSprout.UiFlowCheck.
    // It never shows a form, captures a desktop, or opens the real application's state file.
    internal sealed partial class MainForm
    {
        private void FlowPaint()
        {
            using (Bitmap bitmap = new Bitmap(680, 920))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(colors.Background);
                graphics.ScaleTransform(2, 2);
                PaintScene(graphics);
            }
            UiFlowCheck.Assert(!Visible, "Test form must remain hidden.");
        }

        private Hit FlowFindHit(string hint)
        {
            Hit hit = hits.FirstOrDefault(value => value.Hint == hint);
            UiFlowCheck.Assert(hit != null, "Missing actual hit area: " + hint);
            return hit;
        }

        private void FlowClick(Hit hit)
        {
            int x = (int)Math.Round((hit.Bounds.X + hit.Bounds.Width / 2) * scale);
            int y = (int)Math.Round((hit.Bounds.Y + hit.Bounds.Height / 2) * scale);
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
            UiFlowCheck.Assert(!Visible, "A logical button flow must not reveal the form.");
        }

        private void FlowAssertNoOverlaps(string scene)
        {
            FlowPaint();
            UiFlowCheck.Assert(hits.Count > 10, "Scene has too few hit areas: " + scene);
            foreach (Hit hit in hits)
            {
                UiFlowCheck.Assert(hit.Bounds.Width > 0 && hit.Bounds.Height > 0, "Empty hit rectangle in " + scene);
                UiFlowCheck.Assert(hit.Bounds.Left >= 0 && hit.Bounds.Top >= 0 &&
                    hit.Bounds.Right <= 340 && hit.Bounds.Bottom <= 460,
                    "Hit rectangle extends beyond component: " + scene + " / " + hit.Hint);
            }
            for (int i = 0; i < hits.Count; i++)
                for (int j = i + 1; j < hits.Count; j++)
                {
                    RectangleF intersection = RectangleF.Intersect(hits[i].Bounds, hits[j].Bounds);
                    UiFlowCheck.Assert(intersection.Width <= 0 || intersection.Height <= 0,
                        "Overlapping hit areas in " + scene + ": " + hits[i].Hint + " / " + hits[j].Hint +
                        " intersection=" + intersection);
                }
            UiFlowCheck.Assert(hits.Any(value => value.Hint == "挑选今天最重要的三件事"), "Header priority control missing.");
            UiFlowCheck.Assert(hits.Any(value => value.Hint.StartsWith("小趣味：", StringComparison.Ordinal)),
                "Footer extras control missing.");
        }

        internal static void FlowFutureRecurring(string statePath)
        {
            using (MainForm form = new MainForm(true, statePath))
            {
                DateTime today = DateTime.Today;
                PlanItem daily = form.core.AddItem("Future recurring check", today, null, PlanKind.Plan, RepeatKind.Daily);
                DateTime selected = today.AddDays(11);
                form.page = 2;
                form.SetPlannerDate(selected.AddDays(-1));
                form.SetPlannerMode(0);
                form.FlowPaint();
                string cellHint = selected.ToString("yyyy年M月d日") + " · 1 项";
                form.FlowClick(form.FlowFindHit(cellHint));
                UiFlowCheck.Assert(form.SelectedScheduleDate == selected, "Month calendar must select the clicked future date.");
                form.FlowPaint();
                form.FlowClick(form.FlowFindHit("查看这一天的完整日程"));
                UiFlowCheck.Assert(form.plannerMode == 1 && form.SelectedScheduleDate == selected,
                    "Switching month to daily must preserve the selected date.");
                form.FlowPaint();
                form.FlowClick(form.FlowFindHit("完成这一天的安排"));
                UiFlowCheck.Assert(form.core.IsCompletedOn(daily, selected), "Checkbox must complete selected future occurrence.");
                UiFlowCheck.Assert(!form.core.IsCompletedOn(daily, today), "Today's occurrence must stay incomplete.");
                UiFlowCheck.Assert(!form.core.IsCompletedOn(daily, selected.AddDays(1)), "Another future occurrence must stay incomplete.");
                UiFlowCheck.Assert(form.core.TodayStats.CompletedPlans == 0 && form.core.StatsOn(selected).CompletedPlans == 1,
                    "Selected-day completion statistics must remain separate from today.");
                form.FlowPaint();
                form.FlowClick(form.FlowFindHit("后一天"));
                UiFlowCheck.Assert(form.SelectedScheduleDate == selected.AddDays(1), "Daily next-day control must change selected date.");
                form.FlowPaint();
                form.FlowClick(form.FlowFindHit("完成这一天的安排"));
                UiFlowCheck.Assert(form.core.IsCompletedOn(daily, selected) &&
                    form.core.IsCompletedOn(daily, selected.AddDays(1)) && !form.core.IsCompletedOn(daily, today),
                    "Daily checkbox after navigation must only change that selected occurrence.");
            }
        }

        internal static void FlowOverdueCompletion(string statePath)
        {
            using (MainForm form = new MainForm(true, statePath))
            {
                DateTime today = DateTime.Today;
                PlanItem overdue = form.core.AddItem("Overdue one-off", today.AddDays(-2), null, PlanKind.Plan);
                int before = form.core.TodayStats.CompletedPlans;
                form.page = 2; form.filter = 0; form.SetPlannerMode(2);
                form.FlowPaint();
                form.FlowClick(form.FlowFindHit("完成这一天的安排"));
                UiFlowCheck.Assert(overdue.Completed, "Overdue list checkbox must complete the item.");
                UiFlowCheck.Assert(overdue.CompletedDate.HasValue && overdue.CompletedDate.Value.Date == today,
                    "Today's overdue completion must be attributed to today.");
                UiFlowCheck.Assert(form.core.TodayStats.CompletedPlans == before + 1,
                    "Overdue list completion must increment today's total.");
                UiFlowCheck.Assert(form.core.StatsOn(overdue.Date).CompletedPlans == 0,
                    "Original planned date must not receive today's list completion.");
            }
        }

        internal static void FlowHitAreas(string statePath)
        {
            using (MainForm form = new MainForm(true, statePath))
            {
                DateTime today = DateTime.Today;
                for (int i = 0; i < 6; i++)
                    form.core.AddItem("Layout sample " + (i + 1), today, i * 90 + 540, i % 2 == 0 ? PlanKind.Plan : PlanKind.Schedule);
                form.core.AddItem("Overdue layout sample", today.AddDays(-1), null, PlanKind.Plan);
                form.core.AddItem("Future layout sample", today.AddDays(3), null, PlanKind.Plan);
                form.core.SetPriorities(today, form.core.Items.Where(value => value.Kind == PlanKind.Plan && value.Date == today).Take(3).Select(value => value.Id));
                form.core.Settings.FocusTaskId = form.core.Items[0].Id;
                foreach (string theme in new[] { "light", "dark" })
                {
                    form.core.Settings.ThemeMode = theme; form.ApplyTheme();
                    form.page = 0; form.FlowAssertNoOverlaps(theme + " countdown");
                    form.page = 1; form.FlowAssertNoOverlaps(theme + " pomodoro");
                    form.page = 2; form.SetPlannerDate(today);
                    form.SetPlannerMode(0); form.FlowAssertNoOverlaps(theme + " month calendar");
                    form.SetPlannerMode(1); form.FlowAssertNoOverlaps(theme + " daily agenda");
                    form.SetPlannerMode(2);
                    for (int filter = 0; filter < 2; filter++)
                    {
                        form.filter = filter; form.FlowAssertNoOverlaps(theme + " list filter " + filter);
                        form.ScrollPlanner(-1); form.FlowAssertNoOverlaps(theme + " scrolled list filter " + filter);
                    }
                }
            }
        }

        internal static void FlowLotteryQuick(string statePath)
        {
            using (MainForm form = new MainForm(true, statePath))
            {
                DateTime today = DateTime.Today;
                PlanItem quick = form.core.AddItem("Only quick eligible", today.AddDays(-1), null, PlanKind.Plan);
                form.core.SetEstimatedMinutes(quick.Id, 15); form.core.SetTaskDetail(quick.Id, "Quick eligible body");
                PlanItem longTask = form.core.AddItem("Long task", today, null, PlanKind.Plan); form.core.SetEstimatedMinutes(longTask.Id, 16);
                form.core.AddItem("Unknown estimate", today, null, PlanKind.Plan);
                PlanItem future = form.core.AddItem("Future quick", today.AddDays(1), null, PlanKind.Plan); form.core.SetEstimatedMinutes(future.Id, 5);
                PlanItem schedule = form.core.AddItem("Quick schedule", today, null, PlanKind.Schedule); form.core.SetEstimatedMinutes(schedule.Id, 5);
                PlanItem completed = form.core.AddItem("Completed quick", today, null, PlanKind.Plan);
                form.core.SetEstimatedMinutes(completed.Id, 5); form.core.SetCompleted(completed.Id, true);
                using (LotteryDialog dialog = new LotteryDialog(form.core, false))
                {
                    CheckBox quickControl = dialog.Controls.OfType<CheckBox>().Single();
                    quickControl.Checked = true;
                    UiFlowCheck.Assert(dialog.Controls.OfType<Label>().Any(value => value.Text == quick.Title),
                        "Lottery quick filter must render the eligible task title.");
                    UiFlowCheck.Assert(dialog.Controls.OfType<TextBox>().Any(value => value.Text == quick.DetailNote),
                        "Lottery must render the selected task's real description.");
                    UiFlowCheck.Assert(dialog.Controls.OfType<Label>().Any(value => value.Text.Contains("1 项候选")),
                        "Quick filter must exclude unknown, long, future, schedule and completed tasks.");
                    UiFlowCheck.InvokeAccept(dialog);
                    UiFlowCheck.Assert(dialog.SelectedTaskId == quick.Id, "Lottery accept must return the eligible task ID.");
                    UiFlowCheck.Assert(!dialog.Visible && !form.Visible, "Lottery flow must remain hidden.");
                }
            }
        }
        internal static void FlowLessonOrder(string statePath)
        {
            using(MainForm form=new MainForm(true,statePath)) {
                DateTime start=DateTime.Today;form.core.Import8086Registers(start);
                for(int day=0;day<7;day++) {
                    var items=form.ItemsOnDate(start.AddDays(day));
                    UiFlowCheck.Assert(items.Count==2 && items[0].EstimatedMinutes==10 && items[1].EstimatedMinutes==20,
                        "Untimed lessons must preserve concept then practice order on every day.");
                }
                UiFlowCheck.Assert(!form.Visible,"Lesson order check must remain hidden.");
            }
        }
    }

    internal static class UiFlowCheck
    {
        private static int passed, failed;
        internal static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        internal static void InvokeAccept(Form dialog)
        {
            Control accept = dialog.AcceptButton as Control;
            Assert(accept != null, "Dialog must have an accept control.");
            MethodInfo click = accept.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(click != null, "Dialog accept Click handler must exist.");
            click.Invoke(accept, new object[] { EventArgs.Empty });
        }

        private static void Case(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception error)
            {
                failed++; Console.Error.WriteLine("FAIL " + name + Environment.NewLine + error);
            }
        }

        private static void EntryEstimate()
        {
            foreach (int? estimate in new int?[] { 35, null })
                using (EntryDialog dialog = new EntryDialog("Refilled plan", DateTime.Today, false, "plan", false,
                    "none", true, estimate))
                {
                    NumericUpDown field = dialog.Controls.OfType<NumericUpDown>()
                        .Single(value => value.AccessibleName != null && value.AccessibleName.StartsWith("预计时长", StringComparison.Ordinal));
                    Assert(field.Value == (estimate ?? 0), "Entry dialog must refill the stored estimate, using zero for unknown.");
                    InvokeAccept(dialog);
                    Assert(dialog.EstimatedMinutes == estimate, "Entry save must preserve the refilled estimate.");
                    Assert(dialog.EntryTitle == "Refilled plan" && dialog.DialogResult == DialogResult.OK,
                        "Entry dialog must run the actual save handler.");
                    Assert(!dialog.Visible, "Entry dialog flow must remain hidden.");
                }
        }

        [STAThread]
        public static int Main()
        {
            string root = Path.Combine(Path.GetTempPath(), "DeskSprout-UiFlow-" + Guid.NewGuid().ToString("N"));
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Directory.CreateDirectory(root);
                Case("month and daily navigation complete only selected future recurring occurrence",
                    delegate { MainForm.FlowFutureRecurring(Path.Combine(root, "future.json")); });
                Case("today list completes overdue one-off with today's statistics",
                    delegate { MainForm.FlowOverdueCompletion(Path.Combine(root, "overdue.json")); });
                Case("header footer calendar daily and list hit areas do not overlap",
                    delegate { MainForm.FlowHitAreas(Path.Combine(root, "hitareas.json")); });
                Case("lottery checkbox selects only explicitly estimated quick actionable plans",
                    delegate { MainForm.FlowLotteryQuick(Path.Combine(root, "lottery.json")); });
                Case("entry dialog refills and saves known and unknown estimated minutes", EntryEstimate);
                Case("untimed daily lessons preserve concept before practice order",delegate{MainForm.FlowLessonOrder(Path.Combine(root,"lesson-order.json"));});
                Console.WriteLine("UI flow checks: " + passed + " passed, " + failed + " failed.");
                return failed == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally
            {
                string resolved = Path.GetFullPath(root);
                string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (resolved.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(resolved).StartsWith("DeskSprout-UiFlow-", StringComparison.Ordinal) && Directory.Exists(resolved))
                    Directory.Delete(resolved, true);
            }
        }
    }
}
