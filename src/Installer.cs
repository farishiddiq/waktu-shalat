// Waktu Shalat Setup — per-user installer/uninstaller (no admin needed).
// Args: /S silent, /run launch after silent install, /uninstall (also implied when run as Uninstall.exe).
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace WaktuShalat
{
    static class Setup
    {
        public const string Version = "2.1.0";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WaktuShalat";
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WaktuShalat");
        public static string AppExe { get { return Path.Combine(Dir, "WaktuShalat.exe"); } }
        static string StartMenuLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Waktu Shalat.lnk"); } }
        static string DesktopLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Waktu Shalat.lnk"); } }

        static void KillApp()
        {
            foreach (var p in Process.GetProcessesByName("WaktuShalat"))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }

        public static void Install()
        {
            KillApp();
            Directory.CreateDirectory(Dir);
            var asm = Assembly.GetExecutingAssembly();
            using (var rs = asm.GetManifestResourceStream("app.exe"))
            using (var fs = File.Create(AppExe))
                rs.CopyTo(fs);
            var uninstaller = Path.Combine(Dir, "Uninstall.exe");
            if (!string.Equals(Path.GetFullPath(asm.Location), uninstaller, StringComparison.OrdinalIgnoreCase))
                File.Copy(asm.Location, uninstaller, true);

            Shortcut(StartMenuLnk);
            Shortcut(DesktopLnk);

            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Waktu Shalat");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Waktu Shalat");
                k.SetValue("DisplayIcon", AppExe + ",0");
                k.SetValue("InstallLocation", Dir);
                k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /S");
                k.SetValue("EstimatedSize", (int)(new FileInfo(AppExe).Length / 1024 + 100), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        static void Shortcut(string path)
        {
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic lnk = shell.CreateShortcut(path);
                lnk.TargetPath = AppExe;
                lnk.WorkingDirectory = Dir;
                lnk.IconLocation = AppExe + ",0";
                lnk.Description = "Jadwal waktu shalat harian";
                lnk.Save();
            }
            catch { }
        }

        public static void Uninstall()
        {
            KillApp();
            foreach (var f in new[] { StartMenuLnk, DesktopLnk }) { try { File.Delete(f); } catch { } }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    if (k != null) k.DeleteValue("WaktuShalat", false);
            }
            catch { }
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WaktuShalat"), true); } catch { }
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WaktuShalat"), true); } catch { }
            // Uninstall.exe lives in Dir, so remove the folder after this process exits.
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + Dir + "\"")
            {
                CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath()
            });
        }

        public static void Launch()
        {
            Process.Start(new ProcessStartInfo(AppExe) { WorkingDirectory = Dir, UseShellExecute = true });
        }
    }

    class SetupWindow : Window
    {
        readonly bool uninstall;
        TextBlock title, subtitle, status;
        Border primary;
        TextBlock primaryText, secondary;
        Action primaryAction;
        bool busy;

        public SetupWindow(bool uninstall)
        {
            this.uninstall = uninstall;
            Title = uninstall ? "Hapus Waktu Shalat" : "Instal Waktu Shalat";
            var host = Ui.Chrome(this, 420, 400);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(36, 20, 36, 0) };
            var img = new Image { Width = 92, Height = 92, Source = LoadIcon() };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            sp.Children.Add(img);

            title = Ui.T(uninstall ? "Hapus Waktu Shalat?" : "Waktu Shalat", 26, Ui.Label, FontWeights.SemiBold);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.Margin = new Thickness(0, 18, 0, 0);
            sp.Children.Add(title);

            subtitle = Ui.T(uninstall
                ? "Aplikasi, pintasan, dan pengaturan akan dihapus dari komputer ini."
                : "Notch untuk jadwal shalat, pomodoro, dan to-do. Versi " + Setup.Version,
                14, Ui.Secondary, FontWeights.Normal);
            subtitle.TextAlignment = TextAlignment.Center;
            subtitle.TextWrapping = TextWrapping.Wrap;
            subtitle.HorizontalAlignment = HorizontalAlignment.Center;
            subtitle.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(subtitle);

            primaryText = Ui.T(uninstall ? "Hapus" : "Instal", 15, Brushes.Black, FontWeights.SemiBold);
            primaryText.HorizontalAlignment = HorizontalAlignment.Center;
            primaryText.VerticalAlignment = VerticalAlignment.Center;
            primary = new Border
            {
                Height = 44, Width = 220, CornerRadius = new CornerRadius(22), Margin = new Thickness(0, 30, 0, 0),
                Background = uninstall ? Ui.B("#FF453A") : Ui.Accent, Child = primaryText
            };
            if (uninstall) primaryText.Foreground = Brushes.White;
            primaryAction = uninstall ? (Action)DoUninstall : DoInstall;
            Ui.Clickable(primary, () => { if (!busy) primaryAction(); }, null);
            sp.Children.Add(primary);

            secondary = Ui.T("Batal", 14, Ui.Secondary, FontWeights.Normal);
            secondary.HorizontalAlignment = HorizontalAlignment.Center;
            secondary.Margin = new Thickness(0, 14, 0, 0);
            Ui.Clickable(secondary, () => { if (!busy) Close(); }, null);
            sp.Children.Add(secondary);

            status = Ui.T(uninstall ? "" : "Lokasi: " + Setup.Dir.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~"),
                11, Ui.Tertiary, FontWeights.Normal);
            status.HorizontalAlignment = HorizontalAlignment.Center;
            status.Margin = new Thickness(0, 18, 0, 0);
            status.TextTrimming = TextTrimming.CharacterEllipsis;
            sp.Children.Add(status);

            host.Children.Add(sp);
            host.Children.Add(Ui.Lights(() => { if (!busy) Close(); }, () => WindowState = WindowState.Minimized, null));
        }

        static BitmapSource LoadIcon()
        {
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
            {
                var dec = new IconBitmapDecoder(st, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return dec.Frames.OrderByDescending(f => f.PixelWidth).First();
            }
        }

        async void DoInstall()
        {
            busy = true;
            primaryText.Text = "Memasang…";
            primary.Opacity = 0.6;
            try
            {
                await Task.Run(() => Setup.Install());
                title.Text = "Terpasang";
                subtitle.Text = "Waktu Shalat ada di Start Menu dan desktop, dan berjalan otomatis saat Windows mulai.";
                primaryText.Text = "Buka Waktu Shalat";
                primaryAction = () => { Setup.Launch(); Close(); };
                secondary.Text = "Tutup";
                status.Text = "";
            }
            catch (Exception ex)
            {
                primaryText.Text = "Coba lagi";
                status.Text = "Gagal: " + ex.Message;
            }
            primary.Opacity = 1;
            busy = false;
        }

        async void DoUninstall()
        {
            busy = true;
            primaryText.Text = "Menghapus…";
            primary.Opacity = 0.6;
            await Task.Run(() => Setup.Uninstall());
            title.Text = "Sudah dihapus";
            subtitle.Text = "Waktu Shalat telah dihapus dari komputer ini.";
            primary.Background = Ui.Accent;
            primaryText.Foreground = Brushes.Black;
            primaryText.Text = "Selesai";
            primaryAction = Close;
            primary.Opacity = 1;
            secondary.Visibility = Visibility.Collapsed;
            busy = false;
        }
    }

    static class SetupProgram
    {
        [STAThread]
        static int Main(string[] args)
        {
            var a = args.Select(x => x.ToLowerInvariant()).ToArray();
            bool silent = a.Contains("/s");
            bool uninstall = a.Contains("/uninstall") ||
                Path.GetFileName(Assembly.GetExecutingAssembly().Location).StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase);

            if (silent)
            {
                try
                {
                    if (uninstall) Setup.Uninstall();
                    else { Setup.Install(); if (a.Contains("/run")) Setup.Launch(); }
                    return 0;
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }

            var app = new Application();
            app.Run(new SetupWindow(uninstall));
            return 0;
        }
    }
}
