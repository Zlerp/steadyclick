import Foundation

/// What happened on one click.
struct ClickRecord: Identifiable {
    var index = 0              // 1-based
    var time = Date()          // wall-clock time of the click
    var sinceStart = 0.0       // seconds since the first click of the run
    var wait = 0.0             // seconds since the previous click; 0 for the first
    var pause = false          // that wait was one of the human-like long pauses
    var holdMs = 0             // how long the button was held
    var x = 0
    var y = 0
    var offset = 0.0           // points from the exact spot (0 when following the cursor)
    var button = 0
    var doubleClick = false

    var id: Int { return index }
}

/// Totals for one run, worked out from its records.
struct SessionStats {
    var clicks = 0
    var elapsed = 0.0
    var clicksPerMinute = 0.0
    var waits = 0
    var averageWait = 0.0
    var medianWait = 0.0
    var shortestWait = 0.0
    var longestWait = 0.0
    var pauses = 0
    var averageHoldMs = 0.0
    var furthestOffset = 0.0
}

/// The record of the current (or most recent) run. Written by the click thread, read by the
/// window, saved to a CSV file when the run ends.
final class ClickLog {
    private static let capacity = 500_000   // about 6 days at one click a second
    private static let keepFiles = 200

    /// Tests turn this off so they do not leave files behind.
    static var autoSaveEnabled = true

    private let lock = NSLock()
    private var items: [ClickRecord] = []
    private var started = Date()
    private var saved = ""

    static var folder: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSTemporaryDirectory())
        return base.appendingPathComponent("SteadyClick", isDirectory: true)
            .appendingPathComponent("logs", isDirectory: true)
    }

    var count: Int {
        lock.lock()
        defer { lock.unlock() }
        return items.count
    }

    /// Where the last finished run was saved, or empty.
    var savedPath: String {
        lock.lock()
        defer { lock.unlock() }
        return saved
    }

    func begin() {
        lock.lock()
        items.removeAll(keepingCapacity: true)
        started = Date()
        saved = ""
        lock.unlock()
    }

    func add(_ record: ClickRecord) {
        lock.lock()
        if items.count < ClickLog.capacity {
            items.append(record)
        }
        lock.unlock()
    }

    func all() -> [ClickRecord] {
        lock.lock()
        defer { lock.unlock() }
        return items
    }

    /// The newest records, oldest first.
    func tail(_ limit: Int) -> [ClickRecord] {
        lock.lock()
        defer { lock.unlock() }
        return Array(items.suffix(limit))
    }

    func stats() -> SessionStats {
        let copy = all()
        var s = SessionStats()
        s.clicks = copy.count
        guard let last = copy.last else { return s }

        s.elapsed = last.sinceStart
        s.clicksPerMinute = s.elapsed > 0 ? Double(copy.count - 1) / s.elapsed * 60 : 0

        var holdSum = 0.0
        var waits: [Double] = []
        waits.reserveCapacity(copy.count)
        for (i, r) in copy.enumerated() {
            holdSum += Double(r.holdMs)
            s.furthestOffset = max(s.furthestOffset, r.offset)
            if r.pause { s.pauses += 1 }
            if i > 0 { waits.append(r.wait) }
        }
        s.averageHoldMs = holdSum / Double(copy.count)
        s.waits = waits.count
        if !waits.isEmpty {
            s.averageWait = waits.reduce(0, +) / Double(waits.count)
            waits.sort()
            s.shortestWait = waits[0]
            s.longestWait = waits[waits.count - 1]
            s.medianWait = waits.count % 2 == 1
                ? waits[waits.count / 2]
                : (waits[waits.count / 2 - 1] + waits[waits.count / 2]) / 2
        }
        return s
    }

    func csv() -> String {
        let stamp = DateFormatter()
        stamp.locale = Locale(identifier: "en_US_POSIX")
        stamp.dateFormat = "yyyy-MM-dd HH:mm:ss.SSS"
        let buttons = ["left", "right", "middle"]

        var out = "click,time,seconds_since_start,wait_before_s,long_pause,hold_ms,x,y,offset_px,button,double_click\n"
        for r in all() {
            let fields: [String] = [
                String(r.index),
                stamp.string(from: r.time),
                String(format: "%.3f", r.sinceStart),
                String(format: "%.3f", r.wait),
                r.pause ? "yes" : "no",
                String(r.holdMs),
                String(r.x),
                String(r.y),
                String(format: "%.1f", r.offset),
                buttons[min(max(r.button, 0), 2)],
                r.doubleClick ? "yes" : "no"
            ]
            out += fields.joined(separator: ",") + "\n"
        }
        return out
    }

    func saveCsv(to url: URL) throws {
        try csv().write(to: url, atomically: true, encoding: .utf8)
    }

    /// Saves the finished run to the log folder and trims old files. Never throws.
    func autoSave() {
        if !ClickLog.autoSaveEnabled || count == 0 { return }
        lock.lock()
        let when = started
        lock.unlock()

        let name = DateFormatter()
        name.locale = Locale(identifier: "en_US_POSIX")
        name.dateFormat = "yyyy-MM-dd_HH-mm-ss"
        let folder = ClickLog.folder
        let url = folder.appendingPathComponent("clicks-\(name.string(from: when)).csv")
        do {
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            try saveCsv(to: url)
            lock.lock()
            saved = url.path
            lock.unlock()

            let files = try FileManager.default.contentsOfDirectory(atPath: folder.path)
                .filter { $0.hasPrefix("clicks-") && $0.hasSuffix(".csv") }
                .sorted()   // the names sort by date
            if files.count > ClickLog.keepFiles {
                for old in files.prefix(files.count - ClickLog.keepFiles) {
                    try? FileManager.default.removeItem(at: folder.appendingPathComponent(old))
                }
            }
        } catch {
            // A run that cannot be saved is still shown in the details window.
        }
    }
}
