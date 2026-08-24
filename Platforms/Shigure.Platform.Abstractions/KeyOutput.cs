namespace Shigure.Platform;

public enum KeySendFailureKind
{
    None,
    InvalidHotkey,
    UnknownKey,
    TargetUnavailable,
    TargetChanged,
    PermissionDenied,
    NativeFailure
}

public readonly record struct KeySendResult(
    bool Succeeded,
    KeySendFailureKind FailureKind,
    string? FailureReason)
{
    public static KeySendResult Success { get; } = new(true, KeySendFailureKind.None, null);

    public static KeySendResult Failure(KeySendFailureKind kind, string reason) =>
        new(false, kind, reason);
}

public interface ITargetKeyOutput
{
    KeySendResult Send(string hotkey, TargetIdentity? expectedTarget);
}

public enum HotkeyModifier
{
    Control,
    Alt,
    Shift
}

public sealed record HotkeyBinding(
    IReadOnlyList<HotkeyModifier> Modifiers,
    string MainKey);

public static class HotkeyParser
{
    public static HotkeyBinding? Parse(string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return null;
        }

        var parts = hotkey.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var modifiers = new List<HotkeyModifier>();
        foreach (var raw in parts[..^1])
        {
            var modifier = raw.Trim().ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifier.Control,
                "ALT" or "MENU" => HotkeyModifier.Alt,
                "SHIFT" => HotkeyModifier.Shift,
                _ => (HotkeyModifier?)null
            };

            if (modifier is not null && !modifiers.Contains(modifier.Value))
            {
                modifiers.Add(modifier.Value);
            }
        }

        return new HotkeyBinding(modifiers, parts[^1].Trim());
    }
}
