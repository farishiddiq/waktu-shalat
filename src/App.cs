// Waktu Shalat — prayer times, pomodoro and to-do in a Dynamic Island for Windows.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace WaktuShalat
{
    static class Core
    {
        public static Settings S;
        public static TodoStore Todos;
        public static Pomodoro Pomo;
        public static DayTimes Today, Tomorrow;
        public static string Status = "";
        public static IslandWindow Island;
        public static WidgetWindow Widget;
        public static readonly CultureInfo Id = new CultureInfo("id-ID");
        public static event Action Ticked, DataChanged;
        internal static DispatcherTimer ForegroundTimer;

        static Forms.NotifyIcon tray;
        static Forms.ToolStripMenuItem trayInfo, trayWidget;
        static DateTime loadedFor = DateTime.MinValue, retryAt = DateTime.MinValue;
        static bool loading, reloadPending;
        static readonly HashSet<string> fired = new HashSet<string>();
        static readonly string ExePath = Assembly.GetExecutingAssembly().Location;

        public static void Init()
        {
            S = Settings.Load();
            Todos = TodoStore.Load();
            Pomo = new Pomodoro(S);
            BuildTray();
            Island = new IslandWindow();
            Island.Show();
            if (S.Widget) SetWidget(true);
            ApplyStartup();
            Api.CleanCache();
            Reload();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) => OnTick();
            timer.Start();
            int fg = 0;
            var fs = ForegroundTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            fs.Tick += (s, e) =>
            {
                int st = Native.ForegroundState(Island.NotchRect());
                if (st >= 0) fg = st;
                Island.SetHidden(fg == 2);
                Island.SetSlim(S.AutoSlim && fg == 1);
            };
            fs.Start();
        }

        public static async void Reload()
        {
            if (loading) { reloadPending = true; return; }
            loading = true;
            Status = "Memuat jadwal…";
            var day = DateTime.Today;
            var s = S;
            try
            {
                var res = await Task.Run(() => new[] { Api.Get(day, s), Api.Get(day.AddDays(1), s) });
                Today = res[0]; Tomorrow = res[1]; loadedFor = day;
                Status = Api.MethodName(S.Method) + "  ·  aladhan.com";
            }
            catch
            {
                retryAt = DateTime.Now.AddSeconds(30);
                Status = "Gagal memuat — periksa koneksi internet";
            }
            loading = false;
            if (DataChanged != null) DataChanged();
            if (reloadPending) { reloadPending = false; Reload(); }
        }

        public static NextPrayer Next(DateTime now)
        {
            if (Today == null || Today.Date != now.Date) return null;
            var n = new NextPrayer();
            foreach (var k in Api.Prayers)
                if (Today.Times[k] > now) { n.Key = k; n.At = Today.Times[k]; break; }
            if (n.Key == null)
            {
                n.Key = "Fajr";
                n.Tomorrow = true;
                n.At = Tomorrow != null ? Tomorrow.Times["Fajr"] : Today.Times["Fajr"].AddDays(1);
            }
            n.Prev = Today.Times["Isha"].AddDays(-1);
            foreach (var k in Api.Prayers)
                if (Today.Times[k] <= now) n.Prev = Today.Times[k];
            n.Left = n.At - now;
            n.Progress = Math.Max(0, Math.Min(1, (now - n.Prev).TotalSeconds / (n.At - n.Prev).TotalSeconds));
            return n;
        }

        static int Secs(TimeSpan t) { return Math.Max(0, (int)Math.Ceiling(t.TotalSeconds)); }

        public static string Hms(TimeSpan t)
        {
            int s = Secs(t);
            return string.Format("{0:00}:{1:00}:{2:00}", s / 3600, s / 60 % 60, s % 60);
        }

        // 1:23:45 or 23:45
        public static string Short(TimeSpan t)
        {
            int s = Secs(t);
            return s >= 3600 ? string.Format("{0}:{1:00}:{2:00}", s / 3600, s / 60 % 60, s % 60) : string.Format("{0:00}:{1:00}", s / 60, s % 60);
        }

        static void OnTick()
        {
            var now = DateTime.Now;
            if (now.Date != loadedFor && !loading && now >= retryAt) Reload();

            var ev = Pomo.Update();
            if (ev == "focusDone")
                Alert("", Ui.Orange, "Sesi fokus selesai!", "Istirahat " + (int)Pomo.Total.TotalMinutes + " menit. Kamu keren.", 1);
            else if (ev == "breakDone")
                Alert("", Ui.GreenBrush, "Istirahat selesai", "Siap fokus lagi? Ketuk untuk mulai.", 1);

            var n = Next(now);
            if (n != null)
            {
                string tip = string.Format("{0} {1} · {2} lagi", Api.Name(n.Key), n.At.ToString("HH:mm"), Short(n.Left));
                if (tray.Text != tip) { tray.Text = tip; trayInfo.Text = tip; }
                CheckPrayers(now);
            }
            if (Ticked != null) Ticked();
        }

        static void CheckPrayers(DateTime now)
        {
            foreach (var day in new[] { Today, Tomorrow })
            {
                if (day == null) continue;
                foreach (var k in Api.Prayers)
                {
                    var t = day.Times[k];
                    string name = Api.Name(k), id = day.Date.ToString("yyyyMMdd") + k;
                    if (S.Notify && now >= t && now < t.AddMinutes(1) && fired.Add(id))
                        Alert("", Brushes.White, "Waktu " + name + " telah tiba", name + " " + t.ToString("HH:mm") + "  ·  " + S.City, 0);
                    var pre = t.AddMinutes(-10);
                    if (S.PreRemind && now >= pre && now < pre.AddMinutes(1) && fired.Add(id + "-pre"))
                        Alert("", Brushes.White, "10 menit lagi " + name, "pukul " + t.ToString("HH:mm") + "  ·  " + S.City, 0);
                }
            }
        }

        static void Alert(string glyph, Brush bg, string title, string body, int tab)
        {
            if (S.Sound) Chime.Play();
            if (!Island.ShowAlert(glyph, bg, title, body, tab))
                tray.ShowBalloonTip(8000, title, body, Forms.ToolTipIcon.None);
        }

        public static void SetWidget(bool on)
        {
            S.Widget = on;
            S.Save();
            if (on && Widget == null) { Widget = new WidgetWindow(); Widget.Show(); }
            else if (!on && Widget != null) { Widget.Close(); Widget = null; }
            trayWidget.Checked = on;
        }

        static void BuildTray()
        {
            Drawing.Icon icon;
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                icon = new Drawing.Icon(st, Forms.SystemInformation.SmallIconSize);
            var menu = new Forms.ContextMenuStrip
            {
                Renderer = new Forms.ToolStripProfessionalRenderer(new DarkMenuColors()),
                ShowImageMargin = false, ForeColor = Drawing.Color.White, Font = new Drawing.Font("Segoe UI", 9.5f)
            };
            trayInfo = new Forms.ToolStripMenuItem("Waktu Shalat") { Enabled = false };
            menu.Items.Add(trayInfo);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Buka island", null, (s, e) => Island.Expand(0));
            menu.Items.Add("Tambah tugas   Ctrl+Alt+Spasi", null, (s, e) => Island.QuickAdd());
            trayWidget = new Forms.ToolStripMenuItem("Widget di desktop", null, (s, e) => SetWidget(!S.Widget));
            menu.Items.Add(trayWidget);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Keluar", null, (s, e) => Exit());
            tray = new Forms.NotifyIcon { Icon = icon, Text = "Waktu Shalat", Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) Island.Expand(0); };
            tray.BalloonTipClicked += (s, e) => Island.Expand(0);
        }

        public static void ApplyStartup()
        {
            if (Settings.DirOverride != null) return;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (S.Startup) k.SetValue("WaktuShalat", "\"" + ExePath + "\" --tray");
                    else k.DeleteValue("WaktuShalat", false);
                }
            }
            catch { }
        }

        public static void Exit()
        {
            tray.Visible = false;
            tray.Dispose();
            Application.Current.Shutdown();
        }
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
        [StructLayout(LayoutKind.Sequential)] public struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        // Hide from Alt+Tab.
        public static void MakeToolWindow(IntPtr h) { SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80); }

        public static void SendToBottom(IntPtr h) { SetWindowPos(h, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }

        static readonly uint OwnPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        // What the foreground window does to the notch: 0 = nothing, 1 = overlaps the notch strip
        // (maximized/snapped apps like Chrome), 2 = fullscreen, -1 = it's us (keep the last state).
        public static int ForegroundState(RECT notch)
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return 0;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == OwnPid) return -1;
            var sb = new StringBuilder(64);
            GetClassName(h, sb, 64);
            var cls = sb.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || IsIconic(h)) return 0;
            RECT r;
            if (!GetWindowRect(h, out r)) return 0;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (GetMonitorInfo(MonitorFromWindow(h, 2), ref mi) &&
                r.L <= mi.rcMonitor.L && r.T <= mi.rcMonitor.T && r.R >= mi.rcMonitor.R && r.B >= mi.rcMonitor.B) return 2;
            bool overlap = r.L < notch.R && r.R > notch.L && r.T < notch.B && r.B > notch.T;
            return overlap ? 1 : 0;
        }
    }

    class DarkMenuColors : Forms.ProfessionalColorTable
    {
        static readonly Drawing.Color Bg = Drawing.Color.FromArgb(28, 28, 30), Hi = Drawing.Color.FromArgb(58, 58, 60);
        public override Drawing.Color ToolStripDropDownBackground { get { return Bg; } }
        public override Drawing.Color ImageMarginGradientBegin { get { return Bg; } }
        public override Drawing.Color ImageMarginGradientMiddle { get { return Bg; } }
        public override Drawing.Color ImageMarginGradientEnd { get { return Bg; } }
        public override Drawing.Color MenuBorder { get { return Hi; } }
        public override Drawing.Color MenuItemBorder { get { return Hi; } }
        public override Drawing.Color MenuItemSelected { get { return Hi; } }
        public override Drawing.Color SeparatorDark { get { return Hi; } }
        public override Drawing.Color SeparatorLight { get { return Bg; } }
        public override Drawing.Color CheckBackground { get { return Hi; } }
        public override Drawing.Color CheckSelectedBackground { get { return Hi; } }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            var mutex = new Mutex(true, "WaktuShalat.SingleInstance", out created);
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "WaktuShalat.Show");
            if (!created) { showEvent.Set(); return; }

            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            Forms.Application.EnableVisualStyles();

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) =>
            {
                try { File.AppendAllText(Path.Combine(Settings.DataDir, "error.log"), DateTime.Now + " " + e.Exception + "\r\n"); } catch { }
                e.Handled = true;
            };
            Core.Init();

            var listener = new Thread(() =>
            {
                while (true) { showEvent.WaitOne(); app.Dispatcher.BeginInvoke(new Action(() => Core.Island.Expand(0))); }
            }) { IsBackground = true };
            listener.Start();

            app.Run();
            GC.KeepAlive(mutex);
        }
    }
}
