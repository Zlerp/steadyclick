import AppKit
import ApplicationServices
import Carbon.HIToolbox
import CoreGraphics

/// Thin wrappers over the macOS calls that move and click the real mouse.
/// All positions are in global display coordinates: points from the top-left of the main display.
enum MouseIO {
    private static let source = CGEventSource(stateID: .hidSystemState)

    static var cursor: CGPoint {
        return CGEvent(source: nil)?.location ?? .zero
    }

    /// Posts one mouse event. A button event carries its own position, so the press lands on that
    /// exact point even if a hand is moving the mouse at the same moment.
    @discardableResult
    static func post(_ type: CGEventType, at point: CGPoint, button: CGMouseButton = .left, clickState: Int64 = 1) -> Bool {
        guard let event = CGEvent(mouseEventSource: source, mouseType: type,
                                  mouseCursorPosition: point, mouseButton: button) else {
            return false
        }
        event.setIntegerValueField(.mouseEventClickState, value: clickState)
        event.post(tap: .cghidEventTap)
        return true
    }

    /// macOS silently drops injected clicks unless the app is allowed under
    /// System Settings > Privacy & Security > Accessibility.
    static var trusted: Bool {
        return AXIsProcessTrusted()
    }

    static func promptForTrust() {
        let key = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        _ = AXIsProcessTrustedWithOptions([key: true] as CFDictionary)
    }

    static func openAccessibilitySettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            _ = NSWorkspace.shared.open(url)
        }
    }

    /// Bounds of every active display, in the same coordinates as the cursor.
    static func displays() -> [CGRect] {
        var count: UInt32 = 0
        guard CGGetActiveDisplayList(0, nil, &count) == .success, count > 0 else { return [] }
        var ids = [CGDirectDisplayID](repeating: 0, count: Int(count))
        guard CGGetActiveDisplayList(count, &ids, &count) == .success else { return [] }
        return ids.prefix(Int(count)).map { CGDisplayBounds($0) }
    }

    /// True when the cursor is jammed into the top-left corner of the display it is on.
    static func cursorInCorner() -> Bool {
        let p = cursor
        for bounds in displays() where bounds.insetBy(dx: -1, dy: -1).contains(p) {
            return p.x <= bounds.minX + 2 && p.y <= bounds.minY + 2
        }
        return false
    }
}

/// One choice in the hotkey menus.
struct HotkeyChoice {
    let label: String
    let keyCode: UInt32
    let modifiers: UInt32

    static let controlOption = UInt32(controlKey | optionKey)

    static let all: [HotkeyChoice] = [
        HotkeyChoice(label: "⌃⌥S", keyCode: UInt32(kVK_ANSI_S), modifiers: controlOption),
        HotkeyChoice(label: "⌃⌥P", keyCode: UInt32(kVK_ANSI_P), modifiers: controlOption),
        HotkeyChoice(label: "⌃⌥C", keyCode: UInt32(kVK_ANSI_C), modifiers: controlOption),
        HotkeyChoice(label: "⌃⌥X", keyCode: UInt32(kVK_ANSI_X), modifiers: controlOption),
        HotkeyChoice(label: "⌃⌥G", keyCode: UInt32(kVK_ANSI_G), modifiers: controlOption),
        HotkeyChoice(label: "F5", keyCode: UInt32(kVK_F5), modifiers: 0),
        HotkeyChoice(label: "F6", keyCode: UInt32(kVK_F6), modifiers: 0),
        HotkeyChoice(label: "F7", keyCode: UInt32(kVK_F7), modifiers: 0),
        HotkeyChoice(label: "F8", keyCode: UInt32(kVK_F8), modifiers: 0),
        HotkeyChoice(label: "F9", keyCode: UInt32(kVK_F9), modifiers: 0),
        HotkeyChoice(label: "F10", keyCode: UInt32(kVK_F10), modifiers: 0)
    ]

    static func named(_ label: String) -> HotkeyChoice? {
        return all.first { $0.label == label }
    }
}

/// System-wide hotkeys through Carbon, which works from any app and needs no extra permission.
final class HotkeyCenter {
    static let shared = HotkeyCenter()

    private var refs: [UInt32: EventHotKeyRef] = [:]
    private var handlers: [UInt32: () -> Void] = [:]
    private var installed = false

    private func installHandler() {
        if installed { return }
        installed = true
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        _ = InstallEventHandler(GetEventDispatcherTarget(), { _, event, _ -> OSStatus in
            var pressed = EventHotKeyID()
            let status = GetEventParameter(event,
                                           EventParamName(kEventParamDirectObject),
                                           EventParamType(typeEventHotKeyID),
                                           nil,
                                           MemoryLayout<EventHotKeyID>.size,
                                           nil,
                                           &pressed)
            if status == noErr {
                HotkeyCenter.shared.fire(pressed.id)
            }
            return noErr
        }, 1, &spec, nil, nil)
    }

    /// Returns false if the system refused the key (usually because another app owns it).
    func register(id: UInt32, choice: HotkeyChoice, handler: @escaping () -> Void) -> Bool {
        installHandler()
        unregister(id: id)
        var ref: EventHotKeyRef?
        let hotKeyID = EventHotKeyID(signature: OSType(0x53544B43), id: id)   // 'STKC'
        let status = RegisterEventHotKey(choice.keyCode, choice.modifiers, hotKeyID,
                                         GetEventDispatcherTarget(), 0, &ref)
        guard status == noErr, let registered = ref else { return false }
        refs[id] = registered
        handlers[id] = handler
        return true
    }

    func unregister(id: UInt32) {
        if let ref = refs.removeValue(forKey: id) {
            _ = UnregisterEventHotKey(ref)
        }
        handlers[id] = nil
    }

    private func fire(_ id: UInt32) {
        handlers[id]?()
    }
}
