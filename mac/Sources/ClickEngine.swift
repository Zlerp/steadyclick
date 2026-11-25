import CoreGraphics
import Foundation

/// Runs the click loop on its own thread. The window polls `snapshot()`; nothing here touches the UI.
final class ClickEngine {
    struct Snapshot {
        var running = false
        var count = 0
        var stopReason = ""
        var secondsUntilNext = 0.0
    }

    private enum WaitResult { case elapsed, stopped, failsafe, timeLimit }

    private static let settle = 0.015   // pause after moving the cursor so the target app sees the hover
    private static let poll = 0.02      // how often a wait wakes up to check the stop conditions

    /// Every click of the current (or most recent) run.
    let log = ClickLog()

    private let lock = NSLock()
    private var running = false
    private var cancelled = false
    private var clicks = 0
    private var reason = ""
    private var nextDue = -1.0
    private var options = ClickOptions()
    private var runStart = 0.0

    private static func now() -> Double {
        return ProcessInfo.processInfo.systemUptime
    }

    func snapshot() -> Snapshot {
        lock.lock()
        defer { lock.unlock() }
        var s = Snapshot()
        s.running = running
        s.count = clicks
        s.stopReason = reason
        s.secondsUntilNext = running && nextDue >= 0 ? max(0, nextDue - ClickEngine.now()) : 0
        return s
    }

    var isRunning: Bool {
        lock.lock()
        defer { lock.unlock() }
        return running
    }

    func start(_ o: ClickOptions) {
        lock.lock()
        if running {
            lock.unlock()
            return
        }
        options = o
        cancelled = false
        clicks = 0
        reason = ""
        nextDue = -1
        running = true
        lock.unlock()
        log.begin()

        let thread = Thread { [weak self] in
            self?.run()
        }
        thread.name = "ClickEngine"
        thread.qualityOfService = .userInteractive
        thread.start()
    }

    /// Stops the run and waits briefly for the worker so a restart is always clean.
    func stop() {
        lock.lock()
        let wasRunning = running
        cancelled = true
        lock.unlock()
        if !wasRunning { return }
        let deadline = ClickEngine.now() + 1.0
        while isRunning && ClickEngine.now() < deadline {
            Thread.sleep(forTimeInterval: 0.002)
        }
    }

    private var isCancelled: Bool {
        lock.lock()
        defer { lock.unlock() }
        return cancelled
    }

    private func finish(_ why: String) {
        log.autoSave()
        lock.lock()
        nextDue = -1
        reason = why
        running = false
        lock.unlock()
    }

    private func run() {
        lock.lock()
        let o = options
        lock.unlock()

        // SteadyClick is almost never the front app while it works. Without this, macOS treats it as
        // idle (App Nap) and stretches its timers, which makes every wait run late.
        let activity = ProcessInfo.processInfo.beginActivity(
            options: [.userInitiatedAllowingIdleSystemSleep, .latencyCritical],
            reason: "Clicking on a schedule")
        defer { ProcessInfo.processInfo.endActivity(activity) }

        let rhythm = Rhythm(o, seed: UInt64.random(in: 0...UInt64.max))

        if o.startDelay > 0 {
            let first = wait(o.startDelay, o, countsTowardLimit: false)
            if first != .elapsed {
                finish(describe(first))
                return
            }
        }

        runStart = ClickEngine.now()
        var firstClickAt = -1.0
        var previousClickAt = -1.0
        var cameAfterPause = false
        while true {
            let clickAt = ClickEngine.now()
            var record = ClickRecord()
            record.time = Date()
            if let failure = click(o, rhythm, &record) {
                finish(failure)
                return
            }

            lock.lock()
            clicks += 1
            let done = clicks
            lock.unlock()

            if firstClickAt < 0 { firstClickAt = clickAt }
            record.index = done
            record.sinceStart = clickAt - firstClickAt
            record.wait = previousClickAt < 0 ? 0 : clickAt - previousClickAt
            record.pause = cameAfterPause
            record.button = o.button
            record.doubleClick = o.doubleClick
            log.add(record)
            previousClickAt = clickAt
            if o.maxClicks >= 1 && Double(done) >= o.maxClicks {
                finish("Finished: reached \(done) clicks")
                return
            }

            // The interval is measured click-start to click-start, so the hold time does not stretch it.
            let next = rhythm.nextInterval()
            cameAfterPause = next.pause
            let result = wait(next.wait - (ClickEngine.now() - clickAt), o, countsTowardLimit: true)
            if result != .elapsed {
                finish(describe(result))
                return
            }
        }
    }

    private func describe(_ r: WaitResult) -> String {
        switch r {
        case .failsafe: return "Emergency stop: cursor hit the top-left corner"
        case .timeLimit: return "Finished: reached the time limit"
        default: return "Stopped"
        }
    }

    /// Sleeps for the given time while watching every stop condition.
    private func wait(_ seconds: Double, _ o: ClickOptions, countsTowardLimit: Bool) -> WaitResult {
        let due = ClickEngine.now() + max(0, seconds)
        lock.lock()
        nextDue = due
        lock.unlock()

        while true {
            if isCancelled { return .stopped }
            if o.cornerFailsafe && MouseIO.cursorInCorner() { return .failsafe }
            if countsTowardLimit && o.maxMinutes > 0 && ClickEngine.now() - runStart >= o.maxMinutes * 60 {
                return .timeLimit
            }
            let left = due - ClickEngine.now()
            if left <= 0 { return .elapsed }
            Thread.sleep(forTimeInterval: min(left, ClickEngine.poll))
        }
    }

    /// Sleeps in short slices. Returns true if the run was cancelled meanwhile.
    private func pause(_ seconds: Double) -> Bool {
        let due = ClickEngine.now() + seconds
        while true {
            if isCancelled { return true }
            let left = due - ClickEngine.now()
            if left <= 0 { return false }
            Thread.sleep(forTimeInterval: min(left, 0.005))
        }
    }

    /// Performs one click (or double click). Returns nil on success, otherwise why it failed.
    private func click(_ o: ClickOptions, _ rhythm: Rhythm, _ record: inout ClickRecord) -> String? {
        let original = MouseIO.cursor
        var target = original
        record.x = Int(original.x.rounded())
        record.y = Int(original.y.rounded())

        func restore() {
            if o.fixedPoint && o.restoreCursor {
                MouseIO.post(.mouseMoved, at: original)
            }
        }

        if o.fixedPoint {
            let offset = rhythm.nextOffset()
            target = CGPoint(x: o.x + Double(offset.dx), y: o.y + Double(offset.dy))
            record.x = Int(target.x.rounded())
            record.y = Int(target.y.rounded())
            record.offset = Double(offset.dx * offset.dx + offset.dy * offset.dy).squareRoot()
            if !MouseIO.post(.mouseMoved, at: target) {
                return "Stopped: macOS would not move the cursor"
            }
            if pause(ClickEngine.settle) {
                restore()
                return "Stopped"
            }
        }

        let down: CGEventType, up: CGEventType, button: CGMouseButton
        switch o.button {
        case 1: down = .rightMouseDown; up = .rightMouseUp; button = .right
        case 2: down = .otherMouseDown; up = .otherMouseUp; button = .center
        default: down = .leftMouseDown; up = .leftMouseUp; button = .left
        }

        let presses = o.doubleClick ? 2 : 1
        for press in 1...presses {
            if press > 1 && pause(Double.random(in: 0.05...0.09)) {
                restore()
                return "Stopped"
            }
            // With a fixed spot the press and the release each carry the position themselves, so a
            // hand nudging the mouse mid-click cannot move the click or turn it into a drag.
            let pressAt = o.fixedPoint ? target : MouseIO.cursor
            if !MouseIO.post(down, at: pressAt, button: button, clickState: Int64(press)) {
                restore()
                return "Stopped: macOS would not create the click"
            }
            let hold = rhythm.nextHoldMs()
            if press == 1 { record.holdMs = hold }
            let wasCancelled = pause(Double(hold) / 1000)
            // never leave the button held down
            MouseIO.post(up, at: o.fixedPoint ? target : MouseIO.cursor, button: button, clickState: Int64(press))
            if wasCancelled {
                restore()
                return "Stopped"
            }
        }

        if o.fixedPoint && o.restoreCursor {
            _ = pause(ClickEngine.settle)
            MouseIO.post(.mouseMoved, at: original)
        }
        return nil
    }
}
