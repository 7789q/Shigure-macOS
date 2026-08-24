namespace Shigure.Platform;

public enum TriggerInputKind
{
    Keyboard,
    MouseButton,
    Pulse
}

public readonly record struct TriggerInputBinding(TriggerInputKind Kind, int Code)
{
    public bool IsPulse => Kind == TriggerInputKind.Pulse;
}

public readonly record struct TriggerInputEdges(bool IsPressed, bool Rising, bool Falling);

public sealed class TriggerInputEdgeTracker
{
    private bool _previousPressed;

    public TriggerInputEdges ObserveState(bool pressed)
    {
        var edges = new TriggerInputEdges(
            pressed,
            Rising: pressed && !_previousPressed,
            Falling: !pressed && _previousPressed);
        _previousPressed = pressed;
        return edges;
    }

    public static TriggerInputEdges ObservePulse(bool pulse) =>
        new(IsPressed: false, Rising: pulse, Falling: false);
}

public interface ITriggerInput : IDisposable
{
    TriggerInputBinding? Resolve(string triggerName);

    bool IsPressed(TriggerInputBinding input);

    bool ConsumePulse(TriggerInputBinding input);
}
