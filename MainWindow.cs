using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using static DisplayRest.WindowsNative;

namespace DisplayRest;

internal sealed partial class MainWindow : IDisposable
{
    private enum Control
    {
        Language = 100, LanguageLabel, Heading, Intro, HotkeyTitle, Shortcut, Capture, Clear, ShortcutHint,
        ShortcutStatus, WakeTitle, MouseWake, KeyboardWake, WakeHint, TimerTitle, Scheme, AcLabel, DcLabel,
        Ac, Dc, Apply, TimerUnits, TimerStatus, CloseToTray, StartInTray, Startup, ToggleDisplay, Status, AutoUpdate,
        TabDisplays = 150, TabPc, TabGamepads, TabPresets, TabSettings, TabSaver,
        HdrHeading = 160, HdrPick, HdrRefresh, HdrRun, HdrState, HdrKey, HdrCapture, HdrClear,
        PcHeading = 180, PcIntro, PcStatus, PcKey, PcCapture, PcClear, PcRun,
        GamepadHeading = 200, GamepadIntro, GamepadPick, GamepadRefresh, GamepadRun, GamepadState,
        GamepadKey, GamepadCapture, GamepadClear,
        PresetsHeading = 220, PresetCount, PresetPick, PresetNew, PresetDelete, PresetRun, PresetNameLabel, PresetName,
        PresetDescriptionLabel, PresetDescription, PresetActions, PresetDisplays, PresetSleep, PresetHdr, PresetGamepads,
        PresetLock, PresetHdrMode, PresetKey, PresetCapture, PresetClear, PresetSave, PresetHint,
        SettingsHeading = 250, SettingsExit, UpdateIntervalLabel, UpdateInterval, CheckUpdates, UpdateState,
        SaverHeading = 260, SaverTitle, SaverEnabled, SaverTimeLabel, SaverTime, SaverApply, SaverUnits,
        BlackTitle, BlackRun, BlackHint, BlackKey, BlackCapture, BlackClear, PresetBlack
    }

    private sealed record Notice(string Key, bool Error = false, object? Detail = null);
    private const int ButtonGetCheck = 0x00F0, ButtonSetCheck = 0x00F1;
    private const int ComboAdd = 0x0143, ComboSelected = 0x0147, ComboSelect = 0x014E, ComboReset = 0x014B;
    private const int Width = 652, Height = 695;
    private const uint Ink = 0xFCF3EE, Muted = 0xCAB2A2, Accent = 0xCCE472, Error = 0xAAB4FF;
    private const uint Background = 0x1A100B, Panel = 0x291B12, Outline = 0x473225, Field = 0x23160E;
    private readonly DisplayControl _control;
    private readonly Action<UserSettings> _change;
    private readonly Action<CommandId, bool> _execute;
    private readonly Action _minimize;
    private readonly Action _close;
    private readonly Action _exit;
    private readonly Action _checkUpdates;
    private UpdateStatus _updateStatus = new();
    private readonly NativeWindow _window;
    private readonly double _scale;
    private readonly Dictionary<Control, nint> _controls = new();
    private readonly Dictionary<nint, (Control Id, uint Color, uint Background)> _colors = new();
    private readonly Dictionary<Control, nint> _tipText = new();
    private readonly List<nint> _gdi = new();
    private readonly nint _background, _panel, _field, _outline, _button, _accent, _focus;
    private readonly nint _normal, _medium, _heading, _small;
    private readonly string _version;
    private readonly nint _tips;

    private UserSettings _settings;
    private PowerPlan? _plan;
    private bool _editingTimer, _capturing, _updating, _displaysOff, _initialized;
    private Notice? _timerStatus, _shortcutStatus;
    private bool _startupOtherPath;

    internal nint Handle => _window.Handle;
    internal bool HasPendingEdits => _presetDirty || _editingTimer || SaverTimeEdited || _capturing;

    internal MainWindow(DisplayControl control, UserSettings settings, bool displaysOff, bool settingsError,
        Action<UserSettings> change, Action<CommandId, bool> execute, Action checkUpdates, Action minimize, Action close, Action exit)
    {
        _control = control;
        _settings = settings;
        _displaysOff = displaysOff;
        _change = change;
        _execute = execute;
        _checkUpdates = checkUpdates;
        _minimize = minimize;
        _close = close;
        _exit = exit;

        var assembly = typeof(App).Assembly;
        _version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+', 2)[0]
            ?? assembly.GetName().Version?.ToString(2) ?? "";

        GetWorkArea(48, 0, out var area, 0);
        var workWidth = area.Right - area.Left;
        var workHeight = area.Bottom - area.Top;
        _scale = Math.Min(Math.Max(GetDpiForSystem(), 96) / 96.0,
            Math.Min((workWidth - 20) / (Width + 20.0), (workHeight - 20) / (Height + 40.0)));
        if (_scale <= 0)
            _scale = 1;

        const uint style = 0x00CA0000; // caption, system menu, minimize
        const uint extendedStyle = 0x00040000; // app window on the taskbar
        var bounds = new Rect { Right = Px(Width), Bottom = Px(Height) };
        AdjustWindowRectEx(ref bounds, style, false, extendedStyle);
        int width = bounds.Right - bounds.Left;
        int height = bounds.Bottom - bounds.Top;
        _window = new NativeWindow("DisplayRest.Settings", WindowMessage, App.Name, style,
            area.Left + (workWidth - width) / 2, area.Top + (workHeight - height) / 2, width, height, extendedStyle);
        try
        {
            int dark = 1, rounded = 2;
            DwmSetWindowAttribute(Handle, 20, in dark, sizeof(int));
            DwmSetWindowAttribute(Handle, 33, in rounded, sizeof(int));
            _background = Brush(Background);
            _panel = Brush(Panel);
            _outline = Brush(Outline);
            _field = Brush(Field);
            _button = Brush(0x3B281B);
            _accent = Brush(Accent);
            _focus = Brush(0x836752);
            _normal = Font(14, 400);
            _medium = Font(17, 600);
            _heading = Font(29, 600);
            _small = Font(13, 400);

            InitCommonControlsEx(new CommonControls { Size = 8, Flags = 0xFF });
            _tips = CreateWindow(8, "tooltips_class32", null, 0x80000003, 0, 0, 0, 0, Handle, 0, NativeWindow.Module, 0);
            SendMessage(_tips, 0x0418, 0, Px(310)); // TTM_SETMAXTIPWIDTH
            CreateControls();
            RenderLanguage();
            ShowPage(Page.Displays);
            RefreshPlan();
            if (settingsError)
                SetStatus("SettingsReadError", true);
            SetTimer(Handle, 1, 2000);
            _initialized = true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void CreateControls()
    {
        Label(Control.LanguageLabel, 386, 20, 74, 22, Muted, _small);
        Add(Control.Language, "COMBOBOX", 464, 13, 166, 240, TabStop | 3 | 0x00200000, Ink, Background);
        SetWindowTheme(_controls[Control.Language], "DarkMode_Explorer", null);
        CreateNavigation();
        Label(Control.Status, 23, 655, 606, 36, Muted, _small, wrap: true);

        _buildingPage = Page.Displays;
        Label(Control.Heading, 22, 101, 608, 39, Ink, _heading);
        Label(Control.HotkeyTitle, 40, 159, 390, 22, Ink, _medium, Panel);
        Button(Control.ToggleDisplay, 447, 153, 163);
        AddShortcutRow(Control.Shortcut, Control.Capture, Control.Clear, 192);
        Label(Control.ShortcutHint, 40, 231, 570, 18, Muted, _small, Panel);
        Label(Control.ShortcutStatus, 40, 250, 570, 31, Muted, _small, Panel, wrap: true);
        CheckBox(Control.MouseWake, 40, 285, 310, Panel);
        CheckBox(Control.KeyboardWake, 357, 285, 251, Panel);
        Label(Control.WakeHint, 40, 311, 570, 20, Muted, _small, Panel);

        Label(Control.TimerTitle, 40, 351, 570, 23, Ink, _medium, Panel);
        Label(Control.Scheme, 40, 375, 570, 19, Muted, _small, Panel);
        Label(Control.AcLabel, 41, 400, 212, 19, Ink, _normal, Panel);
        Label(Control.DcLabel, 270, 400, 210, 19, Ink, _normal, Panel);
        TimerEdit(Control.Ac, 40);
        TimerEdit(Control.Dc, 269);
        Button(Control.Apply, 505, 421, 105);
        Label(Control.TimerUnits, 40, 460, 570, 17, Muted, _small, Panel);
        Label(Control.TimerStatus, 40, 478, 570, 30, Muted, _small, Panel, wrap: true);
        CreateHdrControls();
        CreateSaverControls();
        CreatePcControls();
        CreateGamepadControls();
        CreatePresetControls();
        CreateSettingsControls();
        _buildingPage = null;
    }

    internal void Show()
    {
        ShowWindow(Handle, 5);
        SetForegroundWindow(Handle);
    }

    internal bool FilterMessage(Message message)
    {
        if (message.Id == KeyDown && message.WParam == 0x0D && message.Window == _controls[Control.SaverTime])
        {
            if (SaverTimeEdited)
                ApplySaverTimeout();
            return true;
        }
        if (message.Id != KeyDown || message.WParam != 0x0D || !_editingTimer ||
            (message.Window != _controls[Control.Ac] && message.Window != _controls[Control.Dc]))
            return false;
        ApplyTimer();
        return true;
    }

    internal void DisplayChanged(bool off)
    {
        _displaysOff = off;
        Text(Control.ToggleDisplay, UiText.Get(off ? "DisplayOnNow" : "DisplayOffNow"));
    }

    private nint? WindowMessage(nint window, int message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case PaintMessage:
                return _background == 0 ? null : PaintWindow(window);
            case 0x0014 when _background != 0: // WM_ERASEBKGND
                return 1;
            case CommandMessage:
                ControlCommand((Control)((uint)wParam & 0xFFFF), ((uint)wParam >> 16) & 0xFFFF);
                return 0;
            case ColorStaticMessage or ColorEditMessage or ColorListMessage or ColorButtonMessage:
                if (_colors.TryGetValue(lParam, out var color))
                {
                    SetTextColor(wParam, color.Color);
                    SetBkColor(wParam, color.Background);
                    SetBkMode(wParam, 1);
                    return color.Background == Field ? _field : color.Background == Panel ? _panel : _background;
                }
                break;
            case DrawItemMessage:
                DrawButton(Marshal.PtrToStructure<DrawItem>(lParam));
                return 1;
            case TimerMessage when wParam == 1:
                if (_page == Page.Displays)
                    RefreshPlan();
                return 0;
            case 0x007E when _initialized: // WM_DISPLAYCHANGE
                if (_page == Page.Displays)
                    SetTimer(window, 4, 300);
                return 0;
            case TimerMessage when wParam == 4:
                KillTimer(window, 4);
                if (_page == Page.Displays)
                    RefreshHdr();
                return 0;
            case ActivateMessage:
                if (((uint)wParam & 0xFFFF) == 0)
                    StopCapture();
                else if (_initialized)
                {
                    RefreshPage();
                }
                return 0;
            case 0x001A when _initialized && _page == Page.Saver: // WM_SETTINGCHANGE
                PostMessage(window, AppMessage + 10);
                return 0;
            case AppMessage + 10 when _page == Page.Saver:
                RefreshSaver();
                return 0;
            case SystemCommand when ((uint)wParam & 0xFFF0) == 0xF020: // SC_MINIMIZE
                if (ConfirmDiscardDraft())
                    _minimize();
                return 0;
            case CloseMessage:
                if (ConfirmDiscardDraft())
                    _close();
                return 0;
        }
        return null;
    }

    private void ControlCommand(Control id, uint notification)
    {
        if (_updating)
            return;
        if (PageCommand(id, notification))
            return;
        if (id is Control.Ac or Control.Dc && notification == 0x0300)
        {
            TimerChanged();
            return;
        }
        if (id == Control.Language && notification == 1)
        {
            var selection = (int)SendMessage(_controls[id], ComboSelected);
            if (selection >= 0 && selection != (int)_settings.Language)
                Save(_settings with { Language = (LanguagePreference)selection });
            return;
        }
        if (notification != 0)
            return;

        switch (id)
        {
            case Control.Capture:
                BeginCapture(CommandTarget.Displays);
                break;
            case Control.Clear:
                if (!_settings.WakeByMouse && !_settings.WakeByKeyboard)
                    ShowNotice(Control.ShortcutStatus, _shortcutStatus = new("ClearNeedsWake", true));
                else if (Save(_settings with { Shortcut = null }))
                    ShowNotice(Control.ShortcutStatus, _shortcutStatus = null);
                break;
            case Control.MouseWake or Control.KeyboardWake:
                bool mouse = IsChecked(Control.MouseWake);
                bool keyboard = IsChecked(Control.KeyboardWake);
                if (_settings.Shortcut is null && !mouse && !keyboard)
                {
                    RenderChecks();
                    SetStatus("WakeNeedsHotkey", true);
                }
                else
                    Save(_settings with { WakeByMouse = mouse, WakeByKeyboard = keyboard });
                break;
            case Control.CloseToTray: Save(_settings with { CloseToTray = IsChecked(id) }); break;
            case Control.StartInTray: Save(_settings with { StartInTray = IsChecked(id) }); break;
            case Control.AutoUpdate: Save(_settings with { AutoUpdate = IsChecked(id) }); break;
            case Control.Startup: ChangeStartup(); break;
            case Control.Apply: ApplyTimer(); break;
            case Control.ToggleDisplay: _control.Toggle(); break;
        }
    }

    internal void AcceptShortcut(Shortcut shortcut, long id)
    {
        if (!_capturing || id != _captureId)
            return;
        var target = _captureTarget;
        StopCapture();
        if (target != CommandTarget.Displays)
        {
            AcceptActionShortcut(target, shortcut);
            return;
        }
        if (ReportConflict(shortcut, new(CommandTarget.Displays), inDisplayCard: true))
            return;
        var result = HotkeyRegistration.Check(Handle, shortcut);
        if (result == HotkeyCheck.InUse)
            _shortcutStatus = new("HotkeyBusy", true, shortcut);
        else if (Save(_settings with { Shortcut = shortcut }))
            _shortcutStatus = new(ShortcutStatusKey(result));
        RenderShortcut();
    }

    private void StopCapture()
    {
        if (!_capturing)
            return;
        _capturing = false;
        _control.Capture(0);
        RenderShortcut();
        RenderActionShortcuts();
    }

    private bool Save(UserSettings settings)
    {
        try { settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            SetStatus("SettingsWriteError", true);
            RenderChecks();
            SendMessage(_controls[Control.Language], ComboSelect, (int)_settings.Language);
            return false;
        }
        bool languageChanged = settings.Language != _settings.Language;
        _settings = settings;
        if (languageChanged)
            RenderLanguage();
        else
        {
            RenderChecks();
            RenderShortcut();
            RenderActionShortcuts();
        }
        _change(settings);
        SetStatus("SettingsSaved");
        return true;
    }

    private void RenderLanguage()
    {
        UiText.Use(_settings.Language);
        _updating = true;
        nint options = _controls[Control.Language];
        SendMessage(options, ComboReset);
        SendText(options, ComboAdd, 0, UiText.Get("LanguageSystem"));
        SendText(options, ComboAdd, 0, "Русский");
        SendText(options, ComboAdd, 0, "English");
        SendMessage(options, ComboSelect, (int)_settings.Language);

        SetLabels(
            (Control.LanguageLabel, "LanguageLabel"), (Control.Heading, "DisplayHeading"),
            (Control.HotkeyTitle, "Heading"), (Control.Clear, "ClearHotkey"),
            (Control.MouseWake, "WakeMouse"), (Control.KeyboardWake, "WakeKeyboard"), (Control.TimerTitle, "TimerTitle"),
            (Control.AcLabel, "AcLabel"), (Control.DcLabel, "DcLabel"), (Control.Apply, "Apply"),
            (Control.TimerUnits, "TimerUnits"), (Control.CloseToTray, "CloseToTray"),
            (Control.StartInTray, "StartInTray"), (Control.Startup, "Startup"),
            (Control.AutoUpdate, "AutoUpdate"));
        SetTips(
            (Control.Language, "LanguageTip"), (Control.Shortcut, "HotkeyTip"), (Control.Clear, "ClearTip"),
            (Control.MouseWake, "WakeMouseTip"), (Control.KeyboardWake, "WakeKeyboardTip"),
            (Control.Ac, "AcTip"), (Control.AcLabel, "AcTip"), (Control.Dc, "DcTip"), (Control.DcLabel, "DcTip"),
            (Control.Apply, "ApplyTip"), (Control.ToggleDisplay, "DisplayToggleTip"),
            (Control.CloseToTray, "CloseToTrayTip"), (Control.StartInTray, "StartInTrayTip"),
            (Control.AutoUpdate, "AutoUpdateTip"),
            (Control.Startup, _startupOtherPath ? "StartupOtherPath" : "StartupTip"));
        RenderChecks();
        RenderShortcut();
        RenderPlan();
        DisplayChanged(_displaysOff);
        RenderPages();
        _updating = false;
    }

    private void SetLabels(params (Control Id, string Key)[] labels)
    {
        foreach (var (id, key) in labels)
            Text(id, UiText.Get(key));
    }

    private void RenderChecks()
    {
        Check(Control.CloseToTray, _settings.CloseToTray);
        Check(Control.StartInTray, _settings.StartInTray);
        Check(Control.AutoUpdate, _settings.AutoUpdate);
        SendMessage(_controls[Control.UpdateInterval], ComboSelect, Array.IndexOf(UpdateHours, _settings.UpdateIntervalHours));
        EnableWindow(_controls[Control.UpdateInterval], _settings.AutoUpdate);
        Check(Control.MouseWake, _settings.WakeByMouse);
        Check(Control.KeyboardWake, _settings.WakeByKeyboard);
    }

    private void RenderShortcut()
    {
        var shortcut = _settings.Shortcut;
        bool capturing = _capturing && _captureTarget == CommandTarget.Displays;
        Text(Control.Shortcut, capturing ? UiText.Get("HotkeyWaiting") : shortcut?.ToString() ?? UiText.Get("HotkeyNone"));
        Text(Control.Capture, UiText.Get(capturing ? "Cancel" : shortcut is null ? "HotkeyAssign" : "HotkeyChange"));
        Text(Control.ShortcutHint, UiText.Get(capturing ? "CaptureHint" : "HotkeyExamples"));
        Text(Control.WakeHint, shortcut is null ? UiText.Get("WakeDefault") : UiText.Get("WakeHotkey", shortcut));
        SetTips((Control.Capture, capturing ? "CancelTip" : "CaptureTip"));
        EnableWindow(_controls[Control.Clear], !capturing && shortcut is not null);
        ShowNotice(Control.ShortcutStatus, _shortcutStatus);
    }

    private void RefreshStartup()
    {
        try
        {
            var command = UserSettings.RegisteredStartup();
            bool current = string.Equals(command, UserSettings.StartupCommand, StringComparison.OrdinalIgnoreCase);
            Check(Control.Startup, current);
            _startupOtherPath = command is not null && !current;
            SetTips((Control.Startup, _startupOtherPath ? "StartupOtherPath" : "StartupTip"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            SetStatus("StartupReadError", true);
        }
    }

    private void ChangeStartup()
    {
        try
        {
            bool enable = IsChecked(Control.Startup);
            UserSettings.SetStartup(enable);
            SetStatus(enable ? "StartupEnabled" : "StartupDisabled");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            SetStatus("StartupWriteError", true);
        }
        RefreshStartup();
    }

    private void RefreshPlan()
    {
        try
        {
            var latest = PowerPlan.Read();
            if (latest == _plan)
                return;
            _timerStatus = _editingTimer ? new("TimerChanged", true) : null;
            ShowPlan(latest);
        }
        catch (Win32Exception error)
        {
            _plan = null;
            EnableWindow(_controls[Control.Apply], false);
            TimerStatus(new("TimerReadError", true, error.NativeErrorCode));
            RenderPlan();
        }
    }

    private void ShowPlan(PowerPlan plan)
    {
        _plan = plan;
        _updating = true;
        Text(Control.Ac, PowerPlan.Format(plan.Ac));
        Text(Control.Dc, PowerPlan.Format(plan.Dc));
        _updating = false;
        _editingTimer = false;
        EnableWindow(_controls[Control.Apply], false);
        RenderPlan();
    }

    private void RenderPlan()
    {
        Text(Control.Scheme, _plan is null ? UiText.Get("PlanUnavailable") : UiText.Get("PlanLabel", _plan.Name));
        ShowNotice(Control.TimerStatus, _timerStatus);
    }

    private void TimerChanged()
    {
        if (_plan is null)
            return;
        _editingTimer = Read(Control.Ac) != PowerPlan.Format(_plan.Ac) || Read(Control.Dc) != PowerPlan.Format(_plan.Dc);
        EnableWindow(_controls[Control.Apply], _editingTimer);
        TimerStatus(null);
    }

    private void ApplyTimer()
    {
        var plan = _plan;
        if (plan is null || !_editingTimer)
            return;
        if (!PowerPlan.TryParse(Read(Control.Ac), out var ac) || !PowerPlan.TryParse(Read(Control.Dc), out var dc))
        {
            TimerStatus(new("TimerInvalid", true));
            return;
        }
        try
        {
            plan.Apply(ac, dc);
            var saved = PowerPlan.Read();
            ShowPlan(saved);
            bool applied = saved.Id == plan.Id && saved.Ac == ac && saved.Dc == dc;
            TimerStatus(new(applied ? "TimerSaved" : "TimerMismatch", !applied));
        }
        catch (Win32Exception error)
        {
            RefreshPlan();
            TimerStatus(new("TimerWriteError", true, error.NativeErrorCode));
        }
        catch (PowerPlanChangeException error)
        {
            RefreshPlan();
            TimerStatus(new(error.ResourceKey, true));
        }
    }

    private void TimerStatus(Notice? notice) => ShowNotice(Control.TimerStatus, _timerStatus = notice);
    private void SetStatus(string key, bool error = false) => ShowNotice(Control.Status, new(key, error));

    private void ShowNotice(Control id, Notice? notice)
    {
        nint window = _controls[id];
        var (_, _, background) = _colors[window];
        _colors[window] = (id, notice?.Error == true ? Error : Accent, background);
        Text(id, notice is null ? "" : UiText.Get(notice.Key, notice.Detail));
    }

    private void Add(Control id, string type, int x, int y, int w, int h, uint style, uint color, uint background,
        nint font = 0, uint extended = 0)
    {
        nint window = CreateWindow(extended, type, null, Child | Visible | style, Px(x), Px(y), Px(w), Px(h),
            Handle, (int)id, NativeWindow.Module, 0);
        if (window == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        SendMessage(window, 0x0030, font == 0 ? _normal : font, 1); // WM_SETFONT
        _controls.Add(id, window);
        _colors.Add(window, (id, color, background));
        if (_buildingPage is { } page)
            _controlPages.Add(id, page);
    }

    private void Label(Control id, int x, int y, int width, int height, uint color, nint font = 0,
        uint background = Background, bool wrap = false) =>
        Add(id, "STATIC", x, y, width, height, wrap ? 0x80U : 0x4080U, color, background, font);

    private void Button(Control id, int x, int y, int width) =>
        Add(id, "BUTTON", x, y, width, 34, TabStop | 0x0B, Ink, Panel);

    private void CheckBox(Control id, int x, int y, int width, uint background)
    {
        Add(id, "BUTTON", x, y, width, 22, TabStop | 3, Ink, background);
        SetWindowTheme(_controls[id], "", "");
    }

    private void TimerEdit(Control id, int x)
    {
        EditAt(id, x, 421, 214, 32, 18);
    }

    private void EditAt(Control id, int x, int y, int width, int height, int limit, bool multiline = false)
    {
        Add(id, "EDIT", x, y, width, height,
            TabStop | 0x00800000 | (multiline ? 0x00201044u : 0x80u), Ink, Field);
        SendMessage(_controls[id], 0x00C5, limit); // EM_SETLIMITTEXT
        SendMessage(_controls[id], 0x00D3, 3, Px(8) | Px(8) << 16); // EM_SETMARGINS
    }

    private void SetTips(params (Control Id, string Key)[] tips)
    {
        if (_tips == 0)
            return;
        foreach (var (id, key) in tips)
        {
            var tool = new ToolInfo
            {
                Size = (uint)Marshal.SizeOf<ToolInfo>(),
                Flags = 0x11, // TTF_IDISHWND | TTF_SUBCLASS
                Window = Handle,
                Id = _controls[id],
                Text = Marshal.StringToHGlobalUni(UiText.Get(key))
            };
            if (_tipText.Remove(id, out var old))
            {
                SendTool(_tips, 0x0433, 0, in tool); // TTM_DELTOOLW
                Marshal.FreeHGlobal(old);
            }
            SendTool(_tips, 0x0432, 0, in tool); // TTM_ADDTOOLW
            _tipText.Add(id, tool.Text);
        }
    }

    private bool IsChecked(Control id) => SendMessage(_controls[id], ButtonGetCheck) == 1;
    private void Check(Control id, bool value) => SendMessage(_controls[id], ButtonSetCheck, value ? 1 : 0);
    private void Text(Control id, string value) => SetWindowText(_controls[id], value);

    private string Read(Control id) => ReadText(_controls[id]);

    private static string ReadText(nint window)
    {
        var text = new StringBuilder(512);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private int Px(int logical) => (int)Math.Round(logical * _scale);
    private Rect Bounds(int left, int top, int right, int bottom) =>
        new() { Left = Px(left), Top = Px(top), Right = Px(right), Bottom = Px(bottom) };

    private nint Brush(uint color) => Own(CreateSolidBrush(color));
    private nint Font(int size, int weight) => Own(CreateFont(-Px(size), 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI"));

    private nint Own(nint resource)
    {
        if (resource != 0)
            _gdi.Add(resource);
        return resource;
    }

    private nint PaintWindow(nint window)
    {
        var dc = BeginPaint(window, out var paint);
        int saved = SaveDC(dc);
        GetClientRect(window, out var client);
        FillRect(dc, in client, _background);
        PaintPage(dc);
        SelectObject(dc, _medium);
        SetBkMode(dc, 1);
        SetTextColor(dc, Ink);
        var heading = Bounds(58, 17, 135, 40);
        DrawText(dc, App.Name, -1, ref heading, 0x24);
        SelectObject(dc, _small);
        SetTextColor(dc, Muted);
        var version = Bounds(140, 17, 270, 40);
        DrawText(dc, _version, -1, ref version, 0x24);
        DrawIconEx(dc, Px(23), Px(13), NativeWindow.Icon, Px(28), Px(28), 0, 0, 3);
        RestoreDC(dc, saved);
        EndPaint(window, in paint);
        return 0;
    }

    private void DrawButton(DrawItem item)
    {
        int saved = SaveDC(item.Dc);
        var id = (Control)item.Id;
        bool primary = id == TabControl(_page) ||
            ((id is Control.Apply or Control.PresetSave) && (item.State & 4) == 0);
        bool pressed = (item.State & 1) != 0, focused = (item.State & 0x10) != 0;
        var area = item.Area;
        FillRect(item.Dc, in area, _controlPages.ContainsKey(id) ? _panel : _background);
        Rounded(item.Dc, area, focused ? _focus : _outline, primary ? _accent : pressed ? _field : _button, 8);
        SelectObject(item.Dc, _normal);
        SetBkMode(item.Dc, 1);
        SetTextColor(item.Dc, (item.State & 4) != 0 ? Muted : primary ? 0x1E2408 : Ink);
        DrawText(item.Dc, ReadText(item.Window), -1, ref area, 0x825);
        RestoreDC(item.Dc, saved);
    }

    private void Rounded(nint dc, Rect rect, nint border, nint fill, int radius)
    {
        // Both rounded passes use brushes, so there is no selected pen to allocate.
        var pen = SelectObject(dc, GetStockObject(8)); // NULL_PEN
        var brush = SelectObject(dc, border);
        RoundRect(dc, rect.Left, rect.Top, rect.Right, rect.Bottom, Px(radius), Px(radius));
        SelectObject(dc, fill);
        RoundRect(dc, rect.Left + 1, rect.Top + 1, rect.Right - 1, rect.Bottom - 1, Px(radius), Px(radius));
        SelectObject(dc, brush);
        SelectObject(dc, pen);
    }

    public void Dispose()
    {
        _initialized = false;
        if (_capturing)
            _control.Capture(0);
        KillTimer(Handle, 1);
        KillTimer(Handle, 4);
        if (_tips != 0)
            DestroyWindow(_tips);
        foreach (var value in _tipText.Values)
            Marshal.FreeHGlobal(value);
        _window.Dispose();
        foreach (var resource in _gdi)
            DeleteObject(resource);
    }
}
