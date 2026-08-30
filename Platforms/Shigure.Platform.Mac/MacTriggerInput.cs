using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Shigure.Platform;

namespace Shigure.Platform.MacOS;

public sealed class MacTriggerInput : ITriggerInput
{
    private readonly IMacTriggerStateApi _stateApi;
    private readonly Func<IMacTriggerPulseSource> _pulseSourceFactory;
    private IMacTriggerPulseSource? _pulseSource;
    private bool _disposed;

    [SupportedOSPlatform("macos")]
    public MacTriggerInput()
        : this(new MacTriggerStateApi(), static () => new MacTriggerEventTap())
    {
    }

    internal MacTriggerInput(
        IMacTriggerStateApi stateApi,
        Func<IMacTriggerPulseSource> pulseSourceFactory)
    {
        _stateApi = stateApi;
        _pulseSourceFactory = pulseSourceFactory;
    }

    public TriggerInputBinding? Resolve(string triggerName)
    {
        if (_disposed)
        {
            return null;
        }

        var binding = MacTriggerInputMap.Resolve(triggerName);
        if (binding is not null)
        {
            _ = GetPulseSource();
        }

        return binding;
    }

    public bool IsPressed(TriggerInputBinding input)
    {
        if (_disposed)
        {
            return false;
        }

        return input.Kind switch
        {
            TriggerInputKind.Keyboard => _stateApi.IsKeyPressed(checked((ushort)input.Code)),
            TriggerInputKind.MouseButton => _stateApi.IsMouseButtonPressed(checked((uint)input.Code)),
            _ => false
        };
    }

    public bool ConsumePulse(TriggerInputBinding input)
    {
        return !_disposed && GetPulseSource().ConsumePulse(input);
    }

    private IMacTriggerPulseSource GetPulseSource()
    {
        if (_pulseSource?.RequiresRestart == true)
        {
            _pulseSource.Dispose();
            _pulseSource = null;
        }

        return _pulseSource ??= _pulseSourceFactory();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pulseSource?.Dispose();
        _pulseSource = null;
    }
}

internal static class MacTriggerInputMap
{
    internal const int WheelUpCode = 1;
    internal const int WheelDownCode = -1;

    private static readonly Dictionary<string, uint> MouseButtons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MIDDLE"] = 2, ["MBUTTON"] = 2, ["MOUSE3"] = 2,
        ["XBUTTON1"] = 3, ["X1"] = 3, ["MOUSE4"] = 3,
        ["XBUTTON2"] = 4, ["X2"] = 4, ["MOUSE5"] = 4
    };

    private static readonly HashSet<string> WheelUpNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "WHEELUP",
        "MOUSEWHEELUP",
        "MIDDLEWHEELUP",
        "滚轮上滚",
        "鼠标中键上滚"
    };

    private static readonly HashSet<string> WheelDownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "WHEELDOWN",
        "MOUSEWHEELDOWN",
        "MIDDLEWHEELDOWN",
        "滚轮下滚",
        "鼠标中键下滚"
    };

    public static TriggerInputBinding? Resolve(string triggerName)
    {
        if (string.IsNullOrWhiteSpace(triggerName))
        {
            return null;
        }

        var name = triggerName.Trim();
        if (WheelUpNames.Contains(name))
        {
            return new TriggerInputBinding(TriggerInputKind.Pulse, WheelUpCode);
        }

        if (WheelDownNames.Contains(name))
        {
            return new TriggerInputBinding(TriggerInputKind.Pulse, WheelDownCode);
        }

        if (MouseButtons.TryGetValue(name, out var button))
        {
            return new TriggerInputBinding(TriggerInputKind.MouseButton, checked((int)button));
        }

        var keyCode = MacVirtualKeyMap.Resolve(name);
        return keyCode is null
            ? null
            : new TriggerInputBinding(TriggerInputKind.Keyboard, keyCode.Value);
    }
}

internal interface IMacTriggerStateApi
{
    bool IsKeyPressed(ushort keyCode);

    bool IsMouseButtonPressed(uint button);
}

[SupportedOSPlatform("macos")]
internal sealed class MacTriggerStateApi : IMacTriggerStateApi
{
    public bool IsKeyPressed(ushort keyCode) =>
        MacTriggerInterop.CGEventSourceKeyState(MacTriggerInterop.EventSourceStateHidSystem, keyCode);

    public bool IsMouseButtonPressed(uint button) =>
        MacTriggerInterop.CGEventSourceButtonState(MacTriggerInterop.EventSourceStateHidSystem, button);
}

internal interface IMacTriggerPulseSource : IDisposable
{
    bool RequiresRestart { get; }

    bool ConsumePulse(TriggerInputBinding input);
}

internal sealed class TriggerPulseLatch
{
    private readonly ConcurrentDictionary<TriggerInputBinding, long> _pending = new();

    public void Record(TriggerInputBinding input) =>
        _pending.AddOrUpdate(input, 1, static (_, count) => count == long.MaxValue ? count : count + 1);

    public bool Consume(TriggerInputBinding input)
    {
        while (_pending.TryGetValue(input, out var count))
        {
            if (count <= 1)
            {
                return _pending.TryRemove(new KeyValuePair<TriggerInputBinding, long>(input, count));
            }

            if (_pending.TryUpdate(input, count - 1, count))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class WheelPulseCounter
{
    private readonly long _gestureGapTicks;
    private long _lastUpEventAt;
    private long _lastDownEventAt;
    private long _upPulses;
    private long _downPulses;

    public WheelPulseCounter(long gestureGapTicks)
    {
        _gestureGapTicks = Math.Max(1, gestureGapTicks);
    }

    public void RecordWheelUp(long timestamp) =>
        RecordPulse(ref _lastUpEventAt, ref _upPulses, timestamp);

    public void RecordWheelDown(long timestamp) =>
        RecordPulse(ref _lastDownEventAt, ref _downPulses, timestamp);

    public bool ConsumePulse(int direction) => direction switch
    {
        MacTriggerInputMap.WheelUpCode => ConsumePulse(ref _upPulses),
        MacTriggerInputMap.WheelDownCode => ConsumePulse(ref _downPulses),
        _ => false
    };

    private void RecordPulse(ref long lastEventAt, ref long pulses, long timestamp)
    {
        var previous = Interlocked.Exchange(ref lastEventAt, timestamp);
        if (previous == 0 || timestamp - previous >= _gestureGapTicks)
        {
            Interlocked.Increment(ref pulses);
        }
    }

    private static bool ConsumePulse(ref long pulses)
    {
        long current;
        do
        {
            current = Volatile.Read(ref pulses);
            if (current <= 0)
            {
                return false;
            }
        }
        while (Interlocked.CompareExchange(ref pulses, current - 1, current) != current);

        return true;
    }
}

[SupportedOSPlatform("macos")]
internal sealed class MacTriggerEventTap : IMacTriggerPulseSource
{
    private const string DefaultRunLoopMode = "kCFRunLoopDefaultMode";
    private static readonly long WheelGestureGapTicks =
        Math.Max(1, Stopwatch.Frequency * 500 / 1000);

    private readonly MacTriggerInterop.EventTapCallback _callback;
    private readonly WheelPulseCounter _wheelPulses = new(WheelGestureGapTicks);
    private readonly TriggerPulseLatch _pressPulses = new();
    private readonly ManualResetEventSlim _started = new(false);
    private readonly Thread _eventThread;
    private nint _runLoop;
    private nint _tap;
    private nint _source;
    private nint _mode;
    private int _startedSuccessfully;
    private int _disposed;

    public MacTriggerEventTap()
    {
        _callback = HandleEvent;
        _eventThread = new Thread(RunEventLoop)
        {
            IsBackground = true,
            Name = "Shigure macOS trigger event tap"
        };
        _eventThread.Start();
        _started.Wait(TimeSpan.FromSeconds(2));
    }

    public bool ConsumePulse(TriggerInputBinding input)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        return input.IsPulse
            ? _wheelPulses.ConsumePulse(input.Code)
            : _pressPulses.Consume(input);
    }

    public bool RequiresRestart =>
        Volatile.Read(ref _disposed) == 0
        && Volatile.Read(ref _startedSuccessfully) != 0
        && !_eventThread.IsAlive;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var runLoop = Volatile.Read(ref _runLoop);
        if (runLoop != 0)
        {
            MacTriggerInterop.CFRunLoopStop(runLoop);
        }

        var stopped = !_eventThread.IsAlive;
        if (!stopped && Thread.CurrentThread != _eventThread)
        {
            stopped = _eventThread.Join(TimeSpan.FromSeconds(2));
        }

        if (stopped)
        {
            _started.Dispose();
        }
    }

    private void RunEventLoop()
    {
        try
        {
            _runLoop = MacTriggerInterop.CFRunLoopGetCurrent();
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            _tap = MacTriggerInterop.CGEventTapCreate(
                MacTriggerInterop.EventTapLocationSession,
                MacTriggerInterop.HeadInsertEventTap,
                MacTriggerInterop.EventTapOptionListenOnly,
                MacTriggerInterop.TriggerEventMask,
                _callback,
                0);
            if (_tap == 0)
            {
                return;
            }

            _source = MacTriggerInterop.CFMachPortCreateRunLoopSource(0, _tap, 0);
            _mode = MacTriggerInterop.CFStringCreateWithCString(
                0,
                DefaultRunLoopMode,
                MacTriggerInterop.StringEncodingUtf8);
            if (_source == 0 || _mode == 0)
            {
                return;
            }

            MacTriggerInterop.CFRunLoopAddSource(_runLoop, _source, _mode);
            MacTriggerInterop.CGEventTapEnable(_tap, true);
            Volatile.Write(ref _startedSuccessfully, 1);
            _started.Set();
            MacTriggerInterop.CFRunLoopRun();
        }
        catch
        {
            // 权限不足或系统拒绝创建 tap 时，键盘和鼠标按钮轮询仍可继续。
        }
        finally
        {
            _started.Set();
            if (_source != 0 && _runLoop != 0 && _mode != 0)
            {
                MacTriggerInterop.CFRunLoopRemoveSource(_runLoop, _source, _mode);
            }

            if (_tap != 0)
            {
                MacTriggerInterop.CGEventTapEnable(_tap, false);
            }

            if (_source != 0)
            {
                MacTriggerInterop.CFRunLoopSourceInvalidate(_source);
                MacTriggerInterop.CFRelease(_source);
                _source = 0;
            }

            if (_mode != 0)
            {
                MacTriggerInterop.CFRelease(_mode);
                _mode = 0;
            }

            if (_tap != 0)
            {
                MacTriggerInterop.CFMachPortInvalidate(_tap);
                MacTriggerInterop.CFRelease(_tap);
                _tap = 0;
            }

            _runLoop = 0;
        }
    }

    private nint HandleEvent(nint proxy, uint type, nint eventRef, nint userInfo)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return eventRef;
        }

        if (type is MacTriggerInterop.EventTapDisabledByTimeout
            or MacTriggerInterop.EventTapDisabledByUserInput)
        {
            if (_tap != 0)
            {
                MacTriggerInterop.CGEventTapEnable(_tap, true);
            }

            return eventRef;
        }

        if (eventRef == 0)
        {
            return eventRef;
        }

        if (type == MacTriggerInterop.EventScrollWheel)
        {
            var delta = MacTriggerInterop.CGEventGetIntegerValueField(
                eventRef,
                MacTriggerInterop.ScrollWheelEventDeltaAxis1);
            if (delta > 0)
            {
                _wheelPulses.RecordWheelUp(Stopwatch.GetTimestamp());
            }
            else if (delta < 0)
            {
                _wheelPulses.RecordWheelDown(Stopwatch.GetTimestamp());
            }
        }
        else if (type == MacTriggerInterop.EventKeyDown
                 && MacTriggerInterop.CGEventGetIntegerValueField(
                     eventRef,
                     MacTriggerInterop.KeyboardEventAutorepeat) == 0)
        {
            var keyCode = MacTriggerInterop.CGEventGetIntegerValueField(
                eventRef,
                MacTriggerInterop.KeyboardEventKeycode);
            if (keyCode is >= 0 and <= ushort.MaxValue)
            {
                _pressPulses.Record(new TriggerInputBinding(TriggerInputKind.Keyboard, (int)keyCode));
            }
        }
        else if (type == MacTriggerInterop.EventOtherMouseDown)
        {
            var button = MacTriggerInterop.CGEventGetIntegerValueField(
                eventRef,
                MacTriggerInterop.MouseEventButtonNumber);
            if (button is >= 0 and <= int.MaxValue)
            {
                _pressPulses.Record(new TriggerInputBinding(TriggerInputKind.MouseButton, (int)button));
            }
        }

        return eventRef;
    }
}

[SupportedOSPlatform("macos")]
internal static class MacTriggerInterop
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public const int EventSourceStateHidSystem = 1;
    public const uint EventKeyDown = 10;
    public const uint EventScrollWheel = 22;
    public const uint EventOtherMouseDown = 25;
    public const int MouseEventButtonNumber = 3;
    public const int KeyboardEventAutorepeat = 8;
    public const int KeyboardEventKeycode = 9;
    public const int ScrollWheelEventDeltaAxis1 = 11;
    public const ulong TriggerEventMask =
        1UL << (int)EventKeyDown
        | 1UL << (int)EventScrollWheel
        | 1UL << (int)EventOtherMouseDown;
    public const uint EventTapLocationSession = 1;
    public const uint HeadInsertEventTap = 0;
    public const uint EventTapOptionListenOnly = 1;
    public const uint EventTapDisabledByTimeout = 0xFFFFFFFE;
    public const uint EventTapDisabledByUserInput = 0xFFFFFFFF;
    public const uint StringEncodingUtf8 = 0x08000100;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate nint EventTapCallback(nint proxy, uint type, nint eventRef, nint userInfo);

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CGEventSourceKeyState(int stateId, ushort key);

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CGEventSourceButtonState(int stateId, uint button);

    [DllImport(CoreGraphics)]
    public static extern nint CGEventTapCreate(
        uint tap,
        uint place,
        uint options,
        ulong eventsOfInterest,
        EventTapCallback callback,
        nint userInfo);

    [DllImport(CoreGraphics)]
    public static extern void CGEventTapEnable(
        nint tap,
        [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(CoreGraphics)]
    public static extern long CGEventGetIntegerValueField(nint eventRef, int field);

    [DllImport(CoreFoundation)]
    public static extern nint CFMachPortCreateRunLoopSource(nint allocator, nint port, nint order);

    [DllImport(CoreFoundation)]
    public static extern nint CFRunLoopGetCurrent();

    [DllImport(CoreFoundation)]
    public static extern void CFRunLoopAddSource(nint runLoop, nint source, nint mode);

    [DllImport(CoreFoundation)]
    public static extern void CFRunLoopRemoveSource(nint runLoop, nint source, nint mode);

    [DllImport(CoreFoundation)]
    public static extern void CFRunLoopRun();

    [DllImport(CoreFoundation)]
    public static extern void CFRunLoopStop(nint runLoop);

    [DllImport(CoreFoundation)]
    public static extern void CFRunLoopSourceInvalidate(nint source);

    [DllImport(CoreFoundation)]
    public static extern void CFMachPortInvalidate(nint port);

    [DllImport(CoreFoundation)]
    public static extern nint CFStringCreateWithCString(nint allocator, string value, uint encoding);

    [DllImport(CoreFoundation)]
    public static extern void CFRelease(nint value);
}
