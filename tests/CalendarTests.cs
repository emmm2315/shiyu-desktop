using System;
using System.Globalization;
using System.IO;

namespace DeskSprout
{
    internal static class CalendarTests
    {
        private static int passed;
        private static readonly string TestRoot = Path.Combine(Path.GetTempPath(), "DeskSprout-CalendarTests-" + Guid.NewGuid().ToString("N"));
        private sealed class Clock
        {
            public DateTime Utc = new DateTime(2026, 10, 2, 4, 0, 0, DateTimeKind.Utc);
            public DateTime Read() { return Utc; }
        }
        private static WidgetCore NewCore(string name, Clock clock, TimeZoneInfo zone = null)
        {
            return new WidgetCore(Path.Combine(TestRoot, name, "state.json"), clock.Read, zone ?? TimeZoneInfo.Utc);
        }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Test(string name, Action body) { body(); passed++; Console.WriteLine("PASS " + name); }
        private static PlanItem Find(WidgetCore core, string id) { return core.Items.Find(delegate(PlanItem p) { return p.Id == id; }); }
        private static int CompletedCount(WidgetCore core, Clock clock, DateTime date)
        {
            DateTime previous = clock.Utc;
            try { clock.Utc = DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Utc); return core.TodayStats.CompletedPlans; }
            finally { clock.Utc = previous; }
        }
        public static int Main()
        {
            try
            {
                Test("calendar recurrence crosses month, year and leap day", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("boundaries", clock);
                    PlanItem daily = core.AddItem("Daily", new DateTime(2028, 2, 28), 540, PlanKind.Schedule, RepeatKind.Daily);
                    Check(!core.IsDueOn(daily, new DateTime(2028, 2, 27)), "No occurrence before start");
                    Check(core.IsDueOn(daily, new DateTime(2028, 2, 29)) && core.IsDueOn(daily, new DateTime(2028, 3, 1)), "Daily recurrence includes leap day and next month");
                    PlanItem weekly = core.AddItem("Weekly", new DateTime(2026, 12, 28), null, PlanKind.Plan, RepeatKind.Weekly);
                    Check(!core.IsDueOn(weekly, new DateTime(2026, 12, 21)), "Previous matching weekday is excluded");
                    Check(core.IsDueOn(weekly, new DateTime(2027, 1, 4, 22, 15, 0)) && !core.IsDueOn(weekly, new DateTime(2027, 1, 5)), "Weekly recurrence retains weekday across year change");
                });
                Test("future one-off complete and undo preserve today's count", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("future-completion", clock);
                    DateTime today = clock.Utc.Date, future = new DateTime(2026, 11, 3);
                    PlanItem todayPlan = core.AddItem("Today", today, null, PlanKind.Plan); core.SetCompleted(todayPlan.Id, true, today);
                    PlanItem futurePlan = core.AddItem("Future", future, 615, PlanKind.Schedule);
                    core.SetCompleted(futurePlan.Id, true, future.AddHours(15)); core.SetCompleted(futurePlan.Id, true, future);
                    Check(core.TodayStats.CompletedPlans == 1 && CompletedCount(core, clock, future) == 1, "Future completion is credited once to selected date");
                    string id = futurePlan.Id; core = NewCore("future-completion", clock); futurePlan = Find(core, id);
                    Check(futurePlan.Completed && futurePlan.CompletedDate == future, "Selected completion date survives restart");
                    core.SetCompleted(id, false, today);
                    Check(!futurePlan.Completed && !futurePlan.CompletedDate.HasValue, "Undo clears completion");
                    Check(core.TodayStats.CompletedPlans == 1 && CompletedCount(core, clock, future) == 0, "Undo debits original future date without changing today");
                });
                Test("future recurring undo affects only selected occurrence", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("future-recurrence", clock);
                    DateTime today = clock.Utc.Date, future = today.AddMonths(1);
                    PlanItem item = core.AddItem("Habit", today, null, PlanKind.Plan, RepeatKind.Daily);
                    core.SetCompleted(item.Id, true, today); core.SetCompleted(item.Id, true, future);
                    Check(!core.IsCompletedOn(item, future.AddDays(1)), "Following occurrence remains incomplete");
                    string id = item.Id; core = NewCore("future-recurrence", clock); item = Find(core, id);
                    core.SetCompleted(id, false, future.AddHours(23)); core.SetCompleted(id, false, future);
                    Check(core.IsCompletedOn(item, today) && !core.IsCompletedOn(item, future), "Undo leaves today's occurrence complete");
                    Check(core.TodayStats.CompletedPlans == 1 && CompletedCount(core, clock, future) == 0, "Undo affects only selected day and is idempotent");
                    bool rejected = false;
                    try { core.SetCompleted(id, true, today.AddDays(-1)); } catch (ArgumentException) { rejected = true; }
                    Check(rejected && !core.IsCompletedOn(item, today.AddDays(-1)), "Calendar cannot complete an occurrence before series start");
                });
                Test("future calendar edit persists date, time and reminder choice", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("future-edit", clock);
                    DateTime original = new DateTime(2026, 10, 31), moved = new DateTime(2026, 11, 1);
                    PlanItem item = core.AddItem("Appointment", original, 540, PlanKind.Schedule);
                    core.UpdateItem(item.Id, "明日安排", moved.AddHours(18), 1110, PlanKind.Schedule, RepeatKind.None, false);
                    string id = item.Id; core = NewCore("future-edit", clock); item = Find(core, id);
                    Check(item.Title == "明日安排" && item.Date == moved && item.MinuteOfDay == 1110, "Edited date and minute survive restart");
                    Check(!item.ReminderEnabled && !core.IsDueOn(item, original) && core.IsDueOn(item, moved), "Entry moves calendar day and preserves quiet reminder choice");
                    Check(core.Tick().Reminders.Count == 0, "Future date does not trigger early reminder");
                    core.SetCompleted(id, true, moved); core = NewCore("future-edit", clock); item = Find(core, id);
                    Check(item.CompletedDate == moved && core.TodayStats.CompletedPlans == 0, "Edited future entry completes on selected day");
                });
                Test("legacy JSON upgrades glass defaults and preserves editable future items", delegate
                {
                    Clock clock = new Clock(); string path = Path.Combine(TestRoot, "legacy", "state.json");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    DateTime future = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);
                    long milliseconds = (long)(future - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                    string json = "{\"Version\":1,\"Settings\":{\"FocusMinutes\":25,\"ShortBreakMinutes\":5,\"LongBreakMinutes\":15,\"SessionsBeforeLongBreak\":4,\"SoundEnabled\":true,\"ThemeMode\":\"system\",\"SelectedTab\":2,\"FocusTaskId\":\"legacy-future\"}," +
                        "\"Countdown\":{\"DurationSeconds\":90,\"RemainingSeconds\":45,\"Status\":2},\"Pomodoro\":{\"DurationSeconds\":1500,\"RemainingSeconds\":900,\"Status\":2}," +
                        "\"Items\":[{\"Id\":\"legacy-future\",\"Title\":\"旧版日程\",\"Date\":\"\\/Date(" + milliseconds.ToString(CultureInfo.InvariantCulture) + ")\\/\",\"MinuteOfDay\":600,\"Kind\":1,\"Completed\":false}],\"Statistics\":[]}";
                    File.WriteAllText(path, json); WidgetCore core = NewCore("legacy", clock); PlanItem item = Find(core, "legacy-future");
                    Check(String.IsNullOrEmpty(core.LoadWarning) && item != null, "Legacy JSON loads without recovery");
                    Check(core.Settings.GlassEnabled && core.Settings.GlassStyle == "clear" && core.Settings.GlassTint == 16, "Missing glass fields default to new clear glass");
                    Check(item.ReminderEnabled && item.Repeat == RepeatKind.None && item.CompletedOccurrences.Count == 0, "Missing schedule fields receive compatible defaults");
                    Check(core.Countdown.Status == TimerStatus.Paused && core.Countdown.RemainingSeconds == 45 && core.Pomodoro.RemainingSeconds == 900, "Paused timers survive upgrade");
                    DateTime selected = new DateTime(2026, 12, 7);
                    core.UpdateItem(item.Id, "旧版日程已修改", selected, 840, PlanKind.Schedule); core.SetCompleted(item.Id, true, selected);
                    core = NewCore("legacy", clock); item = Find(core, "legacy-future");
                    Check(item.Date == selected && item.CompletedDate == selected && item.Title == "旧版日程已修改", "Legacy item remains editable and completeable after restart");
                    Check(core.Settings.FocusTaskId == item.Id && core.Countdown.RemainingSeconds == 45 && core.Pomodoro.RemainingSeconds == 900, "Editing retains timers and focus task association");
                    Check(core.TodayStats.CompletedPlans == 0 && CompletedCount(core, clock, selected) == 1, "Future completion leaves today's data intact");
                });
                Test("daily agenda uses local day at UTC month rollover", delegate
                {
                    Clock clock = new Clock(); clock.Utc = new DateTime(2026, 10, 31, 16, 5, 0, DateTimeKind.Utc);
                    TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("Calendar-UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
                    WidgetCore core = NewCore("local-month", clock, zone); DateTime selected = new DateTime(2026, 11, 1);
                    Check(core.LocalNow.Date == selected, "Local day has reached next month");
                    PlanItem item = core.AddItem("凌晨日程", selected.AddHours(20), 5, PlanKind.Schedule);
                    Check(item.Date == selected && core.IsDueOn(item, core.LocalNow.Date), "Date picker time normalizes to selected local day");
                    TickResult result = core.Tick();
                    Check(result.Reminders.Count == 1 && result.Reminders[0].Id == item.Id, "Midnight reminder uses local date");
                    core.SetCompleted(item.Id, true, selected); core = NewCore("local-month", clock, zone);
                    Check(core.TodayStats.CompletedPlans == 1 && core.Tick().Reminders.Count == 0, "Restart preserves local-date completion and reminder dedupe");
                });
                Console.WriteLine("All " + passed + " calendar tests passed."); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine("FAILED: " + error); return 1; }
            finally
            {
                string resolvedRoot = Path.GetFullPath(TestRoot);
                string allowedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (resolvedRoot.StartsWith(allowedParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolvedRoot)) Directory.Delete(resolvedRoot, true);
            }
        }
    }
}