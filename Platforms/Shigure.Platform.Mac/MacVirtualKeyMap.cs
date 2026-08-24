namespace Shigure.Platform.MacOS;

internal static class MacVirtualKeyMap
{
    public const ushort ShiftKey = 56;
    public const ushort ControlKey = 59;
    public const ushort OptionKey = 58;

    private static readonly Dictionary<char, ushort> CharacterKeys = new()
    {
        ['a'] = 0, ['s'] = 1, ['d'] = 2, ['f'] = 3, ['h'] = 4, ['g'] = 5, ['z'] = 6, ['x'] = 7,
        ['c'] = 8, ['v'] = 9, ['b'] = 11, ['q'] = 12, ['w'] = 13, ['e'] = 14, ['r'] = 15, ['y'] = 16,
        ['t'] = 17, ['o'] = 31, ['u'] = 32, ['i'] = 34, ['p'] = 35, ['l'] = 37, ['j'] = 38, ['k'] = 40,
        ['n'] = 45, ['m'] = 46,
        ['1'] = 18, ['2'] = 19, ['3'] = 20, ['4'] = 21, ['5'] = 23, ['6'] = 22, ['7'] = 26, ['8'] = 28,
        ['9'] = 25, ['0'] = 29,
        ['='] = 24, ['-'] = 27, [']'] = 30, ['['] = 33, ['\''] = 39, [';'] = 41, ['\\'] = 42,
        [','] = 43, ['/'] = 44, ['.'] = 47, ['`'] = 50, [' '] = 49
    };

    private static readonly Dictionary<string, ushort> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SHIFT"] = ShiftKey, ["CONTROL"] = ControlKey, ["CTRL"] = ControlKey,
        ["F1"] = 122, ["F2"] = 120, ["F3"] = 99, ["F4"] = 118, ["F5"] = 96, ["F6"] = 97,
        ["F7"] = 98, ["F8"] = 100, ["F9"] = 101, ["F10"] = 109, ["F11"] = 103, ["F12"] = 111,
        ["NUMPAD0"] = 82, ["NUMPAD1"] = 83, ["NUMPAD2"] = 84, ["NUMPAD3"] = 85, ["NUMPAD4"] = 86,
        ["NUMPAD5"] = 87, ["NUMPAD6"] = 88, ["NUMPAD7"] = 89, ["NUMPAD8"] = 91, ["NUMPAD9"] = 92,
        ["NUMPADDECIMAL"] = 65, ["NUMPADPLUS"] = 69, ["NUMPADMINUS"] = 78,
        ["NUMPADMULTIPLY"] = 67, ["NUMPADDIVIDE"] = 75,
        ["ENTER"] = 36, ["RETURN"] = 36, ["TAB"] = 48, ["SPACE"] = 49,
        ["ESC"] = 53, ["ESCAPE"] = 53, ["BACKSPACE"] = 51, ["DELETE"] = 51
    };

    public static ushort? Resolve(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return null;
        }

        var key = keyName.Trim();
        if (NamedKeys.TryGetValue(key, out var namedKey))
        {
            return namedKey;
        }

        return key.Length == 1
            && CharacterKeys.TryGetValue(char.ToLowerInvariant(key[0]), out var characterKey)
                ? characterKey
                : null;
    }
}
