import CoreGraphics
import CoreMedia
import CoreVideo
import Foundation
import ScreenCaptureKit

@available(macOS 13.0, *)
private final class CaptureStartResult: @unchecked Sendable {
    private let lock = NSLock()
    private var value: Int32 = -1

    func set(_ newValue: Int32) {
        lock.lock()
        value = newValue
        lock.unlock()
    }

    func get() -> Int32 {
        lock.lock()
        defer { lock.unlock() }
        return value
    }
}

@available(macOS 13.0, *)
private final class ShigureCaptureSession: NSObject, SCStreamOutput, @unchecked Sendable {
    private let frameLock = NSLock()
    private let outputQueue = DispatchQueue(label: "com.arasaka.shigure.capture", qos: .userInteractive)
    private var stream: SCStream?
    private var latestBytes = Data()
    private var latestWidth: Int32 = 0
    private var latestHeight: Int32 = 0
    private var latestBytesPerRow: Int32 = 0

    func start(windowID: CGWindowID, x: Double, y: Double, width: Double, height: Double) -> Int32 {
        guard width > 0, height > 0 else {
            return -2
        }

        stop()
        let completion = DispatchSemaphore(value: 0)
        let result = CaptureStartResult()

        Task {
            do {
                var displayID: CGDirectDisplayID = 0
                var displayCount: UInt32 = 0
                let globalRect = CGRect(x: x, y: y, width: width, height: height)
                guard CGGetDisplaysWithRect(globalRect, 1, &displayID, &displayCount) == .success,
                      displayCount == 1 else {
                    result.set(-3)
                    completion.signal()
                    return
                }

                let displayBounds = CGDisplayBounds(displayID)
                let localRect = CGRect(
                    x: x - displayBounds.origin.x,
                    y: y - displayBounds.origin.y,
                    width: width,
                    height: height)
                guard localRect.minX >= 0,
                      localRect.minY >= 0,
                      localRect.maxX <= displayBounds.width,
                      localRect.maxY <= displayBounds.height else {
                    result.set(-4)
                    completion.signal()
                    return
                }

                let content = try await SCShareableContent.excludingDesktopWindows(
                    false,
                    onScreenWindowsOnly: false)
                guard let display = content.displays.first(where: { $0.displayID == displayID }) else {
                    result.set(-5)
                    completion.signal()
                    return
                }
                guard let window = content.windows.first(where: { $0.windowID == windowID }) else {
                    result.set(-8)
                    completion.signal()
                    return
                }

                let scaleX = Double(CGDisplayPixelsWide(displayID)) / displayBounds.width
                let scaleY = Double(CGDisplayPixelsHigh(displayID)) / displayBounds.height
                let configuration = SCStreamConfiguration()
                configuration.sourceRect = localRect
                configuration.width = max(1, Int(width * scaleX))
                configuration.height = max(1, Int(height * scaleY))
                configuration.pixelFormat = kCVPixelFormatType_32BGRA
                configuration.minimumFrameInterval = CMTime(value: 1, timescale: 10)
                configuration.queueDepth = 2
                configuration.showsCursor = false

                let filter = SCContentFilter(display: display, including: [window])
                let newStream = SCStream(filter: filter, configuration: configuration, delegate: nil)
                try newStream.addStreamOutput(self, type: .screen, sampleHandlerQueue: outputQueue)
                try await newStream.startCapture()
                stream = newStream
                result.set(0)
            } catch {
                result.set(-6)
            }
            completion.signal()
        }

        guard completion.wait(timeout: .now() + 5) == .success else {
            return -7
        }
        return result.get()
    }

    func stop() {
        guard let activeStream = stream else {
            clearFrame()
            return
        }

        stream = nil
        let completion = DispatchSemaphore(value: 0)
        Task {
            try? await activeStream.stopCapture()
            completion.signal()
        }
        _ = completion.wait(timeout: .now() + 2)
        clearFrame()
    }

    func latestSize(
        width: UnsafeMutablePointer<Int32>?,
        height: UnsafeMutablePointer<Int32>?,
        bytesPerRow: UnsafeMutablePointer<Int32>?
    ) -> Int32 {
        frameLock.lock()
        defer { frameLock.unlock() }
        guard !latestBytes.isEmpty else {
            return 0
        }

        width?.pointee = latestWidth
        height?.pointee = latestHeight
        bytesPerRow?.pointee = latestBytesPerRow
        return Int32(latestBytes.count)
    }

    func copyLatest(destination: UnsafeMutableRawPointer?, capacity: Int32) -> Int32 {
        frameLock.lock()
        defer { frameLock.unlock() }
        guard let destination,
              capacity >= latestBytes.count,
              !latestBytes.isEmpty else {
            return 0
        }

        latestBytes.copyBytes(to: destination.assumingMemoryBound(to: UInt8.self), count: latestBytes.count)
        return Int32(latestBytes.count)
    }

    func stream(
        _ stream: SCStream,
        didOutputSampleBuffer sampleBuffer: CMSampleBuffer,
        of outputType: SCStreamOutputType
    ) {
        guard outputType == .screen,
              sampleBuffer.isValid,
              let attachments = CMSampleBufferGetSampleAttachmentsArray(
                sampleBuffer,
                createIfNecessary: false) as? [[SCStreamFrameInfo: Any]],
              let statusValue = attachments.first?[.status] as? Int,
              SCFrameStatus(rawValue: statusValue) == .complete,
              let pixelBuffer = sampleBuffer.imageBuffer else {
            return
        }

        CVPixelBufferLockBaseAddress(pixelBuffer, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(pixelBuffer, .readOnly) }
        guard let baseAddress = CVPixelBufferGetBaseAddress(pixelBuffer) else {
            return
        }

        let width = CVPixelBufferGetWidth(pixelBuffer)
        let height = CVPixelBufferGetHeight(pixelBuffer)
        let bytesPerRow = CVPixelBufferGetBytesPerRow(pixelBuffer)
        let bytes = Data(bytes: baseAddress, count: bytesPerRow * height)

        frameLock.lock()
        latestBytes = bytes
        latestWidth = Int32(width)
        latestHeight = Int32(height)
        latestBytesPerRow = Int32(bytesPerRow)
        frameLock.unlock()
    }

    private func clearFrame() {
        frameLock.lock()
        latestBytes.removeAll(keepingCapacity: false)
        latestWidth = 0
        latestHeight = 0
        latestBytesPerRow = 0
        frameLock.unlock()
    }
}

@_cdecl("shigure_capture_create")
public func shigureCaptureCreate() -> UnsafeMutableRawPointer? {
    guard #available(macOS 13.0, *) else {
        return nil
    }
    return Unmanaged.passRetained(ShigureCaptureSession()).toOpaque()
}

@_cdecl("shigure_capture_start")
public func shigureCaptureStart(
    _ handle: UnsafeMutableRawPointer?,
    _ windowID: UInt32,
    _ x: Double,
    _ y: Double,
    _ width: Double,
    _ height: Double
) -> Int32 {
    guard #available(macOS 13.0, *), let handle else {
        return -1
    }
    return Unmanaged<ShigureCaptureSession>.fromOpaque(handle)
        .takeUnretainedValue()
        .start(windowID: windowID, x: x, y: y, width: width, height: height)
}

@_cdecl("shigure_capture_latest_size")
public func shigureCaptureLatestSize(
    _ handle: UnsafeMutableRawPointer?,
    _ width: UnsafeMutablePointer<Int32>?,
    _ height: UnsafeMutablePointer<Int32>?,
    _ bytesPerRow: UnsafeMutablePointer<Int32>?
) -> Int32 {
    guard #available(macOS 13.0, *), let handle else {
        return 0
    }
    return Unmanaged<ShigureCaptureSession>.fromOpaque(handle)
        .takeUnretainedValue()
        .latestSize(width: width, height: height, bytesPerRow: bytesPerRow)
}

@_cdecl("shigure_capture_copy_latest")
public func shigureCaptureCopyLatest(
    _ handle: UnsafeMutableRawPointer?,
    _ destination: UnsafeMutableRawPointer?,
    _ capacity: Int32
) -> Int32 {
    guard #available(macOS 13.0, *), let handle else {
        return 0
    }
    return Unmanaged<ShigureCaptureSession>.fromOpaque(handle)
        .takeUnretainedValue()
        .copyLatest(destination: destination, capacity: capacity)
}

@_cdecl("shigure_capture_stop")
public func shigureCaptureStop(_ handle: UnsafeMutableRawPointer?) {
    guard #available(macOS 13.0, *), let handle else {
        return
    }
    Unmanaged<ShigureCaptureSession>.fromOpaque(handle).takeUnretainedValue().stop()
}

@_cdecl("shigure_capture_destroy")
public func shigureCaptureDestroy(_ handle: UnsafeMutableRawPointer?) {
    guard #available(macOS 13.0, *), let handle else {
        return
    }
    let session = Unmanaged<ShigureCaptureSession>.fromOpaque(handle).takeRetainedValue()
    session.stop()
}
