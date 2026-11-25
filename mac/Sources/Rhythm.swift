import Foundation

/// Everything one run needs. Numbers are stored as Double so every field can share one editor.
struct ClickOptions: Codable, Equatable {
    var fixedPoint = true          // false = click wherever the cursor happens to be
    var x = 800.0                  // screen position, in points from the top-left of the main display
    var y = 450.0
    var jitterRadius = 0.0         // each click lands within this many points of the spot
    var minInterval = 1.0          // seconds between clicks
    var maxInterval = 2.0
    var humanize = true            // human-like rhythm instead of an even spread
    var humanPauses = true         // human-like only: an occasional wait well past the maximum
    var holdMinMs = 60.0           // how long the button stays down
    var holdMaxMs = 130.0
    var startDelay = 0.0           // seconds before the first click
    var button = 0                 // 0 left, 1 right, 2 middle
    var doubleClick = false
    var restoreCursor = false      // put the cursor back where it was after each click
    var maxClicks = 0.0            // 0 = no limit
    var maxMinutes = 0.0           // 0 = no limit
    var cornerFailsafe = true      // cursor in a screen's top-left corner stops the run
    var toggleKey = "⌃⌥S"
    var pickKey = "⌃⌥P"
    var alwaysOnTop = false

    /// A copy with every number forced into its legal range and min/max pairs put in order.
    func sanitized() -> ClickOptions {
        func clamp(_ v: Double, _ lo: Double, _ hi: Double) -> Double {
            return v.isFinite ? min(max(v, lo), hi) : lo
        }
        var o = self
        o.x = clamp(x, -100000, 100000).rounded()
        o.y = clamp(y, -100000, 100000).rounded()
        o.jitterRadius = clamp(jitterRadius, 0, 500).rounded()
        let a = clamp(minInterval, 0.01, 86400), b = clamp(maxInterval, 0.01, 86400)
        o.minInterval = min(a, b)
        o.maxInterval = max(a, b)
        let h1 = clamp(holdMinMs, 1, 10000).rounded(), h2 = clamp(holdMaxMs, 1, 10000).rounded()
        o.holdMinMs = min(h1, h2)
        o.holdMaxMs = max(h1, h2)
        o.startDelay = clamp(startDelay, 0, 3600)
        o.button = min(max(button, 0), 2)
        o.maxClicks = clamp(maxClicks, 0, 1_000_000_000).rounded()
        o.maxMinutes = clamp(maxMinutes, 0, 100000)
        return o
    }
}

/// Small seedable generator, so the preview chart is identical every time for the same settings.
struct SeededRNG: RandomNumberGenerator {
    private var state: UInt64

    init(seed: UInt64) {
        state = seed
    }

    mutating func next() -> UInt64 {
        state &+= 0x9E3779B97F4A7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58476D1CE4E5B9
        z = (z ^ (z >> 27)) &* 0x94D049BB133111EB
        return z ^ (z >> 31)
    }
}

/// Decides how long to wait, how long to hold, and how far off-centre to land for each click.
///
/// "Even" mode draws every value uniformly from its range.
///
/// "Human-like" mode copies what repeated clicking by a person looks like:
///   * Waits are right-skewed (log-normal shaped): bunched around a typical pace near the quick
///     end of the range, with a thinner tail of slower ones.
///   * Consecutive waits are related. A slow drift rides under the click-to-click noise, so the
///     pace speeds up and eases off over several clicks instead of jumping around independently.
///   * Now and then attention lapses and one wait runs well past the usual maximum.
///   * Hold times follow the same skewed, drifting shape.
///   * Aim scatters like a bell curve around the spot and the centre of that scatter wanders.
///
/// This is a line-for-line port of Rhythm.cs in the Windows version; keep the two in step.
final class Rhythm {
    static let cut = 2.4              // the bell curve is trimmed at +/- this many deviations
    static let skew = 0.35            // how lopsided the wait distribution is
    static let driftWeight = 0.6      // share of the variation that comes from the slow drift
    static let noiseWeight = 0.8      // share from click-to-click noise (0.6^2 + 0.8^2 = 1)
    static let pauseChance = 0.035

    private let o: ClickOptions
    private var rng: SeededRNG
    private let lo: Double
    private let hi: Double
    private var intervalDrift = 0.0
    private var holdDrift = 0.0
    private var aimDriftX = 0.0
    private var aimDriftY = 0.0
    private var lastWasPause = false
    private var spare: Double?

    init(_ options: ClickOptions, seed: UInt64) {
        o = options
        rng = SeededRNG(seed: seed)
        let range = Rhythm.effectiveRange(options)
        lo = range.lo
        hi = range.hi
        intervalDrift = gauss()
        holdDrift = gauss()
        aimDriftX = gauss()
        aimDriftY = gauss()
    }

    /// The wait range actually used. Nobody clicks to a metronome, so in human-like mode a range
    /// narrower than 15% is opened up around its middle.
    static func effectiveRange(_ o: ClickOptions) -> (lo: Double, hi: Double) {
        var lo = min(o.minInterval, o.maxInterval)
        var hi = max(o.minInterval, o.maxInterval)
        if o.humanize && hi < lo * 1.15 {
            let middle = (lo + hi) / 2
            lo = middle * 0.90
            hi = middle * 1.25
        }
        return (lo, hi)
    }

    /// Seconds until the next click. `pause` is true for a long lapse.
    func nextInterval() -> (wait: Double, pause: Bool) {
        if !o.humanize {
            return (lo + unit() * (hi - lo), false)
        }
        var wait = lo + (hi - lo) * Rhythm.shape(correlated(&intervalDrift, rho: 0.92))
        var pause = false
        if o.humanPauses && !lastWasPause && unit() < Rhythm.pauseChance {
            let typical = lo + (hi - lo) * Rhythm.shape(0)
            let r = unit()
            // Mostly short lapses, occasionally a long one; never less than a noticeable beat.
            wait = hi + max(0.25 + 0.5 * r, typical * (0.5 + 3.5 * r * r))
            pause = true
        }
        lastWasPause = pause
        return (wait, pause)
    }

    func nextHoldMs() -> Int {
        let low = min(o.holdMinMs, o.holdMaxMs), high = max(o.holdMinMs, o.holdMaxMs)
        if !o.humanize {
            return Int((low + unit() * (high - low)).rounded())
        }
        return Int((low + (high - low) * Rhythm.shape(correlated(&holdDrift, rho: 0.85))).rounded())
    }

    /// How far from the exact spot this click lands. Never further than the radius.
    func nextOffset() -> (dx: Int, dy: Int) {
        let radius = o.jitterRadius
        if radius <= 0 { return (0, 0) }

        if !o.humanize {
            // sqrt keeps the points evenly spread over the disc instead of bunched in the middle
            let r = radius * unit().squareRoot()
            let a = unit() * Double.pi * 2
            return (Int((r * cos(a)).rounded()), Int((r * sin(a)).rounded()))
        }

        let rho = 0.9
        let step = (1 - rho * rho).squareRoot()
        aimDriftX = rho * aimDriftX + step * gauss()
        aimDriftY = rho * aimDriftY + step * gauss()
        for _ in 0..<12 {
            let x = radius * (0.22 * aimDriftX + 0.33 * gauss())
            let y = radius * (0.22 * aimDriftY + 0.33 * gauss())
            if x * x + y * y <= radius * radius {
                return (Int(x.rounded()), Int(y.rounded()))
            }
        }
        return (0, 0)
    }

    /// A standard-normal value made of slow drift plus fresh noise, trimmed to +/- cut.
    private func correlated(_ drift: inout Double, rho: Double) -> Double {
        drift = rho * drift + (1 - rho * rho).squareRoot() * gauss()
        for _ in 0..<12 {
            let z = Rhythm.driftWeight * drift + Rhythm.noiseWeight * gauss()
            if abs(z) <= Rhythm.cut { return z }
        }
        return max(-Rhythm.cut, min(Rhythm.cut, Rhythm.driftWeight * drift))
    }

    /// Maps a trimmed normal value onto 0...1 with a log-normal (right-skewed) shape.
    static func shape(_ z: Double) -> Double {
        let low = exp(-skew * cut), high = exp(skew * cut)
        return (exp(skew * z) - low) / (high - low)
    }

    private func unit() -> Double {
        return Double.random(in: 0..<1, using: &rng)
    }

    private func gauss() -> Double {
        if let s = spare {
            spare = nil
            return s
        }
        var u = 0.0, v = 0.0, s = 0.0
        repeat {
            u = unit() * 2 - 1
            v = unit() * 2 - 1
            s = u * u + v * v
        } while s >= 1 || s == 0
        let m = (-2 * log(s) / s).squareRoot()
        spare = v * m
        return u * m
    }
}

/// The numbers behind the small histogram of waits shown under the timing settings.
struct PreviewData: Equatable {
    static let bins = 30
    static let samples = 20000

    var share = [Double](repeating: 0, count: PreviewData.bins)
    var pauseShare = 0.0
    var lo = 1.0
    var hi = 2.0
    var caption = ""

    var maxShare: Double {
        return max(share.max() ?? 0, pauseShare, 1e-9)
    }

    static func secs(_ v: Double) -> String {
        return String(format: v < 10 ? "%.2f" : "%.1f", v)
    }

    static func make(_ o: ClickOptions) -> PreviewData {
        var d = PreviewData()
        let range = Rhythm.effectiveRange(o)
        d.lo = range.lo
        d.hi = range.hi

        if !o.humanize {
            d.share = [Double](repeating: 1.0 / Double(bins), count: bins)
            d.caption = d.hi - d.lo < 1e-9
                ? "Every wait is exactly \(secs(d.lo)) s. No variation."
                : "Every wait from \(secs(d.lo)) to \(secs(d.hi)) s is equally likely."
            return d
        }

        // Fixed seed: the picture only changes when the settings do.
        let rhythm = Rhythm(o, seed: 20240613)
        var normal: [Double] = []
        normal.reserveCapacity(samples)
        var pauses = 0
        var longest = 0.0
        for _ in 0..<samples {
            let next = rhythm.nextInterval()
            if next.pause {
                pauses += 1
                longest = max(longest, next.wait)
                continue
            }
            normal.append(next.wait)
            let bin = Int((next.wait - d.lo) / (d.hi - d.lo) * Double(bins))
            d.share[min(max(bin, 0), bins - 1)] += 1.0 / Double(samples)
        }
        d.pauseShare = Double(pauses) / Double(samples)
        normal.sort()
        d.caption = "Usually about \(secs(normal[normal.count / 2])) s, mostly " +
            "\(secs(normal[normal.count / 10])) to \(secs(normal[normal.count * 9 / 10])) s."
        if pauses > 0 {
            let every = Int((Double(samples) / Double(pauses)).rounded())
            d.caption += " About 1 wait in \(every) is a longer pause, up to \(String(format: "%.1f", longest)) s."
        }
        return d
    }
}
