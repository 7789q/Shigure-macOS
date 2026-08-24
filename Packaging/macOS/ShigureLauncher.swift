import AppKit
import Darwin

private enum PermissionKind {
    case screenCapture
    case accessibility

    var argument: String {
        switch self {
        case .screenCapture: "screen-capture"
        case .accessibility: "accessibility"
        }
    }

    var displayName: String {
        switch self {
        case .screenCapture: "屏幕录制"
        case .accessibility: "辅助功能"
        }
    }
}

private enum ChildRole {
    case runtime
    case permission(PermissionKind)
    case moduleImport
}

@main
struct ShigureLauncher {
    static func main() {
        let application = NSApplication.shared
        let delegate = ShigureAppDelegate()
        application.delegate = delegate
        application.run()
    }
}

final class ShigureAppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    private var window: NSWindow?
    private var statusLabel: NSTextField?
    private var startStopButton: NSButton?
    private var screenPermissionLabel: NSTextField?
    private var accessibilityPermissionLabel: NSTextField?
    private var screenPermissionButton: NSButton?
    private var accessibilityPermissionButton: NSButton?
    private var moduleImportButton: NSButton?
    private var statusMenuItem: NSMenuItem?
    private var startStopMenuItem: NSMenuItem?
    private var applicationModuleImportMenuItem: NSMenuItem?
    private var moduleImportMenuItem: NSMenuItem?
    private var childProcess: Process?
    private var childRole: ChildRole?
    private var childLog: FileHandle?
    private var stopCompletions: [() -> Void] = []
    private var isStopping = false
    private var isExclusiveOperation = false
    private var isQuitting = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        configureMainMenu()
        configureStatusItem()
        configureWindow()
        showWindow(nil)
        startRuntime(nil)
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard childProcess?.isRunning == true else {
            return .terminateNow
        }

        isQuitting = true
        stopRuntime {
            sender.reply(toApplicationShouldTerminate: true)
        }
        return .terminateLater
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    private func configureMainMenu() {
        let mainMenu = NSMenu()
        let applicationItem = NSMenuItem()
        mainMenu.addItem(applicationItem)

        let applicationMenu = NSMenu(title: "Shigure")
        applicationMenu.addItem(withTitle: "关于 Shigure", action: #selector(showAbout), keyEquivalent: "")
        applicationMenu.addItem(.separator())
        applicationMenu.addItem(withTitle: "显示 Shigure", action: #selector(showWindow), keyEquivalent: "1")
        applicationModuleImportMenuItem = NSMenuItem(
            title: "导入旧模块…",
            action: #selector(importLegacyModules),
            keyEquivalent: "i")
        applicationMenu.addItem(applicationModuleImportMenuItem!)
        applicationMenu.addItem(withTitle: "打开日志", action: #selector(openLog), keyEquivalent: "l")
        applicationMenu.addItem(.separator())
        applicationMenu.addItem(withTitle: "退出 Shigure", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        applicationItem.submenu = applicationMenu
        NSApp.mainMenu = mainMenu
    }

    private func configureStatusItem() {
        if let button = statusItem.button {
            button.image = NSApp.applicationIconImage
            button.image?.size = NSSize(width: 18, height: 18)
            button.toolTip = "Shigure"
        }

        let menu = NSMenu()
        menu.addItem(withTitle: "显示 Shigure", action: #selector(showWindow), keyEquivalent: "")
        statusMenuItem = NSMenuItem(title: "状态：准备启动", action: nil, keyEquivalent: "")
        statusMenuItem?.isEnabled = false
        menu.addItem(statusMenuItem!)
        menu.addItem(.separator())
        startStopMenuItem = NSMenuItem(title: "停止", action: #selector(toggleRuntime), keyEquivalent: "")
        menu.addItem(startStopMenuItem!)
        moduleImportMenuItem = NSMenuItem(title: "导入旧模块…", action: #selector(importLegacyModules), keyEquivalent: "")
        menu.addItem(moduleImportMenuItem!)
        menu.addItem(withTitle: "打开日志", action: #selector(openLog), keyEquivalent: "")
        menu.addItem(.separator())
        menu.addItem(withTitle: "退出 Shigure", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "")
        statusItem.menu = menu
    }

    private func configureWindow() {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 420, height: 320),
            styleMask: [.titled, .closable, .miniaturizable],
            backing: .buffered,
            defer: false)
        window.title = "Shigure"
        window.center()
        window.delegate = self
        window.isReleasedWhenClosed = false

        let content = NSView(frame: window.contentView?.bounds ?? .zero)
        content.autoresizingMask = [.width, .height]

        let icon = NSImageView(frame: NSRect(x: 28, y: 216, width: 72, height: 72))
        icon.image = NSApp.applicationIconImage
        icon.imageScaling = .scaleProportionallyUpOrDown
        content.addSubview(icon)

        let title = NSTextField(labelWithString: "Shigure")
        title.font = NSFont.systemFont(ofSize: 24, weight: .semibold)
        title.frame = NSRect(x: 120, y: 249, width: 270, height: 32)
        content.addSubview(title)

        let subtitle = NSTextField(labelWithString: "macOS 运行时")
        subtitle.textColor = .secondaryLabelColor
        subtitle.font = NSFont.systemFont(ofSize: 13)
        subtitle.frame = NSRect(x: 120, y: 224, width: 270, height: 22)
        content.addSubview(subtitle)

        let status = NSTextField(labelWithString: "准备启动")
        status.font = NSFont.systemFont(ofSize: 13)
        status.frame = NSRect(x: 28, y: 176, width: 364, height: 22)
        content.addSubview(status)
        statusLabel = status

        let permissionsTitle = NSTextField(labelWithString: "系统权限")
        permissionsTitle.font = NSFont.systemFont(ofSize: 13, weight: .semibold)
        permissionsTitle.frame = NSRect(x: 28, y: 143, width: 120, height: 22)
        content.addSubview(permissionsTitle)

        let screenPermission = NSTextField(labelWithString: "屏幕录制：未请求")
        screenPermission.font = NSFont.systemFont(ofSize: 13)
        screenPermission.frame = NSRect(x: 28, y: 109, width: 242, height: 22)
        content.addSubview(screenPermission)
        screenPermissionLabel = screenPermission

        let screenButton = NSButton(
            title: "请求权限",
            target: self,
            action: #selector(requestScreenCapturePermission))
        screenButton.bezelStyle = .rounded
        screenButton.frame = NSRect(x: 282, y: 104, width: 110, height: 32)
        content.addSubview(screenButton)
        screenPermissionButton = screenButton

        let accessibilityPermission = NSTextField(labelWithString: "辅助功能：未请求")
        accessibilityPermission.font = NSFont.systemFont(ofSize: 13)
        accessibilityPermission.frame = NSRect(x: 28, y: 69, width: 242, height: 22)
        content.addSubview(accessibilityPermission)
        accessibilityPermissionLabel = accessibilityPermission

        let accessibilityButton = NSButton(
            title: "请求权限",
            target: self,
            action: #selector(requestAccessibilityPermission))
        accessibilityButton.bezelStyle = .rounded
        accessibilityButton.frame = NSRect(x: 282, y: 64, width: 110, height: 32)
        content.addSubview(accessibilityButton)
        accessibilityPermissionButton = accessibilityButton

        let startStop = NSButton(title: "停止", target: self, action: #selector(toggleRuntime))
        startStop.bezelStyle = .rounded
        startStop.frame = NSRect(x: 28, y: 28, width: 110, height: 32)
        content.addSubview(startStop)
        startStopButton = startStop

        let openLogButton = NSButton(title: "打开日志", target: self, action: #selector(openLog))
        openLogButton.bezelStyle = .rounded
        openLogButton.frame = NSRect(x: 150, y: 28, width: 110, height: 32)
        content.addSubview(openLogButton)

        let importButton = NSButton(
            title: "导入旧模块…",
            target: self,
            action: #selector(importLegacyModules))
        importButton.bezelStyle = .rounded
        importButton.frame = NSRect(x: 272, y: 28, width: 120, height: 32)
        content.addSubview(importButton)
        moduleImportButton = importButton

        window.contentView = content
        self.window = window
    }

    @objc private func showAbout() {
        NSApp.orderFrontStandardAboutPanel(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    @objc private func showWindow(_ sender: Any?) {
        window?.makeKeyAndOrderFront(sender)
        NSApp.activate(ignoringOtherApps: true)
    }

    @objc private func toggleRuntime(_ sender: Any?) {
        guard !isExclusiveOperation else {
            return
        }

        if runtimeIsRunning {
            stopRuntime(completion: nil)
        } else {
            startRuntime(nil)
        }
    }

    @objc private func requestScreenCapturePermission(_ sender: Any?) {
        requestPermission(.screenCapture)
    }

    @objc private func requestAccessibilityPermission(_ sender: Any?) {
        requestPermission(.accessibility)
    }

    @objc private func importLegacyModules(_ sender: Any?) {
        guard !isExclusiveOperation, !isQuitting, let window else {
            return
        }

        isExclusiveOperation = true
        updateStatus("选择旧数据目录")

        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.canCreateDirectories = false
        panel.prompt = "选择"
        panel.message = "选择包含 module 文件夹的旧 Shigure 数据目录。"
        panel.beginSheetModal(for: window) { [weak self] response in
            guard let self else {
                return
            }
            guard response == .OK, let source = panel.url else {
                self.isExclusiveOperation = false
                self.updateStatus(self.runtimeIsRunning ? "运行中" : "未运行")
                return
            }

            self.updateStatus("准备导入旧模块")
            self.stopRuntime { [weak self] in
                guard let self, !self.isQuitting else {
                    return
                }
                self.startModuleImportCommand(source.path)
            }
        }
    }

    @objc private func openLog(_ sender: Any?) {
        do {
            let url = try logURL()
            if !FileManager.default.fileExists(atPath: url.path) {
                FileManager.default.createFile(atPath: url.path, contents: Data())
            }
            NSWorkspace.shared.open(url)
        } catch {
            updateStatus("无法打开日志")
        }
    }

    @objc private func startRuntime(_ sender: Any?) {
        guard childProcess?.isRunning != true, !isStopping, !isExclusiveOperation else {
            return
        }

        do {
            try launchChild(
                arguments: ["--toggle", "WHEELDOWN", "--mode", "switch"],
                role: .runtime)
            updateStatus("运行中")
        } catch {
            updateStatus("启动失败")
        }
    }

    private func requestPermission(_ permission: PermissionKind) {
        guard !isExclusiveOperation, !isQuitting else {
            return
        }

        isExclusiveOperation = true
        updatePermission(permission, status: "正在准备")
        updateStatus("准备请求\(permission.displayName)权限")
        stopRuntime { [weak self] in
            guard let self, !self.isQuitting else {
                return
            }
            self.startPermissionCommand(permission)
        }
    }

    private func startPermissionCommand(_ permission: PermissionKind) {
        do {
            try launchChild(
                arguments: ["permission", "request", permission.argument],
                role: .permission(permission))
            updatePermission(permission, status: "正在请求")
            updateStatus("正在请求\(permission.displayName)权限")
        } catch {
            isExclusiveOperation = false
            updatePermission(permission, status: "请求失败")
            updateStatus("权限请求启动失败")
            startRuntime(nil)
        }
    }

    private func startModuleImportCommand(_ sourcePath: String) {
        do {
            try launchChild(
                arguments: ["modules", "import", sourcePath],
                role: .moduleImport)
            updateStatus("正在导入旧模块")
        } catch {
            isExclusiveOperation = false
            updateStatus("模块导入启动失败")
            startRuntime(nil)
        }
    }

    private func launchChild(arguments: [String], role: ChildRole) throws {
        let executable = try runtimeExecutableURL()
        let log = try openChildLog(role)
        let process = Process()
        process.executableURL = executable
        process.currentDirectoryURL = executable.deletingLastPathComponent()
        process.arguments = arguments
        var environment = ProcessInfo.processInfo.environment
        environment["SHIGURE_LAUNCHER_PID"] = String(getpid())
        process.environment = environment
        process.standardOutput = log
        process.standardError = log
        process.terminationHandler = { [weak self, weak process] _ in
            DispatchQueue.main.async {
                self?.finishChild(process)
            }
        }

        childProcess = process
        childRole = role
        childLog = log
        do {
            try process.run()
        } catch {
            childProcess = nil
            childRole = nil
            try? childLog?.close()
            childLog = nil
            throw error
        }
    }

    private func stopRuntime(completion: (() -> Void)?) {
        guard let process = childProcess, process.isRunning else {
            completion?()
            return
        }

        if isStopping {
            if let completion {
                stopCompletions.append(completion)
            }
            return
        }

        isStopping = true
        if let completion {
            stopCompletions.append(completion)
        }
        updateStatus("正在停止")
        process.terminate()

        DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + 3) {
            if process.isRunning {
                kill(process.processIdentifier, SIGKILL)
            }
        }
    }

    private func finishChild(_ process: Process?) {
        guard childProcess === process, let process else {
            return
        }

        let finishedRole = childRole
        childProcess = nil
        childRole = nil
        try? childLog?.close()
        childLog = nil
        isStopping = false

        var restartRuntime = false
        switch finishedRole {
        case .permission(let permission):
            isExclusiveOperation = false
            updatePermission(permission, process: process)
            updateStatus(isQuitting ? "已停止" : "权限请求已结束")
            restartRuntime = !isQuitting
        case .moduleImport:
            isExclusiveOperation = false
            updateStatus(isQuitting ? "已停止" : "模块导入已结束")
            if !isQuitting {
                showModuleImportResult(process)
                restartRuntime = true
            }
        case .runtime, .none:
            updateStatus(isQuitting ? "已停止" : "未运行")
        }

        let completions = stopCompletions
        stopCompletions.removeAll()
        completions.forEach { $0() }

        if restartRuntime {
            startRuntime(nil)
        }
    }

    private func updateStatus(_ text: String) {
        statusLabel?.stringValue = "状态：\(text)"
        statusMenuItem?.title = "状态：\(text)"
        let running = runtimeIsRunning && !isStopping
        startStopButton?.title = running ? "停止" : "启动"
        startStopMenuItem?.title = running ? "停止" : "启动"
        let controlsEnabled = !isStopping && !isExclusiveOperation && !isQuitting
        startStopButton?.isEnabled = controlsEnabled
        startStopMenuItem?.isEnabled = controlsEnabled
        screenPermissionButton?.isEnabled = controlsEnabled
        accessibilityPermissionButton?.isEnabled = controlsEnabled
        moduleImportButton?.isEnabled = controlsEnabled
        applicationModuleImportMenuItem?.isEnabled = controlsEnabled
        moduleImportMenuItem?.isEnabled = controlsEnabled
    }

    private var runtimeIsRunning: Bool {
        guard childProcess?.isRunning == true else {
            return false
        }
        if case .runtime? = childRole {
            return true
        }
        return false
    }

    private func updatePermission(_ permission: PermissionKind, status: String) {
        let text = "\(permission.displayName)：\(status)"
        switch permission {
        case .screenCapture:
            screenPermissionLabel?.stringValue = text
        case .accessibility:
            accessibilityPermissionLabel?.stringValue = text
        }
    }

    private func updatePermission(_ permission: PermissionKind, process: Process) {
        guard process.terminationReason == .exit else {
            updatePermission(permission, status: "请求中断")
            return
        }

        switch process.terminationStatus {
        case 0:
            updatePermission(permission, status: "已授权")
        case 10:
            updatePermission(permission, status: "已授权，正在重启")
        case 11:
            updatePermission(permission, status: "需在系统设置中处理")
        case 2:
            updatePermission(permission, status: "请求参数错误")
        default:
            updatePermission(permission, status: "请求失败")
        }
    }

    private func showModuleImportResult(_ process: Process) {
        let alert = NSAlert()
        alert.addButton(withTitle: "好")

        guard process.terminationReason == .exit else {
            alert.alertStyle = .warning
            alert.messageText = "模块导入已中断"
            alert.informativeText = "未完成的文件可在下次选择同一目录时继续。"
            present(alert)
            return
        }

        switch process.terminationStatus {
        case 0:
            alert.messageText = "模块导入完成"
            alert.informativeText = "新模块已加载；现有模块不会被覆盖。详情见日志。"
        case 12:
            alert.messageText = "未找到旧模块"
            alert.informativeText = "所选目录下没有 module 文件夹。"
        case 13:
            alert.alertStyle = .warning
            alert.messageText = "模块导入失败"
            alert.informativeText = "未覆盖现有模块。请打开日志查看失败数量。"
        default:
            alert.alertStyle = .warning
            alert.messageText = "模块导入命令失败"
            alert.informativeText = "请打开日志查看详情。"
        }
        present(alert)
    }

    private func present(_ alert: NSAlert) {
        if let window {
            alert.beginSheetModal(for: window)
        } else {
            alert.runModal()
        }
    }

    private func runtimeExecutableURL() throws -> URL {
        guard let resources = Bundle.main.resourceURL else {
            throw LauncherError.missingResources
        }

#if arch(arm64)
        let runtimeDirectory = "runtime-arm64"
#elseif arch(x86_64)
        let runtimeDirectory = "runtime-x64"
#else
#error("Unsupported macOS architecture")
#endif

        let executable = resources
            .appendingPathComponent(runtimeDirectory, isDirectory: true)
            .appendingPathComponent("Shigure.MacApp", isDirectory: false)
        guard FileManager.default.isExecutableFile(atPath: executable.path) else {
            throw LauncherError.missingRuntime
        }

        return executable
    }

    private func logURL() throws -> URL {
        guard let applicationSupport = FileManager.default.urls(
            for: .applicationSupportDirectory,
            in: .userDomainMask).first else {
            throw LauncherError.missingApplicationSupport
        }

        let logs = applicationSupport
            .appendingPathComponent("Shigure", isDirectory: true)
            .appendingPathComponent("logs", isDirectory: true)
        try FileManager.default.createDirectory(at: logs, withIntermediateDirectories: true)
        return logs.appendingPathComponent("Shigure.log", isDirectory: false)
    }

    private func openChildLog(_ role: ChildRole) throws -> FileHandle {
        let url = try logURL()
        if !FileManager.default.fileExists(atPath: url.path) {
            FileManager.default.createFile(atPath: url.path, contents: Data())
        }

        let handle = try FileHandle(forWritingTo: url)
        try handle.seekToEnd()
        let timestamp = ISO8601DateFormatter().string(from: Date())
        let childDescription: String
        switch role {
        case .runtime:
            childDescription = "runtime"
        case .permission(let permission):
            childDescription = "permission request \(permission.argument)"
        case .moduleImport:
            childDescription = "module import"
        }
        handle.write(Data("\n[\(timestamp)] launcher starting \(childDescription)\n".utf8))
        return handle
    }
}

private enum LauncherError: Error {
    case missingResources
    case missingRuntime
    case missingApplicationSupport
}
