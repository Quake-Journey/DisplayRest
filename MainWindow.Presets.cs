using System;
using System.Linq;
using static DisplayRest.WindowsNative;

namespace DisplayRest;

internal sealed partial class MainWindow
{
    private static readonly (Control Id, RestAction Action)[] PresetActions =
    [
        (Control.PresetDisplays, RestAction.Displays), (Control.PresetSleep, RestAction.Sleep),
        (Control.PresetHdr, RestAction.Hdr), (Control.PresetGamepads, RestAction.Gamepads),
        (Control.PresetLock, RestAction.Lock), (Control.PresetBlack, RestAction.BlackScreen)
    ];

    private Guid _editingPreset;
    private Shortcut? _draftShortcut;
    private bool _presetEditor, _presetDirty;

    private void CreatePresetControls()
    {
        _buildingPage = Page.Presets;
        Label(Control.PresetsHeading, 22, 101, 608, 39, Ink, _heading);
        Label(Control.PresetCount, 40, 158, 565, 19, Muted, _small, Panel);
        AddCombo(Control.PresetPick, 40, 189, 274);
        Button(Control.PresetNew, 324, 183, 88);
        Button(Control.PresetDelete, 421, 183, 88);
        Button(Control.PresetRun, 518, 183, 92);
        Label(Control.PresetNameLabel, 40, 231, 570, 19, Ink, _normal, Panel);
        EditAt(Control.PresetName, 40, 251, 570, 30, RestPreset.MaxName);
        Label(Control.PresetDescriptionLabel, 40, 287, 570, 19, Ink, _normal, Panel);
        EditAt(Control.PresetDescription, 40, 308, 570, 53, RestPreset.MaxDescription, multiline: true);
        Label(Control.PresetActions, 40, 372, 570, 20, Ink, _medium, Panel);
        CheckBox(Control.PresetDisplays, 40, 399, 278, Panel);
        CheckBox(Control.PresetSleep, 325, 399, 282, Panel);
        CheckBox(Control.PresetHdr, 40, 426, 278, Panel);
        CheckBox(Control.PresetGamepads, 325, 426, 282, Panel);
        CheckBox(Control.PresetLock, 40, 453, 278, Panel);
        AddCombo(Control.PresetHdrMode, 325, 453, 282);
        CheckBox(Control.PresetBlack, 40, 481, 278, Panel);
        AddShortcutRow(Control.PresetKey, Control.PresetCapture, Control.PresetClear, 516);
        Label(Control.PresetHint, 40, 557, 570, 31, Muted, _small, Panel, wrap: true);
        Button(Control.PresetSave, 455, 591, 155);
    }

    private void RenderPresetLanguage()
    {
        SetLabels(
            (Control.PresetsHeading, "TabPresets"), (Control.PresetNew, "PresetNew"), (Control.PresetDelete, "PresetDelete"),
            (Control.PresetRun, "PresetRun"), (Control.PresetNameLabel, "PresetName"), (Control.PresetDescriptionLabel, "PresetDescription"),
            (Control.PresetActions, "PresetActions"), (Control.PresetHdr, "PresetHdr"), (Control.PresetSave, "PresetSave"));
        foreach (var (id, action) in PresetActions)
            if (action != RestAction.Hdr)
                Text(id, UiText.Get(App.ActionText(action)));
        SetTips(
            (Control.PresetPick, "PresetPickTip"), (Control.PresetNew, "PresetNewTip"), (Control.PresetDelete, "PresetDeleteTip"),
            (Control.PresetRun, "PresetRunTip"), (Control.PresetName, "PresetNameTip"), (Control.PresetDescription, "PresetDescriptionTip"),
            (Control.PresetHdrMode, "PresetHdrModeTip"), (Control.PresetSave, "PresetSaveTip"),
            (Control.PresetDisplays, "PresetDisplaysTip"), (Control.PresetSleep, "PcRunTip"), (Control.PresetHdr, "HdrPickTip"),
            (Control.PresetGamepads, "GamepadPickTip"), (Control.PresetLock, "PresetLockTip"), (Control.PresetBlack, "PresetBlackTip"));
        int mode = Math.Max(0, ComboIndex(Control.PresetHdrMode));
        FillCombo(Control.PresetHdrMode, [UiText.Get("PresetHdrToggle"), UiText.Get("PresetHdrOn"), UiText.Get("PresetHdrOff")], mode);
        RenderPresetList();
    }

    private void RenderPresetList()
    {
        var presets = _settings.PresetList;
        Text(Control.PresetCount, UiText.Get("PresetCount", presets.Length));
        int selected = _editingPreset == Guid.Empty ? 0 : 1 + Array.FindIndex(presets, preset => preset.Id == _editingPreset);
        string placeholder = _presetEditor && _editingPreset == Guid.Empty ? UiText.Get("PresetDraft") : UiText.Get("PresetPick");
        FillCombo(Control.PresetPick, presets.Select(preset => preset.Name).Prepend(placeholder), selected);
        RenderPresetButtons();
    }

    private void RenderPresetButtons()
    {
        bool existing = _presetEditor && _editingPreset != Guid.Empty;
        EnableWindow(_controls[Control.PresetNew], _settings.PresetList.Length < RestPreset.Limit);
        EnableWindow(_controls[Control.PresetDelete], existing);
        EnableWindow(_controls[Control.PresetRun], existing && !_presetDirty);
        EnableWindow(_controls[Control.PresetSave], _presetEditor && _presetDirty);
        foreach (var (id, _) in PresetActions)
            EnableWindow(_controls[id], _presetEditor);
        EnableWindow(_controls[Control.PresetName], _presetEditor);
        EnableWindow(_controls[Control.PresetDescription], _presetEditor);
        EnableWindow(_controls[Control.PresetCapture], _presetEditor);
        EnableWindow(_controls[Control.PresetClear], _presetEditor && _draftShortcut is not null && !_capturing);
        EnableWindow(_controls[Control.PresetHdrMode], _presetEditor && IsChecked(Control.PresetHdr));
        Text(Control.PresetHint, UiText.Get(_presetEditor ? "PresetHint" : "PresetEmpty"));
    }

    private void LoadPreset(RestPreset? preset, bool draft = false)
    {
        StopCapture();
        bool previous = _updating;
        _updating = true;
        _editingPreset = preset?.Id ?? Guid.Empty;
        _draftShortcut = preset?.Shortcut;
        _presetEditor = preset is not null || draft;
        _presetDirty = false;
        Text(Control.PresetName, preset?.Name ?? "");
        Text(Control.PresetDescription, preset?.Description ?? "");
        foreach (var (id, action) in PresetActions)
            Check(id, ((preset?.Actions ?? RestAction.None) & action) != 0);
        SendMessage(_controls[Control.PresetHdrMode], ComboSelect, (int)(preset?.HdrMode ?? HdrMode.Toggle));
        _updating = previous;
        RenderActionShortcuts();
        RenderPresetList();
    }

    private bool ConfirmDiscardDraft() => !_presetDirty ||
        MessageBox(Handle, UiText.Get("PresetDiscard"), App.Name, 0x124) == 6;

    private void SavePreset()
    {
        if (!_presetEditor)
            return;
        var name = Read(Control.PresetName).Trim();
        var description = Read(Control.PresetDescription).Trim();
        var actions = RestAction.None;
        foreach (var (id, action) in PresetActions)
            if (IsChecked(id))
                actions |= action;
        var idToSave = _editingPreset == Guid.Empty ? Guid.NewGuid() : _editingPreset;
        var preset = new RestPreset(idToSave, name, description, actions,
            (HdrMode)Math.Max(0, ComboIndex(Control.PresetHdrMode)), _draftShortcut);
        if (!preset.IsValid())
        {
            SetStatus("PresetInvalid", error: true);
            return;
        }
        if (_settings.PresetList.Any(existing => existing.Id != idToSave &&
            StringComparer.OrdinalIgnoreCase.Equals(existing.Name, name)))
        {
            SetStatus("PresetNameConflict", error: true);
            return;
        }
        if (_draftShortcut is { } key)
        {
            if (ReportConflict(key, new(CommandTarget.Preset, idToSave), inDisplayCard: false))
                return;
            if (HotkeyRegistration.Check(Handle, key) == HotkeyCheck.InUse)
            {
                ReportAction(UiText.Get("HotkeyBusy", key), true);
                return;
            }
        }
        var existingPresets = _settings.PresetList;
        if (_editingPreset == Guid.Empty && existingPresets.Length >= RestPreset.Limit)
        {
            SetStatus("PresetLimit", error: true);
            return;
        }
        var updated = _editingPreset == Guid.Empty ? [.. existingPresets, preset] :
            existingPresets.Select(existing => existing.Id == idToSave ? preset : existing).ToArray();
        if (Save(_settings with { Presets = updated }))
            LoadPreset(preset);
    }

    private void DeletePreset()
    {
        if (_settings.FindPreset(_editingPreset) is not { } preset ||
            MessageBox(Handle, UiText.Get("PresetConfirmDelete", preset.Name), App.Name, 0x124) != 6)
            return;
        var remaining = _settings.PresetList.Where(existing => existing.Id != _editingPreset).ToArray();
        if (Save(_settings with { Presets = remaining }))
            LoadPreset(null);
    }

    private bool PresetCommand(Control id, uint notification)
    {
        if (id == Control.PresetPick && notification == 1)
        {
            int index = ComboIndex(id) - 1;
            if (ConfirmDiscardDraft())
                LoadPreset(index >= 0 && index < _settings.PresetList.Length ? _settings.PresetList[index] : null);
            else
                RenderPresetList();
            return true;
        }
        if ((_presetEditor && id is Control.PresetName or Control.PresetDescription && notification == 0x0300) ||
            (_presetEditor && id == Control.PresetHdrMode && notification == 1) ||
            (_presetEditor && notification == 0 && PresetActions.Any(item => item.Id == id)))
        {
            _presetDirty = true;
            RenderPresetButtons();
            return true;
        }
        if (notification != 0)
            return false;
        switch (id)
        {
            case Control.PresetNew:
                if (_settings.PresetList.Length < RestPreset.Limit && ConfirmDiscardDraft())
                    LoadPreset(null, draft: true);
                return true;
            case Control.PresetDelete: DeletePreset(); return true;
            case Control.PresetRun:
                if (!_presetDirty && _editingPreset != Guid.Empty)
                    _execute(new(CommandTarget.Preset, _editingPreset), true);
                return true;
            case Control.PresetCapture:
                if (_presetEditor)
                    BeginCapture(CommandTarget.Preset);
                return true;
            case Control.PresetClear:
                _draftShortcut = null;
                _presetDirty = true;
                RenderActionShortcuts();
                RenderPresetButtons();
                return true;
            case Control.PresetSave: SavePreset(); return true;
            default: return false;
        }
    }
}
