namespace DisplayRest;

// One gesture per controller, ending when all its buttons have been released.
internal sealed class GamepadGesture
{
    private GamepadButtons _held;
    private GamepadButtons _pending;
    private bool _consumed;

    internal void Reset()
    {
        _pending = GamepadButtons.None;
        _consumed = _held != GamepadButtons.None;
    }

    internal void Synchronize(GamepadButtons buttons)
    {
        _held = buttons;
        Reset();
    }

    internal GamepadButtons Update(GamepadButtons buttons, bool capturing, GamepadButtons[] bindings)
    {
        var added = buttons & ~_held;
        var removed = _held & ~buttons;
        _held = buttons;

        if (_consumed)
        {
            _consumed = buttons != GamepadButtons.None;
            return GamepadButtons.None;
        }
        if (added == GamepadButtons.None)
            return removed == GamepadButtons.None ? GamepadButtons.None : Consume(_pending);

        _pending = GamepadButtons.None;
        if (capturing)
        {
            _pending = buttons;
            return GamepadButtons.None;
        }

        bool exact = false;
        bool canExtend = false;
        foreach (var binding in bindings)
        {
            exact |= binding == buttons;
            canExtend |= binding != buttons && (binding & buttons) == buttons;
        }
        if (!exact)
            return GamepadButtons.None;
        if (!canExtend)
            return Consume(buttons);

        // An assigned larger chord can still take precedence over this one.
        _pending = buttons;
        return GamepadButtons.None;
    }

    private GamepadButtons Consume(GamepadButtons result)
    {
        _pending = GamepadButtons.None;
        _consumed = result != GamepadButtons.None && _held != GamepadButtons.None;
        return result;
    }
}
