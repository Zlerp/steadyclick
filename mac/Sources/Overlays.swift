import AppKit

/// Converts between the two coordinate systems macOS uses: the cursor and click APIs measure from
/// the top-left of the main display, windows measure from its bottom-left.
enum Coordinates {
    static var mainHeight: CGFloat {
        return NSScreen.screens.first?.frame.height ?? 0
    }

    /// A rectangle centred on a cursor-space point, expressed in window space.
    static func windowRect(centre: CGPoint, radius: CGFloat) -> NSRect {
        return NSRect(x: centre.x - radius, y: mainHeight - centre.y - radius,
                      width: radius * 2, height: radius * 2)
    }
}

/// A borderless window that can still take the keyboard.
final class KeyWindow: NSWindow {
    override var canBecomeKey: Bool { return true }
    override var canBecomeMain: Bool { return true }
}

/// The dimmed layer drawn over one screen while the user picks a spot.
final class PickView: NSView {
    var message = ""
    var onAccept: (() -> Void)?
    var onCancel: (() -> Void)?

    override var acceptsFirstResponder: Bool { return true }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool {
        return true
    }

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: .crosshair)
    }

    override func mouseDown(with event: NSEvent) {
        onAccept?()
    }

    override func rightMouseDown(with event: NSEvent) {
        onCancel?()
    }

    override func keyDown(with event: NSEvent) {
        switch event.keyCode {
        case 53: onCancel?()                 // Esc
        case 36, 76, 49: onAccept?()         // Return, keypad Enter, Space
        default: break
        }
    }

    override func draw(_ dirtyRect: NSRect) {
        NSColor(calibratedWhite: 0.05, alpha: 0.5).setFill()
        bounds.fill()

        let centred = NSMutableParagraphStyle()
        centred.alignment = .center
        let title = NSAttributedString(string: message, attributes: [
            .font: NSFont.systemFont(ofSize: 28, weight: .bold),
            .foregroundColor: NSColor.white,
            .paragraphStyle: centred
        ])
        let hint = NSAttributedString(string: "Return or Space also work.  Esc or right-click cancels.", attributes: [
            .font: NSFont.systemFont(ofSize: 15),
            .foregroundColor: NSColor(calibratedWhite: 0.85, alpha: 1),
            .paragraphStyle: centred
        ])
        let top = bounds.maxY - bounds.height * 0.16
        title.draw(in: NSRect(x: bounds.minX, y: top - 40, width: bounds.width, height: 40))
        hint.draw(in: NSRect(x: bounds.minX, y: top - 70, width: bounds.width, height: 24))
    }
}

/// Dims every screen and lets the user choose the spot by clicking, or by pressing the pick
/// hotkey, Return or Space with the cursor over it.
final class PickOverlay {
    private var windows: [NSWindow] = []
    private var done: ((CGPoint?) -> Void)?

    var isActive: Bool {
        return !windows.isEmpty
    }

    func begin(hotkeyLabel: String, done: @escaping (CGPoint?) -> Void) {
        if isActive { return }
        self.done = done

        for screen in NSScreen.screens {
            let window = KeyWindow(contentRect: screen.frame, styleMask: .borderless,
                                   backing: .buffered, defer: false)
            window.isReleasedWhenClosed = false
            window.level = .screenSaver
            window.isOpaque = false
            window.backgroundColor = .clear
            window.hasShadow = false
            window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]

            let view = PickView(frame: NSRect(origin: .zero, size: screen.frame.size))
            view.message = "Point at the spot, then click or press \(hotkeyLabel)"
            view.onAccept = { [weak self] in self?.acceptAtCursor() }
            view.onCancel = { [weak self] in self?.finish(nil) }
            window.contentView = view
            window.setFrame(screen.frame, display: true)
            windows.append(window)
        }

        NSApp.activate(ignoringOtherApps: true)
        for window in windows {
            window.orderFrontRegardless()
        }
        if let first = windows.first {
            first.makeKeyAndOrderFront(nil)
            first.makeFirstResponder(first.contentView)
        }
    }

    /// Takes the spot under the cursor right now and closes. Called for clicks and the pick hotkey.
    func acceptAtCursor() {
        if !isActive { return }
        let p = MouseIO.cursor
        finish(CGPoint(x: p.x.rounded(), y: p.y.rounded()))
    }

    func cancel() {
        finish(nil)
    }

    private func finish(_ point: CGPoint?) {
        if !isActive { return }
        let closing = windows
        windows = []
        for window in closing {
            window.orderOut(nil)
        }
        let callback = done
        done = nil
        callback?(point)
    }
}

/// The ring drawn by a spot marker.
final class RingView: NSView {
    override func draw(_ dirtyRect: NSRect) {
        let ring = NSBezierPath(ovalIn: bounds.insetBy(dx: 2, dy: 2))
        ring.lineWidth = 3
        NSColor.systemRed.setStroke()
        ring.stroke()

        let c = NSPoint(x: bounds.midX, y: bounds.midY)
        let cross = NSBezierPath()
        cross.move(to: NSPoint(x: c.x - 6, y: c.y))
        cross.line(to: NSPoint(x: c.x + 6, y: c.y))
        cross.move(to: NSPoint(x: c.x, y: c.y - 6))
        cross.line(to: NSPoint(x: c.x, y: c.y + 6))
        cross.lineWidth = 1
        cross.stroke()
    }
}

/// Flashes a ring over the target for a moment. It ignores the mouse and never takes focus, so it
/// cannot swallow a click meant for the app underneath.
enum SpotMarker {
    private static var live: [NSWindow] = []

    static func flash(at centre: CGPoint, jitterRadius: Double) {
        let radius = CGFloat(max(jitterRadius, 12) + 4)
        let window = NSWindow(contentRect: Coordinates.windowRect(centre: centre, radius: radius),
                              styleMask: .borderless, backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.level = .screenSaver
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = false
        window.ignoresMouseEvents = true
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        window.contentView = RingView(frame: NSRect(x: 0, y: 0, width: radius * 2, height: radius * 2))
        window.orderFrontRegardless()
        live.append(window)

        DispatchQueue.main.asyncAfter(deadline: .now() + 1.1) {
            window.orderOut(nil)
            live.removeAll { $0 === window }
        }
    }
}
