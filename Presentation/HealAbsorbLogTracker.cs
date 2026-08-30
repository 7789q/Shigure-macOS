namespace Shigure.Presentation;

public sealed class HealAbsorbLogTracker
{
    private string? _lastSignature;
    private bool _hasObservation;

    public string? Observe(HealAbsorbDiagnosticSnapshot? diagnostic)
    {
        if (diagnostic is null)
        {
            return null;
        }

        var units = diagnostic.PositiveUnits
            .OrderBy(unit => unit.Unit)
            .ToArray();
        var signature = string.Join(
            ';',
            units.Select(unit =>
                $"{unit.Unit}:{unit.RawHealth}:{unit.HealAbsorb}:{unit.EvaluatedHealth}"));
        if (_hasObservation && string.Equals(signature, _lastSignature, StringComparison.Ordinal))
        {
            return null;
        }

        _hasObservation = true;
        _lastSignature = signature;
        if (units.Length == 0)
        {
            return $"治疗吸收诊断：正值 0，解码槽位 {diagnostic.DecodedUnitCount}";
        }

        var details = units.Select(unit =>
            $"单位 {unit.Unit}：原始生命 {unit.RawHealth}%，吸收 {unit.HealAbsorb}%，规则生命 {unit.EvaluatedHealth}%");
        return $"治疗吸收诊断：正值 {units.Length}，解码槽位 {diagnostic.DecodedUnitCount}；{string.Join("；", details)}";
    }

    public void Reset()
    {
        _lastSignature = null;
        _hasObservation = false;
    }
}
