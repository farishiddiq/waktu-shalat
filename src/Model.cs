// Data side of Waktu Shalat: settings, Aladhan API, pomodoro timer, to-do store, chime.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace WaktuShalat
{
    class Settings
    {
        public string City = "Jakarta";
        public double Lat = -6.2088, Lon = 106.8456;
        public int Method = 20;
        public bool Notify = true, PreRemind = true, Startup = true, Sound = true, Widget = false, AutoSlim = true;
        public string NotchPos = "center"; // left | center | right
        public double WidgetX = double.NaN, WidgetY = double.NaN;
        public int FocusMin = 25;
        public string PomoDate = "";
        public int PomoDone = 0;

        public static string DirOverride; // used by tests to keep real data untouched

        public static string DataDir
        {
            get
            {
                var d = DirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WaktuShalat");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        static string FilePath { get { return Path.Combine(DataDir, "settings.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            var inv = CultureInfo.InvariantCulture;
            try
            {
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    switch (k)
                    {
                        case "City": s.City = v; break;
                        case "Lat": s.Lat = double.Parse(v, inv); break;
                        case "Lon": s.Lon = double.Parse(v, inv); break;
                        case "Method": s.Method = int.Parse(v, inv); break;
                        case "Notify": s.Notify = v == "1"; break;
                        case "PreRemind": s.PreRemind = v == "1"; break;
                        case "Startup": s.Startup = v == "1"; break;
                        case "Sound": s.Sound = v == "1"; break;
                        case "Widget": s.Widget = v == "1"; break;
                        case "AutoSlim": s.AutoSlim = v == "1"; break;
                        case "NotchPos": s.NotchPos = v; break;
                        case "WidgetX": s.WidgetX = double.Parse(v, inv); break;
                        case "WidgetY": s.WidgetY = double.Parse(v, inv); break;
                        case "FocusMin": s.FocusMin = int.Parse(v, inv); break;
                        case "PomoDate": s.PomoDate = v; break;
                        case "PomoDone": s.PomoDone = int.Parse(v, inv); break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            var inv = CultureInfo.InvariantCulture;
            Func<bool, string> b = x => x ? "1" : "0";
            try
            {
                File.WriteAllLines(FilePath, new[] {
                    "City=" + City, "Lat=" + Lat.ToString(inv), "Lon=" + Lon.ToString(inv),
                    "Method=" + Method.ToString(inv), "Notify=" + b(Notify), "PreRemind=" + b(PreRemind),
                    "Startup=" + b(Startup), "Sound=" + b(Sound), "Widget=" + b(Widget),
                    "AutoSlim=" + b(AutoSlim), "NotchPos=" + NotchPos,
                    "WidgetX=" + WidgetX.ToString(inv), "WidgetY=" + WidgetY.ToString(inv),
                    "FocusMin=" + FocusMin.ToString(inv), "PomoDate=" + PomoDate, "PomoDone=" + PomoDone.ToString(inv) });
            }
            catch { }
        }
    }

    class DayTimes
    {
        public DateTime Date;
        public Dictionary<string, DateTime> Times = new Dictionary<string, DateTime>();
        public string Hijri = "";
    }

    class NextPrayer
    {
        public string Key;
        public DateTime At, Prev;
        public bool Tomorrow;
        public TimeSpan Left;
        public double Progress;
    }

    static class Api
    {
        public static readonly string[] Keys = { "Imsak", "Fajr", "Sunrise", "Dhuhr", "Asr", "Maghrib", "Isha" };
        public static readonly string[] Prayers = { "Fajr", "Dhuhr", "Asr", "Maghrib", "Isha" };

        static readonly string[] HijriMonths = {
            "Muharram", "Safar", "Rabiul Awal", "Rabiul Akhir", "Jumadil Awal", "Jumadil Akhir",
            "Rajab", "Syaban", "Ramadhan", "Syawal", "Dzulkaidah", "Dzulhijjah" };

        public static readonly object[][] Cities = {
            new object[] { "Jakarta", -6.2088, 106.8456 }, new object[] { "Bogor", -6.5950, 106.8166 },
            new object[] { "Depok", -6.4025, 106.7942 }, new object[] { "Tangerang", -6.1783, 106.6319 },
            new object[] { "Bekasi", -6.2383, 106.9756 }, new object[] { "Bandung", -6.9175, 107.6191 },
            new object[] { "Semarang", -6.9667, 110.4167 }, new object[] { "Yogyakarta", -7.7956, 110.3695 },
            new object[] { "Solo", -7.5755, 110.8243 }, new object[] { "Surabaya", -7.2575, 112.7521 },
            new object[] { "Malang", -7.9666, 112.6326 }, new object[] { "Denpasar", -8.6705, 115.2126 },
            new object[] { "Mataram", -8.5833, 116.1167 }, new object[] { "Kupang", -10.1772, 123.6070 },
            new object[] { "Banda Aceh", 5.5483, 95.3238 }, new object[] { "Medan", 3.5952, 98.6722 },
            new object[] { "Padang", -0.9471, 100.4172 }, new object[] { "Pekanbaru", 0.5071, 101.4478 },
            new object[] { "Batam", 1.0456, 104.0305 }, new object[] { "Jambi", -1.6101, 103.6131 },
            new object[] { "Palembang", -2.9761, 104.7754 }, new object[] { "Bandar Lampung", -5.3971, 105.2668 },
            new object[] { "Pontianak", -0.0263, 109.3425 }, new object[] { "Banjarmasin", -3.3186, 114.5944 },
            new object[] { "Balikpapan", -1.2379, 116.8529 }, new object[] { "Samarinda", -0.5022, 117.1536 },
            new object[] { "Makassar", -5.1477, 119.4327 }, new object[] { "Manado", 1.4748, 124.8421 },
            new object[] { "Ambon", -3.6954, 128.1814 }, new object[] { "Jayapura", -2.5337, 140.7181 },
            new object[] { "Kuala Lumpur", 3.1390, 101.6869 }, new object[] { "Singapura", 1.3521, 103.8198 },
            new object[] { "Makkah", 21.4225, 39.8262 }, new object[] { "Madinah", 24.4672, 39.6111 },
        };

        public static readonly object[][] Methods = {
            new object[] { 20, "Kemenag RI" }, new object[] { 3, "Muslim World League" },
            new object[] { 4, "Umm al-Qura, Makkah" }, new object[] { 17, "JAKIM Malaysia" },
            new object[] { 11, "MUIS Singapura" }, new object[] { 5, "Mesir (EGAS)" },
            new object[] { 2, "ISNA Amerika Utara" },
        };

        public static string Name(string k)
        {
            switch (k)
            {
                case "Fajr": return "Subuh";
                case "Sunrise": return "Terbit";
                case "Dhuhr": return "Dzuhur";
                case "Asr": return "Ashar";
                case "Isha": return "Isya";
            }
            return k;
        }

        public static string MethodName(int id)
        {
            foreach (var m in Methods) if ((int)m[0] == id) return (string)m[1];
            return "Metode " + id;
        }

        public static DayTimes Get(DateTime date, Settings s)
        {
            var inv = CultureInfo.InvariantCulture;
            string d = date.ToString("dd-MM-yyyy", inv);
            string dir = Path.Combine(Settings.DataDir, "cache");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, string.Format(inv, "{0}_{1:0.####}_{2:0.####}_{3}.json", d, s.Lat, s.Lon, s.Method));
            if (File.Exists(file))
            {
                try { return Parse(File.ReadAllText(file, Encoding.UTF8), date); }
                catch { File.Delete(file); }
            }
            string url = string.Format(inv, "https://api.aladhan.com/v1/timings/{0}?latitude={1}&longitude={2}&method={3}", d, s.Lat, s.Lon, s.Method);
            string json;
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "WaktuShalat/2.0 (Windows)";
                json = wc.DownloadString(url);
            }
            var result = Parse(json, date);
            File.WriteAllText(file, json, Encoding.UTF8);
            return result;
        }

        static DayTimes Parse(string json, DateTime date)
        {
            var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            var data = (Dictionary<string, object>)root["data"];
            var t = (Dictionary<string, object>)data["timings"];
            var r = new DayTimes { Date = date.Date };
            foreach (var k in Keys)
            {
                var p = ((string)t[k]).Substring(0, 5).Split(':');
                r.Times[k] = date.Date.AddHours(int.Parse(p[0])).AddMinutes(int.Parse(p[1]));
            }
            try
            {
                var h = (Dictionary<string, object>)((Dictionary<string, object>)data["date"])["hijri"];
                int month = Convert.ToInt32(((Dictionary<string, object>)h["month"])["number"]);
                r.Hijri = string.Format("{0} {1} {2} H", Convert.ToInt32(h["day"]), HijriMonths[month - 1], h["year"]);
            }
            catch { }
            return r;
        }

        public static void CleanCache()
        {
            try
            {
                foreach (var f in Directory.GetFiles(Path.Combine(Settings.DataDir, "cache")))
                    if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-10)) File.Delete(f);
            }
            catch { }
        }
    }

    class Pomodoro
    {
        readonly Settings S;
        DateTime endsAt;
        TimeSpan remaining;
        public string Phase = "focus"; // focus | short | long
        public bool Running { get; private set; }
        public int Cycle; // finished focus sessions in the current set of 4

        public Pomodoro(Settings s)
        {
            S = s;
            remaining = Total;
        }

        static string Today { get { return DateTime.Today.ToString("yyyy-MM-dd"); } }

        public TimeSpan Total { get { return TimeSpan.FromMinutes(Phase == "focus" ? S.FocusMin : Phase == "short" ? 5 : 15); } }

        public TimeSpan Remaining
        {
            get
            {
                if (!Running) return remaining;
                var r = endsAt - DateTime.Now;
                return r < TimeSpan.Zero ? TimeSpan.Zero : r;
            }
        }

        public double Progress { get { return 1 - Remaining.TotalSeconds / Total.TotalSeconds; } }
        public bool Idle { get { return !Running && remaining == Total; } }
        public int DoneToday { get { return S.PomoDate == Today ? S.PomoDone : 0; } }
        public string PhaseName { get { return Phase == "focus" ? "Fokus" : Phase == "short" ? "Istirahat" : "Istirahat panjang"; } }

        public void Start()
        {
            if (Running) return;
            if (remaining <= TimeSpan.Zero) remaining = Total;
            endsAt = DateTime.Now + remaining;
            Running = true;
        }

        public void Pause()
        {
            if (!Running) return;
            remaining = Remaining;
            Running = false;
        }

        public void Toggle() { if (Running) Pause(); else Start(); }

        public void Reset()
        {
            Running = false;
            remaining = Total;
        }

        public void SetFocusMinutes(int m)
        {
            S.FocusMin = m;
            S.Save();
            if (Phase == "focus" && !Running) remaining = Total;
        }

        public void Skip() { Advance(false); }

        // Returns "focusDone" / "breakDone" when a phase ends on its own.
        public string Update()
        {
            if (Running && DateTime.Now >= endsAt) return Advance(true);
            return null;
        }

        string Advance(bool natural)
        {
            Running = false;
            if (Phase == "focus")
            {
                if (natural)
                {
                    if (S.PomoDate != Today) { S.PomoDate = Today; S.PomoDone = 0; }
                    S.PomoDone++;
                    S.Save();
                }
                Cycle++;
                Phase = Cycle >= 4 ? "long" : "short";
                if (Cycle >= 4) Cycle = 0;
                remaining = Total;
                if (natural) Start(); // breaks start by themselves; focus waits for a tap
                return natural ? "focusDone" : null;
            }
            Phase = "focus";
            remaining = Total;
            return natural ? "breakDone" : null;
        }
    }

    class TodoItem
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public bool Done { get; set; }
        public bool Focus { get; set; }
        public string DoneOn { get; set; }
        public long Created { get; set; }
    }

    class TodoStore
    {
        public List<TodoItem> Items = new List<TodoItem>();
        public event Action Changed;

        static string FilePath { get { return Path.Combine(Settings.DataDir, "todos.json"); } }
        static string Today { get { return DateTime.Today.ToString("yyyy-MM-dd"); } }

        public static TodoStore Load()
        {
            var s = new TodoStore();
            try
            {
                var items = new JavaScriptSerializer().Deserialize<List<TodoItem>>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (items != null) s.Items = items;
            }
            catch { }
            return s;
        }

        void Save()
        {
            try { File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(Items), Encoding.UTF8); } catch { }
            if (Changed != null) Changed();
        }

        public TodoItem Focused { get { return Items.FirstOrDefault(i => i.Focus && !i.Done); } }
        public List<TodoItem> Active { get { return Items.Where(i => !i.Done).OrderByDescending(i => i.Focus).ThenBy(i => i.Created).ToList(); } }
        public List<TodoItem> Finished { get { return Items.Where(i => i.Done).OrderByDescending(i => i.DoneOn).ThenByDescending(i => i.Created).ToList(); } }
        public int DoneToday { get { return Items.Count(i => i.Done && i.DoneOn == Today); } }

        public void Add(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            Items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N"), Text = text, Created = DateTime.Now.Ticks });
            Save();
        }

        public void Toggle(TodoItem t)
        {
            t.Done = !t.Done;
            t.DoneOn = t.Done ? Today : null;
            if (t.Done) t.Focus = false;
            Save();
        }

        // Only one task can be "the thing" at a time.
        public void SetFocus(TodoItem t, bool on)
        {
            foreach (var i in Items) i.Focus = false;
            t.Focus = on;
            Save();
        }

        public void Remove(TodoItem t) { Items.Remove(t); Save(); }
        public void ClearDone() { Items.RemoveAll(i => i.Done); Save(); }
    }

    // Soft two-note chime, synthesized once.
    static class Chime
    {
        static byte[] wav;

        public static void Play()
        {
            try
            {
                if (wav == null) wav = Make();
                new SoundPlayer(new MemoryStream(wav)).Play();
            }
            catch { }
        }

        static double Note(double t, double f, double start)
        {
            if (t < start) return 0;
            double x = t - start;
            double env = Math.Exp(-x * 3.2) * Math.Min(1, x * 150);
            return env * (Math.Sin(2 * Math.PI * f * x) + 0.25 * Math.Sin(4 * Math.PI * f * x) * Math.Exp(-x * 6));
        }

        static byte[] Make()
        {
            const int rate = 44100;
            int n = (int)(rate * 1.6);
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2);
            w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
            w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate;
                double s = Note(t, 1046.5, 0) + 0.8 * Note(t, 1568.0, 0.18);
                w.Write((short)(Math.Max(-1, Math.Min(1, s * 0.22)) * short.MaxValue));
            }
            w.Flush();
            return ms.ToArray();
        }
    }
}
