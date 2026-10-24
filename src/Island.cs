// Dynamic Island: a black pill at the top of the screen that morphs into prayer times, pomodoro and to-do.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WaktuShalat
{
    class IslandWindow : Window
    {
        const double WinW = 480, WinH = 680, ExpW = 400, CompactH = 38, PageH = 580, SlimW = 150, SlimH = 6;
        static readonly double[] TabHeights = { 364, 492, 532 };
        enum Mode { Compact, Expanded, Alert }

        Mode mode = Mode.Compact;
        int tab;
        bool hidden;
        bool slim; // a window is sitting under the notch: shrink to a thin bar until hovered
        bool pinned; // opened by a click/hotkey (stays open) vs. a hover peek (closes when the mouse leaves)
        readonly DispatcherTimer hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(170) };
        readonly DispatcherTimer leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        double targetW = -1, targetH = -1;
        Grid root, inner, notch;
        Border island;
        Path notchShape;
        readonly ScaleTransform islandScale = new ScaleTransform(1, 1);
        Grid compactPrayer, compactPomo, expandedHost, alertView, mainPage;
        Grid[] tabViews;
        Segmented seg;
        readonly Stack<Grid> pages = new Stack<Grid>();

        TextBlock cpName, cpTime, cmLabel, cmTask, cmTime;
        Ring cmRing;

        TextBlock sLoc, sHijri, sLabel, sCountdown, sAt, sExtra;
        Grid sTrack;
        Border sFill;
        Border[] sCells;
        TextBlock[] sCellName, sCellTime;

        Ring fRing;
        TextBlock fTime, fPhase, fTask, fSessions, fPlayGlyph;
        readonly List<Tuple<int, Border, TextBlock>> fChips = new List<Tuple<int, Border, TextBlock>>();
        Ellipse[] fDots;

        TextBox tInput;
        TextBlock tPlaceholder, tSummary, tClear;
        StackPanel tList;

        TextBlock cityValue, methodValue;

        TextBlock aTitle, aBody, aGlyph;
        Border aIconBg;
        int alertTab;
        readonly DispatcherTimer alertTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };

        public IslandWindow()
        {
            Title = "Waktu Shalat";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Width = WinW;
            Height = WinH;
            FontFamily = Ui.Text;
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);

            root = new Grid(); // no background: empty pixels stay click-through
            Content = root;

            // The notch: flush with the top edge, rounded bottom corners, and concave "ears" that melt into the bezel.
            // `island` carries the animated body size; `notchShape` is redrawn around it on every frame.
            notch = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = new Point(0.5, 0), RenderTransform = islandScale
            };
            notchShape = new Path { Fill = Brushes.Black, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            notch.Children.Add(notchShape);
            island = new Border
            {
                Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Width = 214, Height = CompactH, Cursor = Cursors.Arrow
            };
            inner = new Grid();
            island.Child = inner;
            island.SizeChanged += (s, e) => Reshape();
            notch.Children.Add(island);
            root.Children.Add(notch);

            compactPrayer = BuildCompactPrayer();
            compactPomo = BuildCompactPomo();
            alertView = BuildAlert();
            expandedHost = BuildExpanded();
            inner.Children.Add(compactPrayer);
            inner.Children.Add(compactPomo);
            inner.Children.Add(alertView);
            inner.Children.Add(expandedHost);
            Ui.Hide(compactPomo);
            Ui.Hide(alertView);
            Ui.Hide(expandedHost);

            hoverTimer.Tick += (s, e) =>
            {
                hoverTimer.Stop();
                if (island.IsMouseOver && mode != Mode.Expanded) Expand(DefaultTab(), false);
            };
            leaveTimer.Tick += (s, e) =>
            {
                leaveTimer.Stop();
                if (mode == Mode.Expanded && !pinned && !island.IsMouseOver) Collapse();
            };
            island.MouseEnter += (s, e) =>
            {
                leaveTimer.Stop();
                if (mode != Mode.Expanded) { Scale(1.04); hoverTimer.Start(); }
            };
            island.MouseLeave += (s, e) =>
            {
                hoverTimer.Stop();
                Scale(1);
                if (mode == Mode.Expanded && !pinned) leaveTimer.Start();
            };
            // Clicking inside a hover peek keeps it open.
            island.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (mode == Mode.Expanded && !pinned) { pinned = true; Activate(); }
            };
            island.MouseLeftButtonUp += (s, e) =>
            {
                if (mode == Mode.Expanded) return;
                e.Handled = true;
                Expand(DefaultTab());
            };
            Deactivated += (s, e) => { if (mode == Mode.Expanded && pinned) Collapse(); };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { Collapse(); e.Handled = true; } };
            alertTimer.Tick += (s, e) => { alertTimer.Stop(); if (mode == Mode.Alert) Collapse(); };

            Loaded += (s, e) =>
            {
                Position();
                var h = new WindowInteropHelper(this).Handle;
                Native.MakeToolWindow(h);
                HwndSource.FromHwnd(h).AddHook(WndProc);
                Native.RegisterHotKey(h, 1, 0x4000 | 0x2 | 0x1, 0x20); // Ctrl+Alt+Space
            };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => Dispatcher.BeginInvoke(new Action(Position));

            Core.Ticked += Refresh;
            Core.DataChanged += Refresh;
            Core.Todos.Changed += BuildTodos;
            BuildTodos();
            SelectTab(0, false);
            Resize(214, CompactH);
        }

        void Reshape()
        {
            double w = island.ActualWidth, h = island.ActualHeight;
            if (w <= 0 || h <= 0) return;
            double r = Math.Min(h * 0.42, 30);                                    // bottom corner radius
            double e = 7 + 7 * Math.Max(0, Math.Min(1, (h - CompactH) / 120));   // ear radius grows as it opens
            e = Math.Min(e, Math.Max(0, h - r));                                  // ...and shrinks with the slim bar

            var body = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true };
            body.Segments.Add(new LineSegment(new Point(w, 0), true));
            body.Segments.Add(new LineSegment(new Point(w, h - r), true));
            body.Segments.Add(new ArcSegment(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
            body.Segments.Add(new LineSegment(new Point(r, h), true));
            body.Segments.Add(new ArcSegment(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
            var clip = new PathGeometry();
            clip.Figures.Add(body);
            clip.Freeze();
            inner.Clip = clip;

            var f = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true };
            f.Segments.Add(new ArcSegment(new Point(e, e), new Size(e, e), 0, false, SweepDirection.Clockwise, true));
            f.Segments.Add(new LineSegment(new Point(e, h - r), true));
            f.Segments.Add(new ArcSegment(new Point(e + r, h), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true));
            f.Segments.Add(new LineSegment(new Point(e + w - r, h), true));
            f.Segments.Add(new ArcSegment(new Point(e + w, h - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true));
            f.Segments.Add(new LineSegment(new Point(e + w, e), true));
            f.Segments.Add(new ArcSegment(new Point(2 * e + w, 0), new Size(e, e), 0, false, SweepDirection.Clockwise, true));
            var g = new PathGeometry();
            g.Figures.Add(f);
            g.Freeze();
            notchShape.Data = g;
            notchShape.Width = w + 2 * e;
            notchShape.Height = h;
        }

        public void Position()
        {
            var wa = SystemParameters.WorkArea;
            var pos = Core.S.NotchPos;
            notch.HorizontalAlignment = pos == "left" ? HorizontalAlignment.Left : pos == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Center;
            Left = pos == "left" ? wa.Left + 16 : pos == "right" ? wa.Right - WinW - 16 : wa.Left + (wa.Width - WinW) / 2;
            Top = wa.Top;
        }

        // Screen pixels of the strip the compact notch can occupy; windows overlapping it make the notch slim.
        public Native.RECT NotchRect()
        {
            Native.RECT r;
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero || !Native.GetWindowRect(h, out r)) return new Native.RECT();
            var src = PresentationSource.FromVisual(this);
            double scale = src != null ? src.CompositionTarget.TransformToDevice.M22 : 1;
            r.B = r.T + (int)Math.Ceiling(CompactH * scale);
            return r;
        }

        public void SetSlim(bool s)
        {
            if (slim == s) return;
            slim = s;
            UpdateCompact();
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0312 && wParam.ToInt32() == 1) { QuickAdd(); handled = true; }
            return IntPtr.Zero;
        }

        // ---------- state machine ----------

        public void QuickAdd()
        {
            Expand(2);
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { tInput.Focus(); Keyboard.Focus(tInput); }));
        }

        int DefaultTab() { return mode == Mode.Alert ? alertTab : Core.Pomo.Running ? 1 : tab; }

        public void Expand(int t, bool activate = true)
        {
            if (hidden) return;
            alertTimer.Stop();
            hoverTimer.Stop();
            leaveTimer.Stop();
            pinned = activate;
            if (mode == Mode.Expanded) { SelectTab(t, true); if (activate) Activate(); return; }
            while (pages.Count > 1) expandedHost.Children.Remove(pages.Pop());
            ((TranslateTransform)mainPage.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null);
            ((TranslateTransform)mainPage.RenderTransform).X = 0;
            mode = Mode.Expanded;
            SelectTab(t, false);
            Refresh();
            Scale(1);
            Morph(compactPrayer, false);
            Morph(compactPomo, false);
            Morph(alertView, false);
            Morph(expandedHost, true, 70);
            Resize(ExpW, CurrentHeight());
            if (activate)
            {
                Activate();
                if (t == 2) Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => tInput.Focus()));
            }
        }

        public void Collapse()
        {
            if (mode == Mode.Compact) return;
            mode = Mode.Compact;
            pinned = false;
            alertTimer.Stop();
            leaveTimer.Stop();
            Morph(expandedHost, false);
            Morph(alertView, false);
            Keyboard.ClearFocus();
            UpdateCompact();
        }

        // Apple-style content morph: fade + spring scale + a brief blur that clears as it lands.
        static void Morph(FrameworkElement el, bool on, int delayMs = 0)
        {
            if (el.IsHitTestVisible == on) return;
            el.IsHitTestVisible = on;
            var begin = TimeSpan.FromMilliseconds(on ? delayMs : 0);
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, Ui.Ms(on ? 300 : 130))
            {
                BeginTime = begin, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });

            var st = el.RenderTransform as ScaleTransform;
            if (st == null) { st = new ScaleTransform(1, 1); el.RenderTransform = st; }
            var sa = on
                ? new DoubleAnimation(0.9, 1, Ui.Ms(640)) { BeginTime = begin, EasingFunction = new SpringEase() }
                : new DoubleAnimation(0.88, Ui.Ms(180)) { EasingFunction = Ui.Ease };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, sa);

            var blur = new BlurEffect { Radius = on ? 12 : 0, RenderingBias = RenderingBias.Performance };
            el.Effect = blur;
            var ba = new DoubleAnimation(on ? 0 : 10, Ui.Ms(on ? 340 : 130)) { BeginTime = begin };
            ba.Completed += (s, e) => { if (el.Effect == blur) el.Effect = null; };
            blur.BeginAnimation(BlurEffect.RadiusProperty, ba);
        }

        // Live-activity style banner. Returns false if the island can't show it (fullscreen app).
        public bool ShowAlert(string glyph, Brush glyphBg, string title, string body, int tabOnClick)
        {
            if (hidden) return false;
            if (mode == Mode.Expanded) return true;
            aGlyph.Text = glyph;
            aIconBg.Background = glyphBg;
            aTitle.Text = title;
            aBody.Text = body;
            alertTab = tabOnClick;
            mode = Mode.Alert;
            Morph(compactPrayer, false);
            Morph(compactPomo, false);
            Morph(alertView, true, 90);
            Resize(380, 80);
            alertTimer.Stop();
            alertTimer.Start();
            return true;
        }

        public void SetHidden(bool h)
        {
            if (hidden == h) return;
            hidden = h;
            if (h) Collapse();
            root.Visibility = h ? Visibility.Hidden : Visibility.Visible;
        }

        void UpdateCompact()
        {
            if (mode != Mode.Compact) return;
            if (slim)
            {
                Morph(compactPomo, false);
                Morph(compactPrayer, false);
                Resize(SlimW, SlimH);
                return;
            }
            bool pomo = Core.Pomo.Running;
            double w = pomo ? (cmTask.Text.Length > 0 ? 312 : 222) : 214;
            compactPomo.Width = w;
            Morph(compactPomo, pomo, 110);
            Morph(compactPrayer, !pomo, 110);
            Resize(w, CompactH);
        }

        double CurrentHeight() { return pages.Count > 1 ? (double)pages.Peek().Tag : TabHeights[tab]; }

        // Width and height ride the same spring, so the pill morphs instead of stretching.
        void Resize(double w, double h)
        {
            if (w == targetW && h == targetH) return;
            bool growing = w * h > targetW * targetH;
            targetW = w; targetH = h;
            var ms = Ui.Ms(growing ? 680 : 560);
            island.BeginAnimation(WidthProperty, new DoubleAnimation(w, ms) { EasingFunction = new SpringEase() });
            island.BeginAnimation(HeightProperty, new DoubleAnimation(h, ms) { EasingFunction = new SpringEase() });
        }

        void Scale(double s)
        {
            var a = new DoubleAnimation(s, Ui.Ms(520)) { EasingFunction = new SpringEase() };
            islandScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            islandScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        void SelectTab(int t, bool animate)
        {
            int prev = tab;
            tab = t;
            seg.Select(t, animate);
            for (int i = 0; i < tabViews.Length; i++)
            {
                var tt = (TranslateTransform)tabViews[i].RenderTransform;
                if (animate && i == t && prev != t)
                {
                    Ui.Show(tabViews[i], true, 260, 40);
                    tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(t > prev ? 28 : -28, 0, Ui.Ms(560)) { EasingFunction = new SpringEase() });
                }
                else if (animate && i == prev && prev != t)
                {
                    Ui.Show(tabViews[i], false, 120);
                    tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(t > prev ? -20 : 20, Ui.Ms(200)) { EasingFunction = Ui.Ease });
                }
                else if (i == t)
                {
                    tt.BeginAnimation(TranslateTransform.XProperty, null);
                    tt.X = 0;
                    tabViews[i].BeginAnimation(OpacityProperty, null);
                    tabViews[i].Opacity = 1;
                    tabViews[i].IsHitTestVisible = true;
                }
                else Ui.Hide(tabViews[i]);
            }
            if (mode == Mode.Expanded && pages.Count == 1) Resize(ExpW, TabHeights[t]);
            if (t == 2 && animate) Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => tInput.Focus()));
        }

        // ---------- compact & alert ----------

        Grid BuildCompactPrayer()
        {
            var g = new Grid { Width = 214, Height = CompactH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, RenderTransformOrigin = new Point(0.5, 0.5) };
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(19, 0, 0, 0) };
            var moon = Ui.Icon("", 12, Ui.Label);
            moon.Margin = new Thickness(0, 1, 9, 0);
            left.Children.Add(moon);
            cpName = Ui.T("Shalat", 13, Ui.Label, FontWeights.SemiBold);
            left.Children.Add(cpName);
            g.Children.Add(left);
            cpTime = Ui.T("--:--", 13, Ui.Label, FontWeights.SemiBold);
            cpTime.HorizontalAlignment = HorizontalAlignment.Right;
            cpTime.VerticalAlignment = VerticalAlignment.Center;
            cpTime.Margin = new Thickness(0, 0, 19, 0);
            g.Children.Add(cpTime);
            return g;
        }

        Grid BuildCompactPomo()
        {
            var g = new Grid { Width = 222, Height = CompactH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, RenderTransformOrigin = new Point(0.5, 0.5) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            cmRing = new Ring(16, 3, Ui.Raised, Ui.Orange);
            cmRing.Margin = new Thickness(0, 0, 9, 0);
            left.Children.Add(cmRing);
            cmLabel = Ui.T("Fokus", 13, Ui.Label, FontWeights.SemiBold);
            left.Children.Add(cmLabel);
            g.Children.Add(left);
            cmTask = Ui.T("", 12, Ui.Secondary, FontWeights.Normal);
            cmTask.VerticalAlignment = VerticalAlignment.Center;
            cmTask.TextTrimming = TextTrimming.CharacterEllipsis;
            cmTask.Margin = new Thickness(12, 0, 10, 0);
            Grid.SetColumn(cmTask, 1);
            g.Children.Add(cmTask);
            cmTime = Ui.T("25:00", 13, Ui.Orange, FontWeights.SemiBold);
            cmTime.VerticalAlignment = VerticalAlignment.Center;
            cmTime.Margin = new Thickness(0, 0, 19, 0);
            Grid.SetColumn(cmTime, 2);
            g.Children.Add(cmTime);
            return g;
        }

        Grid BuildAlert()
        {
            var g = new Grid { Width = 378, Height = 78, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, RenderTransformOrigin = new Point(0.5, 0.5) };
            aGlyph = Ui.Icon("", 20, Brushes.Black);
            aGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            aIconBg = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), Child = aGlyph };
            g.Children.Add(aIconBg);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(74, 0, 18, 0) };
            aTitle = Ui.T("", 15, Ui.Label, FontWeights.SemiBold);
            aTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            aBody = Ui.T("", 13, Ui.Secondary, FontWeights.Normal);
            aBody.TextTrimming = TextTrimming.CharacterEllipsis;
            aBody.Margin = new Thickness(0, 3, 0, 0);
            sp.Children.Add(aTitle);
            sp.Children.Add(aBody);
            g.Children.Add(sp);
            return g;
        }

        // ---------- expanded ----------

        static Grid NewPage(double height)
        {
            return new Grid { Background = Brushes.Black, RenderTransform = new TranslateTransform(), Tag = height, Width = ExpW, Height = PageH, VerticalAlignment = VerticalAlignment.Top };
        }

        Grid BuildExpanded()
        {
            var host = new Grid { Width = ExpW, Height = PageH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, RenderTransformOrigin = new Point(0.5, 0) };
            mainPage = NewPage(0);
            mainPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
            mainPage.RowDefinitions.Add(new RowDefinition());

            seg = new Segmented(new[] { "Shalat", "Fokus", "Tugas" }, 76);
            seg.Margin = new Thickness(18, 4, 0, 0);
            seg.VerticalAlignment = VerticalAlignment.Center;
            seg.Changed += i => SelectTab(i, true);
            mainPage.Children.Add(seg);

            var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 14, 0) };
            TextBlock dummy;
            tools.Children.Add(Ui.CircleButton(32, Ui.CardBg, "", 14, Ui.Secondary, () => Push(BuildSettings()), out dummy));
            var up = Ui.CircleButton(32, Ui.CardBg, "", 12, Ui.Secondary, Collapse, out dummy);
            up.Margin = new Thickness(8, 0, 0, 0);
            tools.Children.Add(up);
            mainPage.Children.Add(tools);

            var content = new Grid();
            Grid.SetRow(content, 1);
            tabViews = new[] { BuildShalat(), BuildFokus(), BuildTugas() };
            foreach (var v in tabViews) { v.RenderTransform = new TranslateTransform(); content.Children.Add(v); }
            mainPage.Children.Add(content);

            host.Children.Add(mainPage);
            pages.Push(mainPage);
            return host;
        }

        Grid BuildShalat()
        {
            var g = new Grid { VerticalAlignment = VerticalAlignment.Top };
            var sp = new StackPanel { Margin = new Thickness(24, 4, 24, 0) };
            g.Children.Add(sp);

            var top = new Grid();
            var loc = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            var pin = Ui.Icon("", 11, Ui.Secondary);
            pin.Margin = new Thickness(0, 1, 6, 0);
            loc.Children.Add(pin);
            sLoc = Ui.T("", 13, Ui.Secondary, FontWeights.SemiBold);
            loc.Children.Add(sLoc);
            Ui.Clickable(loc, () => Push(BuildCityPicker()), null);
            top.Children.Add(loc);
            sHijri = Ui.T("", 12, Ui.Tertiary, FontWeights.Normal);
            sHijri.HorizontalAlignment = HorizontalAlignment.Right;
            sHijri.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(sHijri);
            sp.Children.Add(top);

            sLabel = Ui.T("", 11, Ui.Secondary, FontWeights.SemiBold);
            sLabel.Margin = new Thickness(0, 26, 0, 0);
            sp.Children.Add(sLabel);
            var row = new Grid();
            sCountdown = Ui.T("--:--:--", 50, Ui.Label, FontWeights.Light);
            sCountdown.Margin = new Thickness(-2, 2, 0, 0);
            row.Children.Add(sCountdown);
            sAt = Ui.T("", 15, Ui.Secondary, FontWeights.Normal);
            sAt.HorizontalAlignment = HorizontalAlignment.Right;
            sAt.VerticalAlignment = VerticalAlignment.Bottom;
            sAt.Margin = new Thickness(0, 0, 0, 14);
            row.Children.Add(sAt);
            sp.Children.Add(row);

            sTrack = new Grid { Height = 4, Margin = new Thickness(0, 12, 0, 0) };
            sTrack.Children.Add(new Border { Background = Ui.CardBg, CornerRadius = new CornerRadius(2) });
            sFill = new Border { Background = Ui.Label, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            sTrack.Children.Add(sFill);
            sp.Children.Add(sTrack);

            var cells = new UniformGrid { Columns = 5, Margin = new Thickness(-4, 28, -4, 0), Height = 68 };
            sCells = new Border[5]; sCellName = new TextBlock[5]; sCellTime = new TextBlock[5];
            for (int i = 0; i < 5; i++)
            {
                var c = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                sCellName[i] = Ui.T(Api.Name(Api.Prayers[i]), 11, Ui.Secondary, FontWeights.Normal);
                sCellName[i].HorizontalAlignment = HorizontalAlignment.Center;
                sCellTime[i] = Ui.T("--.--", 15, Ui.Label, FontWeights.SemiBold);
                sCellTime[i].HorizontalAlignment = HorizontalAlignment.Center;
                sCellTime[i].Margin = new Thickness(0, 4, 0, 0);
                c.Children.Add(sCellName[i]);
                c.Children.Add(sCellTime[i]);
                sCells[i] = new Border { Background = Ui.CardBg, CornerRadius = new CornerRadius(16), Margin = new Thickness(4), Child = c };
                cells.Children.Add(sCells[i]);
            }
            sp.Children.Add(cells);

            sExtra = Ui.T("", 12, Ui.Secondary, FontWeights.Normal);
            sExtra.HorizontalAlignment = HorizontalAlignment.Center;
            sExtra.Margin = new Thickness(0, 22, 0, 0);
            sp.Children.Add(sExtra);
            return g;
        }

        Grid BuildFokus()
        {
            var g = new Grid { VerticalAlignment = VerticalAlignment.Top };
            var sp = new StackPanel { Margin = new Thickness(24, 10, 24, 0) };
            g.Children.Add(sp);

            var ringHost = new Grid { Width = 158, Height = 158 };
            fRing = new Ring(158, 9, Ui.CardBg, Ui.Orange);
            ringHost.Children.Add(fRing);
            var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            fTime = Ui.T("25:00", 40, Ui.Label, FontWeights.Light);
            fTime.HorizontalAlignment = HorizontalAlignment.Center;
            fPhase = Ui.T("FOKUS", 11, Ui.Secondary, FontWeights.SemiBold);
            fPhase.HorizontalAlignment = HorizontalAlignment.Center;
            fPhase.Margin = new Thickness(0, 4, 0, 0);
            center.Children.Add(fTime);
            center.Children.Add(fPhase);
            ringHost.Children.Add(center);
            sp.Children.Add(ringHost);

            fTask = Ui.T("", 14, Ui.Label, FontWeights.Normal);
            fTask.HorizontalAlignment = HorizontalAlignment.Center;
            fTask.TextTrimming = TextTrimming.CharacterEllipsis;
            fTask.MaxWidth = 340;
            fTask.Margin = new Thickness(0, 22, 0, 0);
            Ui.Clickable(fTask, () => SelectTab(2, true), null);
            sp.Children.Add(fTask);

            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
            TextBlock dummy;
            var reset = Ui.CircleButton(46, Ui.CardBg, "", 16, Ui.Label, () => { Core.Pomo.Reset(); Refresh(); }, out dummy);
            reset.VerticalAlignment = VerticalAlignment.Center;
            reset.Margin = new Thickness(0, 0, 24, 0);
            controls.Children.Add(reset);
            controls.Children.Add(Ui.CircleButton(62, Brushes.White, "", 22, Brushes.Black, () => { Core.Pomo.Toggle(); Refresh(); }, out fPlayGlyph));
            var skip = Ui.CircleButton(46, Ui.CardBg, "", 16, Ui.Label, () => { Core.Pomo.Skip(); Refresh(); }, out dummy);
            skip.VerticalAlignment = VerticalAlignment.Center;
            skip.Margin = new Thickness(24, 0, 0, 0);
            controls.Children.Add(skip);
            sp.Children.Add(controls);

            var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) };
            foreach (var m in new[] { 15, 25, 45, 60 })
            {
                var t = Ui.T(m + " mnt", 13, Ui.Label, FontWeights.SemiBold);
                t.VerticalAlignment = VerticalAlignment.Center;
                var b = new Border { Height = 32, Padding = new Thickness(15, 0, 15, 0), CornerRadius = new CornerRadius(16), Background = Ui.CardBg, Margin = new Thickness(5, 0, 5, 0), Child = t };
                int min = m;
                Ui.Clickable(b, () => { Core.Pomo.SetFocusMinutes(min); Refresh(); }, null);
                fChips.Add(Tuple.Create(m, b, t));
                chips.Children.Add(b);
            }
            sp.Children.Add(chips);

            var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
            fDots = new Ellipse[4];
            for (int i = 0; i < 4; i++)
            {
                fDots[i] = new Ellipse { Width = 7, Height = 7, Fill = Ui.Raised, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
                dots.Children.Add(fDots[i]);
            }
            fSessions = Ui.T("", 12, Ui.Secondary, FontWeights.Normal);
            fSessions.Margin = new Thickness(8, 0, 0, 0);
            dots.Children.Add(fSessions);
            sp.Children.Add(dots);
            return g;
        }

        Grid BuildTugas()
        {
            var g = new Grid { VerticalAlignment = VerticalAlignment.Top };
            var sp = new StackPanel { Margin = new Thickness(20, 4, 20, 0) };
            g.Children.Add(sp);

            var box = new Grid();
            var plus = Ui.Icon("", 14, Ui.Secondary);
            plus.Margin = new Thickness(14, 0, 0, 0);
            box.Children.Add(plus);
            tPlaceholder = Ui.T("Apa satu hal kecil berikutnya?", 15, Ui.Tertiary, FontWeights.Normal);
            tPlaceholder.VerticalAlignment = VerticalAlignment.Center;
            tPlaceholder.Margin = new Thickness(42, 0, 12, 0);
            tPlaceholder.IsHitTestVisible = false;
            box.Children.Add(tPlaceholder);
            tInput = new TextBox
            {
                Background = Brushes.Transparent, Foreground = Ui.Label, CaretBrush = Ui.Label, BorderThickness = new Thickness(0),
                FontSize = 15, FontFamily = Ui.Text, SelectionBrush = Ui.Blue, VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(38, 0, 12, 0), Padding = new Thickness(0)
            };
            tInput.TextChanged += (s, e) => tPlaceholder.Visibility = tInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            tInput.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                Core.Todos.Add(tInput.Text);
                tInput.Clear();
                e.Handled = true;
            };
            box.Children.Add(tInput);
            sp.Children.Add(new Border { Background = Ui.CardBg, CornerRadius = new CornerRadius(16), Height = 50, Child = box });

            var meta = new Grid { Margin = new Thickness(4, 18, 4, 10) };
            tSummary = Ui.T("", 12, Ui.Secondary, FontWeights.Normal);
            meta.Children.Add(tSummary);
            tClear = Ui.T("Bersihkan yang selesai", 12, Ui.Blue, FontWeights.Normal);
            tClear.HorizontalAlignment = HorizontalAlignment.Right;
            Ui.Clickable(tClear, () => Core.Todos.ClearDone(), null);
            meta.Children.Add(tClear);
            sp.Children.Add(meta);

            tList = new StackPanel();
            sp.Children.Add(new ScrollViewer { Content = tList, Height = TabHeights[2] - 60 - 4 - 50 - 44 - 24, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden });
            return g;
        }

        void BuildTodos()
        {
            var store = Core.Todos;
            tList.Children.Clear();
            var active = store.Active;
            var done = store.Finished;
            if (active.Count == 0 && done.Count == 0)
            {
                var empty = Ui.T("Belum ada tugas.\nTulis satu hal kecil yang bisa kamu mulai sekarang —\nsisanya nanti saja.", 13, Ui.Secondary, FontWeights.Normal);
                empty.TextAlignment = TextAlignment.Center;
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                empty.LineHeight = 22;
                empty.Margin = new Thickness(0, 44, 0, 0);
                tList.Children.Add(empty);
            }
            foreach (var t in active) tList.Children.Add(TodoRow(t));
            foreach (var t in done) tList.Children.Add(TodoRow(t));

            int dt = store.DoneToday;
            tSummary.Text = active.Count + " tersisa" + (dt > 0 ? "  ·  " + dt + " selesai hari ini" + (dt >= 3 ? " — mantap!" : "") : "");
            tClear.Visibility = done.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Refresh();
        }

        FrameworkElement TodoRow(TodoItem item)
        {
            var row = new Border { CornerRadius = new CornerRadius(14), MinHeight = 50, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 4) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Child = g;

            var check = new Grid { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
            var circle = new Ellipse { Stroke = item.Done ? Ui.GreenBrush : Ui.Dim, StrokeThickness = 1.6, Fill = item.Done ? Ui.GreenBrush : Brushes.Transparent };
            var tick = Ui.Icon("", 11, Brushes.Black);
            tick.HorizontalAlignment = HorizontalAlignment.Center;
            tick.Opacity = item.Done ? 1 : 0;
            check.Children.Add(circle);
            check.Children.Add(tick);
            Ui.Clickable(check, () =>
            {
                // Fill first, then let the list re-sort a beat later so the tick registers.
                bool nowDone = !item.Done;
                circle.Fill = nowDone ? Ui.GreenBrush : Brushes.Transparent;
                circle.Stroke = nowDone ? Ui.GreenBrush : Ui.Dim;
                tick.Opacity = nowDone ? 1 : 0;
                var st = (ScaleTransform)check.RenderTransform;
                var pop = new DoubleAnimation(1.35, 1, Ui.Ms(380)) { EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                if (nowDone) Chime.Play();
                var later = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                later.Tick += (s, e) => { later.Stop(); Core.Todos.Toggle(item); };
                later.Start();
            }, null);
            g.Children.Add(check);

            var text = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 14, 10, 14) };
            if (item.Focus)
            {
                var flag = Ui.Icon("", 12, Ui.Orange);
                flag.Margin = new Thickness(0, 1, 9, 0);
                text.Children.Add(flag);
            }
            var label = Ui.T(item.Text, 14, item.Done ? Ui.Dim : Ui.Label, item.Focus ? FontWeights.SemiBold : FontWeights.Normal);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.MaxWidth = 260;
            if (item.Done) label.TextDecorations = TextDecorations.Strikethrough;
            text.Children.Add(label);
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Opacity = 0 };
            TextBlock dummy;
            if (!item.Done)
            {
                var focus = Ui.CircleButton(28, Ui.Raised, item.Focus ? "" : "", 11, item.Focus ? Ui.Label : Ui.Orange, () =>
                {
                    Core.Todos.SetFocus(item, !item.Focus);
                    if (item.Focus) { Core.Pomo.Phase = "focus"; Core.Pomo.Reset(); Core.Pomo.Start(); SelectTab(1, true); }
                    else Core.Pomo.Pause();
                }, out dummy);
                focus.ToolTip = item.Focus ? "Berhenti fokus" : "Fokus ke tugas ini";
                focus.Margin = new Thickness(0, 0, 6, 0);
                actions.Children.Add(focus);
            }
            var del = Ui.CircleButton(28, Ui.Raised, "", 10, Ui.Secondary, () => Core.Todos.Remove(item), out dummy);
            del.ToolTip = "Hapus";
            actions.Children.Add(del);
            Grid.SetColumn(actions, 2);
            g.Children.Add(actions);

            row.MouseEnter += (s, e) => { row.Background = Ui.CardBg; actions.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Ui.Ms(160))); };
            row.MouseLeave += (s, e) => { row.Background = Brushes.Transparent; actions.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ui.Ms(160))); };
            return row;
        }

        // ---------- settings pages ----------

        void Push(Grid page)
        {
            var under = pages.Peek();
            pages.Push(page);
            Panel.SetZIndex(page, pages.Count);
            expandedHost.Children.Add(page);
            Slide(page, ExpW, 0, null);
            Slide(under, 0, -ExpW * 0.3, null);
            Resize(ExpW, (double)page.Tag);
        }

        void Pop()
        {
            if (pages.Count < 2) return;
            var top = pages.Pop();
            Slide(top, 0, ExpW, () => expandedHost.Children.Remove(top));
            Slide(pages.Peek(), -ExpW * 0.3, 0, null);
            Resize(ExpW, CurrentHeight());
        }

        static void Slide(Grid page, double from, double to, Action done)
        {
            var a = new DoubleAnimation(from, to, Ui.Ms(520)) { EasingFunction = new SpringEase() };
            if (done != null) a.Completed += (s, e) => done();
            ((TranslateTransform)page.RenderTransform).BeginAnimation(TranslateTransform.XProperty, a);
        }

        Grid PageShell(string backLabel, string title, double height, out StackPanel body)
        {
            var g = NewPage(height);
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height - 56) });

            var back = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 4, 0, 0) };
            var chev = Ui.Icon("", 13, Ui.Blue);
            chev.Margin = new Thickness(0, 1, 5, 0);
            back.Children.Add(chev);
            back.Children.Add(Ui.T(backLabel, 15, Ui.Blue, FontWeights.Normal));
            Ui.Clickable(back, Pop, null);
            g.Children.Add(back);

            body = new StackPanel { Margin = new Thickness(18, 0, 18, 24) };
            var t = Ui.T(title, 28, Ui.Label, FontWeights.Bold);
            t.Margin = new Thickness(4, 0, 0, 4);
            body.Children.Add(t);
            var sv = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
            Grid.SetRow(sv, 1);
            g.Children.Add(sv);
            return g;
        }

        Grid BuildSettings()
        {
            var S = Core.S;
            StackPanel sp;
            var g = PageShell("Kembali", "Pengaturan", 560, out sp);

            sp.Children.Add(Section("Lokasi & perhitungan"));
            var c1 = new StackPanel();
            c1.Children.Add(Row("Kota", Value(S.City, out cityValue), () => Push(BuildCityPicker())));
            c1.Children.Add(Ui.Sep());
            c1.Children.Add(Row("Metode", Value(Api.MethodName(S.Method), out methodValue), () => Push(BuildMethodPicker())));
            sp.Children.Add(Ui.Card(c1));

            sp.Children.Add(Section("Tampilan"));
            var c2 = new StackPanel();
            var positions = new[] { "left", "center", "right" };
            var posSeg = new Segmented(new[] { "Kiri", "Tengah", "Kanan" }, 60);
            posSeg.Select(Math.Max(0, Array.IndexOf(positions, S.NotchPos)), false);
            posSeg.Changed += i =>
            {
                posSeg.Select(i, true);
                S.NotchPos = positions[i];
                S.Save();
                Position();
            };
            c2.Children.Add(Row("Posisi notch", posSeg, null));
            c2.Children.Add(Ui.Sep());
            c2.Children.Add(Row("Tipis saat ada jendela di bawahnya", Ui.Switch(S.AutoSlim, v => { S.AutoSlim = v; S.Save(); if (!v) SetSlim(false); }), null));
            c2.Children.Add(Ui.Sep());
            c2.Children.Add(Row("Widget di desktop", Ui.Switch(S.Widget, v => Core.SetWidget(v)), null));
            sp.Children.Add(Ui.Card(c2));

            sp.Children.Add(Section("Notifikasi"));
            var c3 = new StackPanel();
            c3.Children.Add(Row("Saat masuk waktu shalat", Ui.Switch(S.Notify, v => { S.Notify = v; S.Save(); }), null));
            c3.Children.Add(Ui.Sep());
            c3.Children.Add(Row("Pengingat 10 menit sebelum", Ui.Switch(S.PreRemind, v => { S.PreRemind = v; S.Save(); }), null));
            c3.Children.Add(Ui.Sep());
            c3.Children.Add(Row("Suara lembut", Ui.Switch(S.Sound, v => { S.Sound = v; S.Save(); if (v) Chime.Play(); }), null));
            sp.Children.Add(Ui.Card(c3));

            sp.Children.Add(Section("Umum"));
            var c4 = new StackPanel();
            c4.Children.Add(Row("Jalankan saat Windows mulai", Ui.Switch(S.Startup, v => { S.Startup = v; S.Save(); Core.ApplyStartup(); }), null));
            sp.Children.Add(Ui.Card(c4));

            var foot = Ui.T("Ctrl + Alt + Spasi — tambah tugas cepat dari mana saja.\nJadwal dari Aladhan API (aladhan.com).", 12, Ui.Secondary, FontWeights.Normal);
            foot.Margin = new Thickness(16, 10, 16, 0);
            foot.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(foot);

            var quit = Ui.T("Keluar dari Waktu Shalat", 15, Ui.B("#FF453A"), FontWeights.Normal);
            quit.Margin = new Thickness(16, 0, 0, 0);
            quit.VerticalAlignment = VerticalAlignment.Center;
            var qg = new Grid { Height = 46 };
            qg.Children.Add(quit);
            Ui.Clickable(qg, Core.Exit, Ui.CardHover);
            var qc = Ui.Card(qg);
            qc.Margin = new Thickness(0, 20, 0, 0);
            sp.Children.Add(qc);
            return g;
        }

        Grid BuildCityPicker()
        {
            var items = Api.Cities.Select(c => (string)c[0]).ToList();
            return BuildPicker("Kota", items, items.IndexOf(Core.S.City), i =>
            {
                var c = Api.Cities[i];
                Core.S.City = (string)c[0]; Core.S.Lat = (double)c[1]; Core.S.Lon = (double)c[2];
                Core.S.Save();
                if (cityValue != null) cityValue.Text = Core.S.City;
                Core.Reload();
            });
        }

        Grid BuildMethodPicker()
        {
            var items = Api.Methods.Select(m => (string)m[1]).ToList();
            int sel = Array.FindIndex(Api.Methods, m => (int)m[0] == Core.S.Method);
            return BuildPicker("Metode", items, sel, i =>
            {
                Core.S.Method = (int)Api.Methods[i][0];
                Core.S.Save();
                if (methodValue != null) methodValue.Text = Api.MethodName(Core.S.Method);
                Core.Reload();
            });
        }

        Grid BuildPicker(string title, List<string> items, int selected, Action<int> onSelect)
        {
            StackPanel sp;
            var g = PageShell("Kembali", title, 560, out sp);
            var col = new StackPanel();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) col.Children.Add(Ui.Sep());
                TextBlock check = i == selected ? Ui.Icon("", 14, Ui.Blue) : null;
                int idx = i;
                col.Children.Add(Row(items[i], check, () => { onSelect(idx); Pop(); }));
            }
            var card = Ui.Card(col);
            card.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(card);
            return g;
        }

        static TextBlock Section(string text)
        {
            var t = Ui.T(text.ToUpper(), 12, Ui.Secondary, FontWeights.Normal);
            t.Margin = new Thickness(16, 28, 0, 9);
            return t;
        }

        static StackPanel Value(string text, out TextBlock valueText)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            valueText = Ui.T(text, 15, Ui.Secondary, FontWeights.Normal);
            sp.Children.Add(valueText);
            var chev = Ui.Icon("", 11, Ui.Tertiary);
            chev.Margin = new Thickness(8, 2, 0, 0);
            sp.Children.Add(chev);
            return sp;
        }

        static Grid Row(string label, FrameworkElement right, Action click)
        {
            var g = new Grid { Height = 52, Background = Brushes.Transparent };
            var l = Ui.T(label, 15, Ui.Label, FontWeights.Normal);
            l.VerticalAlignment = VerticalAlignment.Center;
            l.Margin = new Thickness(16, 0, 0, 0);
            g.Children.Add(l);
            if (right != null)
            {
                right.HorizontalAlignment = HorizontalAlignment.Right;
                right.VerticalAlignment = VerticalAlignment.Center;
                right.Margin = new Thickness(0, 0, 14, 0);
                g.Children.Add(right);
            }
            if (click != null) Ui.Clickable(g, click, Ui.CardHover);
            return g;
        }

        // ---------- refresh ----------

        void Refresh()
        {
            var now = DateTime.Now;
            var n = Core.Next(now);
            var p = Core.Pomo;
            var pomoColor = p.Phase == "focus" ? Ui.Orange : Ui.GreenBrush;
            var focused = Core.Todos.Focused;

            if (n != null)
            {
                cpName.Text = Api.Name(n.Key);
                cpTime.Text = Core.Short(n.Left);
                cpTime.Foreground = n.Left.TotalMinutes < 10 ? Ui.Orange : Ui.Label;
            }
            cmRing.Stroke = pomoColor;
            cmRing.Set(p.Progress);
            cmLabel.Text = p.Phase == "focus" ? "Fokus" : "Istirahat";
            cmTime.Text = Core.Short(p.Remaining);
            cmTime.Foreground = pomoColor;
            cmTask.Text = focused != null && p.Phase == "focus" ? focused.Text : "";
            UpdateCompact();

            if (mode != Mode.Expanded) return;

            // Shalat
            sLoc.Text = Core.S.City + "  ·  " + DateTime.Today.ToString("ddd, d MMM", Core.Id);
            var today = Core.Today;
            sHijri.Text = today != null && today.Date == DateTime.Today ? today.Hijri : "";
            if (n != null)
            {
                sLabel.Text = "MENUJU " + Api.Name(n.Key).ToUpper() + (n.Tomorrow ? " BESOK" : "");
                sCountdown.Text = Core.Hms(n.Left);
                sAt.Text = "pukul " + n.At.ToString("HH:mm");
                sFill.Width = sTrack.ActualWidth * n.Progress;
                for (int i = 0; i < 5; i++)
                {
                    var k = Api.Prayers[i];
                    bool isNext = !n.Tomorrow && k == n.Key;
                    bool past = today.Times[k] <= now;
                    sCellTime[i].Text = today.Times[k].ToString("HH:mm");
                    sCells[i].Background = isNext ? Brushes.White : Ui.CardBg;
                    sCellName[i].Foreground = isNext ? Brushes.Black : Ui.Secondary;
                    sCellTime[i].Foreground = isNext ? Brushes.Black : past ? Ui.Dim : Ui.Label;
                }
                sExtra.Text = "Imsak " + today.Times["Imsak"].ToString("HH:mm") + "   ·   Terbit " + today.Times["Sunrise"].ToString("HH:mm");
            }
            else
            {
                sLabel.Text = "MEMUAT";
                sCountdown.Text = "--:--:--";
                sExtra.Text = Core.Status;
            }

            // Fokus
            fRing.Stroke = pomoColor;
            fRing.Set(p.Progress);
            fTime.Text = Core.Short(p.Remaining);
            fPhase.Text = p.PhaseName.ToUpper();
            fPhase.Foreground = p.Running ? pomoColor : Ui.Secondary;
            fPlayGlyph.Text = p.Running ? "" : "";
            if (p.Phase != "focus") { fTask.Text = "Tarik napas, minum air, gerakkan badan."; fTask.Foreground = Ui.Secondary; }
            else if (focused != null) { fTask.Text = focused.Text; fTask.Foreground = Ui.Label; }
            else { fTask.Text = "Pilih satu tugas untuk difokuskan  ›"; fTask.Foreground = Ui.Secondary; }
            foreach (var c in fChips)
            {
                bool sel = c.Item1 == Core.S.FocusMin;
                c.Item2.Background = sel ? Brushes.White : Ui.CardBg;
                c.Item3.Foreground = sel ? Brushes.Black : Ui.Label;
            }
            int filled = p.Phase == "long" ? 4 : p.Cycle;
            for (int i = 0; i < 4; i++) fDots[i].Fill = i < filled ? Ui.Orange : Ui.Raised;
            int d = p.DoneToday;
            fSessions.Text = d == 0 ? "Belum ada sesi hari ini" : d + " sesi hari ini";
        }
    }
}
