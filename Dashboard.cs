using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

internal sealed partial class LecooPanel
{
    private readonly ActionButton service = Button("Установить службу");
    private readonly ActionButton refresh = Button("↻  Обновить", true);
    private readonly Toggle auto = new Toggle { Text = "Автообновление" };
    private readonly Label status = Label("Подключение к Lecoo…", Palette.Ink);
    private readonly Label updatedAt = Label("Обновлено —", Palette.Muted);
    private readonly Label connection = Label("●  Подключение…", Palette.Warning);
    private readonly Label footerConnection = Label("●  Подключение…", Palette.Warning);
    private readonly Label[] heat = new Label[2], rpm = new Label[2], rpmUnits = new Label[2], batteryFields = new Label[5], chargeFields = new Label[4];
    private readonly Label batteryLevel = Label("—", Palette.Mint, 30, true);
    private readonly Label batteryState = Label("—", Palette.Muted);
    private readonly Glyph batteryGlyph = new Glyph("battery");
    private readonly Panel scroll = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Palette.Base };
    private TableLayoutPanel feed, monitor, settings, header, actions;
    private Control heatCard, fanCard, batteryCard, powerCard, coolingCard, brand;
    private bool? stacked;
    private float layoutScale;
    private int layoutWidth;
    private int layoutHeight;
    private Font dashboardFont;
    private readonly System.Collections.Generic.List<Action<float>> dpiRules = new System.Collections.Generic.List<Action<float>>();
    private readonly ToolTip hints = new ToolTip { OwnerDraw = true };

    private void BuildDashboard()
    {
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None;
        Text = "Lecoo Panel";
        dashboardFont = new Font("Segoe UI", 14, GraphicsUnit.Pixel);
        Font = dashboardFont;
        Disposed += (s, e) => dashboardFont.Dispose();
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Normal;
        ClientSize = new Size(1200, 820);
        BackColor = Palette.Base;
        ForeColor = Palette.Ink;
        hints.Popup += (s, e) => e.ToolTipSize = TextRenderer.MeasureText(hints.GetToolTip(e.AssociatedControl), Font) + new Size(18, 12);
        hints.Draw += (s, e) =>
        {
            using (var fill = new SolidBrush(Palette.Field)) e.Graphics.FillRectangle(fill, e.Bounds);
            using (var border = new Pen(Palette.Edge)) e.Graphics.DrawRectangle(border, Rectangle.Inflate(e.Bounds, -1, -1));
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, Font, e.Bounds, Palette.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        };

        var frame = Grid(1);
        frame.RowCount = 3;
        frame.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        frame.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        Controls.Add(frame);
        frame.Controls.Add(BuildCaption(), 0, 0);
        frame.Controls.Add(scroll, 0, 1);
        feed = Grid(1);
        feed.Dock = DockStyle.None;
        feed.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        feed.Padding = new Padding(21, 9, 21, 0);
        scroll.Controls.Add(feed);
        header = new CardGrid { ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0), Banner = true };
        monitor = Grid(3);
        settings = Grid(2);
        feed.Controls.Add(header, 0, 0);
        feed.Controls.Add(monitor, 0, 1);
        feed.Controls.Add(settings, 0, 2);

        brand = BuildBrand();
        actions = Grid(6);
        actions.Margin = new Padding(4, 16, 8, 0);
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int width in new[] { 137, 16, 174, 16, 201 }) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        actions.RowCount = 2;
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        var badge = new CardGrid { Inset = true, Dock = DockStyle.Fill, Margin = new Padding(5, 6, 0, 6), Padding = new Padding(8, 0, 4, 0) };
        badge.Controls.Add(connection, 0, 0);
        actions.Controls.Add(refresh, 1, 0);
        actions.Controls.Add(auto, 3, 0);
        actions.Controls.Add(badge, 5, 0);
        actions.Controls.Add(service, 3, 1);
        actions.SetColumnSpan(service, 3);
        service.Visible = false;

        heatCard = PairCard("Температуры", "temp", new[] { "ЦП", "Система" }, heat);
        fanCard = PairCard("Вентиляторы", "fan", new[] { "CPU", "GPU" }, rpm);
        batteryCard = BuildBattery();
        powerCard = BuildPower();
        coolingCard = BuildCooling();

        var footer = new CardGrid { ColumnCount = 4, RowCount = 1, Dock = DockStyle.Fill, Margin = new Padding(29, 6, 29, 16), Padding = new Padding(12, 0, 5, 0), Inset = true };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 214));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 123));
        status.AutoEllipsis = updatedAt.AutoEllipsis = footerConnection.AutoEllipsis = true;
        status.Margin = updatedAt.Margin = footerConnection.Margin = new Padding(2, 0, 2, 0);
        footer.Controls.Add(status, 0, 0);
        footer.Controls.Add(updatedAt, 1, 0);
        footer.Controls.Add(footerConnection, 2, 0);
        var details = Button("Диагностика");
        footer.Controls.Add(details, 3, 0);
        details.Click += (s, e) => ShowDiagnostics();
        frame.Controls.Add(footer, 0, 2);
        hints.SetToolTip(refresh, "Запросить показания Lecoo сейчас");
        hints.SetToolTip(auto, "Обновлять показания каждые 5 секунд, пока окно открыто");
        hints.SetToolTip(details, "Исходные ответы CLI и ошибки подключения");
        refresh.Click += async (s, e) => await RefreshAsync();
        auto.CheckedChanged += (s, e) => UpdateTimer();
        service.Click += async (s, e) => await SetUpServiceAsync();
        scroll.Resize += (s, e) => { if (IsHandleCreated && Visible) ArrangeDashboard(); };
        RememberMetrics(frame);
        foreach (var section in new[] { brand, actions, heatCard, fanCard, batteryCard, powerCard, coolingCard })
            RememberMetrics(section);
        ResumeLayout(true);
    }

    private Control BuildCaption()
    {
        var caption = Grid(4);
        caption.RowCount = 1;
        caption.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        caption.BackColor = Palette.Field;
        caption.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 33));
        caption.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        caption.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        caption.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        var smallIcon = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Palette.Field };
        smallIcon.Paint += (s, e) =>
        {
            int side = Math.Max(1, (int)Math.Round(17 * layoutScale));
            e.Graphics.DrawIcon(appIcon, new Rectangle((smallIcon.Width - side) / 2, (smallIcon.Height - side) / 2, side, side));
        };
        var captionText = Label("Lecoo Panel", Palette.Ink, 10);
        foreach (var part in new Control[] { caption, smallIcon, captionText })
            part.MouseDown += (s, e) => CaptionPointer(e);
        var minimize = new CaptionButton(false);
        var close = new CaptionButton(true);
        minimize.Click += (s, e) => Hide();
        close.Click += (s, e) => Close();
        caption.Controls.Add(smallIcon, 0, 0);
        caption.Controls.Add(captionText, 1, 0);
        caption.Controls.Add(minimize, 2, 0);
        caption.Controls.Add(close, 3, 0);
        return caption;
    }

    private Control BuildBrand()
    {
        var row = Grid(2);
        row.RowCount = 1;
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 83));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var icon = new CardGrid { Inset = true, Dock = DockStyle.Fill, Margin = new Padding(5, 13, 10, 10) };
        using (var resized = new Icon(appIcon, new Size(48, 48))) headingIcon = resized.ToBitmap();
        icon.Paint += (s, e) =>
        {
            int side = Math.Max(1, Math.Min((int)(52 * layoutScale), Math.Min(icon.Width, icon.Height) - 6));
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(headingIcon, (icon.Width - side) / 2, (icon.Height - side) / 2, side, side);
        };
        var words = Grid(1);
        words.RowCount = 2;
        words.Padding = new Padding(3, 10, 0, 8);
        words.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        words.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        words.Controls.Add(Label("Lecoo Panel", Palette.Ink, 24, true), 0, 0);
        words.Controls.Add(Label("LECOO PRO 14 · N155A", Palette.Mint, 12), 0, 1);
        row.Controls.Add(icon, 0, 0);
        row.Controls.Add(words, 1, 0);
        return row;
    }

    private void ArrangeDashboard()
    {
        if (coolingCard == null || IsDisposed || !IsHandleCreated) return;
        float scale = WindowDpi / 96f;
        int width = scroll.ClientSize.Width, height = scroll.ClientSize.Height;
        if (width <= 0 || height <= 0) return;
        bool narrow = width < 1140 * scale;
        if (stacked == narrow && layoutScale == scale && layoutWidth == width && layoutHeight == height) return;
        bool scaleChanged = layoutScale != scale;
        stacked = narrow;
        layoutScale = scale;
        layoutWidth = width;
        layoutHeight = height;
        if (scaleChanged)
        {
            SuspendLayout();
            var previousFont = dashboardFont;
            dashboardFont = new Font("Segoe UI", 14 * scale, GraphicsUnit.Pixel);
            Font = dashboardFont;
            previousFont.Dispose();
            foreach (var rule in dpiRules) rule(scale);
            ResumeLayout(false);
        }

        feed.SuspendLayout();
        Arrange(header, narrow ? new[] { 100f } : new[] { 44f, 56f },
            narrow ? new[] { 94, 102 } : new[] { 103 }, new[] { brand, actions }, scale);
        Arrange(monitor, narrow ? new[] { 50f, 50f } : new[] { 29f, 29f, 42f },
            narrow ? new[] { 218, 239 } : new[] { 226 }, new[] { heatCard, fanCard, batteryCard }, scale);
        if (narrow) monitor.SetColumnSpan(batteryCard, 2);
        Arrange(settings, narrow ? new[] { 100f } : new[] { 45f, 55f },
            narrow ? new[] { 386, 386 } : new[] { 374 }, new[] { powerCard, coolingCard }, scale);
        feed.RowStyles.Clear();
        var heights = narrow ? new[] { 196, 457, 772 } : new[] { 103, 226, 374 };
        int total = feed.Padding.Vertical;
        foreach (int h in heights)
        {
            int pixels = (int)Math.Round(h * scale);
            feed.RowStyles.Add(new RowStyle(SizeType.Absolute, pixels));
            total += pixels;
        }
        // Maintain readable type and full control widths on displays that need scrolling.
        int needed = (int)Math.Ceiling(680 * scale);
        int scrollbar = (int)Math.Ceiling(18 * scale);
        int contentWidth = Math.Max(needed, width - (total > height ? scrollbar : 0));
        var origin = scroll.AutoScrollPosition;
        feed.SetBounds(origin.X, origin.Y, contentWidth, total);
        bool scrollNeeded = total > height || contentWidth > width;
        scroll.AutoScrollMinSize = scrollNeeded ? new Size(contentWidth, total) : Size.Empty;
        scroll.AutoScroll = scrollNeeded;
        feed.ResumeLayout(true);
    }
    private void RememberMetrics(Control control)
    {
        var padding = control.Padding;
        var label = control as Label;
        if (label != null)
        {
            var original = label.Font;
            Font scaled = null;
            dpiRules.Add(scale =>
            {
                var previous = scaled;
                scaled = new Font(original.FontFamily, original.Size * scale, original.Style, original.Unit);
                label.Font = scaled;
                if (previous != null) previous.Dispose();
            });
            label.Disposed += (s, e) =>
            {
                if (scaled != null) scaled.Dispose();
                original.Dispose();
            };
        }
        var margin = control.Margin;
        dpiRules.Add(scale =>
        {
            control.Padding = ScalePadding(padding, scale);
            control.Margin = ScalePadding(margin, scale);
        });
        var table = control as TableLayoutPanel;
        if (table != null)
        {
            foreach (RowStyle row in table.RowStyles)
            {
                if (row.SizeType != SizeType.Absolute) continue;
                var size = row.Height;
                dpiRules.Add(scale => row.Height = (float)Math.Round(size * scale));
            }
            foreach (ColumnStyle column in table.ColumnStyles)
            {
                if (column.SizeType != SizeType.Absolute) continue;
                var size = column.Width;
                dpiRules.Add(scale => column.Width = (float)Math.Round(size * scale));
            }
        }
        foreach (Control child in control.Controls) RememberMetrics(child);
    }

    private static Padding ScalePadding(Padding value, float scale)
    {
        return new Padding((int)Math.Round(value.Left * scale), (int)Math.Round(value.Top * scale),
            (int)Math.Round(value.Right * scale), (int)Math.Round(value.Bottom * scale));
    }

    private static void Arrange(TableLayoutPanel container, float[] widths, int[] heights, Control[] children, float scale)
    {
        container.SuspendLayout();
        container.Controls.Clear();
        container.ColumnStyles.Clear();
        container.RowStyles.Clear();
        container.ColumnCount = widths.Length;
        container.RowCount = heights.Length;
        foreach (float width in widths) container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        foreach (int height in heights) container.RowStyles.Add(new RowStyle(SizeType.Absolute, (float)Math.Round(height * scale)));
        for (int i = 0; i < children.Length; i++)
        {
            container.SetColumnSpan(children[i], 1);
            container.Controls.Add(children[i], i % widths.Length, i / widths.Length);
        }
        container.ResumeLayout(true);
    }

    private static CardGrid Section(string title, string glyph)
    {
        var box = new CardGrid { ColumnCount = 1, Dock = DockStyle.Fill, Padding = new Padding(21, 10, 21, 14), Margin = new Padding(8, 7, 8, 9) };
        var heading = Grid(2);
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.Controls.Add(new Glyph(glyph), 0, 0);
        heading.Controls.Add(Label(title, Palette.Ink, 15, true), 1, 0);
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        box.Controls.Add(heading, 0, 0);
        return box;
    }

    private Control PairCard(string title, string glyph, string[] names, Label[] values)
    {
        var box = Section(title, glyph);
        box.Wave = true;
        box.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        box.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        for (int i = 0; i < 2; i++)
        {
            var line = Grid(glyph == "fan" ? 3 : 2);
            line.Controls.Add(Label(names[i], Palette.Ink, 11), 0, 0);
            if (glyph == "fan")
            {
                line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
                line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
                line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
                values[i] = Label("—", Palette.Mint, 21, true);
                values[i].TextAlign = ContentAlignment.MiddleRight;
                values[i].AutoEllipsis = false;
                rpmUnits[i] = Label("", Palette.Muted, 9.5f);
                rpmUnits[i].TextAlign = ContentAlignment.MiddleLeft;
                line.Controls.Add(values[i], 1, 0);
                line.Controls.Add(rpmUnits[i], 2, 0);
            }
            else
            {
                line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
                line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
                values[i] = Label("—", Palette.Mint, 26, true);
                values[i].TextAlign = ContentAlignment.MiddleRight;
                values[i].AutoEllipsis = false;
                line.Controls.Add(values[i], 1, 0);
            }
            box.Controls.Add(line, 0, i == 0 ? 1 : 3);
        }
        box.Controls.Add(Divider(), 0, 2);
        return box;
    }

    private Control BuildBattery()
    {
        var box = Section("Батарея", "battery");
        box.Wave = true;
        box.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var body = Grid(3);
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        var summary = Grid(1);
        summary.Padding = new Padding(0, 2, 5, 2);
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        batteryLevel.TextAlign = batteryState.TextAlign = ContentAlignment.MiddleCenter;
        summary.Controls.Add(batteryGlyph, 0, 0);
        summary.Controls.Add(batteryLevel, 0, 1);
        summary.Controls.Add(batteryState, 0, 2);
        body.Controls.Add(summary, 0, 0);
        body.Controls.Add(Divider(), 1, 0);
        var details = FieldList(new[] { "Напряжение", "Заряд", "Энергия", "Здоровье", "Циклы" }, batteryFields, 103);
        details.Padding = new Padding(9, 0, 0, 0);
        body.Controls.Add(details, 2, 0);
        box.Controls.Add(body, 0, 1);
        return box;
    }

    private Control BuildPower()
    {
        var box = Section("Питание и аккумулятор", "battery");
        Rows(box, 52, 160, 1, 52, 33);
        box.Controls.Add(Setting("Лимит заряда", "charge", new[] { "Полный", "Высокий", "Баланс", "Ресурс", "От сети" },
            new[] { "full", "high", "balanced", "lifespan", "desk" }), 0, 1);
        var charging = new CardGrid { Inset = true, ColumnCount = 2, Dock = DockStyle.Fill, Padding = new Padding(13, 9, 12, 10), Margin = new Padding(0, 8, 0, 11) };
        charging.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        charging.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var glyph = new Glyph("battery");
        glyph.Margin = new Padding(3, 8, 19, 8);
        charging.Controls.Add(glyph, 0, 0);
        var info = Grid(1);
        Rows(info, 24);
        info.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        info.Controls.Add(Label("Состояние зарядки", Palette.Mint, 11, true), 0, 0);
        info.Controls.Add(FieldList(new[] { "Целевое", "Реальное", "Батарея", "Ожидание" }, chargeFields, 98), 0, 1);
        charging.Controls.Add(info, 1, 0);
        box.Controls.Add(charging, 0, 2);
        box.Controls.Add(Divider(), 0, 3);
        box.Controls.Add(Setting("Профиль питания", "power", new[] { "Тихий", "Стандартный", "Производительность" },
            new[] { "silent", "default", "perf" }), 0, 4);
        box.Controls.Add(Label("ⓘ  Может конфликтовать со штатным Fn+Q.", Palette.Muted), 0, 5);
        return box;
    }

    private Control BuildCooling()
    {
        var box = Section("Подсветка и охлаждение", "fan");
        Rows(box, 84, 1, 32, 49, 44, 49, 27);
        var lights = Grid(2);
        lights.RowCount = 1;
        lights.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        lights.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lights.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lights.Controls.Add(Setting("Клавиатура", "kbd", new[] { "Выключена", "Низкая", "Средняя", "Высокая", "Максимум (255)" },
            new[] { "off", "low", "medium", "high", "custom 255" }, true), 0, 0);
        lights.Controls.Add(Setting("Кольцо сзади", "led", new[] { "Авто", "Выключено", "Плавная пульсация" },
            new[] { "auto", "custom 0", "breath smooth" }, true), 1, 0);
        box.Controls.Add(lights, 0, 1);
        box.Controls.Add(Divider(), 0, 2);
        box.Controls.Add(Label("Охлаждение", Palette.Ink, 14, true), 0, 3);

        var target = new Choice("Оба вентилятора", "Процессор (CPU)", "Графика (GPU)") { AccessibleName = "Вентилятор", SelectedIndex = 0 };
        var mode = new Choice("Авто", "Полная скорость", "Ручной") { AccessibleName = "Режим охлаждения", SelectedIndex = 0 };
        box.Controls.Add(TwoColumns(target, mode), 0, 4);
        var pwm = new PwmSlider { AccessibleName = "Мощность вентилятора, ШИМ", Enabled = false };
        var pwmValue = Label("150 / 165", Palette.Dim, 11);
        pwmValue.TextAlign = ContentAlignment.MiddleCenter;
        var slider = Grid(2);
        slider.RowCount = 1;
        slider.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        slider.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        slider.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 101));
        slider.Controls.Add(pwm, 0, 0);
        var pwmBadge = new CardGrid { Inset = true, Dock = DockStyle.Fill, Margin = new Padding(6, 5, 3, 5) };
        pwmBadge.Controls.Add(pwmValue, 0, 0);
        slider.Controls.Add(pwmBadge, 1, 0);
        box.Controls.Add(slider, 0, 5);
        pwm.ValueChanged += (s, e) => pwmValue.Text = pwm.Value + " / 165";
        mode.Changed += (s, e) =>
        {
            pwm.Enabled = mode.SelectedIndex == 2;
            pwmValue.ForeColor = pwm.Enabled ? Palette.Ink : Palette.Dim;
        };
        var apply = Button("Применить", true);
        var reset = Button("Оба вентилятора в авто");
        RegisterCommand(apply);
        RegisterCommand(reset);
        apply.Click += async (s, e) =>
        {
            int selected = mode.SelectedIndex;
            var arg = "fan " + new[] { "both", "cpu", "gpu" }[target.SelectedIndex] + " " + new[] { "auto", "full", "custom" }[selected];
            if (selected == 2)
            {
                arg += " " + pwm.Value.ToString(CultureInfo.InvariantCulture);
                if (!ConfirmManual()) return;
            }
            await ApplyAsync(arg);
        };
        reset.Click += async (s, e) => await ApplyAsync("fan both auto", () => { target.SelectedIndex = mode.SelectedIndex = 0; });
        box.Controls.Add(TwoColumns(apply, reset), 0, 6);
        box.Controls.Add(Label("Ползунок задаёт ШИМ. Фактические обороты в об/мин показаны выше.", Palette.Muted, 9.5f), 0, 7);
        return box;
    }

    private Control Setting(string title, string command, string[] names, string[] args, bool stackedLabel = false)
    {
        var row = Grid(stackedLabel ? 2 : 3);
        row.RowCount = stackedLabel ? 2 : 1;
        var picker = new Choice(names) { AccessibleName = title };
        var apply = Button("Применить", !stackedLabel);
        if (!stackedLabel) row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        if (stackedLabel)
        {
            row.Margin = new Padding(3, 0, 5, 0);
            Rows(row, 26, 49);
        }
        else row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = Label(title, Palette.Ink, 10);
        row.Controls.Add(label, 0, 0);
        if (stackedLabel) row.SetColumnSpan(label, 2);
        row.Controls.Add(picker, stackedLabel ? 0 : 1, stackedLabel ? 1 : 0);
        row.Controls.Add(apply, stackedLabel ? 1 : 2, stackedLabel ? 1 : 0);
        RegisterCommand(apply, picker);
        apply.Click += async (s, e) => await ApplyAsync(command + " " + args[picker.SelectedIndex]);
        return row;
    }

    private bool ConfirmManual()
    {
        using (var dialog = Dialog("Ручное охлаждение", new Size(500, 164)))
        {
            var body = Grid(1);
            body.Padding = new Padding(16);
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Rows(body, 46);
            body.Controls.Add(Label("Ручной режим фиксирует мощность вентилятора. Слишком низкое значение под нагрузкой может привести к перегреву. Применить?", Palette.Warning), 0, 0);
            var apply = Button("Применить", true);
            var cancel = Button("Отмена");
            apply.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            dialog.AcceptButton = cancel;
            dialog.CancelButton = cancel;
            dialog.Shown += (s, e) => cancel.Focus();
            body.Controls.Add(TwoColumns(apply, cancel), 0, 1);
            dialog.Controls.Add(body);
            return dialog.ShowDialog(this) == DialogResult.OK;
        }
    }

    private void ShowDiagnostics()
    {
        using (var form = Dialog("Диагностика Lecoo", new Size(680, 415)))
        {
            var area = Grid(1);
            area.Padding = new Padding(14);
            area.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Rows(area, 42);
            var text = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                Dock = DockStyle.Fill, Text = diagnosticText(), BackColor = Palette.Field, ForeColor = Palette.Ink
            };
            var close = Button("Закрыть");
            close.DialogResult = DialogResult.Cancel;
            form.CancelButton = close;
            area.Controls.Add(text, 0, 0);
            area.Controls.Add(close, 0, 1);
            form.Controls.Add(area);
            form.ShowDialog(this);
        }
    }

    private Form Dialog(string title, Size size)
    {
        return new Form
        {
            Text = title, Icon = appIcon, ClientSize = size, StartPosition = FormStartPosition.CenterParent,
            BackColor = Palette.Base, ForeColor = Palette.Ink, Font = Font, ShowInTaskbar = false,
            AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96), MinimizeBox = false, MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog
        };
    }

    private static TableLayoutPanel FieldList(string[] names, Label[] values, int keyWidth)
    {
        var grid = Grid(2);
        grid.RowCount = names.Length;
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, keyWidth));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < names.Length; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / names.Length));
            var name = Label(names[i], Palette.Muted, 9.5f);
            name.AutoEllipsis = true;
            grid.Controls.Add(name, 0, i);
            values[i] = Label("—", Palette.Ink, 9.5f);
            values[i].AutoEllipsis = true;
            grid.Controls.Add(values[i], 1, i);
        }
        return grid;
    }

    private static TableLayoutPanel Grid(int columns)
    {
        return new LayoutGrid { ColumnCount = columns, Dock = DockStyle.Fill, Margin = new Padding(0) };
    }

    private static TableLayoutPanel TwoColumns(Control first, Control second)
    {
        var grid = Grid(2);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.Controls.Add(first, 0, 0);
        grid.Controls.Add(second, 1, 0);
        return grid;
    }

    private static void Rows(TableLayoutPanel box, params int[] heights)
    {
        foreach (int height in heights) box.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
    }

    private static Control Divider()
    {
        return new Panel { Dock = DockStyle.Fill, BackColor = Palette.Edge, Margin = new Padding(0) };
    }

    private static Label Label(string text, Color color, float size = 10.5f, bool semibold = false)
    {
        return new Label
        {
            Text = text, ForeColor = color, BackColor = Color.Transparent, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(2, 0, 2, 0),
            Font = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size * 4 / 3, GraphicsUnit.Pixel), UseMnemonic = false
        };
    }

    private static ActionButton Button(string title, bool primary = false)
    {
        return new ActionButton { Text = title, Primary = primary, AccessibleName = title };
    }
}
