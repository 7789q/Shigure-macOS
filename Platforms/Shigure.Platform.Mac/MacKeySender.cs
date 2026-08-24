using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Shigure.Platform;

namespace Shigure.Platform.MacOS;

public sealed class MacKeySender : ITargetKeyOutput
{
    private const ulong FlagShift = 0x20000;
    private const ulong FlagControl = 0x40000;
    private const ulong FlagOption = 0x80000;

    private readonly ITargetWindowLocator _targetLocator;
    private readonly IPlatformPermissionService _permissionService;
    private readonly IMacKeyEventApi _eventApi;
    private readonly IMacFrontmostApplicationProvider _frontmostApplication;

    [SupportedOSPlatform("macos")]
    public MacKeySender(
        ITargetWindowLocator targetLocator,
        IPlatformPermissionService permissionService)
        : this(
            targetLocator,
            permissionService,
            new MacKeyEventApi(),
            new MacFrontmostApplicationProvider())
    {
    }

    internal MacKeySender(
        ITargetWindowLocator targetLocator,
        IPlatformPermissionService permissionService,
        IMacKeyEventApi eventApi,
        IMacFrontmostApplicationProvider frontmostApplication)
    {
        _targetLocator = targetLocator;
        _permissionService = permissionService;
        _eventApi = eventApi;
        _frontmostApplication = frontmostApplication;
    }

    public KeySendResult Send(string hotkey, TargetIdentity? expectedTarget)
    {
        var binding = HotkeyParser.Parse(hotkey);
        if (binding is null)
        {
            return Fail(KeySendFailureKind.InvalidHotkey, $"无法解析按键“{hotkey}”");
        }

        var mainKeyCode = MacVirtualKeyMap.Resolve(binding.MainKey);
        if (mainKeyCode is null)
        {
            return Fail(KeySendFailureKind.UnknownKey, $"无法识别主键“{binding.MainKey}”");
        }

        var target = _targetLocator is IMacFreshTargetWindowLocator freshLocator
            ? freshLocator.FindFrontmostTargetFresh()
            : _targetLocator.FindFrontmostTarget();
        if (target is null || !target.Identity.IsValid || target.Identity.Platform != TargetPlatforms.MacOS)
        {
            return Fail(
                KeySendFailureKind.TargetUnavailable,
                $"未找到目标进程的可见 macOS 窗口（wow_process.txt: {_targetLocator.DescribeConfiguredProcesses()}）");
        }

        if (expectedTarget is not null && target.Identity != expectedTarget.Value)
        {
            return Fail(KeySendFailureKind.TargetChanged, "目标窗口已切换，等待重新扫描后再发送按键");
        }

        if (_frontmostApplication.GetProcessId() != target.Identity.ProcessId)
        {
            return Fail(KeySendFailureKind.TargetChanged, "目标窗口不在前台，已停止发送按键");
        }

        if (!_permissionService.Check().Accessibility.IsReady)
        {
            return Fail(KeySendFailureKind.PermissionDenied, "缺少辅助功能权限，无法向目标进程发送按键");
        }

        var source = _eventApi.CreateSource();
        if (source == 0)
        {
            return Fail(KeySendFailureKind.NativeFailure, "无法创建 macOS 键盘事件源");
        }

        var eventRefs = new List<nint>();
        try
        {
            var flags = binding.Modifiers.Aggregate(
                0UL,
                (current, modifier) => current | ResolveModifierFlag(modifier));
            MacKeyEventSpec[] sequence =
            [
                new(mainKeyCode.Value, true, flags),
                new(mainKeyCode.Value, false, flags)
            ];

            foreach (var item in sequence)
            {
                var eventRef = _eventApi.CreateKeyboardEvent(source, item.KeyCode, item.KeyDown);
                if (eventRef == 0)
                {
                    return Fail(KeySendFailureKind.NativeFailure, "无法创建完整的 macOS 键盘事件序列");
                }

                eventRefs.Add(eventRef);
                if (item.Flags != 0)
                {
                    _eventApi.SetFlags(eventRef, item.Flags);
                }
            }

            foreach (var eventRef in eventRefs)
            {
                _eventApi.Post(eventRef);
            }

            return KeySendResult.Success;
        }
        finally
        {
            foreach (var eventRef in eventRefs)
            {
                _eventApi.Release(eventRef);
            }

            _eventApi.Release(source);
        }
    }

    private static ulong ResolveModifierFlag(HotkeyModifier modifier) => modifier switch
    {
        HotkeyModifier.Control => FlagControl,
        HotkeyModifier.Alt => FlagOption,
        HotkeyModifier.Shift => FlagShift,
        _ => throw new ArgumentOutOfRangeException(nameof(modifier), modifier, null)
    };

    private static KeySendResult Fail(KeySendFailureKind kind, string reason) =>
        KeySendResult.Failure(kind, reason);

    private readonly record struct MacKeyEventSpec(ushort KeyCode, bool KeyDown, ulong Flags);
}

internal interface IMacKeyEventApi
{
    nint CreateSource();

    nint CreateKeyboardEvent(nint source, ushort keyCode, bool keyDown);

    void SetFlags(nint eventRef, ulong flags);

    void Post(nint eventRef);

    void Release(nint value);
}

[SupportedOSPlatform("macos")]
internal sealed class MacKeyEventApi : IMacKeyEventApi
{
    public nint CreateSource() => MacKeyOutputInterop.CGEventSourceCreate(MacKeyOutputInterop.EventSourceStateHidSystem);

    public nint CreateKeyboardEvent(nint source, ushort keyCode, bool keyDown) =>
        MacKeyOutputInterop.CGEventCreateKeyboardEvent(source, keyCode, keyDown);

    public void SetFlags(nint eventRef, ulong flags) => MacKeyOutputInterop.CGEventSetFlags(eventRef, flags);

    public void Post(nint eventRef) =>
        MacKeyOutputInterop.CGEventPost(MacKeyOutputInterop.EventTapHid, eventRef);

    public void Release(nint value) => MacKeyOutputInterop.CFRelease(value);
}

[SupportedOSPlatform("macos")]
internal static class MacKeyOutputInterop
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public const int EventSourceStateHidSystem = 1;
    public const uint EventTapHid = 0;
    [DllImport(CoreGraphics)]
    public static extern nint CGEventSourceCreate(int stateId);

    [DllImport(CoreGraphics)]
    public static extern nint CGEventCreateKeyboardEvent(
        nint source,
        ushort virtualKey,
        [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(CoreGraphics)]
    public static extern void CGEventSetFlags(nint eventRef, ulong flags);

    [DllImport(CoreGraphics)]
    public static extern void CGEventPost(uint tap, nint eventRef);

    [DllImport(CoreFoundation)]
    public static extern void CFRelease(nint value);
}

internal interface IMacFrontmostApplicationProvider
{
    int? GetProcessId();
}

[SupportedOSPlatform("macos")]
internal sealed class MacFrontmostApplicationProvider : IMacFrontmostApplicationProvider
{
    public int? GetProcessId()
    {
        var workspaceClass = MacApplicationInterop.objc_getClass("NSWorkspace");
        if (workspaceClass == 0)
        {
            return null;
        }

        var workspace = MacApplicationInterop.SendObject(
            workspaceClass,
            MacApplicationInterop.sel_registerName("sharedWorkspace"));
        var application = MacApplicationInterop.SendObject(
            workspace,
            MacApplicationInterop.sel_registerName("frontmostApplication"));
        if (application == 0)
        {
            return null;
        }

        var processId = MacApplicationInterop.SendInt32(
            application,
            MacApplicationInterop.sel_registerName("processIdentifier"));
        return processId > 0 ? processId : null;
    }
}

[SupportedOSPlatform("macos")]
internal static class MacApplicationInterop
{
    private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjectiveC)]
    public static extern nint objc_getClass(string name);

    [DllImport(ObjectiveC)]
    public static extern nint sel_registerName(string name);

    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    public static extern nint SendObject(nint receiver, nint selector);

    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    public static extern int SendInt32(nint receiver, nint selector);
}
