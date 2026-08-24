namespace Shigure;

public static class TriggerModePolicy
{
    public static bool IsSingleShot(SendMode mode, bool isPulseTrigger) =>
        mode == SendMode.Click || (mode == SendMode.Hold && isPulseTrigger);
}
