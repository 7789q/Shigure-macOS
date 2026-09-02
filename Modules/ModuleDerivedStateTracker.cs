namespace Shigure;

public sealed class ModuleDerivedStateTracker
{
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, DateTimeOffset> _heldUntil = new(StringComparer.Ordinal);
    private string? _moduleId;

    public ModuleDerivedStateTracker(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public void Apply(ModuleDefinition module, GameState state, bool enabled)
    {
        if (!enabled)
        {
            Reset();
            WriteValues(module, state, null);
            return;
        }

        if (!string.Equals(_moduleId, module.Id, StringComparison.OrdinalIgnoreCase))
        {
            Reset();
            _moduleId = module.Id;
        }

        var now = _timeProvider.GetUtcNow();
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        var activeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in module.DerivedStates)
        {
            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                continue;
            }

            activeNames.Add(definition.Name);
            var matched = definition.Enabled
                && ModuleConditionEvaluator.TryEvaluate(definition.Condition, state, out var conditionMatched, out _)
                && conditionMatched;
            if (matched && definition.HoldMs > 0)
            {
                _heldUntil[definition.Name] = now.AddMilliseconds(definition.HoldMs);
            }
            else if (!definition.Enabled)
            {
                _heldUntil.Remove(definition.Name);
            }

            var held = _heldUntil.TryGetValue(definition.Name, out var heldUntil) && now < heldUntil;
            values[definition.Name] = matched || held ? 1 : 0;
            if (!held && !matched)
            {
                _heldUntil.Remove(definition.Name);
            }
        }

        foreach (var staleName in _heldUntil.Keys.Where(name => !activeNames.Contains(name)).ToList())
        {
            _heldUntil.Remove(staleName);
        }

        state.Values["$derived"] = values;
    }

    public void Reset()
    {
        _moduleId = null;
        _heldUntil.Clear();
    }

    private static void WriteValues(ModuleDefinition module, GameState state, int? value)
    {
        state.Values["$derived"] = module.DerivedStates
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Name))
            .ToDictionary(
                definition => definition.Name,
                _ => value.GetValueOrDefault(),
                StringComparer.Ordinal);
    }
}
