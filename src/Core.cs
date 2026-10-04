using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DeskSprout
{
    public enum TimerStatus { Stopped, Running, Paused, Completed }
    public enum PomodoroPhase { Focus, ShortBreak, LongBreak }
    public enum PlanKind { Plan, Schedule }
    public enum RepeatKind { None, Daily, Weekly }

    [DataContract]
    public sealed class WidgetSettings
    {
        [DataMember] public int FocusMinutes { get; set; }
        [DataMember] public int ShortBreakMinutes { get; set; }
        [DataMember] public int LongBreakMinutes { get; set; }
        [DataMember] public int SessionsBeforeLongBreak { get; set; }
        [DataMember] public bool AutoAdvance { get; set; }
        [DataMember] public bool AlwaysOnTop { get; set; }
        [DataMember] public bool Compact { get; set; }
        [DataMember] public bool SoundEnabled { get; set; }
        [DataMember] public bool GlassEnabled { get; set; }
        [DataMember] public string GlassStyle { get; set; }
        [DataMember] public int GlassTint { get; set; }
        [DataMember] public bool HasWindowPosition { get; set; }
        [DataMember] public int WindowX { get; set; }
        [DataMember] public int WindowY { get; set; }
        [DataMember] public string ThemeMode { get; set; }
        [DataMember] public int SelectedTab { get; set; }
        [DataMember] public string FocusTaskId { get; set; }

        public WidgetSettings()
        {
            FocusMinutes = 25; ShortBreakMinutes = 5; LongBreakMinutes = 15;
            SessionsBeforeLongBreak = 4; SoundEnabled = true; GlassEnabled = true; ThemeMode = "system";
            GlassStyle = "clear"; GlassTint = 16;
        }
        [OnDeserializing] private void BeforeRead(StreamingContext context) { GlassEnabled = true; GlassStyle = "clear"; GlassTint = 16; }
    }

    [DataContract]
    public sealed class CountdownState
    {
        [DataMember] public int DurationSeconds { get; set; }
        [DataMember] public double RemainingSeconds { get; set; }
        [DataMember] public TimerStatus Status { get; set; }
        [DataMember] public DateTime? DeadlineUtc { get; set; }
        public CountdownState() { DurationSeconds = 300; RemainingSeconds = 300; }
    }

    [DataContract]
    public sealed class PomodoroState
    {
        [DataMember] public PomodoroPhase Phase { get; set; }
        [DataMember] public PomodoroPhase NextPhase { get; set; }
        [DataMember] public int DurationSeconds { get; set; }
        [DataMember] public double RemainingSeconds { get; set; }
        [DataMember] public TimerStatus Status { get; set; }
        [DataMember] public DateTime? DeadlineUtc { get; set; }
        [DataMember] public int FocusSessionsInCycle { get; set; }
        [DataMember] public string ActiveTaskId { get; set; }
        public PomodoroState() { DurationSeconds = 1500; RemainingSeconds = 1500; }
    }

    [DataContract]
    public sealed class PlanItem
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Title { get; set; }
        [DataMember] public DateTime Date { get; set; }
        [DataMember] public int? MinuteOfDay { get; set; }
        [DataMember] public int? EstimatedMinutes { get; set; }
        [DataMember] public string DetailNote { get; set; }
        [DataMember] public PlanKind Kind { get; set; }
        [DataMember] public RepeatKind Repeat { get; set; }
        [DataMember] public bool Completed { get; set; }
        [DataMember] public DateTime? CompletedDate { get; set; }
        [DataMember] public List<string> CompletedOccurrences { get; set; }
        [DataMember] public string LastReminderOccurrenceKey { get; set; }
        [DataMember] public bool ReminderEnabled { get; set; }
        [DataMember] public int FocusSessions { get; set; }
        [DataMember] public long FocusSeconds { get; set; }
        public int FocusMinutes { get { return (int)(FocusSeconds / 60); } }
        public PlanItem() { CompletedOccurrences = new List<string>(); ReminderEnabled = true; DetailNote = ""; }
        [OnDeserializing] private void BeforeRead(StreamingContext context) { ReminderEnabled = true; }
    }

    [DataContract]
    public sealed class DailyStats
    {
        [DataMember] public string DateKey { get; set; }
        [DataMember] public int FocusSessions { get; set; }
        [DataMember] public long FocusSeconds { get; set; }
        [DataMember] public int CompletedPlans { get; set; }
        public int FocusMinutes { get { return (int)(FocusSeconds / 60); } }
    }

    [DataContract]
    public sealed class SystemState
    {
        [DataMember] public int Version { get; set; }
        [DataMember] public WidgetSettings Settings { get; set; }
        [DataMember] public CountdownState Countdown { get; set; }
        [DataMember] public PomodoroState Pomodoro { get; set; }
        [DataMember] public List<PlanItem> Items { get; set; }
        [DataMember] public List<DailyStats> Statistics { get; set; }
        [DataMember] public ExtrasState Extras { get; set; }
        public SystemState()
        {
            Version = 1; Settings = new WidgetSettings(); Countdown = new CountdownState();
            Pomodoro = new PomodoroState(); Items = new List<PlanItem>(); Statistics = new List<DailyStats>(); Extras = new ExtrasState();
        }
    }

    public sealed class TickResult
    {
        public bool CountdownCompleted { get; internal set; }
        public bool PomodoroCompleted { get; internal set; }
        public PomodoroPhase CompletedPhase { get; internal set; }
        public List<PlanItem> Reminders { get; private set; }
        public TickResult() { Reminders = new List<PlanItem>(); }
    }

    public sealed partial class WidgetCore
    {
        private SystemState state;
        private readonly Func<DateTime> utcNow;
        private readonly TimeZoneInfo zone;
        private readonly string statePath;
        private bool unpreservedCorruptFile;
        public string LoadWarning { get; private set; }
        public string LastSaveError { get; private set; }
        public string StatePath { get { return statePath; } }
        public WidgetSettings Settings { get { return state.Settings; } }
        public CountdownState Countdown { get { return state.Countdown; } }
        public PomodoroState Pomodoro { get { return state.Pomodoro; } }
        public List<PlanItem> Items { get { return state.Items; } }
        public DailyStats TodayStats { get { return StatsFor(LocalNow.Date); } }
        public DateTime LocalNow { get { return TimeZoneInfo.ConvertTimeFromUtc(Now(), zone); } }

        public WidgetCore(string path = null, Func<DateTime> utcNow = null, TimeZoneInfo zone = null)
        {
            this.utcNow = utcNow ?? delegate { return DateTime.UtcNow; };
            this.zone = zone ?? TimeZoneInfo.Local;
            statePath = Path.GetFullPath(path ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskSprout", "state.json"));
            state = Load();
            Normalize();
        }

        private DateTime Now() { return DateTime.SpecifyKind(utcNow(), DateTimeKind.Utc); }
        private static string DateKey(DateTime date) { return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        private static int Clamp(int n, int minimum, int maximum) { return Math.Max(minimum, Math.Min(maximum, n)); }

        private SystemState ReadState(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                var result = new DataContractJsonSerializer(typeof(SystemState)).ReadObject(stream) as SystemState;
                if (result == null || result.Version > 1 || result.Settings == null || result.Countdown == null || result.Pomodoro == null)
                    throw new SerializationException("The state file is missing required data or uses a newer format.");
                return result;
            }
        }

        private SystemState Load()
        {
            if (!File.Exists(statePath)) return new SystemState();
            try { return ReadState(statePath); }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is SerializationException)
                    && !(error is ArgumentException) && !(error is InvalidOperationException)) throw;
                string preservedPath = statePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                    + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                try { File.Copy(statePath, preservedPath, false); }
                catch (IOException) { preservedPath = statePath; unpreservedCorruptFile = File.Exists(statePath); }
                catch (UnauthorizedAccessException) { preservedPath = statePath; unpreservedCorruptFile = File.Exists(statePath); }
                LoadWarning = "原数据未能读取，已保留在：" + preservedPath;
                if (File.Exists(statePath + ".bak"))
                {
                    try { SystemState recovered = ReadState(statePath + ".bak"); LoadWarning += "。已恢复上次备份。"; return recovered; }
                    catch (Exception backupError)
                    {
                        if (!(backupError is IOException) && !(backupError is UnauthorizedAccessException) && !(backupError is SerializationException)
                            && !(backupError is ArgumentException) && !(backupError is InvalidOperationException)) throw;
                    }
                }
                LoadWarning += "。已使用初始设置。";
                return new SystemState();
            }
        }

        private void Normalize()
        {
            Settings.FocusMinutes = Clamp(Settings.FocusMinutes, 1, 180);
            Settings.ShortBreakMinutes = Clamp(Settings.ShortBreakMinutes, 1, 60);
            Settings.LongBreakMinutes = Clamp(Settings.LongBreakMinutes, 1, 120);
            Settings.SessionsBeforeLongBreak = Clamp(Settings.SessionsBeforeLongBreak, 1, 12);
            Settings.SelectedTab = Clamp(Settings.SelectedTab, 0, 2);
            if (Settings.ThemeMode != "light" && Settings.ThemeMode != "dark") Settings.ThemeMode = "system";
            if (Settings.GlassStyle != "acrylic") Settings.GlassStyle = "clear";
            Settings.GlassTint = Clamp(Settings.GlassTint, 0, 80);
            if (state.Items == null) state.Items = new List<PlanItem>();
            if (state.Statistics == null) state.Statistics = new List<DailyStats>();
            state.Items.RemoveAll(delegate(PlanItem item) { return item == null; });
            state.Statistics.RemoveAll(delegate(DailyStats stats) { return stats == null || String.IsNullOrEmpty(stats.DateKey); });
            foreach (PlanItem item in Items)
            {
                if (String.IsNullOrEmpty(item.Id)) item.Id = Guid.NewGuid().ToString("N");
                item.Title = item.Title ?? "未命名计划"; item.Date = item.Date.Date;
                if (item.CompletedOccurrences == null) item.CompletedOccurrences = new List<string>();
                if (item.MinuteOfDay.HasValue) item.MinuteOfDay = Clamp(item.MinuteOfDay.Value, 0, 1439);
                if (item.EstimatedMinutes.HasValue && (item.EstimatedMinutes.Value < 1 || item.EstimatedMinutes.Value > 480)) item.EstimatedMinutes = null;
                item.DetailNote = item.DetailNote ?? "";
                if (item.DetailNote.Length > 4000) item.DetailNote = TruncateExtraText(item.DetailNote, 4000);
                if (!Enum.IsDefined(typeof(RepeatKind), item.Repeat)) item.Repeat = RepeatKind.None;
                if (!Enum.IsDefined(typeof(PlanKind), item.Kind)) item.Kind = PlanKind.Plan;
            }
            NormalizeExtras();
            Countdown.DurationSeconds = Clamp(Countdown.DurationSeconds, 1, 604800);
            Countdown.RemainingSeconds = SanitizeRemaining(Countdown.RemainingSeconds, Countdown.DurationSeconds);
            Pomodoro.DurationSeconds = Clamp(Pomodoro.DurationSeconds, 1, 10800);
            Pomodoro.RemainingSeconds = SanitizeRemaining(Pomodoro.RemainingSeconds, Pomodoro.DurationSeconds);
            Pomodoro.FocusSessionsInCycle = Math.Max(0, Pomodoro.FocusSessionsInCycle);
            if (!Enum.IsDefined(typeof(PomodoroPhase), Pomodoro.Phase)) Pomodoro.Phase = PomodoroPhase.Focus;
            if (!Enum.IsDefined(typeof(PomodoroPhase), Pomodoro.NextPhase)) Pomodoro.NextPhase = PomodoroPhase.Focus;
            if (!Enum.IsDefined(typeof(TimerStatus), Countdown.Status)) Countdown.Status = TimerStatus.Stopped;
            if (!Enum.IsDefined(typeof(TimerStatus), Pomodoro.Status)) Pomodoro.Status = TimerStatus.Stopped;
            if (Countdown.Status == TimerStatus.Running && !Countdown.DeadlineUtc.HasValue) Countdown.Status = TimerStatus.Paused;
            if (Pomodoro.Status == TimerStatus.Running && !Pomodoro.DeadlineUtc.HasValue) Pomodoro.Status = TimerStatus.Paused;
        }

        private static double SanitizeRemaining(double seconds, int duration)
        {
            return Double.IsNaN(seconds) || Double.IsInfinity(seconds) ? duration : Math.Max(0, Math.Min(duration, seconds));
        }

        public void Save()
        {
            if (unpreservedCorruptFile && File.Exists(statePath))
                throw new IOException("无法保留损坏的原数据，因此暂停保存。请先备份或移走：" + statePath);
            Normalize();
            string directory = Path.GetDirectoryName(statePath);
            Directory.CreateDirectory(directory);
            string temporaryPath = statePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(SystemState)).WriteObject(stream, state);
                    stream.Flush(true);
                }
                if (File.Exists(statePath)) File.Replace(temporaryPath, statePath, statePath + ".bak", true);
                else File.Move(temporaryPath, statePath);
            }
            finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            LastSaveError = null;
        }

        private void SaveAfterChange()
        {
            try { Save(); }
            catch (IOException error) { LastSaveError = error.Message; }
            catch (UnauthorizedAccessException error) { LastSaveError = error.Message; }
        }

        public void ConfigureCountdown(int seconds)
        {
            if (seconds < 1 || seconds > 604800) throw new ArgumentOutOfRangeException("seconds", "计时长度需在 1 秒至 7 天之间。");
            Countdown.DurationSeconds = seconds; ResetCountdown();
        }
        public void StartCountdown()
        {
            Countdown.RemainingSeconds = Countdown.DurationSeconds; Countdown.Status = TimerStatus.Running;
            Countdown.DeadlineUtc = Now().AddSeconds(Countdown.RemainingSeconds); SaveAfterChange();
        }
        public void PauseCountdown()
        {
            if (Countdown.Status != TimerStatus.Running) return;
            Countdown.RemainingSeconds = Math.Max(0, (Countdown.DeadlineUtc.Value.ToUniversalTime() - Now()).TotalSeconds);
            if (Countdown.RemainingSeconds <= 0) return;
            Countdown.Status = TimerStatus.Paused; Countdown.DeadlineUtc = null; SaveAfterChange();
        }
        public void ResumeCountdown()
        {
            if (Countdown.Status != TimerStatus.Paused) return;
            Countdown.Status = TimerStatus.Running; Countdown.DeadlineUtc = Now().AddSeconds(Countdown.RemainingSeconds); SaveAfterChange();
        }
        public void ResetCountdown()
        {
            Countdown.Status = TimerStatus.Stopped; Countdown.DeadlineUtc = null;
            Countdown.RemainingSeconds = Countdown.DurationSeconds; SaveAfterChange();
        }

        public int GetPomodoroDuration(PomodoroPhase phase)
        {
            switch (phase)
            {
                case PomodoroPhase.ShortBreak: return Clamp(Settings.ShortBreakMinutes, 1, 60) * 60;
                case PomodoroPhase.LongBreak: return Clamp(Settings.LongBreakMinutes, 1, 120) * 60;
                default: return Clamp(Settings.FocusMinutes, 1, 180) * 60;
            }
        }
        private void PreparePhase(PomodoroPhase phase)
        {
            Pomodoro.Phase = phase; Pomodoro.NextPhase = phase;
            Pomodoro.DurationSeconds = GetPomodoroDuration(phase); Pomodoro.RemainingSeconds = Pomodoro.DurationSeconds;
            Pomodoro.DeadlineUtc = null; Pomodoro.Status = TimerStatus.Stopped; Pomodoro.ActiveTaskId = null;
        }
        public void StartPomodoro()
        {
            if (Pomodoro.Status == TimerStatus.Running) return;
            if (Pomodoro.Status == TimerStatus.Paused) { ResumePomodoro(); return; }
            if (Pomodoro.Status == TimerStatus.Completed) PreparePhase(Pomodoro.NextPhase);
            else PreparePhase(Pomodoro.Phase);
            Pomodoro.Status = TimerStatus.Running; Pomodoro.DeadlineUtc = Now().AddSeconds(Pomodoro.RemainingSeconds);
            if (Pomodoro.Phase == PomodoroPhase.Focus) Pomodoro.ActiveTaskId = Settings.FocusTaskId;
            SaveAfterChange();
        }
        public void PausePomodoro()
        {
            if (Pomodoro.Status != TimerStatus.Running) return;
            Pomodoro.RemainingSeconds = Math.Max(0, (Pomodoro.DeadlineUtc.Value.ToUniversalTime() - Now()).TotalSeconds);
            if (Pomodoro.RemainingSeconds <= 0) return;
            Pomodoro.Status = TimerStatus.Paused; Pomodoro.DeadlineUtc = null; SaveAfterChange();
        }
        public void ResumePomodoro()
        {
            if (Pomodoro.Status != TimerStatus.Paused) return;
            Pomodoro.Status = TimerStatus.Running; Pomodoro.DeadlineUtc = Now().AddSeconds(Pomodoro.RemainingSeconds); SaveAfterChange();
        }
        public void ResetPomodoro()
        {
            Pomodoro.FocusSessionsInCycle = 0; PreparePhase(PomodoroPhase.Focus); SaveAfterChange();
        }
        private PomodoroPhase NextAfter(PomodoroPhase phase)
        {
            if (phase != PomodoroPhase.Focus) return PomodoroPhase.Focus;
            return Pomodoro.FocusSessionsInCycle >= Clamp(Settings.SessionsBeforeLongBreak, 1, 12)
                ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
        }
        public void SkipPomodoro()
        {
            if (Pomodoro.Phase == PomodoroPhase.LongBreak) Pomodoro.FocusSessionsInCycle = 0;
            PreparePhase(Pomodoro.Status == TimerStatus.Completed ? Pomodoro.NextPhase : NextAfter(Pomodoro.Phase));
            SaveAfterChange();
        }

        public TickResult Tick()
        {
            TickResult result = new TickResult(); DateTime now = Now(); bool changed = false;
            if (Countdown.Status == TimerStatus.Running && Countdown.DeadlineUtc.HasValue)
            {
                Countdown.RemainingSeconds = Math.Max(0, (Countdown.DeadlineUtc.Value.ToUniversalTime() - now).TotalSeconds);
                if (Countdown.RemainingSeconds <= 0)
                {
                    Countdown.Status = TimerStatus.Completed; Countdown.DeadlineUtc = null;
                    result.CountdownCompleted = true; changed = true;
                }
            }
            if (Pomodoro.Status == TimerStatus.Running && Pomodoro.DeadlineUtc.HasValue)
            {
                DateTime finishedAt = Pomodoro.DeadlineUtc.Value.ToUniversalTime();
                Pomodoro.RemainingSeconds = Math.Max(0, (finishedAt - now).TotalSeconds);
                if (Pomodoro.RemainingSeconds <= 0)
                {
                    result.PomodoroCompleted = true; result.CompletedPhase = Pomodoro.Phase;
                    if (Pomodoro.Phase == PomodoroPhase.Focus)
                    {
                        Pomodoro.FocusSessionsInCycle++;
                        DailyStats stats = StatsFor(TimeZoneInfo.ConvertTimeFromUtc(finishedAt, zone).Date);
                        stats.FocusSessions++; stats.FocusSeconds += Pomodoro.DurationSeconds;
                        PlanItem task = FindItem(Pomodoro.ActiveTaskId);
                        if (task != null) { task.FocusSessions++; task.FocusSeconds += Pomodoro.DurationSeconds; }
                    }
                    else if (Pomodoro.Phase == PomodoroPhase.LongBreak) Pomodoro.FocusSessionsInCycle = 0;
                    Pomodoro.NextPhase = NextAfter(Pomodoro.Phase);
                    Pomodoro.Status = TimerStatus.Completed; Pomodoro.DeadlineUtc = null; changed = true;
                    if (Settings.AutoAdvance)
                    {
                        PreparePhase(Pomodoro.NextPhase); Pomodoro.Status = TimerStatus.Running;
                        Pomodoro.DeadlineUtc = now.AddSeconds(Pomodoro.RemainingSeconds);
                        if (Pomodoro.Phase == PomodoroPhase.Focus) Pomodoro.ActiveTaskId = Settings.FocusTaskId;
                    }
                }
            }
            DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(now, zone);
            foreach (PlanItem item in Items)
            {
                if (!item.ReminderEnabled || !item.MinuteOfDay.HasValue) continue;
                DateTime occurrenceDate = item.Repeat == RepeatKind.None ? item.Date.Date : localNow.Date;
                if (!IsDueOn(item, occurrenceDate) || IsCompletedOn(item, occurrenceDate)) continue;
                if (occurrenceDate.AddMinutes(item.MinuteOfDay.Value) > localNow) continue;
                string reminderKey = DateKey(occurrenceDate) + ":" + item.MinuteOfDay.Value.ToString(CultureInfo.InvariantCulture);
                if (item.LastReminderOccurrenceKey == reminderKey) continue;
                item.LastReminderOccurrenceKey = reminderKey; result.Reminders.Add(item); changed = true;
            }
            if (changed) SaveAfterChange();
            return result;
        }

        private DailyStats StatsFor(DateTime localDate)
        {
            string key = DateKey(localDate);
            foreach (DailyStats stats in state.Statistics) if (stats.DateKey == key) return stats;
            DailyStats created = new DailyStats { DateKey = key }; state.Statistics.Add(created); return created;
        }
        private PlanItem FindItem(string id)
        {
            if (String.IsNullOrEmpty(id)) return null;
            return Items.Find(delegate(PlanItem item) { return item.Id == id; });
        }
        private PlanItem RequireItem(string id)
        {
            PlanItem item = FindItem(id); if (item == null) throw new ArgumentException("计划不存在。", "id"); return item;
        }
        private static string ValidateTitle(string title)
        {
            title = (title ?? "").Trim();
            if (title.Length == 0 || title.Length > 200) throw new ArgumentException("标题需包含 1 至 200 个字符。", "title");
            return title;
        }
        private static void ValidateSchedule(int? minuteOfDay, PlanKind kind, RepeatKind repeat)
        {
            if (minuteOfDay.HasValue && (minuteOfDay < 0 || minuteOfDay > 1439)) throw new ArgumentOutOfRangeException("minuteOfDay");
            if (!Enum.IsDefined(typeof(PlanKind), kind)) throw new ArgumentOutOfRangeException("kind");
            if (!Enum.IsDefined(typeof(RepeatKind), repeat)) throw new ArgumentOutOfRangeException("repeat");
        }
        public PlanItem AddItem(string title, DateTime localDate, int? minuteOfDay, PlanKind kind, RepeatKind repeat = RepeatKind.None, bool reminderEnabled = true)
        {
            title = ValidateTitle(title); ValidateSchedule(minuteOfDay, kind, repeat);
            PlanItem item = new PlanItem { Id = Guid.NewGuid().ToString("N"), Title = title, Date = localDate.Date,
                MinuteOfDay = minuteOfDay, Kind = kind, Repeat = repeat, ReminderEnabled = reminderEnabled };
            Items.Add(item); SaveAfterChange(); return item;
        }
        public void UpdateItem(string id, string title, DateTime localDate, int? minuteOfDay, PlanKind kind, RepeatKind repeat = RepeatKind.None, bool reminderEnabled = true)
        {
            title = ValidateTitle(title); ValidateSchedule(minuteOfDay, kind, repeat); PlanItem item = RequireItem(id);
            bool priorityScheduleChanged = item.Date.Date != localDate.Date || item.Repeat != repeat || item.Kind != kind;
            bool occurrenceChanged = item.Date.Date != localDate.Date || item.MinuteOfDay != minuteOfDay || item.Repeat != repeat || item.Kind != kind
                || item.ReminderEnabled != reminderEnabled;
            item.Title = title; item.Date = localDate.Date; item.MinuteOfDay = minuteOfDay; item.Kind = kind;
            if (item.Repeat != repeat)
            {
                item.Completed = false; item.CompletedDate = null; item.CompletedOccurrences.Clear();
            }
            item.Repeat = repeat; item.ReminderEnabled = reminderEnabled;
            if (priorityScheduleChanged) RemoveIneligibleEditedPriorities(item);
            if (occurrenceChanged) item.LastReminderOccurrenceKey = null; SaveAfterChange();
        }
        public bool IsDueOn(PlanItem item, DateTime date)
        {
            date = date.Date; if (date < item.Date.Date) return false;
            if (item.Repeat == RepeatKind.Daily) return true;
            if (item.Repeat == RepeatKind.Weekly) return date.DayOfWeek == item.Date.DayOfWeek;
            return date == item.Date.Date;
        }
        public bool IsCompletedOn(PlanItem item, DateTime date)
        {
            if (item.Repeat == RepeatKind.None) return item.Completed;
            return item.CompletedOccurrences.Contains(DateKey(date.Date));
        }
        public void SetCompleted(string id, bool completed) { SetCompleted(id, completed, LocalNow.Date); }
        public void SetCompleted(string id, bool completed, DateTime occurrenceDate)
        {
            PlanItem item = RequireItem(id); occurrenceDate = occurrenceDate.Date;
            if (item.Repeat == RepeatKind.None)
            {
                if (item.Completed == completed) return;
                if (completed) { item.CompletedDate = occurrenceDate; StatsFor(occurrenceDate).CompletedPlans++; }
                else if (item.CompletedDate.HasValue)
                {
                    DailyStats previous = StatsFor(item.CompletedDate.Value); previous.CompletedPlans = Math.Max(0, previous.CompletedPlans - 1);
                    item.CompletedDate = null;
                }
                item.Completed = completed;
            }
            else
            {
                if (!IsDueOn(item, occurrenceDate)) throw new ArgumentException("这一天没有该重复计划。", "occurrenceDate");
                string key = DateKey(occurrenceDate); bool previous = item.CompletedOccurrences.Contains(key);
                if (previous == completed) return;
                DailyStats stats = StatsFor(occurrenceDate);
                if (completed) { item.CompletedOccurrences.Add(key); stats.CompletedPlans++; }
                else { item.CompletedOccurrences.Remove(key); stats.CompletedPlans = Math.Max(0, stats.CompletedPlans - 1); }
            }
            SaveAfterChange();
        }
        public void RemoveItem(string id)
        {
            PlanItem item = RequireItem(id); Items.Remove(item);
            RemoveTaskExtras(id);
            if (Settings.FocusTaskId == id) Settings.FocusTaskId = null; SaveAfterChange();
        }
    }
}
