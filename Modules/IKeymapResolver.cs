namespace Shigure;

public sealed record KeyInputBinding(string DisplayText, IReadOnlyList<string> Hotkeys)
{
    public static KeyInputBinding Single(string hotkey) => new(hotkey, [hotkey]);
}

public interface IKeymapResolver
{
    void SelectForClass(int? classId, int? specId);

    string? GetHotkey(int? unit, string spell, string? macroCondition = null);

    KeyInputBinding? GetBinding(int? unit, string spell, string? macroCondition = null)
    {
        var hotkey = GetHotkey(unit, spell, macroCondition);
        return string.IsNullOrWhiteSpace(hotkey) ? null : KeyInputBinding.Single(hotkey);
    }

    IReadOnlyDictionary<int, string> GetCurrentFailedSpells();

    IReadOnlyDictionary<int, string> GetCurrentOneKeySpells();
}
