using System;
using System.Collections.Generic;

namespace VoK.HealersHelper;

// Pure portable hotkey matcher and deduplicator. Deduplicates overlapping
// EventProvider.KeyDown hooks and foreground GetAsyncKeyState polls.
internal sealed class HotkeyMatcher
{
    private readonly double _dedupeWindowSeconds;
    private readonly Dictionary<HotkeyBinding, double> _lastTriggered = new();
    private readonly HashSet<int> _polledDownKeys = new();

    public HotkeyMatcher(double dedupeWindowSeconds = 0.25)
    {
        _dedupeWindowSeconds = Math.Max(0.01, dedupeWindowSeconds);
    }

    public static bool Matches(HotkeyBinding? binding, int virtualKey, bool ctrl, bool shift, bool alt)
    {
        if (binding is null || binding.IsEmpty || virtualKey <= 0) return false;
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
        if (virtualKey <= 0) return false;
        if (!isDown)
        {
            _polledDownKeys.Remove(virtualKey);
            return false;
        }
        return _polledDownKeys.Add(virtualKey);
    }

    public void ClearPollState() => _polledDownKeys.Clear();

    public void Reset()
    {
        _lastTriggered.Clear();
        _polledDownKeys.Clear();
    }
}
