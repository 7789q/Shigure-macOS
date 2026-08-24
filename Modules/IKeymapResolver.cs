namespace Shigure;

public interface IKeymapResolver
{
    void SelectForClass(int? classId, int? specId);

    string? GetHotkey(int? unit, string spell, string? macroCondition = null);

    IReadOnlyDictionary<int, string> GetCurrentFailedSpells();

    IReadOnlyDictionary<int, string> GetCurrentOneKeySpells();
}
