using System;
using System.Collections.Generic;

namespace VoK.HealersHelper;

// Pure portable hotkey matcher and deduplicator. Deduplicates overlapping
// EventProvider.KeyDown hooks and foreground GetAsyncKeyState polls, and suppresses
// OS auto-repeat oscillations while keys remain held.
internal sealed class HotkeyMatcher
{
    private readonly double _dedupeWindowSeconds;
    private readonly Dictionary<HotkeyBinding, double> _lastTriggered = new();
    private readonly HashSet<int> _polledDownKeys = new();
    private readonly HashSet<int> _sdkDownKeys = new();

    public HotkeyMatcher(double dedupeWindowSeconds = 0.25)
    {
        _dedupeWindowSeconds = Math.Max(0.01, dedupeWindowSeconds);
    }

    public static bool IsModifierKey(int virtualKey) =>
        virtualKey is (>= 0x10 and <= 0x12) or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    public static bool Matches(HotkeyBinding? binding, int virtualKey, bool ctrl, bool shift, bool alt)
    {
        if (binding is null || binding.IsEmpty || virtualKey <= 0 || virtualKey > 0xFE || IsModifierKey(virtualKey))
            return false;
        return binding.VirtualKey == virtualKey
            && binding.Ctrl == ctrl
            && binding.Shift == shift
            && binding.Alt == alt;
    }

    public bool ShouldTrigger(HotkeyBinding? binding, int virtualKey, bool ctrl, bool shift, bool alt, double now)
    {
        if (!Matches(binding, virtualKey, ctrl, shift, alt)) return false;

        if (_lastTriggered.TryGetValue(binding!, out double lastAt) && (now - lastAt) < _dedupeWindowSeconds)
            return false;

        _lastTriggered[binding!] = now;
        return true;
    }

    public bool UpdatePollKeyState(int virtualKey, bool isDown)
    {
        if (virtualKey <= 0 || virtualKey > 0xFE || IsModifierKey(virtualKey)) return false;
        if (!isDown)
        {
            _polledDownKeys.Remove(virtualKey);
            return false;
        }
        return _polledDownKeys.Add(virtualKey);
    }

    public bool UpdateSdkKeyState(int virtualKey, bool isDown)
    {
        if (virtualKey <= 0 || virtualKey > 0xFE || IsModifierKey(virtualKey)) return false;
        if (!isDown)
        {
            _sdkDownKeys.Remove(virtualKey);
            return false;
        }
        return _sdkDownKeys.Add(virtualKey);
    }

    public void ReleaseSdkKey(int virtualKey) => _sdkDownKeys.Remove(virtualKey);

    public void ClearPollState()
    {
        _polledDownKeys.Clear();
        _sdkDownKeys.Clear();
    }

    public void Reset()
    {
        _lastTriggered.Clear();
        _polledDownKeys.Clear();
        _sdkDownKeys.Clear();
    }
}
