import CoreGraphics
import Foundation

/// The same checks the Windows build runs, minus the ones that need a visible window.
enum SelfTest {
    private static var allPassed = true

    private static func check(_ name: String, _ ok: Bool, _ detail: String) {
        print((ok ? "PASS  " : "FAIL  ") + name + "   [" + detail + "]")
        if !ok { allPassed = false }
    }

    private static func f(_ v: Double) -> String {
        return String(format: "%.3f", v)
    }

    static func run() -> Bool {
        rhythmChecks()
        previewChecks()
        engineChecks()
        print(allPassed ? "RESULT: PASS" : "RESULT: FAIL")
        return allPassed
    }

    /// Statistical checks on the timing model itself. Pure arithmetic, no mouse involved.
    private static func rhythmChecks() {
        let n = 50000

        // --- human-like, 1 to 2 s, pauses on
        var h = ClickOptions()
        h.minInterval = 1; h.maxInterval = 2; h.humanize = true; h.humanPauses = true
        h.jitterRadius = 20; h.holdMinMs = 60; h.holdMaxMs = 130
        let rhythm = Rhythm(h, seed: 7)
        var waits: [Double] = []
        var pauses = 0, outOfRange = 0, shortPauses = 0
        var longestPause = 0.0
        for _ in 0..<n {
            let next = rhythm.nextInterval()
            if next.pause {
                pauses += 1
                if next.wait <= 2.0 { shortPauses += 1 }
                longestPause = max(longestPause, next.wait)
            } else {
                if next.wait < 1.0 || next.wait > 2.0 { outOfRange += 1 }
                waits.append(next.wait)
            }
        }
        let mean = waits.reduce(0, +) / Double(waits.count)
        var variance = 0.0, covariance = 0.0
        for i in 0..<waits.count {
            variance += (waits[i] - mean) * (waits[i] - mean)
            if i > 0 { covariance += (waits[i] - mean) * (waits[i - 1] - mean) }
        }
        let sd = (variance / Double(waits.count)).squareRoot()
        let lag1 = covariance / variance
        let sorted = waits.sorted()
        let median = sorted[sorted.count / 2]
        let p10 = sorted[sorted.count / 10], p90 = sorted[sorted.count * 9 / 10]
        let distinct = Set(waits).count

        check("R  human: ordinary waits stay inside 1-2 s", outOfRange == 0, "\(outOfRange) outside")
        check("R  human: real spread", sd >= 0.15 && p90 - p10 >= 0.4,
              "sd \(f(sd)) s, 10th-90th pct \(f(p10)) to \(f(p90)) s")
        check("R  human: right-skewed (slow tail)", mean > median + 0.01 && median < 1.45,
              "median \(f(median)) s, mean \(f(mean)) s")
        check("R  human: pace drifts (neighbouring waits related)", lag1 > 0.15 && lag1 < 0.6,
              "lag-1 correlation \(f(lag1))")
        check("R  human: no repeated values", Double(distinct) > Double(waits.count) * 0.999,
              "\(distinct) distinct of \(waits.count)")
        let pauseShare = Double(pauses) / Double(n)
        check("R  human: occasional pauses, all past the maximum",
              pauseShare > 0.02 && pauseShare < 0.05 && shortPauses == 0,
              String(format: "%.1f%% of waits, longest %.3f s", pauseShare * 100, longestPause))

        // --- human-like aim and hold
        var sumR = 0.0, maxR = 0.0, holdSum = 0.0
        var holdLow = Int.max, holdHigh = 0
        for _ in 0..<n {
            let off = rhythm.nextOffset()
            let r = Double(off.dx * off.dx + off.dy * off.dy).squareRoot()
            sumR += r
            maxR = max(maxR, r)
            let hold = rhythm.nextHoldMs()
            holdLow = min(holdLow, hold)
            holdHigh = max(holdHigh, hold)
            holdSum += Double(hold)
        }
        check("R  human: aim clusters near the spot, never past the radius",
              maxR <= 20.8 && sumR / Double(n) < 12, "mean \(f(sumR / Double(n))) pt, furthest \(f(maxR)) pt of 20")
        check("R  human: hold times stay in 60-130 ms, skewed short",
              holdLow >= 60 && holdHigh <= 130 && holdSum / Double(n) < 95,
              "\(holdLow) to \(holdHigh) ms, mean \(f(holdSum / Double(n)))")

        // --- human-like with min = max still varies
        var same = ClickOptions()
        same.minInterval = 1; same.maxInterval = 1; same.humanize = true; same.humanPauses = false
        let steady = Rhythm(same, seed: 11)
        var low = Double.greatestFiniteMagnitude, high = 0.0
        for _ in 0..<n {
            let t = steady.nextInterval().wait
            low = min(low, t)
            high = max(high, t)
        }
        check("R  human: equal min and max still varies (0.90-1.25 s)",
              low >= 0.9 && high <= 1.25 && high - low > 0.25, "\(f(low)) to \(f(high)) s")

        // --- even spread
        var even = ClickOptions()
        even.minInterval = 1; even.maxInterval = 2; even.humanize = false
        let flat = Rhythm(even, seed: 13)
        var evenMean = 0.0
        var evenOut = 0
        for _ in 0..<n {
            let next = flat.nextInterval()
            if next.wait < 1 || next.wait > 2 || next.pause { evenOut += 1 }
            evenMean += next.wait / Double(n)
        }
        check("R  even: uniform across 1-2 s, no pauses", evenOut == 0 && abs(evenMean - 1.5) < 0.01,
              "mean \(f(evenMean)) s, \(evenOut) outside")
    }

    private static func previewChecks() {
        let data = PreviewData.make(ClickOptions().sanitized())
        let total = data.share.reduce(0, +) + data.pauseShare
        check("P  preview chart adds up to every wait", abs(total - 1) < 0.001 && !data.caption.isEmpty,
              "total \(f(total)), caption: \(data.caption)")
    }

    /// Runs the real click loop. Without Accessibility permission macOS drops the clicks, but the
    /// loop, its timing and its record are exercised either way.
    private static func engineChecks() {
        let displays = MouseIO.displays()
        check("E  found a display", !displays.isEmpty, "\(displays.count) display(s)")
        print("INFO  Accessibility permission: \(MouseIO.trusted ? "granted" : "not granted (clicks are dropped by macOS)")")
        guard let main = displays.first else { return }

        var o = ClickOptions()
        o.x = Double(main.midX.rounded()); o.y = Double(main.midY.rounded())
        o.jitterRadius = 10
        // Waits well above the time one click takes (settle + hold), so the recorded wait is the
        // planned wait and not just how long the click itself took.
        o.minInterval = 0.30; o.maxInterval = 0.50
        o.holdMinMs = 20; o.holdMaxMs = 40
        o.humanize = true; o.humanPauses = false
        o.maxClicks = 8
        o.cornerFailsafe = false

        let engine = ClickEngine()
        engine.start(o.sanitized())
        let deadline = Date().addingTimeInterval(10)
        while engine.isRunning && Date() < deadline {
            Thread.sleep(forTimeInterval: 0.02)
        }
        let snap = engine.snapshot()
        let stats = engine.log.stats()
        check("E  the loop made 8 clicks and stopped by itself",
              !snap.running && snap.count == 8 && snap.stopReason.hasPrefix("Finished"),
              "\(snap.count) clicks, reason: \(snap.stopReason)")
        check("L  record has one row per click", engine.log.count == 8 && stats.waits == 7,
              "\(engine.log.count) rows, \(stats.waits) waits")
        let recorded = engine.log.all().dropFirst().map { f($0.wait) }.joined(separator: " ")
        print("INFO  recorded waits: \(recorded)")
        // Never early, and on time within 60 ms: a late click means the timers are being throttled.
        check("L  recorded waits sit in the 0.30-0.50 s range",
              stats.shortestWait >= 0.295 && stats.longestWait <= 0.56,
              "\(f(stats.shortestWait)) to \(f(stats.longestWait)) s, median \(f(stats.medianWait)) s")
        check("L  rows carry hold and offset", stats.furthestOffset <= 10.5 && stats.averageHoldMs >= 20 && stats.averageHoldMs <= 40,
              "furthest \(f(stats.furthestOffset)) pt, average hold \(f(stats.averageHoldMs)) ms")
        let lines = engine.log.csv().split(separator: "\n")
        check("L  CSV export has a header and 8 rows",
              lines.count == 9 && lines[0].hasPrefix("click,time,") && lines[8].hasPrefix("8,"),
              "\(lines.count) lines")

        // A long wait must be interruptible.
        var slow = o
        slow.minInterval = 30; slow.maxInterval = 30; slow.humanize = false; slow.maxClicks = 0
        engine.start(slow.sanitized())
        Thread.sleep(forTimeInterval: 0.4)
        let before = Date()
        engine.stop()
        let latency = Date().timeIntervalSince(before) * 1000
        check("E  Stop interrupts a 30 s wait", !engine.isRunning && latency < 200,
              String(format: "%.1f ms", latency) + ", reason: \(engine.snapshot().stopReason)")
    }
}
