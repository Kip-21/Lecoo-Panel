using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class LecooPanel : Form
{
    private const string ServiceName = "LecooControlDaemon";
    private readonly LecooClient client = new LecooClient(AppDomain.CurrentDomain.BaseDirectory);
    private readonly Timer timer = new Timer { Interval = 5000 };
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
    private readonly Dictionary<ActionButton, Choice> commands = new Dictionary<ActionButton, Choice>();
    private readonly Dictionary<string, CliResult> readings = new Dictionary<string, CliResult>();
    private ServiceControllerStatus? serviceState;
    private bool refreshInProgress, busy, connected, exitRequested;
    private DateTime? lastRefresh;
    private string lastError, lastCommand;
    private Icon appIcon;
    private Bitmap headingIcon;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr window, bool revert);

    [DllImport("user32.dll")]
    private static extern uint EnableMenuItem(IntPtr menu, uint item, uint flags);

    private int WindowDpi
    {
        get
        {
            if (!IsHandleCreated) return 96;
            try
            {
                uint dpi = GetDpiForWindow(Handle);
                if (dpi != 0) return (int)dpi;
            }
            catch (EntryPointNotFoundException) { }
            using (var graphics = CreateGraphics()) return (int)graphics.DpiX;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr options);

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.Style &= ~0x00C00000; // The app paints its caption, with no maximize affordance.
            parameters.Style |= 0x00800000; // Keep a thin, non-resizable native frame.
            return parameters;
        }
    }

    private void CaptionPointer(MouseEventArgs e)
    {
        if (e.Clicks != 1) return;
        if (e.Button == MouseButtons.Left)
        {
            ReleaseCapture();
            SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
        }
        else if (e.Button == MouseButtons.Right)
        {
            var point = Cursor.Position;
            var command = TrackPopupMenuEx(GetSystemMenu(Handle, false), 0x0102, point.X, point.Y, Handle, IntPtr.Zero);
            if (command != 0) SendMessage(Handle, 0x0112, new IntPtr((int)command), IntPtr.Zero);
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        FitFixedWindow(true);
        base.OnLoad(e);
    }

    private void FitFixedWindow(bool center)
    {
        var work = Screen.FromHandle(Handle).WorkingArea;
        float scale = WindowDpi / 96f;
        var client = new Size((int)Math.Round(1200 * scale), (int)Math.Round(820 * scale));
        var outer = SizeFromClientSize(client);
        outer.Width = Math.Min(outer.Width, Math.Max(1, work.Width - 20));
        outer.Height = Math.Min(outer.Height, Math.Max(1, work.Height - 20));
        int x = center ? work.Left + (work.Width - outer.Width) / 2
            : Math.Max(work.Left, Math.Min(Left, work.Right - outer.Width));
        int y = center ? work.Top + (work.Height - outer.Height) / 2
            : Math.Max(work.Top, Math.Min(Top, work.Bottom - outer.Height));
        MaximumSize = MinimumSize = Size.Empty;
        Bounds = new Rectangle(x, y, outer.Width, outer.Height);
        MinimumSize = MaximumSize = Size;
        ArrangeDashboard();
    }

    protected override void WndProc(ref Message message)
    {
        const int sysCommand = 0x0112, nonClientDoubleClick = 0x00A3, dpiChanged = 0x02E0;
        const int sizeCommand = 0xF000, minimizeCommand = 0xF020, maximizeCommand = 0xF030, caption = 2;
        int command = (int)(message.WParam.ToInt64() & 0xFFF0);
        if (message.Msg == sysCommand && command == minimizeCommand)
        {
            Hide();
            return;
        }
        if ((message.Msg == sysCommand && (command == sizeCommand || command == maximizeCommand)) ||
            (message.Msg == nonClientDoubleClick && message.WParam.ToInt64() == caption))
            return;
        base.WndProc(ref message);
        if (message.Msg == dpiChanged && IsHandleCreated && Visible)
            BeginInvoke((Action)(() => { if (!IsDisposed && WindowState == FormWindowState.Normal) FitFixedWindow(false); }));
    }

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new LecooPanel());
    }

    private LecooPanel()
    {
        appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
        Icon = appIcon;
        BuildDashboard();
        ConfigureTray();
        UpdateCommandButtons();
        timer.Tick += async (s, e) => await RefreshAsync(true);
        Shown += async (s, e) => await RefreshAsync();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var systemMenu = GetSystemMenu(Handle, false);
        if (systemMenu != IntPtr.Zero)
        {
            EnableMenuItem(systemMenu, 0xF000, 1);
            EnableMenuItem(systemMenu, 0xF030, 1);
        }
        int enabled = 1;
        try { DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)); }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }
    }

    private void ConfigureTray()
    {
        trayMenu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
        trayMenu.Items.Add("Открыть Lecoo Panel").Click += (s, e) => RestoreWindow();
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Выход").Click += (s, e) => { exitRequested = true; Close(); };
        foreach (ToolStripItem item in trayMenu.Items) item.ForeColor = Palette.Ink;
        tray.Icon = appIcon;
        tray.Text = "Lecoo Panel";
        tray.ContextMenuStrip = trayMenu;
        tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) RestoreWindow(); };
        tray.Visible = true;
        Disposed += (s, e) =>
        {
            headingIcon.Dispose();
            appIcon.Dispose();
        };
        VisibleChanged += (s, e) => UpdateTimer();
        Resize += (s, e) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosing += (s, e) =>
        {
            if (e.CloseReason != CloseReason.UserClosing || exitRequested) return;
            e.Cancel = true;
            Hide();
        };
        FormClosed += (s, e) =>
        {
            timer.Dispose();
            tray.Visible = false;
            tray.Dispose();
            trayMenu.Dispose();
            hints.Dispose();
        };
    }

    private async void RestoreWindow()
    {
        bool hidden = !Visible;
        WindowState = FormWindowState.Normal;
        Show();
        Activate();
        if (hidden) await RefreshAsync(true);
    }

    private void UpdateTimer()
    {
        timer.Enabled = Visible && WindowState != FormWindowState.Minimized && auto.Checked;
    }

    private void RegisterCommand(ActionButton button, Choice choice = null)
    {
        commands.Add(button, choice);
        if (choice != null) choice.Changed += (s, e) => UpdateCommandButtons();
    }

    private void UpdateCommandButtons()
    {
        foreach (var command in commands)
            command.Key.Enabled = connected && !busy && (command.Value == null || command.Value.SelectedIndex >= 0);
    }

    private void PaintConnection()
    {
        var color = refreshInProgress ? Palette.Warning : connected ? Palette.Good : Palette.Error;
        connection.ForeColor = footerConnection.ForeColor = color;
        connection.Text = refreshInProgress ? "●  Подключение…" : connected
            ? "●  Служба подключена" : "●  Служба недоступна";
        footerConnection.Text = refreshInProgress ? "●  Проверка службы"
            : connected ? "●  LecooControlDaemon: активна" : "●  Нет связи со службой";
        hints.SetToolTip(footerConnection, footerConnection.Text);
    }

    private void SetStatus(string text, Color color)
    {
        status.Text = text;
        status.ForeColor = color;
        hints.SetToolTip(status, text);
    }

    private async Task RefreshAsync(bool automatic = false)
    {
        if (automatic && !Visible) return;
        await RunAsync(ReadMetricsAsync, "Подключение к Lecoo…", true);
    }

    private async Task ReadMetricsAsync()
    {
        try
        {
            serviceState = await Task.Run(() => client.CurrentService());
        }
        catch
        {
            connected = false;
            readings.Clear();
            if (!IsDisposed)
            {
                UpdateReadouts();
                service.Text = "Открыть службы Windows";
                service.Visible = true;
            }
            throw;
        }
        if (IsDisposed) return;
        if (serviceState != ServiceControllerStatus.Running)
        {
            connected = false;
            readings.Clear();
            UpdateReadouts();
            service.Text = serviceState == null ? "Установить службу" : "Запустить службу";
            service.Visible = true;
            SetStatus(serviceState == null ? "Для доступа к ноутбуку установите службу Lecoo."
                : serviceState == ServiceControllerStatus.Stopped ? "Служба Lecoo остановлена."
                : "Служба Lecoo: " + serviceState.Value + ".", Palette.Warning);
            return;
        }

        var snapshot = await Task.Run(() => client.ReadSnapshot());
        if (IsDisposed) return;
        readings.Clear();
        foreach (var value in snapshot) readings.Add(value.Key, value.Value);
        connected = readings.Values.Any(x => x.Success);
        service.Text = "Открыть службы Windows";
        service.Visible = !connected;
        bool complete = UpdateReadouts();
        if (complete)
        {
            lastRefresh = DateTime.Now;
            updatedAt.Text = "Обновлено " + lastRefresh.Value.ToString("HH:mm:ss");
            hints.SetToolTip(updatedAt, "Последнее успешное обновление данных");
        }
        SetStatus(complete ? "Показания обновлены"
            : connected ? "Часть данных недоступна. См. диагностику."
            : "Lecoo не отвечает. Откройте службы Windows или диагностику.", complete ? Palette.Ink : connected ? Palette.Warning : Palette.Error);
    }

    private bool UpdateReadouts()
    {
        var temp = Fields("temps");
        var fan = Fields("fans");
        var bat = Fields("battery");
        var charge = Fields("charge");
        heat[0].Text = temp.Get("ЦП", "CPU");
        heat[1].Text = temp.Get("Система", "System");
        SetFanReading(0, fan.Get("ЦП", "CPU"));
        SetFanReading(1, fan.Get("ГП", "GPU"));
        var values = new[] { bat.Get("Напряжение", "Voltage"), bat.Get("Заряд", "Charge"), bat.Get("Энергия", "Energy"),
            bat.Get("Здоровье", "Health"), bat.Get("Циклы", "Cycles") };
        var match = Regex.Match(values[1], @"\(\s*(\d+(?:[.,]\d+)?)\s*%\s*\)");
        var percent = match.Success ? match.Groups[1].Value : null;
        int? fillLevel = null;
        decimal amount = 0;
        if (percent != null && decimal.TryParse(percent.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out amount)
            && amount >= 0 && amount <= 100) fillLevel = (int)Math.Round(amount);
        batteryLevel.Text = fillLevel.HasValue ? FormatBatteryPercentage(amount) : "—";
        var reportedState = bat.Get("Состояние", "State");
        batteryState.Text = reportedState == "On AC, not charging" || reportedState == "От сети, не заряжается"
            ? "От сети," + Environment.NewLine + "не заряжается"
            : reportedState == "Charging" ? "Заряжается" : reportedState == "Discharging" ? "Разряжается" : reportedState;
        batteryGlyph.SetBattery(fillLevel, batteryState.Text == "Заряжается");
        values[1] = Regex.Replace(values[1], @"\s*\((?:\d+(?:[.,]\d+)?%|n/a)\)\s*$", "", RegexOptions.IgnoreCase);
        AssignFields(batteryFields, values);
        AssignFields(chargeFields, new[] { charge.Get("Целевое", "Desired"), charge.Get("Реальное", "Effective"),
            charge.Get("Батарея", "Battery"), charge.Get("Ожидание", "Pending") });
        return readings.Count == 4 && readings.Values.All(x => x.Success) &&
            heat.Concat(rpm).Concat(batteryFields).Concat(chargeFields.Take(3)).All(x => x.Text != "—" && x.Text != "n/a") &&
            fillLevel.HasValue && batteryState.Text != "—";
    }

    internal static string FormatBatteryPercentage(decimal percentage)
    {
        return percentage.ToString("0.#", CultureInfo.GetCultureInfo("ru-RU")) + "%";
    }

    private void SetFanReading(int index, string value)
    {
        var match = Regex.Match(value, @"^\s*(\d+(?:[\s,]\d{3})*)\s*(?:RPM|об/мин)?\s*$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            rpm[index].Text = Regex.Replace(match.Groups[1].Value, @"[\s,]", "");
            rpmUnits[index].Text = "об/мин";
        }
        else
        {
            rpm[index].Text = value;
            rpmUnits[index].Text = "";
        }
    }

    private void AssignFields(Label[] labels, string[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            labels[i].Text = values[i];
            hints.SetToolTip(labels[i], values[i] == "—" ? "" : values[i]);
        }
    }

    private CliFields Fields(string operation)
    {
        CliResult reply;
        return new CliFields(readings.TryGetValue(operation, out reply) && reply.Success ? reply.Output : "");
    }

    private string diagnosticText()
    {
        var output = new StringBuilder("Служба: " + (serviceState.HasValue ? serviceState.ToString() : "нет данных"));
        if (lastError != null) output.AppendLine().AppendLine(lastError);
        if (lastCommand != null) output.AppendLine().AppendLine(lastCommand);
        foreach (string name in LecooClient.ReadCommands)
        {
            CliResult reply;
            output.AppendLine().AppendLine().Append("> lecoo-ctrl.exe ").AppendLine(name);
            output.Append(readings.TryGetValue(name, out reply) ? reply.Diagnostic : "Ответ ещё не получен.");
        }
        return output.ToString();
    }

    private async Task ApplyAsync(string args, Action onSuccess = null)
    {
        if (!connected) return;
        await RunAsync(async () =>
        {
            var result = await Task.Run(() => client.Cli(args));
            if (IsDisposed) return;
            lastCommand = "> lecoo-ctrl.exe " + args + Environment.NewLine + result.Diagnostic;
            if (!result.Success) throw new InvalidOperationException(result.Explanation);
            if (onSuccess != null) onSuccess();
            await ReadMetricsAsync();
            if (!IsDisposed && connected)
                SetStatus("Команда выполнена", Palette.Good);
        }, "Выполнение команды…", false);
    }

    private async Task SetUpServiceAsync()
    {
        await RunAsync(async () =>
        {
            bool returnedFromSetup = await Task.Run(() => client.SetUpService());
            if (returnedFromSetup) await ReadMetricsAsync();
            else if (!IsDisposed) SetStatus("Открыты службы Windows. После изменений нажмите «Обновить».", Palette.Muted);
        }, "Подключение к Lecoo…", true);
    }

    private async Task RunAsync(Func<Task> action, string loading, bool reading)
    {
        if (busy || IsDisposed) return;
        busy = true;
        refreshInProgress = reading;
        refresh.Text = reading ? "↻  Загрузка…" : "↻  Обновить";
        refresh.Enabled = service.Enabled = false;
        UpdateCommandButtons();
        PaintConnection();
        SetStatus(loading, Palette.Warning);
        try { await action(); }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            lastError = ex.Message;
            string[] lines = ex.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            SetStatus("Ошибка: " + (lines.Length == 0 ? "подробности в диагностике" : lines[0]), Palette.Error);
        }
        finally
        {
            busy = refreshInProgress = false;
            if (!IsDisposed)
            {
                refresh.Text = "↻  Обновить";
                refresh.Enabled = service.Enabled = true;
                PaintConnection();
                UpdateCommandButtons();
            }
        }
    }
}
