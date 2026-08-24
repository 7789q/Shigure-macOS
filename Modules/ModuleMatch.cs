using System.Globalization;
using System.Text.Json.Serialization;

namespace Shigure;

public sealed class ModuleMatch
{
    public int? ClassId { get; set; }
    public int? SpecId { get; set; }
    public string? PartyType { get; set; }
    public int? HeroTalent { get; set; }

    [JsonIgnore]
    public int Specificity =>
        Count(ClassId) + Count(SpecId) + Count(PartyType) + Count(HeroTalent);

    public bool Matches(int? classId, int? specId, int? partyType, int? heroTalent)
    {
        return MatchesOne(ClassId, classId)
            && MatchesOne(SpecId, specId)
            && MatchesPartyType(PartyType, partyType)
            && MatchesOne(HeroTalent, heroTalent);
    }

    public ModuleMatch Clone()
    {
        return new ModuleMatch
        {
            ClassId = ClassId,
            SpecId = SpecId,
            PartyType = NormalizePartyTypeValue(PartyType),
            HeroTalent = HeroTalent
        };
    }

    public static string? NormalizePartyTypeValue(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text)
            || text == "*"
            || string.Equals(text, "any", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(text, "单人", StringComparison.OrdinalIgnoreCase))
        {
            return "0";
        }

        if (string.Equals(text, "团队", StringComparison.OrdinalIgnoreCase))
        {
            return "1-40";
        }

        if (string.Equals(text, "队伍", StringComparison.OrdinalIgnoreCase))
        {
            return "46";
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            || int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out number))
        {
            return number is >= 1 and <= 40 ? "1-40" : number.ToString(CultureInfo.InvariantCulture);
        }

        var rangeParts = text.Split('-', 2, StringSplitOptions.TrimEntries);
        if (rangeParts.Length == 2
            && int.TryParse(rangeParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start)
            && int.TryParse(rangeParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var end))
        {
            return start <= end
                ? $"{start.ToString(CultureInfo.InvariantCulture)}-{end.ToString(CultureInfo.InvariantCulture)}"
                : $"{end.ToString(CultureInfo.InvariantCulture)}-{start.ToString(CultureInfo.InvariantCulture)}";
        }

        return text;
    }

    private static bool MatchesOne(int? expected, int? actual)
    {
        return expected is null || actual == expected;
    }

    private static bool MatchesPartyType(string? expected, int? actual)
    {
        var normalized = NormalizePartyTypeValue(expected);
        if (normalized is null)
        {
            return true;
        }

        if (actual is null)
        {
            return false;
        }

        var rangeParts = normalized.Split('-', 2, StringSplitOptions.TrimEntries);
        if (rangeParts.Length == 2
            && int.TryParse(rangeParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start)
            && int.TryParse(rangeParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var end))
        {
            return actual.Value >= start && actual.Value <= end;
        }

        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exact)
            && actual.Value == exact;
    }

    private static int Count(int? value)
    {
        return value is null ? 0 : 1;
    }

    private static int Count(string? value)
    {
        return NormalizePartyTypeValue(value) is null ? 0 : 1;
    }
}
