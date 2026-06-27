using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Utils;



public static class KeyCodeExtensions
{
    // Map non printable KeyCodes to UI String
    private static readonly Dictionary<KeyCode, string> FriendlyNames = new Dictionary<KeyCode, string>
    {
        // Punctuation & Symbols
        { KeyCode.Semicolon, ";" },
        { KeyCode.Comma, "," },
        { KeyCode.Period, "." },
        { KeyCode.Question, "?" },
        { KeyCode.Quote, "'" },
        { KeyCode.Slash, "/" },
        { KeyCode.Backslash, "\\" },
        { KeyCode.LeftBracket, "[" },
        { KeyCode.RightBracket, "]" },
        { KeyCode.Minus, "-" },
        { KeyCode.Equals, "=" },
        { KeyCode.BackQuote, "`" },

        // Non-printable layout adjustments 
        { KeyCode.Space, "Space" },
        //{ KeyCode.Return, "Enter" },
        { KeyCode.Escape, "Esc" },
        { KeyCode.Backspace, "Back" },
        { KeyCode.Tab, "Tab" },
    
        // Modifiers
        { KeyCode.LeftShift, "L-Shift" },
        { KeyCode.RightShift, "R-Shift" },
        { KeyCode.LeftControl, "L-Ctrl" },
        { KeyCode.RightControl, "R-Ctrl" },
        { KeyCode.LeftAlt, "L-Alt" },
        { KeyCode.RightAlt, "R-Alt" },
    };

    /// <summary>
    /// Converts a KeyCode into a clean, human-readable string for UI menus.
    /// </summary>
    public static string ToDisplayString(this KeyCode key)
    {
        if (FriendlyNames.TryGetValue(key, out string friendlyName))
            return friendlyName;

        string keyString = key.ToString();

        // Check for main KB number keys ('Alpha0' -> '0')
        if (keyString.StartsWith("Alpha") && keyString.Length == 6)
            return keyString.Substring(5);

        // Check for Num Pad key ('Keypad0' -> 'Num 0')
        if (keyString.StartsWith("Keypad") && keyString.Length == 7)
            return "Num " + keyString.Substring(6);

        return keyString;
    }

    /// <summary>
    /// Checks if a KeyCode is a keyboard key (not a mouse button or joystick button) and not the escape or print screen keys.
    /// </summary>
    /// <param name="key"></param>
    /// <returns>true if the KeyCode is a keyboard key, false otherwise.</returns>
    public static bool IsKeyboardKey(this KeyCode key)
    {
        return  key >= KeyCode.Backspace &&
                key <= KeyCode.Menu &&
                key != KeyCode.SysReq &&
                key != KeyCode.Escape;
    }
}
