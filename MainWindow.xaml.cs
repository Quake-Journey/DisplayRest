using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace DisplayRest;

public partial class MainWindow : Window
{
    private sealed record Notice(string Key, bool Error = false, object? Detail = null);

    private readonly DisplayControl _control;
    private readonly DispatcherTimer _refresh;
    private UserSettings _settings;
    private PowerPlan? _plan;
    private bool _editingTimer;
    private bool _capturing;
    private bool _updating;
    private bool _allowClose;
    private bool _displaysOff;
    private Notice? _timerStatus;
    private Notice? _shortcutStatus;

    internal event Action? LanguageChanged;

    internal MainWindow(DisplayControl control, UserSettings settings, bool useDefaults)
    {
        _control = control;
        _settings = settings;
        _updating = true;
        InitializeComponent();
        var assembly = typeof(App).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        VersionText.Text = version?.Split('+', 2)[0] ?? assembly.GetName().Version?.ToString(2);
        _updating = false;
        UpdateWindowLanguage();
        RenderSettings();
        if (useDefaults)
            SetStatus("SettingsReadError", error: true);

        _control.ShortcutCaptured += AcceptShortcut;
        _control.DisplayChanged += off =>
        {
            _displaysOff = off;
            RenderDisplayButton();
        };
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refresh.Tick += (_, _) => RefreshTimer();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { RefreshTimer(); RefreshStartup(); _refresh.Start(); }
            else { StopCapture(); _refresh.Stop(); }
        };
        Activated += (_, _) => { RefreshTimer(); RefreshStartup(); };
        Deactivated += (_, _) => StopCapture();
        Closing += (_, args) =>
        {
            if (_allowClose || !_settings.CloseToTray)
                return;
            args.Cancel = true;
            Hide();
        };
    }

    internal void AllowClose() => _allowClose = true;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        int dark = 1;
        int rounded = 2;
        nint handle = new WindowInteropHelper(this).Handle;
        WindowsNative.DwmSetWindowAttribute(handle, 20, in dark, sizeof(int));
        WindowsNative.DwmSetWindowAttribute(handle, 33, in rounded, sizeof(int));
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (_capturing)
        {
            StopCapture();
            return;
        }
        _capturing = true;
        _shortcutStatus = null;
        _control.Capture(new WindowInteropHelper(this).Handle);
        RenderSettings();
    }

    private void StopCapture()
    {
        if (!_capturing)
            return;
        _capturing = false;
        _control.Capture(0);
        RenderSettings();
    }

    private void AcceptShortcut(Shortcut shortcut)
    {
        if (!_capturing)
            return;
        StopCapture();
        var result = HotkeyRegistration.Check(new WindowInteropHelper(this).Handle, shortcut);
        if (result == HotkeyCheck.InUse)
            _shortcutStatus = new Notice("HotkeyBusy", Error: true, Detail: shortcut);
        else if (SaveSettings(_settings with { Shortcut = shortcut }))
            _shortcutStatus = new Notice(result == HotkeyCheck.Available ? "HotkeyAvailable" : "HotkeyUnverified");
        RenderNotice(ShortcutStatus, _shortcutStatus);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.WakeByMouse && !_settings.WakeByKeyboard)
        {
            SetStatus("ClearNeedsWake", error: true);
            return;
        }
        if (SaveSettings(_settings with { Shortcut = null }))
        {
            _shortcutStatus = null;
            RenderNotice(ShortcutStatus, null);
        }
    }

    private void Wake_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating)
            return;
        bool mouse = MouseWake.IsChecked == true;
        bool keyboard = KeyboardWake.IsChecked == true;
        if (_settings.Shortcut is null && !mouse && !keyboard)
        {
            RenderSettings();
            SetStatus("WakeNeedsHotkey", error: true);
            return;
        }
        SaveSettings(_settings with { WakeByMouse = mouse, WakeByKeyboard = keyboard });
    }

    private void CloseToTray_Changed(object sender, RoutedEventArgs e)
    {
        if (!_updating)
            SaveSettings(_settings with { CloseToTray = CloseToTrayCheck.IsChecked == true });
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || !Enum.TryParse<LanguagePreference>(LanguageBox.SelectedValue as string, out var preference))
            return;
        SaveSettings(_settings with { Language = preference });
    }

    private bool SaveSettings(UserSettings settings)
    {
        try
        {
            settings.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            SetStatus("SettingsWriteError", error: true);
            RenderSettings();
            return false;
        }
        bool languageChanged = _settings.Language != settings.Language;
        _settings = settings;
        _control.Update(settings);
        if (languageChanged)
        {
            UpdateWindowLanguage();
            RefreshStartup();
            LanguageChanged?.Invoke();
        }
        SetStatus("SettingsSaved");
        RenderSettings();
        return true;
    }

    private void UpdateWindowLanguage()
    {
        _updating = true;
        UiText.Use(_settings.Language);
        Language = XmlLanguage.GetLanguage(UiText.LanguageTag);
        RenderDisplayButton();
        RenderPlanName();
        RenderNotice(TimerStatus, _timerStatus);
        _updating = false;
    }

    private void RenderSettings()
    {
        _updating = true;
        string? shortcut = _settings.Shortcut?.ToString();
        ShortcutText.Text = _capturing ? UiText.Get("HotkeyWaiting") : shortcut ?? UiText.Get("HotkeyNone");
        CaptureButton.Content = UiText.Get(_capturing ? "Cancel" : shortcut is null ? "HotkeyAssign" : "HotkeyChange");
        CaptureButton.ToolTip = UiText.Get(_capturing ? "CancelTip" : "CaptureTip");
        ClearButton.IsEnabled = !_capturing && shortcut is not null;
        ShortcutHint.Text = UiText.Get(_capturing ? "CaptureHint" : "HotkeyExamples");
        RenderNotice(ShortcutStatus, _shortcutStatus);
        MouseWake.IsChecked = _settings.WakeByMouse;
        KeyboardWake.IsChecked = _settings.WakeByKeyboard;
        CloseToTrayCheck.IsChecked = _settings.CloseToTray;
        LanguageBox.SelectedValue = _settings.Language.ToString();
        WakeHint.Text = shortcut is null ? UiText.Get("WakeDefault") : UiText.Get("WakeHotkey", shortcut);
        _updating = false;
    }

    private void RefreshStartup()
    {
        try
        {
            var command = UserSettings.RegisteredStartup();
            bool current = string.Equals(command, UserSettings.StartupCommand, StringComparison.OrdinalIgnoreCase);
            _updating = true;
            StartupCheck.IsChecked = current;
            StartupCheck.ToolTip = UiText.Get(command is not null && !current ? "StartupOtherPath" : "StartupTip");
        }
        catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or IOException)
        {
            SetStatus("StartupReadError", error: true);
        }
        finally { _updating = false; }
    }

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating)
            return;
        try
        {
            bool enable = StartupCheck.IsChecked == true;
            UserSettings.SetStartup(enable);
            SetStatus(enable ? "StartupEnabled" : "StartupDisabled");
        }
        catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or IOException)
        {
            SetStatus("StartupWriteError", error: true);
        }
        RefreshStartup();
    }

    private void RefreshTimer()
    {
        try
        {
            var latest = PowerPlan.Read();
            if (latest == _plan)
                return;
            bool hadEdits = _editingTimer;
            ShowPlan(latest);
            SetTimerStatus(hadEdits ? "TimerChanged" : null, hadEdits);
        }
        catch (Win32Exception error)
        {
            ApplyButton.IsEnabled = false;
            _plan = null;
            RenderPlanName();
            SetTimerStatus("TimerReadError", error: true, detail: error.NativeErrorCode);
        }
    }

    private void ShowPlan(PowerPlan plan)
    {
        _plan = plan;
        _updating = true;
        RenderPlanName();
        AcBox.Text = PowerPlan.Format(plan.Ac);
        DcBox.Text = PowerPlan.Format(plan.Dc);
        _editingTimer = false;
        ApplyButton.IsEnabled = false;
        _updating = false;
    }

    private void Timer_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updating || _plan is null)
            return;
        _editingTimer = AcBox.Text != PowerPlan.Format(_plan.Ac) || DcBox.Text != PowerPlan.Format(_plan.Dc);
        ApplyButton.IsEnabled = _editingTimer;
        SetTimerStatus(null);
    }

    private void Timer_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !ApplyButton.IsEnabled)
            return;
        e.Handled = true;
        ApplyTimer();
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => ApplyTimer();

    private void ApplyTimer()
    {
        var plan = _plan;
        if (plan is null)
            return;
        if (!PowerPlan.TryParse(AcBox.Text, out uint ac) || !PowerPlan.TryParse(DcBox.Text, out uint dc))
        {
            SetTimerStatus("TimerInvalid", error: true);
            return;
        }
        try
        {
            plan.Apply(ac, dc);
            var saved = PowerPlan.Read();
            ShowPlan(saved);
            bool applied = saved.Id == plan.Id && saved.Ac == ac && saved.Dc == dc;
            SetTimerStatus(applied ? "TimerSaved" : "TimerMismatch", error: !applied);
        }
        catch (Win32Exception error)
        {
            RefreshTimer();
            SetTimerStatus("TimerWriteError", error: true, detail: error.NativeErrorCode);
        }
        catch (PowerPlanChangeException error)
        {
            RefreshTimer();
            SetTimerStatus(error.ResourceKey, error: true);
        }
    }

    private void RenderPlanName() => SchemeLabel.Text = _plan is null
        ? UiText.Get("PlanUnavailable") : UiText.Get("PlanLabel", _plan.Name);

    private void RenderDisplayButton() =>
        SleepButton.Content = UiText.Get(_displaysOff ? "DisplayOnNow" : "DisplayOffNow");

    private void SetTimerStatus(string? key, bool error = false, object? detail = null)
    {
        _timerStatus = key is null ? null : new Notice(key, error, detail);
        RenderNotice(TimerStatus, _timerStatus);
    }

    private void SetStatus(string key, bool error = false) => RenderNotice(Status, new Notice(key, error), "Muted");

    private void RenderNotice(TextBlock element, Notice? notice, string normalBrush = "Accent")
    {
        element.Text = notice is null ? "" : UiText.Get(notice.Key, notice.Detail);
        element.Visibility = notice is null ? Visibility.Collapsed : Visibility.Visible;
        element.Foreground = (Brush)FindResource(notice?.Error == true ? "Error" : normalBrush);
    }

    private void Sleep_Click(object sender, RoutedEventArgs e) => _control.Toggle();
}
