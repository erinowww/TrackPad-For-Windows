using System.Diagnostics;
using System.Runtime.InteropServices;
using static TrackPadForWindows.Native;

namespace TrackPadForWindows;

internal static class ActionRunner
{
    private static readonly HashSet<Keys> ExtendedKeys = new()
    {
        Keys.Insert, Keys.Delete, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown,
        Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.RControlKey, Keys.RMenu,
        Keys.PrintScreen, Keys.NumLock, Keys.Divide, Keys.LWin, Keys.RWin, Keys.Apps,
    };

    private static readonly Dictionary<string, Keys> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = Keys.ControlKey, ["control"] = Keys.ControlKey,
        ["alt"] = Keys.Menu,
        ["shift"] = Keys.ShiftKey,
        ["win"] = Keys.LWin, ["windows"] = Keys.LWin,
        ["esc"] = Keys.Escape, ["del"] = Keys.Delete, ["ins"] = Keys.Insert,
        ["pgup"] = Keys.PageUp, ["pgdn"] = Keys.PageDown,
        ["prtsc"] = Keys.PrintScreen,
        ["0"] = Keys.D0, ["1"] = Keys.D1, ["2"] = Keys.D2, ["3"] = Keys.D3, ["4"] = Keys.D4,
        ["5"] = Keys.D5, ["6"] = Keys.D6, ["7"] = Keys.D7, ["8"] = Keys.D8, ["9"] = Keys.D9,
    };

    public static void Run(ActionConfig action)
    {
        switch (action.Kind)
        {
            case "run":
                if (string.IsNullOrWhiteSpace(action.Target))
                    throw new InvalidOperationException($"'{action.Label}' için target boş.");
                Process.Start(new ProcessStartInfo(action.Target, action.Args ?? "") { UseShellExecute = true });
                break;
            case "keys":
                SendKeyCombo(action.Keys ?? "");
                break;
            default:
                throw new InvalidOperationException($"Bilinmeyen eylem türü: {action.Type}");
        }
    }

    // "Ctrl+Shift+Escape", "Win+D", "MediaPlayPause" gibi kombinasyonları basar.
    public static void SendKeyCombo(string combo)
    {
        var keys = ParseCombo(combo);
        foreach (var k in keys) Press(k, down: true);
        for (int i = keys.Count - 1; i >= 0; i--) Press(keys[i], down: false);
    }

    public static List<Keys> ParseCombo(string combo)
    {
        var result = new List<Keys>();
        foreach (var part in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Aliases.TryGetValue(part, out var alias)) result.Add(alias);
            else if (Enum.TryParse<Keys>(part, ignoreCase: true, out var key)) result.Add(key);
            else throw new FormatException($"Tanınmayan tuş: '{part}' ({combo})");
        }
        if (result.Count == 0) throw new FormatException("Tuş kombinasyonu boş.");
        return result;
    }

    private static void Press(Keys key, bool down)
    {
        uint flags = ExtendedKeys.Contains(key) ? KEYEVENTF_EXTENDEDKEY : 0;
        if (!down) flags |= KEYEVENTF_KEYUP;
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = (ushort)key, dwFlags = flags },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }
}
