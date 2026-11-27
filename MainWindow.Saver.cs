using System;
using System.ComponentModel;
using System.IO;
using System.Security;
using static DisplayRest.WindowsNative;

namespace DisplayRest;

internal sealed partial class MainWindow
{
    private ScreenSaver? _saver;
    private bool SaverTimeEdited => _saver is { TimeoutManaged: false } saver &&
        Read(Control.SaverTime) != PowerPlan.Format(saver.Seconds);

    private void CreateSaverControls()
    {
        _buildingPage = Page.Saver;
        Label(Control.SaverHeading, 22, 101, 608, 39, Ink, _heading);
        Label(Control.SaverTitle, 40, 159, 570, 24, Ink, _medium, Panel);
        CheckBox(Control.SaverEnabled, 40, 193, 570, Panel);
        Label(Control.SaverTimeLabel, 40, 231, 570, 20, Ink, _normal, Panel);
        EditAt(Control.SaverTime, 40, 254, 208, 32, 18);
        Button(Control.SaverApply, 260, 253, 121);
        Label(Control.SaverUnits, 40, 295, 570, 22, Muted, _small, Panel);
        Label(Control.BlackTitle, 40, 352, 365, 25, Ink, _medium, Panel);
        Button(Control.BlackRun, 428, 346, 182);
        Label(Control.BlackHint, 40, 390, 570, 42, Muted, _small, Panel, wrap: true);
        AddShortcutRow(Control.BlackKey, Control.BlackCapture, Control.BlackClear, 440);
    }

    private void RenderSaverLanguage()
    {
        SetLabels((Control.SaverHeading, "TabSaver"), (Control.SaverTitle, "SaverTitle"), (Control.SaverEnabled, "SaverEnabled"),
            (Control.SaverTimeLabel, "SaverTime"), (Control.SaverApply, "Apply"), (Control.SaverUnits, "SaverUnits"),
            (Control.BlackTitle, "BlackScreen"), (Control.BlackRun, "BlackRun"), (Control.BlackHint, "BlackHint"));
        SetTips((Control.SaverEnabled, "SaverEnableTip"), (Control.SaverTime, "SaverUnits"),
            (Control.SaverApply, "SaverApplyTip"), (Control.BlackRun, "BlackHint"));
    }

    private void RefreshSaver()
    {
        var previous = _saver;
        try { _saver = ScreenSaver.Read(); }
        catch (Exception error) when (SaverError(error))
        {
            _saver = null;
            ReportSaverError(error);
        }
        bool canSetTime = _saver is { TimeoutManaged: false };
        EnableWindow(_controls[Control.SaverEnabled], _saver is { EnableManaged: false });
        EnableWindow(_controls[Control.SaverTime], canSetTime);
        Check(Control.SaverEnabled, _saver?.Enabled == true);
        if (previous?.Seconds != _saver?.Seconds || _saver is null)
            Text(Control.SaverTime, _saver is null ? "" : PowerPlan.Format(_saver.Seconds));
        EnableWindow(_controls[Control.SaverApply], SaverTimeEdited);
        Text(Control.SaverUnits, UiText.Get(_saver is { EnableManaged: true } or { TimeoutManaged: true } ? "SaverManaged" : "SaverUnits"));
    }

    private void ApplySaverEnabled()
    {
        bool enabled = IsChecked(Control.SaverEnabled);
        try
        {
            ScreenSaver.SetEnabled(enabled);
            RefreshSaver();
            if (_saver is not null)
                SetStatus(_saver.Enabled == enabled ? "SaverSaved" : "SaverMismatch", _saver.Enabled != enabled);
        }
        catch (Exception error) when (SaverError(error))
        {
            RefreshSaver();
            ReportSaverError(error);
        }
    }

    private void ApplySaverTimeout()
    {
        if (!PowerPlan.TryParse(Read(Control.SaverTime), out var seconds) || seconds is 0 or > int.MaxValue)
        {
            SetStatus("SaverInvalid", true);
            return;
        }
        try
        {
            ScreenSaver.SetTimeout(seconds);
            _saver = null; // Also normalize a different spelling of the current timeout.
            RefreshSaver();
            if (_saver is not null)
                SetStatus(_saver.Seconds == seconds ? "SaverSaved" : "SaverMismatch", _saver.Seconds != seconds);
        }
        catch (Exception error) when (SaverError(error))
        {
            RefreshSaver();
            ReportSaverError(error);
        }
    }

    private static bool SaverError(Exception error) =>
        error is Win32Exception or IOException or UnauthorizedAccessException or SecurityException or RestUnavailableException;

    private void ReportSaverError(Exception error)
    {
        if (error is RestUnavailableException unavailable)
            SetStatus(unavailable.Key, true);
        else
            ReportAction(UiText.Get("SaverError", error is Win32Exception native ? native.NativeErrorCode : error.HResult), true);
    }
}
