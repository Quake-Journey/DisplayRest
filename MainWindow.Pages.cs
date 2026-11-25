using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using static DisplayRest.WindowsNative;

namespace DisplayRest;

internal sealed partial class MainWindow
{
    private enum Page { Displays, Pc, Gamepads, Presets, Settings }
    private static readonly string[] PageKeys = ["TabDisplays", "TabPc", "TabGamepads", "TabPresets", "TabSettings"];
    private readonly Dictionary<Control, Page> _controlPages = new();
    private Page? _buildingPage;
    private Page _page;
    private CommandTarget _captureTarget;
    private static long _lastCaptureId;
    private long _captureId;
    private HdrDisplays.Monitor[] _monitors = [];
    private BluetoothGamepads.Controller[] _controllers = [];

    private static Control TabControl(Page page) => Control.TabDisplays + (int)page;

    private void CreateNavigation()
    {
        for (int i = 0; i < PageKeys.Length; i++)
            Button(TabControl((Page)i), 22 + 123 * i, 55, 116);
    }

    private void CreateHdrControls()
    {
        Label(Control.HdrHeading, 40, 520, 565, 22, Ink, _medium, Panel);
        AddCombo(Control.HdrPick, 40, 548, 282);
        Button(Control.HdrRefresh, 331, 542, 108);
        Button(Control.HdrRun, 448, 542, 162);
        Label(Control.HdrState, 40, 579, 570, 22, Muted, _small, Panel);
        AddShortcutRow(Control.HdrKey, Control.HdrCapture, Control.HdrClear, 606);
    }

    private void CreatePcControls()
    {
        _buildingPage = Page.Pc;
        Label(Control.PcHeading, 22, 101, 608, 39, Ink, _heading);
        Label(Control.PcIntro, 40, 165, 570, 48, Ink, _normal, Panel, wrap: true);
        Label(Control.PcStatus, 40, 215, 570, 30, Muted, _small, Panel, wrap: true);
        AddShortcutRow(Control.PcKey, Control.PcCapture, Control.PcClear, 256);
        Button(Control.PcRun, 40, 309, 172);
    }

    private void CreateGamepadControls()
    {
        _buildingPage = Page.Gamepads;
        Label(Control.GamepadHeading, 22, 101, 608, 39, Ink, _heading);
        Label(Control.GamepadIntro, 40, 165, 570, 52, Ink, _normal, Panel, wrap: true);
        AddCombo(Control.GamepadPick, 40, 230, 282);
        Button(Control.GamepadRefresh, 331, 223, 108);
        Button(Control.GamepadRun, 448, 223, 162);
        Label(Control.GamepadState, 40, 269, 570, 37, Muted, _small, Panel, wrap: true);
        AddShortcutRow(Control.GamepadKey, Control.GamepadCapture, Control.GamepadClear, 319);
    }

    private void CreateSettingsControls()
    {
        _buildingPage = Page.Settings;
        Label(Control.SettingsHeading, 22, 101, 608, 39, Ink, _heading);
        CheckBox(Control.CloseToTray, 40, 165, 545, Panel);
        CheckBox(Control.StartInTray, 40, 203, 545, Panel);
        CheckBox(Control.Startup, 40, 241, 545, Panel);
        CheckBox(Control.AutoUpdate, 40, 279, 545, Panel);
        Button(Control.SettingsExit, 40, 324, 175);
    }

    private void AddShortcutRow(Control field, Control capture, Control clear, int y)
    {
        Label(field, 51, y + 7, 335, 22, Accent, _normal, Field);
        Button(capture, 411, y, 103);
        Button(clear, 520, y, 90);
    }

    private void AddCombo(Control id, int x, int y, int width)
    {
        Add(id, "COMBOBOX", x, y, width, 240, TabStop | 3 | 0x00200000, Ink, Panel);
        SetWindowTheme(_controls[id], "DarkMode_Explorer", null);
    }

    private void FillCombo(Control id, IEnumerable<string> values, int selected)
    {
        nint combo = _controls[id];
        SendMessage(combo, ComboReset);
        foreach (var value in values)
            SendText(combo, ComboAdd, 0, value);
        SendMessage(combo, ComboSelect, selected);
    }

    private int ComboIndex(Control id) => (int)SendMessage(_controls[id], ComboSelected);

    private void ShowPage(Page page)
    {
        StopCapture();
        _page = page;
        foreach (var (id, controlPage) in _controlPages)
            ShowWindow(_controls[id], controlPage == page ? 4 : 0);
        foreach (var index in Enumerable.Range(0, PageKeys.Length))
            InvalidateRect(_controls[TabControl((Page)index)], 0, false);
        Text(Control.Status, "");
        InvalidateRect(Handle, 0, false);
        RefreshPage();
    }

    private void PaintPage(nint dc)
    {
        void Card(int top, int bottom) => Rounded(dc, Bounds(22, top, 630, bottom), _outline, _panel, 12);
        void ShortcutField(int top) => Rounded(dc, Bounds(40, top, 400, top + 34), _outline, _field, 8);
        switch (_page)
        {
            case Page.Displays:
                Card(146, 337);
                Card(342, 508);
                Card(513, 647);
                ShortcutField(192);
                ShortcutField(606);
                break;
            case Page.Pc: Card(146, 369); ShortcutField(256); break;
            case Page.Gamepads: Card(146, 378); ShortcutField(319); break;
            case Page.Presets: Card(146, 643); ShortcutField(485); break;
            case Page.Settings: Card(146, 375); break;
        }
    }

    private void RenderPages()
    {
        for (int i = 0; i < PageKeys.Length; i++)
            Text(TabControl((Page)i), UiText.Get(PageKeys[i]));
        SetLabels(
            (Control.HdrHeading, "Hdr"), (Control.HdrRefresh, "Refresh"), (Control.HdrRun, "HdrToggle"),
            (Control.PcHeading, "TabPc"), (Control.PcIntro, "PcIntro"), (Control.PcRun, "PcRun"),
            (Control.GamepadHeading, "TabGamepads"), (Control.GamepadIntro, "GamepadIntro"),
            (Control.GamepadRefresh, "Refresh"), (Control.GamepadRun, "GamepadDisconnect"),
            (Control.SettingsHeading, "TabSettings"), (Control.SettingsExit, "MenuExit"));
        SetTips(
            (Control.HdrPick, "HdrPickTip"), (Control.HdrRun, "HdrToggleTip"), (Control.HdrRefresh, "RefreshTip"),
            (Control.PcRun, "PcRunTip"), (Control.GamepadPick, "GamepadPickTip"),
            (Control.GamepadRun, "GamepadRunTip"), (Control.GamepadRefresh, "RefreshTip"),
            (Control.SettingsExit, "SettingsExitTip"));
        RenderActionShortcuts();
        RenderPresetLanguage();
        RenderHdrState();
        RenderGamepadState();
        RenderPcState();
        RefreshPage();
    }

    private void RefreshPage()
    {
        switch (_page)
        {
            case Page.Displays: RefreshPlan(); RefreshHdr(); break;
            case Page.Pc: RenderPcState(); break;
            case Page.Gamepads: RefreshGamepads(); break;
            case Page.Settings: RefreshStartup(); break;
        }
    }

    internal void RefreshActionDevices()
    {
        if (_page == Page.Displays)
            RefreshHdr();
        else if (_page == Page.Gamepads)
            RefreshGamepads();
    }

    internal void ReportAction(string message, bool error)
    {
        var handle = _controls[Control.Status];
        _colors[handle] = (Control.Status, error ? Error : Accent, Background);
        Text(Control.Status, message);
    }

    private void RenderPcState()
    {
        bool allowed = PcPower.CanSleep;
        Text(Control.PcStatus, UiText.Get(allowed ? "PcSleepAvailable" : "PcSleepUnavailable"));
        EnableWindow(_controls[Control.PcRun], allowed);
    }

    private void RefreshHdr()
    {
        try
        {
            _monitors = HdrDisplays.Read();
            RenderHdrState();
        }
        catch (Win32Exception error)
        {
            _monitors = [];
            RenderHdrState();
            Text(Control.HdrState, UiText.Get("DeviceReadError", error.NativeErrorCode));
        }
    }

    private void RenderHdrState()
    {
        var names = new List<string> { UiText.Get("HdrAll") };
        names.AddRange(_monitors.Select(monitor => monitor.Name));
        int selected = _settings.HdrTarget is null ? 0 :
            1 + Array.FindIndex(_monitors, monitor => StringComparer.OrdinalIgnoreCase.Equals(monitor.Key, _settings.HdrTarget));
        if (selected == 0 && _settings.HdrTarget is not null)
        {
            selected = names.Count;
            names.Add(UiText.Get("PreviousDevice"));
        }
        FillCombo(Control.HdrPick, names, selected);
        var active = HdrDisplays.Select(_monitors, _settings.HdrTarget);
        if (active.Length == 0)
            Text(Control.HdrState, UiText.Get(_settings.HdrTarget is null ? "HdrUnavailable" : "HdrTargetUnavailable"));
        else
        {
            int enabled = active.Count(monitor => monitor.Enabled);
            string status = enabled == 0 ? "HdrOff" : enabled != active.Length ? "HdrMixed" :
                active.All(monitor => monitor.UsingHdr) ? "HdrOn" : "HdrConfigured";
            Text(Control.HdrState, UiText.Get(status));
        }
        EnableWindow(_controls[Control.HdrRun], active.Length != 0);
    }

    private void RefreshGamepads()
    {
        try
        {
            _controllers = BluetoothGamepads.Read();
            RenderGamepadState();
        }
        catch (Exception error) when (error is Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            _controllers = [];
            RenderGamepadState();
            Text(Control.GamepadState, error is Win32Exception native ? UiText.Get("DeviceReadError", native.NativeErrorCode) :
                UiText.Get("ActionUnsupported"));
        }
    }

    private void RenderGamepadState()
    {
        var names = new List<string> { UiText.Get("GamepadAll") };
        names.AddRange(_controllers.Select(device => $"{device.Name} · {device.Address & 0xFFFF:X4}"));
        int selected = _settings.GamepadTarget == 0 ? 0 :
            1 + Array.FindIndex(_controllers, device => device.Address == _settings.GamepadTarget);
        if (selected == 0 && _settings.GamepadTarget != 0)
        {
            selected = names.Count;
            names.Add(UiText.Get("PreviousDevice"));
        }
        FillCombo(Control.GamepadPick, names, selected);
        int connected = _settings.GamepadTarget == 0 ? _controllers.Length :
            _controllers.Count(device => device.Address == _settings.GamepadTarget);
        Text(Control.GamepadState, connected != 0 ? UiText.Get("GamepadConnected", connected) :
            UiText.Get(_settings.GamepadTarget == 0 ? "GamepadUnavailable" : "GamepadTargetUnavailable"));
        EnableWindow(_controls[Control.GamepadRun], connected != 0);
    }

    private bool PageCommand(Control id, uint notification)
    {
        if (PresetCommand(id, notification))
            return true;
        if (notification == 1 && id is Control.HdrPick or Control.GamepadPick)
        {
            int index = ComboIndex(id);
            if (id == Control.HdrPick && index >= 0 && index <= _monitors.Length)
            {
                Save(_settings with { HdrTarget = index == 0 ? null : _monitors[index - 1].Key });
                RenderHdrState();
            }
            else if (id == Control.GamepadPick && index >= 0 && index <= _controllers.Length)
            {
                Save(_settings with { GamepadTarget = index == 0 ? 0 : _controllers[index - 1].Address });
                RenderGamepadState();
            }
            return true;
        }
        if (notification != 0)
            return false;
        if (id is >= Control.TabDisplays and <= Control.TabSettings)
        {
            ShowPage((Page)(id - Control.TabDisplays));
            return true;
        }
        switch (id)
        {
            case Control.SettingsExit:
                if (MessageBox(Handle, UiText.Get(_presetDirty ? "SettingsConfirmExitDraft" : "SettingsConfirmExit"),
                    App.Name, 0x124) == 6)
                    _exit();
                return true;
            case Control.HdrRefresh: RefreshHdr(); return true;
            case Control.GamepadRefresh: RefreshGamepads(); return true;
            case Control.HdrRun: _execute(new(CommandTarget.Hdr), false); return true;
            case Control.PcRun: _execute(new(CommandTarget.Sleep), true); return true;
            case Control.GamepadRun: _execute(new(CommandTarget.Gamepads), false); return true;
            case Control.HdrCapture: BeginCapture(CommandTarget.Hdr); return true;
            case Control.PcCapture: BeginCapture(CommandTarget.Sleep); return true;
            case Control.GamepadCapture: BeginCapture(CommandTarget.Gamepads); return true;
            case Control.HdrClear: Save(_settings.SetShortcut(CommandTarget.Hdr, null)); return true;
            case Control.PcClear: Save(_settings.SetShortcut(CommandTarget.Sleep, null)); return true;
            case Control.GamepadClear: Save(_settings.SetShortcut(CommandTarget.Gamepads, null)); return true;
            default: return false;
        }
    }

    private void BeginCapture(CommandTarget target)
    {
        bool cancel = _capturing && _captureTarget == target;
        StopCapture();
        if (cancel)
            return;
        _captureTarget = target;
        _capturing = true;
        _shortcutStatus = null;
        _captureId = ++_lastCaptureId;
        _control.Capture(Handle, _captureId);
        if (target != CommandTarget.Displays)
            SetStatus("CaptureHint");
        RenderShortcut();
        RenderActionShortcuts();
    }

    private bool ReportConflict(Shortcut shortcut, CommandId owner, bool inDisplayCard)
    {
        if (_settings.Conflict(shortcut, owner) is not { } conflict)
            return false;
        var label = conflict.Label(_settings);
        if (inDisplayCard)
            ShowNotice(Control.ShortcutStatus, _shortcutStatus = new("HotkeyInternal", true, label));
        else
            ReportAction(UiText.Get("HotkeyInternal", label), true);
        return true;
    }

    private void AcceptActionShortcut(CommandTarget target, Shortcut shortcut)
    {
        var owner = new CommandId(target, target == CommandTarget.Preset ? _editingPreset : default);
        if (ReportConflict(shortcut, owner, inDisplayCard: false))
            return;
        var result = HotkeyRegistration.Check(Handle, shortcut);
        if (result == HotkeyCheck.InUse)
        {
            ReportAction(UiText.Get("HotkeyBusy", shortcut), error: true);
            return;
        }
        if (target == CommandTarget.Preset)
        {
            _draftShortcut = shortcut;
            _presetDirty = true;
            RenderPresetButtons();
            RenderActionShortcuts();
        }
        else if (!Save(_settings.SetShortcut(target, shortcut)))
            return;
        SetStatus(result == HotkeyCheck.Available ? "HotkeyAvailable" :
            target == CommandTarget.Preset ? "HotkeyDraftUnverified" : "HotkeyUnverified");
    }

    private void RenderActionShortcuts()
    {
        RenderRow(CommandTarget.Hdr, _settings.HdrShortcut, Control.HdrKey, Control.HdrCapture, Control.HdrClear);
        RenderRow(CommandTarget.Sleep, _settings.SleepShortcut, Control.PcKey, Control.PcCapture, Control.PcClear);
        RenderRow(CommandTarget.Gamepads, _settings.GamepadShortcut, Control.GamepadKey, Control.GamepadCapture, Control.GamepadClear);
        RenderRow(CommandTarget.Preset, _draftShortcut, Control.PresetKey, Control.PresetCapture, Control.PresetClear);
    }

    private void RenderRow(CommandTarget target, Shortcut? key, Control field, Control capture, Control clear)
    {
        bool waiting = _capturing && _captureTarget == target;
        Text(field, waiting ? UiText.Get("HotkeyWaiting") : key?.ToString() ?? UiText.Get("HotkeyNone"));
        Text(capture, UiText.Get(waiting ? "Cancel" : key is null ? "HotkeyAssign" : "HotkeyChange"));
        Text(clear, UiText.Get("ClearHotkey"));
        SetTips((field, "ActionHotkeyTip"), (capture, waiting ? "CancelTip" : "CaptureTip"), (clear, "ActionClearHotkeyTip"));
        EnableWindow(_controls[clear], key is not null && !waiting);
    }
}
