namespace Shigure;

internal static class ModuleMatchSelector
{
    public static T? FindSelectedOrBestMatch<T>(
        IEnumerable<T> candidates,
        string? selectedId,
        Func<T, string?> idSelector,
        Func<T, string?> nameSelector,
        Func<T, int> specificitySelector,
        Func<T, bool> matchPredicate)
        where T : class
    {
        var matches = SortMatches(
                candidates,
                nameSelector,
                specificitySelector,
                matchPredicate)
            .ToList();

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            var selected = matches.FirstOrDefault(candidate =>
                string.Equals(idSelector(candidate), selectedId, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                return selected;
            }
        }

        return matches.FirstOrDefault();
    }

    public static IEnumerable<T> SortMatches<T>(
        IEnumerable<T> candidates,
        Func<T, string?> nameSelector,
        Func<T, int> specificitySelector,
        Func<T, bool> matchPredicate)
    {
        return candidates
            .Where(matchPredicate)
            .OrderByDescending(specificitySelector)
            .ThenBy(nameSelector, StringComparer.CurrentCultureIgnoreCase);
    }
}
