using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DeskSprout
{
    internal static class ExtrasTests
    {
        private static int passed;
        private static readonly string TestRoot = Path.Combine(Path.GetTempPath(), "DeskSprout-ExtrasTests-" + Guid.NewGuid().ToString("N"));
        private sealed class Clock
        {
            public DateTime Utc = new DateTime(2026, 10, 2, 4, 0, 0, DateTimeKind.Utc);
            public DateTime Read() { return Utc; }
        }
        private static WidgetCore NewCore(string name, Clock clock)
        {
            return new WidgetCore(Path.Combine(TestRoot, name, "state.json"), clock.Read, TimeZoneInfo.Utc);
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Test(string name, Action body) { body(); passed++; Console.WriteLine("PASS " + name); }
        private static void Expect<T>(Action body) where T : Exception
        {
            try { body(); } catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
        private static bool Has(List<PlanItem> items, string id) { return items.Exists(delegate(PlanItem p) { return p.Id == id; }); }

        public static int Main()
        {
            try
            {
                Test("actionable plans include overdue one-offs but exclude inactive and completed entries", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("candidates", clock); DateTime day = clock.Utc.Date;
                    PlanItem today = core.AddItem("Today", day, null, PlanKind.Plan);
                    PlanItem late = core.AddItem("Overdue", day.AddDays(-10), null, PlanKind.Plan);
                    PlanItem tomorrow = core.AddItem("Tomorrow", day.AddDays(1), null, PlanKind.Plan);
                    PlanItem schedule = core.AddItem("Meeting", day, 540, PlanKind.Schedule);
                    PlanItem weekly = core.AddItem("Other weekday", day.AddDays(-1), null, PlanKind.Plan, RepeatKind.Weekly);
                    PlanItem done = core.AddItem("Finished", day, null, PlanKind.Plan); core.SetCompleted(done.Id, true, day);
                    PlanItem daily = core.AddItem("Daily", day.AddDays(-1), null, PlanKind.Plan, RepeatKind.Daily);
                    core.SetCompleted(daily.Id, true, day.AddDays(-1));
                    List<PlanItem> candidates = core.ActionablePlans(day.AddHours(23));
                    Check(candidates.Count == 3 && Has(candidates, today.Id) && Has(candidates, late.Id) && Has(candidates, daily.Id), "Only currently actionable plans enter picker");
                    Check(!Has(candidates, tomorrow.Id) && !Has(candidates, schedule.Id) && !Has(candidates, weekly.Id) && !Has(candidates, done.Id), "Future, schedule, inactive recurrence and completed plans are excluded");
                });
                Test("quick plans require an explicit estimate within fifteen minutes", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("quick", clock); DateTime day = clock.Utc.Date;
                    PlanItem one = core.AddItem("One", day, null, PlanKind.Plan); core.SetEstimatedMinutes(one.Id, 1);
                    PlanItem fifteen = core.AddItem("Fifteen", day.AddDays(-1), null, PlanKind.Plan); core.SetEstimatedMinutes(fifteen.Id, 15);
                    PlanItem sixteen = core.AddItem("Sixteen", day, null, PlanKind.Plan); core.SetEstimatedMinutes(sixteen.Id, 16);
                    PlanItem unknown = core.AddItem("Unknown", day, null, PlanKind.Plan);
                    List<PlanItem> quick = core.ActionablePlans(day, true);
                    Check(quick.Count == 2 && Has(quick, one.Id) && Has(quick, fifteen.Id), "Inclusive quick-task estimate boundaries");
                    Check(!Has(quick, sixteen.Id) && !Has(quick, unknown.Id), "Long and unknown estimates do not enter quick mode");
                    Expect<ArgumentOutOfRangeException>(delegate { core.SetEstimatedMinutes(one.Id, 0); });
                    Expect<ArgumentOutOfRangeException>(delegate { core.SetEstimatedMinutes(one.Id, 481); });
                    Check(one.EstimatedMinutes == 1, "Invalid estimate does not mutate task");
                    core.SetEstimatedMinutes(one.Id, 480); core.SetEstimatedMinutes(one.Id, null);
                    Check(!one.EstimatedMinutes.HasValue, "Upper bound and clearing estimate are supported");
                });
                Test("random draw respects normal and quick candidate sets and handles empty lists", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("draw", clock);
                    Check(core.DrawNextTask(false, new Random(42)) == null && core.DrawNextTask(true, new Random(42)) == null, "Empty picker safely returns null");
                    PlanItem quick = core.AddItem("Quick", clock.Utc.Date, null, PlanKind.Plan); core.SetEstimatedMinutes(quick.Id, 5);
                    PlanItem longer = core.AddItem("Long", clock.Utc.Date, null, PlanKind.Plan); core.SetEstimatedMinutes(longer.Id, 45);
                    Random random = new Random(42); HashSet<string> seen = new HashSet<string>();
                    for (int i = 0; i < 60; i++)
                    {
                        PlanItem chosen = core.DrawNextTask(false, random);
                        Check(chosen != null && (chosen.Id == quick.Id || chosen.Id == longer.Id), "Normal draw cannot return ineligible item");
                        seen.Add(chosen.Id);
                        Check(core.DrawNextTask(true, random).Id == quick.Id, "Quick draw respects filtered set");
                    }
                    Check(seen.Count == 2, "Fixed-seed draw can reach both candidates");
                    core.SetCompleted(quick.Id, true, clock.Utc.Date);
                    Check(core.DrawNextTask(true, random) == null && core.DrawNextTask(false, random).Id == longer.Id, "Completing last quick candidate produces empty quick picker");
                });
                Test("priority selection deduplicates in order and validates atomically", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("priority-validation", clock); DateTime day = clock.Utc.Date;
                    PlanItem a = core.AddItem("A", day, null, PlanKind.Plan), b = core.AddItem("B", day, null, PlanKind.Plan);
                    PlanItem c = core.AddItem("C", day, null, PlanKind.Plan), d = core.AddItem("D", day, null, PlanKind.Plan);
                    PlanItem future = core.AddItem("Future", day.AddDays(1), null, PlanKind.Plan);
                    PlanItem schedule = core.AddItem("Schedule", day, 540, PlanKind.Schedule);
                    core.SetPriorities(day, new string[] { b.Id, a.Id, b.Id });
                    List<PlanItem> priorities = core.PrioritiesFor(day);
                    Check(priorities.Count == 2 && priorities[0].Id == b.Id && priorities[1].Id == a.Id, "Duplicate IDs collapse while preserving user order");
                    Expect<ArgumentException>(delegate { core.SetPriorities(day, new string[] { a.Id, b.Id, c.Id, d.Id }); });
                    Expect<ArgumentException>(delegate { core.SetPriorities(day, new string[] { future.Id }); });
                    Expect<ArgumentException>(delegate { core.SetPriorities(day, new string[] { schedule.Id }); });
                    Expect<ArgumentException>(delegate { core.SetPriorities(day, new string[] { "missing" }); });
                    priorities = core.PrioritiesFor(day);
                    Check(priorities.Count == 2 && priorities[0].Id == b.Id && priorities[1].Id == a.Id, "Rejected selections preserve earlier priorities");
                });
                Test("daily priorities remain isolated and retain completion while cleaning deleted tasks", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("priority-days", clock); DateTime today = clock.Utc.Date, tomorrow = today.AddDays(1);
                    PlanItem daily = core.AddItem("Daily", today, null, PlanKind.Plan, RepeatKind.Daily);
                    PlanItem oneoff = core.AddItem("One-off", today, null, PlanKind.Plan);
                    core.SetPriorities(today, new string[] { daily.Id, oneoff.Id }); core.SetPriorities(tomorrow, new string[] { daily.Id });
                    core.SetCompleted(oneoff.Id, true, today); core.SetPriorities(today, new string[] { daily.Id, oneoff.Id });
                    Check(core.PrioritiesFor(today).Count == 2 && core.PrioritiesFor(tomorrow).Count == 1, "A completed priority remains visible and each day retains its own list");
                    core.SetNextStep(oneoff.Id, "Keep this until deletion"); core.RemoveItem(oneoff.Id);
                    Check(core.PrioritiesFor(today).Count == 1 && core.ExtrasFor(today).PriorityTaskIds.Count == 1, "Deletion cleans both rendered and stored priority IDs");
                    Check(String.IsNullOrEmpty(core.NextStepFor(oneoff.Id)), "Deletion clears task's resume note");
                    core.SetPriorities(tomorrow, null);
                    Check(core.PrioritiesFor(tomorrow).Count == 0 && core.PrioritiesFor(today).Count == 1, "Clearing tomorrow does not alter today");
                });
                Test("quick-note conversion keeps full text and commits note removal with plan creation once", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("note-conversion", clock);
                    string body = "想做的事情\n" + new string('文', 700);
                    QuickNote note = core.AddQuickNote(body); DateTime selected = clock.Utc.Date.AddDays(5);
                    PlanItem converted = core.ConvertQuickNote(note.Id, selected.AddHours(15), PlanKind.Plan, 615, 12);
                    Check(converted.Title == "想做的事情" && converted.DetailNote == body, "First line becomes title while full note remains on task");
                    Check(converted.Date == selected && converted.MinuteOfDay == 615 && converted.EstimatedMinutes == 12, "Conversion retains selected scheduling details");
                    WidgetCore backup = new WidgetCore(core.StatePath + ".bak", clock.Read, TimeZoneInfo.Utc);
                    Check(backup.Items.Count == 0 && backup.Extras.Notes.Count == 1 && backup.Extras.Notes[0].Id == note.Id, "Backup contains state before conversion, proving one save rather than an intermediate duplicate");
                    core = NewCore("note-conversion", clock);
                    Check(core.Extras.Notes.Count == 0 && core.Items.Count == 1 && core.Items[0].DetailNote == body, "Restart preserves exactly one converted task and its complete text");
                    string emojiText = new string('文', 119) + "\uD83D\uDE00" + " more text";
                    QuickNote unicodeNote = core.AddQuickNote(emojiText);
                    PlanItem unicodeTask = core.ConvertQuickNote(unicodeNote.Id, selected, PlanKind.Plan, null, null);
                    Check(unicodeTask.Title == new string('文', 119) && unicodeTask.DetailNote == emojiText, "Title truncation never splits an emoji surrogate pair or loses complete note text");
                });
                Test("invalid quick-note conversion leaves original intact and removing notes persists", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("note-invalid", clock);
                    Expect<ArgumentException>(delegate { core.AddQuickNote("   "); });
                    Expect<ArgumentException>(delegate { core.AddQuickNote(new string('x', 2001)); });
                    QuickNote note = core.AddQuickNote("Keep my idea"); string previous = File.ReadAllText(core.StatePath);
                    Expect<ArgumentOutOfRangeException>(delegate { core.ConvertQuickNote(note.Id, clock.Utc.Date, PlanKind.Plan, 1440, 5); });
                    Expect<ArgumentOutOfRangeException>(delegate { core.ConvertQuickNote(note.Id, clock.Utc.Date, PlanKind.Plan, null, 0); });
                    Check(core.Extras.Notes.Count == 1 && core.Items.Count == 0 && File.ReadAllText(core.StatePath) == previous, "Rejected conversion does not remove note, create task or save partial state");
                    core.RemoveQuickNote(note.Id); core = NewCore("note-invalid", clock);
                    Check(core.Extras.Notes.Count == 0 && core.Items.Count == 0, "Explicit note removal survives restart");
                });
                Test("next steps are isolated by task and free focus and survive renaming", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("next-step", clock);
                    PlanItem a = core.AddItem("A", clock.Utc.Date, null, PlanKind.Plan), b = core.AddItem("B", clock.Utc.Date, null, PlanKind.Plan);
                    core.SetNextStep(a.Id, "Open chapter two"); core.SetNextStep(b.Id, "Fix the final paragraph"); core.SetNextStep(null, "Free focus scratchpad");
                    core.UpdateItem(a.Id, "A renamed", a.Date, a.MinuteOfDay, a.Kind, a.Repeat, a.ReminderEnabled);
                    core = NewCore("next-step", clock);
                    Check(core.NextStepFor(a.Id) == "Open chapter two" && core.NextStepFor(b.Id) == "Fix the final paragraph" && core.NextStepFor(null) == "Free focus scratchpad", "Independent IDs and free focus retain their own text through restart and rename");
                    Expect<ArgumentException>(delegate { core.SetNextStep(a.Id, new string('x', 501)); });
                    Check(core.NextStepFor(a.Id) == "Open chapter two", "Rejected text leaves previous next step intact");
                    core.SetNextStep(a.Id, "");
                    Check(String.IsNullOrEmpty(core.NextStepFor(a.Id)) && core.NextStepFor(b.Id) == "Fix the final paragraph" && core.NextStepFor(null) == "Free focus scratchpad", "Clearing one task preserves the others");
                });
                Test("important annual dates handle year rollover and February twenty-nine", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("important", clock);
                    ImportantDay annual = core.AddImportantDay("New year", new DateTime(2020, 1, 1), true);
                    Check(core.NextImportantOccurrence(annual, new DateTime(2026, 12, 31, 18, 0, 0)) == new DateTime(2027, 1, 1), "Annual date advances into next year");
                    Check(core.NextImportantOccurrence(annual, new DateTime(2027, 1, 1, 19, 0, 0)) == new DateTime(2027, 1, 1), "Today's annual date remains today");
                    ImportantDay leap = core.AddImportantDay("Leap anniversary", new DateTime(2024, 2, 29), true);
                    Check(core.NextImportantOccurrence(leap, new DateTime(2027, 2, 27)) == new DateTime(2027, 2, 28), "Non-leap year maps February twenty-nine to February twenty-eight");
                    Check(core.NextImportantOccurrence(leap, new DateTime(2027, 3, 1)) == new DateTime(2028, 2, 29), "Following leap year restores February twenty-nine");
                    ImportantDay once = core.AddImportantDay("Appointment", new DateTime(2026, 12, 1), false);
                    Check(core.NextImportantOccurrence(once, new DateTime(2027, 1, 1)) == new DateTime(2026, 12, 1), "One-off important date is not silently moved into another year");
                    core.RemoveImportantDay(once.Id); core = NewCore("important", clock);
                    Check(core.Extras.ImportantDays.Count == 2, "Important date removal persists");
                });
                Test("reflection and historical statistics stay on the selected day", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("reflection", clock); DateTime today = clock.Utc.Date, future = today.AddDays(1);
                    core.SaveReflection(today.AddHours(12), "开心", "Finished the difficult part");
                    core.SaveReflection(future, "平静", "Prepared tomorrow");
                    Expect<ArgumentException>(delegate { core.SaveReflection(today, "not-a-mood", "Overwrite"); });
                    Expect<ArgumentException>(delegate { core.SaveReflection(today, "开心", new string('x', 2001)); });
                    Check(core.ExtrasFor(today).Mood == "开心" && core.ExtrasFor(today).Reflection == "Finished the difficult part", "Invalid reflection cannot erase earlier entry");
                    PlanItem item = core.AddItem("Future", future, null, PlanKind.Plan); core.SetCompleted(item.Id, true, future);
                    Check(core.StatsOn(future).CompletedPlans == 1 && core.StatsOn(today).CompletedPlans == 0 && core.TodayStats.CompletedPlans == 0, "Selected-date statistics are independent from today's statistics");
                    core = NewCore("reflection", clock);
                    Check(core.ExtrasFor(today).DateKey == "2026-10-02" && core.ExtrasFor(future).Mood == "平静", "Daily reflection keys and values survive restart");
                    core.SaveReflection(today, "", "");
                    Check(String.IsNullOrEmpty(core.ExtrasFor(today).Reflection) && core.ExtrasFor(future).Reflection == "Prepared tomorrow", "Clearing a reflection does not clear another day");
                });
                Test("extras roundtrip preserves notes priorities details estimates and important dates", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("roundtrip", clock); DateTime day = clock.Utc.Date;
                    PlanItem task = core.AddItem("完整计划", day, null, PlanKind.Plan); core.SetEstimatedMinutes(task.Id, 25);
                    core.SetTaskDetail(task.Id, "Keep the source links and full context");
                    core.SetPriorities(day, new string[] { task.Id }); core.ExtrasFor(day).PriorityCelebrated = true;
                    core.SetNextStep(task.Id, "Start from page three");
                    QuickNote note = core.AddQuickNote("A note to keep"); ImportantDay important = core.AddImportantDay("周年", day.AddYears(-2), true);
                    core.SaveReflection(day, "充实", "A productive day"); core.Save(); core = NewCore("roundtrip", clock);
                    Check(core.Items.Count == 1 && core.Items[0].EstimatedMinutes == 25 && core.Items[0].DetailNote == "Keep the source links and full context", "Task detail and estimate persist");
                    Check(core.PrioritiesFor(day).Count == 1 && core.ExtrasFor(day).PriorityCelebrated && core.NextStepFor(task.Id) == "Start from page three", "Priority state and task next step persist");
                    Check(core.Extras.Notes.Count == 1 && core.Extras.Notes[0].Id == note.Id && core.Extras.Notes[0].Text == "A note to keep", "Unconverted notes preserve IDs and text");
                    Check(core.Extras.ImportantDays.Count == 1 && core.Extras.ImportantDays[0].Id == important.Id && core.Extras.ImportantDays[0].Annual, "Annual important day survives persistence");
                });
                Test("legacy JSON without extras upgrades without changing existing timers and plans", delegate
                {
                    Clock clock = new Clock(); string path = Path.Combine(TestRoot, "legacy", "state.json"); Directory.CreateDirectory(Path.GetDirectoryName(path));
                    long milliseconds = (long)(clock.Utc.Date - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                    string json = "{\"Version\":1,\"Settings\":{\"FocusMinutes\":25,\"ShortBreakMinutes\":5,\"LongBreakMinutes\":15,\"SessionsBeforeLongBreak\":4,\"ThemeMode\":\"system\",\"FocusTaskId\":\"legacy-plan\"}," +
                        "\"Countdown\":{\"DurationSeconds\":300,\"RemainingSeconds\":123,\"Status\":2},\"Pomodoro\":{\"DurationSeconds\":1500,\"RemainingSeconds\":456,\"Status\":2}," +
                        "\"Items\":[{\"Id\":\"legacy-plan\",\"Title\":\"旧计划\",\"Date\":\"\\/Date(" + milliseconds.ToString(CultureInfo.InvariantCulture) + ")\\/\",\"Kind\":0}],\"Statistics\":[]}";
                    File.WriteAllText(path, json); WidgetCore core = NewCore("legacy", clock);
                    Check(String.IsNullOrEmpty(core.LoadWarning) && core.Items.Count == 1 && core.Items[0].Title == "旧计划", "Old document loads without corrupt recovery");
                    Check(core.Extras != null && core.Extras.Notes.Count == 0 && core.Extras.ImportantDays.Count == 0 && core.Extras.Days.Count == 0 && core.Extras.ResumeNotes.Count == 0 && core.Extras.ImportedLearningPacks.Count == 0, "Missing extras receive empty collections");
                    Check(!core.Items[0].EstimatedMinutes.HasValue && String.IsNullOrEmpty(core.Items[0].DetailNote), "Old tasks have no fabricated estimate or detail");
                    core.SetPriorities(clock.Utc.Date, new string[] { "legacy-plan" }); core.SetNextStep("legacy-plan", "Continue this old plan");
                    core.AddQuickNote("New idea"); core = NewCore("legacy", clock);
                    Check(core.Countdown.Status == TimerStatus.Paused && core.Countdown.RemainingSeconds == 123 && core.Pomodoro.RemainingSeconds == 456 && core.Settings.FocusTaskId == "legacy-plan", "Extras edits do not alter legacy timer or task attribution");
                    Check(core.PrioritiesFor(clock.Utc.Date).Count == 1 && core.NextStepFor("legacy-plan") == "Continue this old plan" && core.Extras.Notes.Count == 1, "New extras persist alongside the old data");
                });
                Test("learning pack creates fourteen tasks across seven calendar days and imports only once", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("learning-days", clock);
                    DateTime start = new DateTime(2026, 10, 29);
                    Check(core.Import8086Registers(start.AddHours(14)) == 14 && core.Items.Count == 14, "First import creates fourteen tasks");
                    for (int day = 0; day < 7; day++)
                    {
                        DateTime selected = start.AddDays(day);
                        List<PlanItem> tasks = core.Items.FindAll(delegate(PlanItem p) { return p.Date == selected; });
                        Check(tasks.Count == 2, "Every calendar day contains two learning tasks across month rollover");
                        Check(tasks.Exists(delegate(PlanItem p) { return p.EstimatedMinutes == 10; }) && tasks.Exists(delegate(PlanItem p) { return p.EstimatedMinutes == 20; }), "Each day has a ten-minute explanation and twenty-minute practice");
                        foreach (PlanItem task in tasks)
                        {
                            Check(task.Kind == PlanKind.Plan && !String.IsNullOrEmpty(task.DetailNote) && !String.IsNullOrEmpty(core.NextStepFor(task.Id)), "Learning tasks include explanation and a specific next step");
                        }
                    }
                    HashSet<string> ids = new HashSet<string>(core.Items.ConvertAll(delegate(PlanItem p) { return p.Id; }));
                    core = NewCore("learning-days", clock); string saved = File.ReadAllText(core.StatePath);
                    Check(core.Import8086Registers(start.AddDays(30)) == 0 && core.Items.Count == 14, "Persisted import marker prevents importing the same pack again");
                    Check(ids.SetEquals(core.Items.ConvertAll(delegate(PlanItem p) { return p.Id; })) && File.ReadAllText(core.StatePath) == saved, "Repeated import retains task identities and avoids another save");
                });
                Test("learning import preserves existing plans and initializes priorities only on empty days", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("learning-preservation", clock);
                    DateTime start = new DateTime(2026, 10, 29), occupied = start.AddDays(2), empty = start.AddDays(1);
                    PlanItem today = core.AddItem("Existing today", clock.Utc.Date, null, PlanKind.Plan);
                    PlanItem future = core.AddItem("Existing future priority", occupied, null, PlanKind.Plan);
                    core.SetTaskDetail(future.Id, "Preserve existing context"); core.SetNextStep(future.Id, "Keep this existing next step");
                    core.SetPriorities(occupied, new string[] { future.Id }); core.SaveReflection(occupied, "平静", "Existing reflection");
                    core.Import8086Registers(start);
                    Check(core.Items.Count == 16 && Has(core.Items, today.Id) && Has(core.Items, future.Id), "Learning import retains all existing tasks");
                    List<PlanItem> retained = core.PrioritiesFor(occupied), initialized = core.PrioritiesFor(empty);
                    Check(retained.Count == 1 && retained[0].Id == future.Id && core.ExtrasFor(occupied).PriorityTaskIds.Count == 1, "An occupied future day keeps its exact existing priority selection");
                    Check(initialized.Count == 2 && initialized[0].Date == empty && initialized[1].Date == empty, "An empty learning day initializes two learning priorities");
                    Check(core.ExtrasFor(occupied).Reflection == "Existing reflection" && core.NextStepFor(future.Id) == "Keep this existing next step", "Import preserves the user's future reflection and next step");
                    core = NewCore("learning-preservation", clock);
                    retained = core.PrioritiesFor(occupied);
                    Check(core.Items.Count == 16 && retained.Count == 1 && retained[0].Id == future.Id && core.PrioritiesFor(empty).Count == 2, "Preserved and initialized priorities survive restart");
                });
                Test("learning import rejects a date overflow before changing state or saving", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("learning-overflow", clock);
                    PlanItem existing = core.AddItem("Keep this", clock.Utc.Date, null, PlanKind.Plan);
                    core.SetPriorities(clock.Utc.Date, new string[] { existing.Id });
                    core.SetNextStep(existing.Id, "Existing next step"); QuickNote note = core.AddQuickNote("Existing note");
                    string saved = File.ReadAllText(core.StatePath);
                    Expect<ArgumentOutOfRangeException>(delegate { core.Import8086Registers(DateTime.MaxValue.AddDays(-5)); });
                    Check(core.Items.Count == 1 && core.Items[0].Id == existing.Id && core.Extras.ImportedLearningPacks.Count == 0, "Date overflow creates no tasks or imported marker");
                    Check(core.Extras.Notes.Count == 1 && core.Extras.Notes[0].Id == note.Id && core.PrioritiesFor(clock.Utc.Date).Count == 1 && core.NextStepFor(existing.Id) == "Existing next step", "Date overflow retains all existing extras");
                    Check(File.ReadAllText(core.StatePath) == saved, "Rejected import does not persist a partial state");
                    core = NewCore("learning-overflow", clock);
                    Check(core.Items.Count == 1 && core.Extras.ImportedLearningPacks.Count == 0 && core.Extras.Notes.Count == 1, "Restart observes only the original state");
                });
                Test("all learning packs have complete daily lessons including fourteen-day OS and algorithm courses", delegate
                {
                    string[] ids = { "8086-registers-v1", "os-principles-v1", "algorithm-design-v1" };
                    int[] counts = { 14, 28, 28 }, days = { 7, 14, 14 };
                    DateTime start = new DateTime(2026, 10, 29);
                    for (int pack = 0; pack < ids.Length; pack++)
                    {
                        Clock clock = new Clock(); WidgetCore core = NewCore("all-packs-" + pack.ToString(CultureInfo.InvariantCulture), clock);
                        Check(core.ImportLearningPack(ids[pack], start.AddHours(15)) == counts[pack] && core.Items.Count == counts[pack], "Each course imports its complete task count");
                        for (int day = 0; day < days[pack]; day++)
                        {
                            DateTime selected = start.AddDays(day);
                            List<PlanItem> tasks = core.Items.FindAll(delegate(PlanItem p) { return p.Date == selected; });
                            Check(tasks.Count == 2 && tasks.Exists(delegate(PlanItem p) { return p.EstimatedMinutes == 10; }) && tasks.Exists(delegate(PlanItem p) { return p.EstimatedMinutes == 20; }), "Every course day has a ten-minute concept task and twenty-minute exercise");
                            foreach (PlanItem task in tasks)
                            {
                                Check(task.Kind == PlanKind.Plan && task.Repeat == RepeatKind.None && !String.IsNullOrEmpty(task.DetailNote) && !String.IsNullOrEmpty(core.NextStepFor(task.Id)), "Imported lessons have complete explanations and a resumable next step");
                                if (pack > 0)
                                    Check(task.DetailNote.Contains("自检") || task.DetailNote.Contains("自测") || task.DetailNote.Contains("完成标准"), "University-course lessons include an explicit self-check or completion criterion");
                            }
                        }
                    }
                });
                Test("learning packs deduplicate independently and preserve existing priorities when sharing start dates", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("pack-independence", clock); DateTime start = new DateTime(2026, 10, 29);
                    PlanItem a = core.AddItem("Existing A", start, null, PlanKind.Plan);
                    PlanItem b = core.AddItem("Existing B", start, null, PlanKind.Plan);
                    PlanItem c = core.AddItem("Existing C", start, null, PlanKind.Plan);
                    core.SetPriorities(start, new string[] { c.Id, a.Id, b.Id });
                    Check(core.ImportLearningPack("os-principles-v1", start) == 28, "OS course imports independently");
                    List<PlanItem> osPriorities = core.PrioritiesFor(start.AddDays(1));
                    string firstOsPriority = osPriorities[0].Id, secondOsPriority = osPriorities[1].Id;
                    Check(core.ImportLearningPack("algorithm-design-v1", start) == 28 && core.ImportLearningPack("8086-registers-v1", start) == 14, "Algorithm and registers courses are not blocked by another course's import marker");
                    List<PlanItem> retained = core.PrioritiesFor(start);
                    Check(retained.Count == 3 && retained[0].Id == c.Id && retained[1].Id == a.Id && retained[2].Id == b.Id, "Three existing priorities keep their exact selection and order");
                    retained = core.PrioritiesFor(start.AddDays(1));
                    Check(retained.Count == 2 && retained[0].Id == firstOsPriority && retained[1].Id == secondOsPriority, "Following course imports preserve the first course's initialized priorities");
                    for (int day = 0; day < 14; day++) Check(core.PrioritiesFor(start.AddDays(day)).Count <= 3, "Multiple courses never exceed the daily priority limit");
                    Check(core.Items.Count == 73 && core.Extras.ImportedLearningPacks.Count == 3, "All three courses coexist alongside pre-existing plans");
                    core = NewCore("pack-independence", clock); string saved = File.ReadAllText(core.StatePath);
                    Check(core.ImportLearningPack("8086-registers-v1", start.AddMonths(1)) == 0 && core.ImportLearningPack("os-principles-v1", start.AddMonths(1)) == 0 && core.ImportLearningPack("algorithm-design-v1", start.AddMonths(1)) == 0, "Every independent import marker survives restart");
                    Check(core.Items.Count == 73 && File.ReadAllText(core.StatePath) == saved && core.PrioritiesFor(start).Count == 3, "Repeated imports add no tasks, save no state and keep existing priorities");
                });

                Test("priority edits remove ineligible references while preserving valid history and daily metadata", delegate
                {
                    Clock clock = new Clock(); WidgetCore core = NewCore("priority-edits", clock);
                    DateTime history = new DateTime(2026, 10, 1), today = clock.Utc.Date, future = new DateTime(2026, 10, 5), thursday = new DateTime(2026, 10, 8);
                    PlanItem other = core.AddItem("Keep other priority", history, null, PlanKind.Plan, RepeatKind.Daily);
                    PlanItem moved = core.AddItem("Move later", history, null, PlanKind.Plan);
                    core.SetPriorities(history, new string[] { moved.Id, other.Id }); core.SetPriorities(today, new string[] { moved.Id, other.Id });
                    core.SetPriorities(future, new string[] { moved.Id, other.Id }); core.SetCompleted(moved.Id, true, history);
                    core.SaveReflection(today, "平静", "Preserve this reflection"); core.ExtrasFor(today).SummaryPrompted = true;
                    core.ExtrasFor(today).PriorityCelebrated = true; core.ExtrasFor(future).PriorityCelebrated = true;
                    core.UpdateItem(moved.Id, moved.Title, new DateTime(2026, 10, 4), null, PlanKind.Plan);
                    Check(!core.ExtrasFor(history).PriorityTaskIds.Contains(moved.Id) && !core.ExtrasFor(today).PriorityTaskIds.Contains(moved.Id), "Moving task into future removes priority references before its new date");
                    Check(core.ExtrasFor(future).PriorityTaskIds.Contains(moved.Id) && core.ExtrasFor(future).PriorityCelebrated, "Still-valid later record remains, regardless of task's completion date");
                    Check(!core.ExtrasFor(today).PriorityCelebrated && core.ExtrasFor(today).PriorityTaskIds.Contains(other.Id), "Removing an invalid reference resets celebration and retains other task");
                    core.UpdateItem(moved.Id, moved.Title, moved.Date, 600, PlanKind.Schedule);
                    Check(!core.ExtrasFor(future).PriorityTaskIds.Contains(moved.Id) && !core.ExtrasFor(future).PriorityCelebrated && core.ExtrasFor(future).PriorityTaskIds.Contains(other.Id), "Changing a plan into a schedule clears its priority references only");

                    PlanItem weekly = core.AddItem("Weekly conversion", history, null, PlanKind.Plan);
                    DateTime[] dates = { history, today, future, thursday };
                    foreach (DateTime day in dates) { core.SetPriorities(day, new string[] { weekly.Id, other.Id }); core.ExtrasFor(day).PriorityCelebrated = true; }
                    core.UpdateItem(weekly.Id, "Renamed only", history, 660, PlanKind.Plan);
                    foreach (DateTime day in dates) Check(core.ExtrasFor(day).PriorityTaskIds.Contains(weekly.Id) && core.ExtrasFor(day).PriorityCelebrated, "Renaming or changing time preserves priorities and celebration");
                    core.UpdateItem(weekly.Id, weekly.Title, history, weekly.MinuteOfDay, PlanKind.Plan, RepeatKind.Weekly);
                    Check(core.ExtrasFor(history).PriorityTaskIds.Contains(weekly.Id) && core.ExtrasFor(thursday).PriorityTaskIds.Contains(weekly.Id), "Matching historical and future weekdays remain valid after weekly conversion");
                    Check(!core.ExtrasFor(today).PriorityTaskIds.Contains(weekly.Id) && !core.ExtrasFor(future).PriorityTaskIds.Contains(weekly.Id), "Nonmatching weekdays lose the edited priority reference");
                    foreach (DateTime day in dates) Check(core.ExtrasFor(day).PriorityTaskIds.Contains(other.Id), "Priority edits preserve every unrelated task reference");
                    core = NewCore("priority-edits", clock);
                    Check(core.ExtrasFor(today).Mood == "平静" && core.ExtrasFor(today).Reflection == "Preserve this reflection" && core.ExtrasFor(today).SummaryPrompted, "Cleanup and restart preserve mood, reflection and summary prompt state");
                    Check(core.ExtrasFor(history).PriorityTaskIds.Contains(weekly.Id) && !core.ExtrasFor(today).PriorityTaskIds.Contains(weekly.Id), "Edited priority eligibility is persisted");
                });

                Console.WriteLine("All " + passed + " extras tests passed."); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine("FAILED: " + error); return 1; }
            finally
            {
                string resolved = Path.GetFullPath(TestRoot);
                string allowed = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved)) Directory.Delete(resolved, true);
            }
        }
    }
}
