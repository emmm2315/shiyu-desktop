using System;
using System.IO;
using DeskSprout;

internal static class CoreTests
{
    private static int passed;
    private static readonly string TestRoot = Path.Combine(Path.GetTempPath(), "DeskSprout-CoreTests-" + Guid.NewGuid().ToString("N"));

    private sealed class Clock
    {
        public DateTime Utc = new DateTime(2026, 10, 2, 1, 0, 0, DateTimeKind.Utc);
        public DateTime Read() { return Utc; }
        public void Advance(double seconds) { Utc = Utc.AddSeconds(seconds); }
    }
    private static WidgetCore NewCore(string name, Clock clock, TimeZoneInfo zone = null)
    {
        return new WidgetCore(Path.Combine(TestRoot, name, "state.json"), clock.Read, zone ?? TimeZoneInfo.Utc);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Near(double actual, double expected, string message)
    {
        Check(Math.Abs(actual - expected) < 0.001, message + " (actual " + actual + ")");
    }
    private static void Test(string name, Action body)
    {
        body(); passed++; Console.WriteLine("PASS " + name);
    }
    private static void ExpectException<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    public static int Main()
    {
        try
        {
            Test("defaults and input boundaries", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("defaults", clock);
                Check(core.Settings.ThemeMode == "system", "System theme is the default");
                Check(!core.Settings.AutoAdvance, "Automatic advancement is opt in");
                Check(core.Countdown.Status == TimerStatus.Stopped && core.Countdown.DurationSeconds == 300, "Default timer is ready");
                Check(core.Pomodoro.DurationSeconds == 1500, "Default focus duration");
                ExpectException<ArgumentOutOfRangeException>(delegate { core.ConfigureCountdown(0); });
                ExpectException<ArgumentException>(delegate { core.AddItem("   ", clock.Utc.Date, null, PlanKind.Plan); });
                ExpectException<ArgumentOutOfRangeException>(delegate { core.AddItem("bad time", clock.Utc.Date, 1440, PlanKind.Schedule); });
            });
            Test("countdown pause, resume, reset and fractional remainder", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("countdown", clock);
                core.ConfigureCountdown(90); core.StartCountdown(); clock.Advance(12.5); core.PauseCountdown();
                Near(core.Countdown.RemainingSeconds, 77.5, "Pause preserves the remainder");
                Check(core.Countdown.Status == TimerStatus.Paused && !core.Countdown.DeadlineUtc.HasValue, "Paused timer has no deadline");
                clock.Advance(1000); core.Tick(); Near(core.Countdown.RemainingSeconds, 77.5, "Paused elapsed wall time has no effect");
                core.ResumeCountdown(); clock.Advance(77.4); Check(!core.Tick().CountdownCompleted, "Not early");
                clock.Advance(0.1); Check(core.Tick().CountdownCompleted, "Deadline completes timer");
                Check(!core.Tick().CountdownCompleted, "Completion emitted once");
                core.ResetCountdown(); Near(core.Countdown.RemainingSeconds, 90, "Reset restores configured duration");
                Check(core.Countdown.Status == TimerStatus.Stopped, "Reset stops timer");
            });
            Test("countdown deadline survives relaunch and long sleep", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("sleep", clock);
                core.ConfigureCountdown(40); core.StartCountdown(); clock.Advance(10);
                core = NewCore("sleep", clock); core.Tick(); Near(core.Countdown.RemainingSeconds, 30, "Relaunch derives remaining time from deadline");
                clock.Advance(100000); core = NewCore("sleep", clock);
                Check(core.Tick().CountdownCompleted, "Expired persisted deadline completes on wake");
                core = NewCore("sleep", clock); Check(!core.Tick().CountdownCompleted, "Persisted completion does not repeat");
            });
            Test("pomodoro full cycle, manual advance and long break", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("cycle", clock);
                core.Settings.FocusMinutes = 1; core.Settings.ShortBreakMinutes = 1; core.Settings.LongBreakMinutes = 2;
                core.Settings.SessionsBeforeLongBreak = 2; core.ResetPomodoro();
                core.StartPomodoro(); clock.Advance(60); TickResult result = core.Tick();
                Check(result.PomodoroCompleted && result.CompletedPhase == PomodoroPhase.Focus, "Focus completion emitted");
                Check(core.Pomodoro.Phase == PomodoroPhase.Focus && core.Pomodoro.NextPhase == PomodoroPhase.ShortBreak, "Next phase prepared without starting");
                Check(core.Pomodoro.Status == TimerStatus.Completed && core.TodayStats.FocusSessions == 1, "Manual advance stays completed");
                clock.Advance(600); core.Tick(); Check(core.TodayStats.FocusSessions == 1, "Idle does not fabricate focus sessions");
                core.StartPomodoro(); Check(core.Pomodoro.Phase == PomodoroPhase.ShortBreak, "Start begins break");
                clock.Advance(60); core.Tick(); core.StartPomodoro(); clock.Advance(60); core.Tick();
                Check(core.Pomodoro.NextPhase == PomodoroPhase.LongBreak && core.TodayStats.FocusMinutes == 2, "Cycle switches to long break");
                core.StartPomodoro(); Check(core.Pomodoro.DurationSeconds == 120, "Long duration applied");
                clock.Advance(120); core.Tick(); Check(core.Pomodoro.FocusSessionsInCycle == 0, "Long break clears cycle");
                core.StartPomodoro(); Check(core.Pomodoro.Phase == PomodoroPhase.Focus, "New cycle starts at focus");
            });
            Test("pomodoro pause, independent timer and explicit reset", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("independent", clock);
                core.Settings.FocusMinutes = 1; core.StartPomodoro(); core.ConfigureCountdown(15); core.StartCountdown();
                clock.Advance(10); core.PausePomodoro(); Near(core.Pomodoro.RemainingSeconds, 50, "Focus pause remainder");
                clock.Advance(10); Check(core.Tick().CountdownCompleted, "Countdown is independent");
                Check(core.Pomodoro.Status == TimerStatus.Paused, "Countdown completion leaves focus paused");
                core.ResumePomodoro(); clock.Advance(20); core.ResetPomodoro();
                Check(core.Pomodoro.Status == TimerStatus.Stopped && core.Pomodoro.Phase == PomodoroPhase.Focus, "Reset prepares first focus");
                Check(core.TodayStats.FocusSessions == 0, "Partial focus is not a completed session");
            });
            Test("pause never consumes reminders or another timer's completion", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("pause-events", clock);
                core.Settings.FocusMinutes = 1; core.StartPomodoro(); core.ConfigureCountdown(20); core.StartCountdown();
                core.AddItem("Due reminder", clock.Utc.Date, 60, PlanKind.Schedule);
                clock.Advance(20); core.PausePomodoro(); TickResult result = core.Tick();
                Check(result.CountdownCompleted && result.Reminders.Count == 1, "Focus pause leaves countdown and reminder events for Tick");
                Check(core.Pomodoro.Status == TimerStatus.Paused, "Focus pauses normally");
                core.ResumePomodoro(); core.ConfigureCountdown(40); core.StartCountdown(); clock.Advance(40);
                core.PauseCountdown(); Check(core.Countdown.Status == TimerStatus.Running, "Expired pause keeps completion pending");
                result = core.Tick(); Check(result.CountdownCompleted && result.PomodoroCompleted, "Both expired timers still deliver events");
                core.ResetPomodoro(); core.StartPomodoro(); clock.Advance(60); core.PausePomodoro();
                Check(core.Tick().PomodoroCompleted, "Expired focus pause preserves its own completion");
            });
            Test("skip does not count focus and auto advance handles one wake interval", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("skip", clock);
                core.Settings.FocusMinutes = 1; core.Settings.ShortBreakMinutes = 1; core.StartPomodoro(); clock.Advance(50); core.SkipPomodoro();
                Check(core.Pomodoro.Phase == PomodoroPhase.ShortBreak && core.Pomodoro.Status == TimerStatus.Stopped, "Skip prepares stopped break");
                Check(core.TodayStats.FocusSessions == 0 && core.Pomodoro.FocusSessionsInCycle == 0, "Skip has no credit");
                core.ResetPomodoro(); core.Settings.AutoAdvance = true; core.StartPomodoro(); clock.Advance(3600); TickResult result = core.Tick();
                Check(result.PomodoroCompleted && core.Pomodoro.Status == TimerStatus.Running, "Automatic next phase starts");
                Check(core.Pomodoro.Phase == PomodoroPhase.ShortBreak && core.TodayStats.FocusSessions == 1, "Only actual expired focus gets counted");
                Near(core.Pomodoro.RemainingSeconds, 60, "Break starts fresh after sleep");
            });
            Test("focus attribution captures selected task and local deadline date", delegate
            {
                Clock clock = new Clock(); TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("UTC+8-test", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
                clock.Utc = new DateTime(2026, 10, 2, 15, 58, 0, DateTimeKind.Utc);
                WidgetCore core = NewCore("attribution", clock, zone); core.Settings.FocusMinutes = 1;
                PlanItem first = core.AddItem("First", core.LocalNow.Date, null, PlanKind.Plan);
                PlanItem second = core.AddItem("Second", core.LocalNow.Date, null, PlanKind.Plan);
                core.Settings.FocusTaskId = first.Id; core.StartPomodoro(); core.Settings.FocusTaskId = second.Id;
                clock.Advance(180); core.Tick(); Check(first.FocusSessions == 1 && first.FocusMinutes == 1 && second.FocusSessions == 0, "Started task keeps attribution");
                Check(core.TodayStats.FocusSessions == 0, "Completion belongs to previous local date at 23:59");
                clock.Utc = new DateTime(2026, 10, 2, 15, 59, 30, DateTimeKind.Utc); core.ResetPomodoro(); core.StartPomodoro(); clock.Advance(70); core.Tick();
                Check(core.TodayStats.FocusSessions == 1, "Deadline crossing midnight belongs to new date");
            });
            Test("one off completion counters are idempotent and undo across days", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("tasks", clock);
                PlanItem item = core.AddItem("  Write plan  ", clock.Utc.Date, null, PlanKind.Plan);
                Check(item.Title == "Write plan" && core.IsDueOn(item, clock.Utc.Date), "Title and date normalized");
                core.SetCompleted(item.Id, true); core.SetCompleted(item.Id, true); Check(core.TodayStats.CompletedPlans == 1, "Double complete counts once");
                clock.Advance(86400); core.SetCompleted(item.Id, false); Check(core.TodayStats.CompletedPlans == 0 && !item.Completed, "Undo on later date does not credit or debit today");
                clock.Advance(-86400); Check(core.TodayStats.CompletedPlans == 0, "Undo debits original completion date");
                core.SetCompleted(item.Id, true); core.RemoveItem(item.Id); Check(core.TodayStats.CompletedPlans == 1, "Removing completed item preserves history");
            });
            Test("recurring daily and weekly occurrences complete independently", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("repeat", clock);
                PlanItem daily = core.AddItem("Read", clock.Utc.Date, null, PlanKind.Plan, RepeatKind.Daily);
                PlanItem weekly = core.AddItem("Review", clock.Utc.Date, null, PlanKind.Plan, RepeatKind.Weekly);
                Check(!core.IsDueOn(daily, clock.Utc.Date.AddDays(-1)), "Recurrence cannot precede start");
                Check(core.IsDueOn(weekly, clock.Utc.Date.AddDays(7)) && !core.IsDueOn(weekly, clock.Utc.Date.AddDays(1)), "Weekly weekday retained");
                core.SetCompleted(daily.Id, true); Check(core.IsCompletedOn(daily, clock.Utc.Date), "Today's occurrence completed");
                clock.Advance(86400); Check(core.IsDueOn(daily, clock.Utc.Date) && !core.IsCompletedOn(daily, clock.Utc.Date), "Tomorrow occurrence is fresh");
                core.SetCompleted(daily.Id, true); core.SetCompleted(daily.Id, false); Check(core.TodayStats.CompletedPlans == 0, "Recurring undo debits only today");
                Check(core.IsCompletedOn(daily, clock.Utc.Date.AddDays(-1)), "Previous occurrence remains completed");
                ExpectException<ArgumentException>(delegate { core.SetCompleted(weekly.Id, true); });
                core = NewCore("repeat", clock); daily = core.Items.Find(delegate(PlanItem p) { return p.Id == daily.Id; });
                Check(core.IsCompletedOn(daily, clock.Utc.Date.AddDays(-1)), "Occurrence completion persists");
            });
            Test("timed reminders honor time, completion and persisted dedupe", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("reminder", clock);
                PlanItem due = core.AddItem("Meeting", clock.Utc.Date, 61, PlanKind.Schedule);
                core.AddItem("Untimed", clock.Utc.Date, null, PlanKind.Schedule);
                PlanItem timedPlan = core.AddItem("Plan with time", clock.Utc.Date, 61, PlanKind.Plan);
                PlanItem done = core.AddItem("Already done", clock.Utc.Date, 0, PlanKind.Schedule); core.SetCompleted(done.Id, true);
                Check(core.Tick().Reminders.Count == 0, "Future, untimed and completed items do not alert");
                clock.Advance(60); TickResult result = core.Tick();
                Check(result.Reminders.Count == 2 && result.Reminders.Exists(delegate(PlanItem p) { return p.Id == due.Id; })
                    && result.Reminders.Exists(delegate(PlanItem p) { return p.Id == timedPlan.Id; }), "Both timed plans and schedules remind");
                Check(core.Tick().Reminders.Count == 0, "No repeated tick reminder");
                core = NewCore("reminder", clock); Check(core.Tick().Reminders.Count == 0, "No repeated relaunch reminder");
                core.UpdateItem(due.Id, "Meeting moved", clock.Utc.Date, 63, PlanKind.Schedule); clock.Advance(120);
                Check(core.Tick().Reminders.Count == 1, "Moving time creates new reminder occurrence");
            });
            Test("recurring reminders refresh daily and one off catches midnight rollover", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("rollover", clock);
                PlanItem daily = core.AddItem("Daily standup", clock.Utc.Date, 60, PlanKind.Schedule, RepeatKind.Daily);
                Check(core.Tick().Reminders.Count == 1, "Daily reminder first day");
                core.SetCompleted(daily.Id, true); clock.Advance(86400); Check(core.Tick().Reminders.Count == 1, "Yesterday completion does not suppress today");
                clock.Utc = new DateTime(2026, 10, 3, 23, 58, 0, DateTimeKind.Utc);
                PlanItem late = core.AddItem("Late call", clock.Utc.Date, 1439, PlanKind.Schedule);
                clock.Advance(180); TickResult result = core.Tick();
                Check(result.Reminders.Count == 1 && result.Reminders[0].Id == late.Id, "One off due across midnight still alerts");
                Check(core.Tick().Reminders.Count == 0, "Cross midnight reminder also deduped");
            });
            Test("per item reminder toggle persists and enables a due reminder once", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("reminder-toggle", clock);
                PlanItem item = core.AddItem("Quiet task", clock.Utc.Date, 60, PlanKind.Plan, RepeatKind.None, false);
                Check(core.Tick().Reminders.Count == 0, "Disabled timed item does not remind");
                core = NewCore("reminder-toggle", clock); item = core.Items[0];
                Check(!item.ReminderEnabled && core.Tick().Reminders.Count == 0, "Disabled state persists");
                core.UpdateItem(item.Id, item.Title, item.Date, item.MinuteOfDay, item.Kind, item.Repeat, true);
                Check(core.Tick().Reminders.Count == 1 && core.Tick().Reminders.Count == 0, "Enabling a due reminder emits once");
                core.UpdateItem(item.Id, item.Title, item.Date, item.MinuteOfDay, item.Kind, item.Repeat, false);
                core.UpdateItem(item.Id, item.Title, item.Date, item.MinuteOfDay, item.Kind, item.Repeat, true);
                Check(core.Tick().Reminders.Count == 1, "Re enabling explicitly creates a new reminder opportunity");
            });
            Test("persistence restores settings, paused states and task data", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("persistence", clock);
                core.Settings.ThemeMode = "dark"; core.Settings.WindowX = -100; core.Settings.WindowY = 200;
                core.Settings.HasWindowPosition = true; core.Settings.AlwaysOnTop = true; core.Settings.SelectedTab = 2;
                core.ConfigureCountdown(130); core.StartCountdown(); clock.Advance(20); core.PauseCountdown();
                core.Settings.FocusMinutes = 2; core.StartPomodoro(); clock.Advance(30); core.PausePomodoro();
                PlanItem item = core.AddItem("你好，桌面", clock.Utc.Date, 600, PlanKind.Schedule, RepeatKind.Weekly);
                core.Settings.FocusTaskId = item.Id; core.Save(); core = NewCore("persistence", clock);
                Check(core.Settings.ThemeMode == "dark" && core.Settings.HasWindowPosition && core.Settings.WindowX == -100, "Window settings persist");
                Check(core.Settings.SelectedTab == 2 && core.Settings.FocusTaskId == item.Id, "Selection persists");
                Check(core.Countdown.Status == TimerStatus.Paused && core.Pomodoro.Status == TimerStatus.Paused, "Both paused timers persist");
                Near(core.Countdown.RemainingSeconds, 110, "Countdown remainder restored"); Near(core.Pomodoro.RemainingSeconds, 90, "Pomodoro remainder restored");
                Check(core.Items[0].Title == "你好，桌面" && core.Items[0].Repeat == RepeatKind.Weekly, "Unicode and recurrence persist");
                Check(Directory.GetFiles(Path.GetDirectoryName(core.StatePath), "*.tmp-*").Length == 0, "Atomic save leaves no temp files");
            });
            Test("corrupt primary preserves original and recovers valid backup", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("corrupt", clock);
                core.ConfigureCountdown(77); core.Save(); string path = core.StatePath;
                File.WriteAllText(path, "{malformed-original-content"); core = NewCore("corrupt", clock);
                Check(!String.IsNullOrEmpty(core.LoadWarning) && core.LoadWarning.Contains("恢复"), "Recovery is visible");
                Check(core.Countdown.DurationSeconds == 77, "Good backup restored");
                string[] preserved = Directory.GetFiles(Path.GetDirectoryName(path), "state.json.corrupt-*");
                Check(preserved.Length == 1 && File.ReadAllText(preserved[0]) == "{malformed-original-content", "Malformed original preserved byte for byte");
                core.Save(); core = NewCore("corrupt", clock); Check(String.IsNullOrEmpty(core.LoadWarning), "Repaired state opens normally");
                Check(File.ReadAllText(preserved[0]) == "{malformed-original-content", "Later saves preserve corrupt archive");
            });
            Test("corrupt primary without backup uses defaults and preserves original", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("corrupt-no-backup", clock);
                Directory.CreateDirectory(Path.GetDirectoryName(core.StatePath)); File.WriteAllText(core.StatePath, "null");
                core = NewCore("corrupt-no-backup", clock); Check(!String.IsNullOrEmpty(core.LoadWarning), "Failure warning visible");
                Check(core.Settings.FocusMinutes == 25 && core.Items.Count == 0, "Defaults restored");
                string[] preserved = Directory.GetFiles(Path.GetDirectoryName(core.StatePath), "state.json.corrupt-*");
                Check(preserved.Length == 1 && File.ReadAllText(preserved[0]) == "null", "Bad payload preserved");
            });
            Test("save failure preserves in memory timer events and reports retryable error", delegate
            {
                Clock clock = new Clock(); WidgetCore core = NewCore("blocked-directory", clock);
                string blockedDirectory = Path.GetDirectoryName(core.StatePath);
                Directory.CreateDirectory(Path.GetDirectoryName(blockedDirectory)); File.WriteAllText(blockedDirectory, "blocker");
                core.ConfigureCountdown(5); core.StartCountdown(); core.AddItem("Still notify", clock.Utc.Date, 60, PlanKind.Schedule);
                Check(!String.IsNullOrEmpty(core.LastSaveError), "Internal persistence failure is visible");
                clock.Advance(5); TickResult result = core.Tick();
                Check(result.CountdownCompleted && result.Reminders.Count == 1, "Save failure does not consume completion or reminder events");
                Check(core.Countdown.Status == TimerStatus.Completed && core.Items.Count == 1, "In memory changes survive failure");
                ExpectException<IOException>(delegate { core.Save(); });
                File.Delete(blockedDirectory); core.Save(); Check(String.IsNullOrEmpty(core.LastSaveError), "Successful retry clears error");
                core = NewCore("blocked-directory", clock); Check(core.Countdown.Status == TimerStatus.Completed && core.Items.Count == 1, "Retry persists all accumulated changes");
                Check(core.Tick().Reminders.Count == 0, "Retried reminder marker prevents repetition");
            });
            Console.WriteLine("All " + passed + " tests passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAILED: " + error); return 1; }
        finally
        {
            if (Directory.Exists(TestRoot)) Directory.Delete(TestRoot, true);
        }
    }
}
