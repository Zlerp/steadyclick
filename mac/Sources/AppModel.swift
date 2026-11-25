import AppKit
import SwiftUI
import UniformTypeIdentifiers

/// Everything the windows show, and every action they can take. Lives on the main thread.
final class AppModel: ObservableObject {
    @Published var o: ClickOptions {
        didSet { optionsChanged(from: oldValue) }
    }
    @Published var notice = "Ready."
    @Published var running = false
    @Published var runLine = ""
    @Published var preview = PreviewData()
    @Published var hotkeyProblem = ""
    @Published var trusted = true

    // Session details
    @Published var stats = SessionStats()
    @Published var recent: [ClickRecord] = []
    @Published var savedPath = ""
    @Published var detailsNote = ""

    weak var window: NSWindow?
    let engine = ClickEngine()

    private let overlay = PickOverlay()
    private var timer: Timer?
    private var wasRunning = false
    private var shownCount = -1
    private var detailsWindow: NSWindow?
    private var detailsVisible = false
    private var closeObserver: NSObjectProtocol?

    private static let storeKey = "options.v1"

    init() {
        if let data = UserDefaults.standard.data(forKey: AppModel.storeKey),
           let saved = try? JSONDecoder().decode(ClickOptions.self, from: data) {
            o = saved
        } else {
            // Default target: the middle of the main display (never the emergency-stop corner).
            var fresh = ClickOptions()
            if let main = MouseIO.displays().first {
                fresh.x = Double(main.midX.rounded())
                fresh.y = Double(main.midY.rounded())
            }
            o = fresh
        }
        preview = PreviewData.make(o.sanitized())
        trusted = MouseIO.trusted
        registerHotkeys()
        timer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in
            self?.tick()
        }
    }

    // MARK: - Text for the window

    /// The line under the Start button.
    var statusLine: String {
        if running { return runLine }
        return hotkeyProblem.isEmpty ? notice : hotkeyProblem
    }

    /// One plain sentence describing exactly what pressing Start will do.
    var summary: String {
        let s = o.sanitized()
        func short(_ v: Double) -> String {
            let text = String(format: "%.2f", v)
            if !text.contains(".") { return text }
            var trimmed = text
            while trimmed.hasSuffix("0") { trimmed.removeLast() }
            if trimmed.hasSuffix(".") { trimmed.removeLast() }
            return trimmed
        }

        let names = ["Left", "Right", "Middle"]
        let what = names[s.button] + (s.doubleClick ? " double-click" : " click")

        var place = "wherever the cursor is"
        if s.fixedPoint {
            place = "at \(Int(s.x)), \(Int(s.y))"
            if s.jitterRadius > 0 { place += " (within \(Int(s.jitterRadius)) pt)" }
        }

        let range = Rhythm.effectiveRange(s)
        var when = "every \(short(range.lo)) to \(short(range.hi)) s at random"
        if s.humanize {
            when = "every \(short(range.lo)) to \(short(range.hi)) s in a human-like rhythm"
        } else if range.lo == range.hi {
            when = "every \(short(range.lo)) s"
        }

        let clicksPart = s.maxClicks >= 1 ? "\(Int(s.maxClicks)) \(s.maxClicks == 1 ? "click" : "clicks")" : ""
        let timePart = s.maxMinutes > 0 ? "\(short(s.maxMinutes)) min" : ""
        var until = "until you press \(s.toggleKey)"
        if !clicksPart.isEmpty && !timePart.isEmpty {
            until = "for \(clicksPart) or \(timePart), whichever comes first"
        } else if !clicksPart.isEmpty {
            until = "for \(clicksPart)"
        } else if !timePart.isEmpty {
            until = "for \(timePart)"
        }
        return "\(what) \(place), \(when), \(until)."
    }

    // MARK: - Settings

    private func optionsChanged(from old: ClickOptions) {
        if let data = try? JSONEncoder().encode(o) {
            UserDefaults.standard.set(data, forKey: AppModel.storeKey)
        }
        let next = PreviewData.make(o.sanitized())
        if next != preview { preview = next }
        if old.toggleKey != o.toggleKey || old.pickKey != o.pickKey { registerHotkeys() }
        if old.alwaysOnTop != o.alwaysOnTop { applyWindowLevel() }
    }

    func applyWindowLevel() {
        window?.level = o.alwaysOnTop ? .floating : .normal
    }

    private func registerHotkeys() {
        HotkeyCenter.shared.unregister(id: 1)
        HotkeyCenter.shared.unregister(id: 2)
        guard let toggleChoice = HotkeyChoice.named(o.toggleKey),
              let pickChoice = HotkeyChoice.named(o.pickKey) else {
            hotkeyProblem = "Choose a hotkey for start / stop and for pick spot."
            return
        }

        let okToggle = HotkeyCenter.shared.register(id: 1, choice: toggleChoice) { [weak self] in
            self?.toggle()
        }
        if toggleChoice.label == pickChoice.label {
            hotkeyProblem = "Start / stop and Pick spot can't share a key. Pick spot is off until you change one."
            return
        }
        let okPick = HotkeyCenter.shared.register(id: 2, choice: pickChoice) { [weak self] in
            self?.pickHotkeyPressed()
        }
        if !okToggle {
            hotkeyProblem = "\(toggleChoice.label) is taken by another app. Choose a different start / stop key."
        } else if !okPick {
            hotkeyProblem = "\(pickChoice.label) is taken by another app. Choose a different pick key."
        } else {
            hotkeyProblem = ""
        }
    }

    // MARK: - Choosing the spot

    private func setSpot(_ p: CGPoint) {
        var next = o
        next.fixedPoint = true
        next.x = Double(p.x.rounded())
        next.y = Double(p.y.rounded())
        o = next
        notice = "Spot set to \(Int(next.x)), \(Int(next.y))."
        flashSpot()
    }

    /// The pick hotkey: inside the overlay it confirms the spot under the cursor, anywhere else it
    /// sets the spot to wherever the cursor is right now.
    private func pickHotkeyPressed() {
        if overlay.isActive {
            overlay.acceptAtCursor()
        } else if !engine.isRunning {
            setSpot(MouseIO.cursor)
        }
    }

    func pickOnScreen() {
        if overlay.isActive || engine.isRunning { return }
        overlay.begin(hotkeyLabel: o.pickKey) { [weak self] point in
            guard let self = self else { return }
            if let p = point { self.setSpot(p) }
            self.window?.makeKeyAndOrderFront(nil)
        }
    }

    func flashSpot() {
        let s = o.sanitized()
        SpotMarker.flash(at: CGPoint(x: s.x, y: s.y), jitterRadius: s.jitterRadius)
    }

    // MARK: - Start / stop

    func toggle() {
        if overlay.isActive { return }
        if engine.isRunning {
            engine.stop()
        } else {
            start()
        }
        tick()
    }

    private func start() {
        trusted = MouseIO.trusted
        if !trusted {
            MouseIO.promptForTrust()
            notice = "Allow SteadyClick under Privacy & Security > Accessibility, then press Start again."
            return
        }
        let s = o.sanitized()
        if s.fixedPoint {
            let target = CGPoint(x: s.x, y: s.y)
            guard let screen = MouseIO.displays().first(where: { $0.contains(target) }) else {
                notice = "That spot is off-screen. Pick a new one."
                return
            }
            let reach = CGFloat(s.jitterRadius + 3)
            if s.cornerFailsafe && target.x <= screen.minX + reach && target.y <= screen.minY + reach {
                notice = "That spot is inside the emergency-stop corner. Move it, or turn the emergency stop off."
                return
            }
        }
        engine.start(s)
    }

    func shutdown() {
        engine.stop()
        timer?.invalidate()
    }

    private func tick() {
        let s = engine.snapshot()

        let nowTrusted = MouseIO.trusted
        if nowTrusted != trusted { trusted = nowTrusted }

        if s.running != wasRunning {
            wasRunning = s.running
            running = s.running
            if !s.running {
                notice = "\(s.stopReason) after \(s.count) \(s.count == 1 ? "click" : "clicks")."
            }
        }
        if s.running {
            let line = "Running: \(s.count) \(s.count == 1 ? "click" : "clicks"), next in " +
                String(format: "%.1f", s.secondsUntilNext) + " s"
            if line != runLine { runLine = line }
        }
        if detailsVisible { refreshDetails() }
    }

    // MARK: - Session details

    private func refreshDetails() {
        let count = engine.log.count
        let saved = engine.log.savedPath
        if count == shownCount && saved == savedPath { return }
        shownCount = count
        stats = engine.log.stats()
        recent = Array(engine.log.tail(500).reversed())   // newest first
        savedPath = saved
    }

    func showDetails() {
        if detailsWindow == nil {
            let host = NSHostingController(rootView: DetailsView().environmentObject(self))
            let created = NSWindow(contentViewController: host)
            created.title = "SteadyClick: session details"
            created.styleMask = [.titled, .closable, .resizable, .miniaturizable]
            created.isReleasedWhenClosed = false
            created.setContentSize(NSSize(width: 660, height: 540))
            if let main = window {
                created.setFrameTopLeftPoint(NSPoint(x: main.frame.maxX + 10, y: main.frame.maxY))
            }
            closeObserver = NotificationCenter.default.addObserver(
                forName: NSWindow.willCloseNotification, object: created, queue: .main
            ) { [weak self] _ in
                self?.detailsVisible = false
            }
            detailsWindow = created
        }
        detailsVisible = true
        shownCount = -1
        refreshDetails()
        detailsWindow?.makeKeyAndOrderFront(nil)
    }

    func saveCsv() {
        let stamp = DateFormatter()
        stamp.locale = Locale(identifier: "en_US_POSIX")
        stamp.dateFormat = "yyyy-MM-dd_HH-mm-ss"
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.commaSeparatedText]
        panel.nameFieldStringValue = "steadyclick-\(stamp.string(from: Date())).csv"
        if panel.runModal() == .OK, let url = panel.url {
            do {
                try engine.log.saveCsv(to: url)
                detailsNote = "Saved to \(url.path)"
            } catch {
                detailsNote = "Could not save: \(error.localizedDescription)"
            }
        }
    }

    func openLogFolder() {
        try? FileManager.default.createDirectory(at: ClickLog.folder, withIntermediateDirectories: true)
        _ = NSWorkspace.shared.open(ClickLog.folder)
    }
}
