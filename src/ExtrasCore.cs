using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace DeskSprout
{
    [DataContract]
    public sealed class ExtrasState
    {
        [DataMember] public List<QuickNote> Notes { get; set; }
        [DataMember] public List<ImportantDay> ImportantDays { get; set; }
        [DataMember] public List<DailyExtraRecord> Days { get; set; }
        [DataMember] public List<ResumeNote> ResumeNotes { get; set; }
        [DataMember] public List<string> ImportedLearningPacks { get; set; }
        public ExtrasState()
        {
            Notes = new List<QuickNote>(); ImportantDays = new List<ImportantDay>();
            Days = new List<DailyExtraRecord>(); ResumeNotes = new List<ResumeNote>();
            ImportedLearningPacks = new List<string>();
        }
    }

    [DataContract]
    public sealed class QuickNote
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Text { get; set; }
        [DataMember] public DateTime CreatedAtUtc { get; set; }
    }

    [DataContract]
    public sealed class ImportantDay
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Title { get; set; }
        [DataMember] public DateTime Date { get; set; }
        [DataMember] public bool Annual { get; set; }
    }

    [DataContract]
    public sealed class DailyExtraRecord
    {
        [DataMember] public string DateKey { get; set; }
        [DataMember] public List<string> PriorityTaskIds { get; set; }
        [DataMember] public string Mood { get; set; }
        [DataMember] public string Reflection { get; set; }
        [DataMember] public bool PriorityCelebrated { get; set; }
        [DataMember] public bool SummaryPrompted { get; set; }
        public DailyExtraRecord() { PriorityTaskIds = new List<string>(); Mood = ""; Reflection = ""; }
    }

    [DataContract]
    public sealed class ResumeNote
    {
        [DataMember] public string TaskId { get; set; }
        [DataMember] public string Text { get; set; }
    }

    public sealed partial class WidgetCore
    {
        public ExtrasState Extras { get { return state.Extras; } }

        private void NormalizeExtras()
        {
            if (state.Extras == null) state.Extras = new ExtrasState();
            if (Extras.Notes == null) Extras.Notes = new List<QuickNote>();
            if (Extras.ImportantDays == null) Extras.ImportantDays = new List<ImportantDay>();
            if (Extras.Days == null) Extras.Days = new List<DailyExtraRecord>();
            if (Extras.ResumeNotes == null) Extras.ResumeNotes = new List<ResumeNote>();
            if (Extras.ImportedLearningPacks == null) Extras.ImportedLearningPacks = new List<string>();

            HashSet<string> noteIds = new HashSet<string>(StringComparer.Ordinal);
            Extras.Notes.RemoveAll(delegate(QuickNote note)
            {
                if (note == null || String.IsNullOrWhiteSpace(note.Text)) return true;
                if (String.IsNullOrEmpty(note.Id)) note.Id = Guid.NewGuid().ToString("N");
                if (!noteIds.Add(note.Id)) return true;
                note.Text = note.Text.Trim();
                if (note.Text.Length > 2000) note.Text = TruncateExtraText(note.Text, 2000);
                note.CreatedAtUtc = DateTime.SpecifyKind(note.CreatedAtUtc.ToUniversalTime(), DateTimeKind.Utc);
                return false;
            });
            HashSet<string> dayIds = new HashSet<string>(StringComparer.Ordinal);
            Extras.ImportantDays.RemoveAll(delegate(ImportantDay day)
            {
                if (day == null || String.IsNullOrWhiteSpace(day.Title)) return true;
                if (String.IsNullOrEmpty(day.Id)) day.Id = Guid.NewGuid().ToString("N");
                if (!dayIds.Add(day.Id)) return true;
                day.Title = day.Title.Trim();
                if (day.Title.Length > 200) day.Title = TruncateExtraText(day.Title, 200);
                day.Date = day.Date.Date;
                return false;
            });
            HashSet<string> dailyKeys = new HashSet<string>(StringComparer.Ordinal);
            Extras.Days.RemoveAll(delegate(DailyExtraRecord record)
            {
                DateTime date;
                if (record == null || !DateTime.TryParseExact(record.DateKey, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
                record.DateKey = DateKey(date);
                if (!dailyKeys.Add(record.DateKey)) return true;
                if (record.PriorityTaskIds == null) record.PriorityTaskIds = new List<string>();
                HashSet<string> priorities = new HashSet<string>(StringComparer.Ordinal);
                record.PriorityTaskIds.RemoveAll(delegate(string id)
                {
                    return String.IsNullOrEmpty(id) || FindItem(id) == null || !priorities.Add(id);
                });
                if (record.PriorityTaskIds.Count > 3) record.PriorityTaskIds.RemoveRange(3, record.PriorityTaskIds.Count - 3);
                record.Mood = NormalizeMood(record.Mood, false);
                record.Reflection = record.Reflection ?? "";
                if (record.Reflection.Length > 2000) record.Reflection = TruncateExtraText(record.Reflection, 2000);
                return false;
            });
            HashSet<string> resumeKeys = new HashSet<string>(StringComparer.Ordinal);
            Extras.ResumeNotes.RemoveAll(delegate(ResumeNote note)
            {
                if (note == null || String.IsNullOrWhiteSpace(note.Text)) return true;
                note.TaskId = (note.TaskId ?? "").Trim();
                if (note.TaskId.Length > 0 && FindItem(note.TaskId) == null) return true;
                if (!resumeKeys.Add(note.TaskId)) return true;
                note.Text = note.Text.Trim();
                if (note.Text.Length > 500) note.Text = TruncateExtraText(note.Text, 500);
                return false;
            });
            HashSet<string> imports = new HashSet<string>(StringComparer.Ordinal);
            Extras.ImportedLearningPacks.RemoveAll(delegate(string id)
            {
                return String.IsNullOrWhiteSpace(id) || !imports.Add(id);
            });
        }

        private static string TruncateExtraText(string text, int maximum)
        {
            if (text.Length <= maximum) return text;
            if (maximum > 0 && Char.IsHighSurrogate(text[maximum - 1]) && Char.IsLowSurrogate(text[maximum])) maximum--;
            return text.Substring(0, maximum);
        }

        private void RemoveIneligibleEditedPriorities(PlanItem item)
        {
            foreach (DailyExtraRecord record in Extras.Days)
            {
                DateTime day;
                if (!DateTime.TryParseExact(record.DateKey, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out day)) continue;
                bool eligible = item.Kind == PlanKind.Plan && item.Date.Date <= day.Date &&
                    (item.Repeat == RepeatKind.None || IsDueOn(item, day));
                if (!eligible && record.PriorityTaskIds.RemoveAll(delegate(string id) { return id == item.Id; }) > 0)
                    record.PriorityCelebrated = false;
            }
        }

        private void RemoveTaskExtras(string taskId)
        {
            Extras.ResumeNotes.RemoveAll(delegate(ResumeNote note) { return note.TaskId == taskId; });
            foreach (DailyExtraRecord record in Extras.Days)
                if (record.PriorityTaskIds.RemoveAll(delegate(string id) { return id == taskId; }) > 0)
                    record.PriorityCelebrated = false;
        }

        private bool IsActionablePlan(PlanItem item, DateTime day)
        {
            return item.Kind == PlanKind.Plan && !IsCompletedOn(item, day) &&
                (IsDueOn(item, day) || (item.Repeat == RepeatKind.None && item.Date.Date < day.Date));
        }

        public List<PlanItem> ActionablePlans(DateTime day, bool quickOnly = false)
        {
            day = day.Date;
            List<PlanItem> result = new List<PlanItem>();
            foreach (PlanItem item in Items)
                if (IsActionablePlan(item, day) && (!quickOnly ||
                    (item.EstimatedMinutes.HasValue && item.EstimatedMinutes.Value >= 1 && item.EstimatedMinutes.Value <= 15)))
                    result.Add(item);
            result.Sort(delegate(PlanItem a, PlanItem b)
            {
                int comparison = a.Date.CompareTo(b.Date);
                if (comparison != 0) return comparison;
                comparison = (a.MinuteOfDay ?? 1440).CompareTo(b.MinuteOfDay ?? 1440);
                return comparison != 0 ? comparison : String.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
            });
            return result;
        }

        public PlanItem DrawNextTask(bool quickOnly, Random random)
        {
            if (random == null) throw new ArgumentNullException("random");
            List<PlanItem> candidates = ActionablePlans(LocalNow.Date, quickOnly);
            return candidates.Count == 0 ? null : candidates[random.Next(candidates.Count)];
        }

        private static void ValidateEstimate(int? minutes)
        {
            if (minutes.HasValue && (minutes.Value < 1 || minutes.Value > 480))
                throw new ArgumentOutOfRangeException("minutes", "预计时长需为 1 至 480 分钟，也可以留空。");
        }

        public void SetEstimatedMinutes(string id, int? minutes)
        {
            ValidateEstimate(minutes); RequireItem(id).EstimatedMinutes = minutes; SaveAfterChange();
        }

        public void SetTaskDetail(string id, string text)
        {
            text = text ?? "";
            if (text.Length > 4000) throw new ArgumentException("任务说明最多 4000 个字符。", "text");
            RequireItem(id).DetailNote = text; SaveAfterChange();
        }

        public DailyExtraRecord ExtrasFor(DateTime date)
        {
            string key = DateKey(date.Date);
            foreach (DailyExtraRecord record in Extras.Days) if (record.DateKey == key) return record;
            DailyExtraRecord created = new DailyExtraRecord { DateKey = key };
            Extras.Days.Add(created); return created;
        }

        private bool CanPrioritize(PlanItem item, DateTime day)
        {
            if (item.Kind != PlanKind.Plan || item.Date.Date > day.Date) return false;
            if (IsActionablePlan(item, day)) return true;
            if (item.Repeat != RepeatKind.None) return IsDueOn(item, day) && IsCompletedOn(item, day);
            return item.Completed && item.CompletedDate.HasValue && item.CompletedDate.Value.Date == day.Date;
        }

        public void SetPriorities(DateTime day, IEnumerable<string> ids)
        {
            day = day.Date;
            List<string> selection = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (ids != null) foreach (string id in ids)
            {
                if (String.IsNullOrEmpty(id)) throw new ArgumentException("优先事项不能为空。", "ids");
                if (!seen.Add(id)) continue;
                PlanItem item = RequireItem(id);
                if (!CanPrioritize(item, day)) throw new ArgumentException("只能选择这一天可做或已完成的计划。", "ids");
                selection.Add(id);
                if (selection.Count > 3) throw new ArgumentException("每天最多选择三项优先计划。", "ids");
            }
            DailyExtraRecord record = ExtrasFor(day);
            bool changed = selection.Count != record.PriorityTaskIds.Count;
            for (int i = 0; !changed && i < selection.Count; i++)
                changed = selection[i] != record.PriorityTaskIds[i];
            record.PriorityTaskIds = selection;
            if (changed) record.PriorityCelebrated = false;
            SaveAfterChange();
        }

        public List<PlanItem> PrioritiesFor(DateTime day)
        {
            List<PlanItem> result = new List<PlanItem>();
            foreach (string id in ExtrasFor(day).PriorityTaskIds)
            {
                PlanItem item = FindItem(id); if (item != null) result.Add(item);
            }
            return result;
        }

        public string NextStepFor(string taskId)
        {
            string key = (taskId ?? "").Trim();
            ResumeNote note = Extras.ResumeNotes.Find(delegate(ResumeNote value) { return value.TaskId == key; });
            return note == null ? "" : note.Text;
        }

        public void SetNextStep(string taskId, string text)
        {
            string key = (taskId ?? "").Trim(); text = (text ?? "").Trim();
            if (text.Length > 500) throw new ArgumentException("下一步最多 500 个字符。", "text");
            if (key.Length > 0) RequireItem(key);
            ResumeNote note = Extras.ResumeNotes.Find(delegate(ResumeNote value) { return value.TaskId == key; });
            if (text.Length == 0)
            {
                if (note != null) Extras.ResumeNotes.Remove(note);
            }
            else if (note == null) Extras.ResumeNotes.Add(new ResumeNote { TaskId = key, Text = text });
            else note.Text = text;
            SaveAfterChange();
        }

        public QuickNote AddQuickNote(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0 || text.Length > 2000) throw new ArgumentException("随手记需包含 1 至 2000 个字符。", "text");
            QuickNote note = new QuickNote { Id = Guid.NewGuid().ToString("N"), Text = text, CreatedAtUtc = Now() };
            Extras.Notes.Add(note); SaveAfterChange(); return note;
        }

        public void RemoveQuickNote(string id)
        {
            QuickNote note = Extras.Notes.Find(delegate(QuickNote value) { return value.Id == id; });
            if (note == null) throw new ArgumentException("随手记不存在。", "id");
            Extras.Notes.Remove(note); SaveAfterChange();
        }

        public PlanItem ConvertQuickNote(string id, DateTime day, PlanKind kind, int? minute, int? estimate)
        {
            QuickNote note = Extras.Notes.Find(delegate(QuickNote value) { return value.Id == id; });
            if (note == null) throw new ArgumentException("随手记不存在。", "id");
            ValidateSchedule(minute, kind, RepeatKind.None); ValidateEstimate(estimate);
            string title = "";
            foreach (string line in note.Text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (!String.IsNullOrWhiteSpace(line)) { title = line.Trim(); break; }
            if (title.Length > 120) title = TruncateExtraText(title, 120);
            title = ValidateTitle(title);
            PlanItem item = new PlanItem { Id = Guid.NewGuid().ToString("N"), Title = title, DetailNote = note.Text,
                Date = day.Date, Kind = kind, Repeat = RepeatKind.None, MinuteOfDay = minute, EstimatedMinutes = estimate };
            Items.Add(item); Extras.Notes.Remove(note); SaveAfterChange(); return item;
        }

        public ImportantDay AddImportantDay(string title, DateTime date, bool annual)
        {
            title = ValidateTitle(title);
            ImportantDay day = new ImportantDay { Id = Guid.NewGuid().ToString("N"), Title = title, Date = date.Date, Annual = annual };
            Extras.ImportantDays.Add(day); SaveAfterChange(); return day;
        }

        public void RemoveImportantDay(string id)
        {
            ImportantDay day = Extras.ImportantDays.Find(delegate(ImportantDay value) { return value.Id == id; });
            if (day == null) throw new ArgumentException("重要日不存在。", "id");
            Extras.ImportantDays.Remove(day); SaveAfterChange();
        }

        private static DateTime ImportantOccurrence(ImportantDay day, int year)
        {
            return new DateTime(year, day.Date.Month, Math.Min(day.Date.Day, DateTime.DaysInMonth(year, day.Date.Month)));
        }

        public DateTime NextImportantOccurrence(ImportantDay day, DateTime from)
        {
            if (day == null) throw new ArgumentNullException("day");
            if (!day.Annual) return day.Date.Date;
            from = from.Date;
            DateTime occurrence = ImportantOccurrence(day, from.Year);
            if (occurrence >= from) return occurrence;
            if (from.Year == 9999) throw new ArgumentOutOfRangeException("from", "已超出可表示的年度日期。");
            return ImportantOccurrence(day, from.Year + 1);
        }

        private static string NormalizeMood(string mood, bool validate)
        {
            mood = (mood ?? "").Trim();
            if (mood == "" || mood == "开心" || mood == "平静" || mood == "充实" || mood == "疲惫" || mood == "低落") return mood;
            if (validate) throw new ArgumentException("请选择列表中的心情，也可以留空。", "mood");
            return "";
        }

        public void SaveReflection(DateTime date, string mood, string text)
        {
            mood = NormalizeMood(mood, true); text = (text ?? "").Trim();
            if (text.Length > 2000) throw new ArgumentException("每日小结最多 2000 个字符。", "text");
            DailyExtraRecord record = ExtrasFor(date); record.Mood = mood; record.Reflection = text; SaveAfterChange();
        }

        public DailyStats StatsOn(DateTime day) { return StatsFor(day.Date); }
    }
}