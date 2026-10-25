// Desktop widget: pinned behind all windows, shows prayer countdown, pomodoro and the top tasks.
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace WaktuShalat
{
    class WidgetWindow : Window
    {
        TextBlock wLoc, wDate, wLabel, wCount, wAt, wPomoTitle, wPomoSub, wPlay, wMore;
        Ring wRing;
        Border[] cells = new Border[5];
        TextBlock[] cellName = new TextBlock[5], cellTime = new TextBlock[5];
        StackPanel wTasks;

        public WidgetWindow()
        {
            Title = "Waktu Shalat Widget";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 316 + 28;
            FontFamily = Ui.Text;
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);

            var sp = new StackPanel();
            var body = new Grid();
            var star = Ui.Star8(120, Ui.Accent, 1.1);
            star.Opacity = 0.08;
            star.HorizontalAlignment = HorizontalAlignment.Right;
            star.VerticalAlignment = VerticalAlignment.Top;
            star.Margin = new Thickness(0, 18, -4, 0);
            body.Children.Add(star);
            body.Children.Add(sp);
            var frame = new Border
            {
                Margin = new Thickness(14), CornerRadius = new CornerRadius(26), Background = Brushes.Black,
                BorderBrush = Ui.B("#262628"), BorderThickness = new Thickness(1), Padding = new Thickness(24, 22, 24, 22), Child = body,
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5 }
            };
            frame.MouseLeftButtonDown += (s, e) =>
            {
                try { DragMove(); } catch { }
                Core.S.WidgetX = Left; Core.S.WidgetY = Top; Core.S.Save();
            };
            Content = frame;

            var head = new Grid();
            wLoc = Ui.T("", 12, Ui.Secondary, FontWeights.SemiBold);
            head.Children.Add(wLoc);
            wDate = Ui.T("", 12, Ui.Tertiary, FontWeights.Normal);
            wDate.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(wDate);
            sp.Children.Add(head);

            wLabel = Ui.T("", 10, Ui.Secondary, FontWeights.SemiBold);
            wLabel.Margin = new Thickness(0, 20, 0, 0);
            sp.Children.Add(wLabel);
            wCount = Ui.T("--:--:--", 40, Ui.Label, FontWeights.Light);
            wCount.Margin = new Thickness(-2, 2, 0, 0);
            sp.Children.Add(wCount);
            wAt = Ui.T("", 13, Ui.Secondary, FontWeights.Normal);
            wAt.Margin = new Thickness(0, 2, 0, 0);
            sp.Children.Add(wAt);

            var grid = new UniformGrid { Columns = 5, Margin = new Thickness(-3, 20, -3, 0), Height = 56 };
            for (int i = 0; i < 5; i++)
            {
                var c = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                cellName[i] = Ui.T(Api.Name(Api.Prayers[i]), 10, Ui.Secondary, FontWeights.Normal);
                cellName[i].HorizontalAlignment = HorizontalAlignment.Center;
                cellTime[i] = Ui.T("--.--", 13, Ui.Label, FontWeights.SemiBold);
                cellTime[i].HorizontalAlignment = HorizontalAlignment.Center;
                cellTime[i].Margin = new Thickness(0, 3, 0, 0);
                c.Children.Add(cellName[i]);
                c.Children.Add(cellTime[i]);
                cells[i] = new Border { Background = Ui.CardBg, CornerRadius = new CornerRadius(12), Margin = new Thickness(3), Child = c };
                grid.Children.Add(cells[i]);
            }
            sp.Children.Add(grid);

            sp.Children.Add(new Border { Height = 1, Background = Ui.Separator, Margin = new Thickness(0, 22, 0, 20) });

            var pomo = new Grid();
            pomo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pomo.ColumnDefinitions.Add(new ColumnDefinition());
            pomo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            wRing = new Ring(34, 4, Ui.CardBg, Ui.Accent);
            pomo.Children.Add(wRing);
            var pt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 10, 0) };
            wPomoTitle = Ui.T("Pomodoro", 14, Ui.Label, FontWeights.SemiBold);
            wPomoSub = Ui.T("", 12, Ui.Secondary, FontWeights.Normal);
            wPomoSub.TextTrimming = TextTrimming.CharacterEllipsis;
            wPomoSub.Margin = new Thickness(0, 3, 0, 0);
            pt.Children.Add(wPomoTitle);
            pt.Children.Add(wPomoSub);
            Grid.SetColumn(pt, 1);
            pomo.Children.Add(pt);
            var play = Ui.CircleButton(34, Ui.Accent, "", 13, Brushes.Black, () => { Core.Pomo.Toggle(); Refresh(); }, out wPlay);
            Grid.SetColumn(play, 2);
            pomo.Children.Add(play);
            sp.Children.Add(pomo);

            var tasksHead = Ui.T("TUGAS", 10, Ui.Secondary, FontWeights.SemiBold);
            tasksHead.Margin = new Thickness(0, 22, 0, 8);
            Ui.Clickable(tasksHead, () => Core.Island.Expand(2), null);
            sp.Children.Add(tasksHead);
            wTasks = new StackPanel();
            sp.Children.Add(wTasks);
            wMore = Ui.T("", 11, Ui.Tertiary, FontWeights.Normal);
            wMore.Margin = new Thickness(28, 2, 0, 0);
            sp.Children.Add(wMore);

            var wa = SystemParameters.WorkArea;
            Left = double.IsNaN(Core.S.WidgetX) ? wa.Right - Width - 16 : Core.S.WidgetX;
            Top = double.IsNaN(Core.S.WidgetY) ? wa.Top + 60 : Core.S.WidgetY;

            Loaded += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                Native.MakeToolWindow(h);
                HwndSource.FromHwnd(h).AddHook(PinToBottom);
                Native.SendToBottom(h);
            };
            Core.Ticked += Refresh;
            Core.DataChanged += Refresh;
            Core.Todos.Changed += BuildTasks;
            Closed += (s, e) => { Core.Ticked -= Refresh; Core.DataChanged -= Refresh; Core.Todos.Changed -= BuildTasks; };
            BuildTasks();
        }

        // Keep the widget below every other window, like it lives on the wallpaper.
        static IntPtr PinToBottom(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0046) // WM_WINDOWPOSCHANGING
            {
                var wp = (Native.WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(Native.WINDOWPOS));
                wp.hwndInsertAfter = new IntPtr(1); // HWND_BOTTOM
                wp.flags &= ~0x0004u;               // drop SWP_NOZORDER
                Marshal.StructureToPtr(wp, lParam, false);
            }
            return IntPtr.Zero;
        }

        void BuildTasks()
        {
            wTasks.Children.Clear();
            var active = Core.Todos.Active;
            if (active.Count == 0)
            {
                var none = Ui.T("Tidak ada tugas — santai dulu.", 13, Ui.Secondary, FontWeights.Normal);
                wTasks.Children.Add(none);
            }
            foreach (var t in active.Take(4))
            {
                var item = t;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
                var circle = new Ellipse { Width = 16, Height = 16, Stroke = Ui.Dim, StrokeThickness = 1.5, Fill = Brushes.Transparent, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                var hit = new Grid { Background = Brushes.Transparent };
                hit.Children.Add(circle);
                Ui.Clickable(hit, () => { circle.Fill = Ui.Accent; circle.Stroke = Ui.Accent; Chime.Play(); Core.Todos.Toggle(item); }, null);
                row.Children.Add(hit);
                if (item.Focus)
                {
                    var flag = Ui.Icon("", 11, Ui.Accent);
                    flag.Margin = new Thickness(0, 1, 6, 0);
                    row.Children.Add(flag);
                }
                var label = Ui.T(item.Text, 13, Ui.Label, item.Focus ? FontWeights.SemiBold : FontWeights.Normal);
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.MaxWidth = 220;
                row.Children.Add(label);
                wTasks.Children.Add(row);
            }
            wMore.Text = active.Count > 4 ? "+" + (active.Count - 4) + " lainnya" : "";
            wMore.Visibility = active.Count > 4 ? Visibility.Visible : Visibility.Collapsed;
            Refresh();
        }

        void Refresh()
        {
            var now = DateTime.Now;
            var n = Core.Next(now);
            var today = Core.Today;
            wLoc.Text = Core.S.City;
            wDate.Text = DateTime.Today.ToString("ddd, d MMM", Core.Id);
            if (n != null)
            {
                var iq = Core.Iqamah(now);
                var target = iq ?? n;
                wLabel.Text = iq != null ? "MENUJU IQAMAH " + Api.Name(iq.Key).ToUpper() : "MENUJU " + Api.Name(n.Key).ToUpper() + (n.Tomorrow ? " BESOK" : "");
                wLabel.Foreground = iq != null ? Ui.Accent : Ui.Secondary;
                wCount.Text = Core.Hms(target.Left);
                wAt.Text = "pukul " + target.At.ToString("HH:mm");
                string highlight = iq != null ? iq.Key : n.Tomorrow ? null : n.Key;
                for (int i = 0; i < 5; i++)
                {
                    var k = Api.Prayers[i];
                    bool isNext = k == highlight;
                    cellTime[i].Text = today.Times[k].ToString("HH:mm");
                    cells[i].Background = isNext ? Ui.AccentDeep : Ui.CardBg;
                    cellName[i].Foreground = isNext ? Ui.Accent : Ui.Secondary;
                    cellTime[i].Foreground = isNext ? Ui.Label : today.Times[k] <= now ? Ui.Dim : Ui.Label;
                }
            }

            var p = Core.Pomo;
            var color = p.Phase == "focus" ? Ui.Accent : Ui.Sky;
            wRing.Stroke = color;
            wRing.Set(p.Progress);
            var f = Core.Todos.Focused;
            wPomoTitle.Text = p.Idle ? "Pomodoro" : p.PhaseName + "  " + Core.Short(p.Remaining);
            wPomoSub.Text = p.Phase != "focus" ? "Istirahat sejenak" : f != null ? f.Text : p.Idle ? Core.S.FocusMin + " menit fokus" : "Fokus";
            wPlay.Text = p.Running ? "" : "";
        }
    }
}
