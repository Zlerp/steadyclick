import AppKit
import SwiftUI

/// A titled group of settings.
struct Card<Content: View>: View {
    let title: String
    let content: Content

    init(_ title: String, @ViewBuilder content: () -> Content) {
        self.title = title
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(title).font(.headline)
            content
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color(nsColor: .controlBackgroundColor)))
        .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color(nsColor: .separatorColor), lineWidth: 1))
    }
}

/// A number you can type into or nudge with a stepper.
struct NumberField: View {
    let value: Binding<Double>
    let range: ClosedRange<Double>
    let step: Double
    var decimals: Int = 0
    var width: CGFloat = 72

    var body: some View {
        HStack(spacing: 2) {
            TextField("", value: value, format: FloatingPointFormatStyle<Double>()
                .grouping(.never)
                .precision(.fractionLength(0...decimals)))
                .textFieldStyle(.roundedBorder)
                .multilineTextAlignment(.trailing)
                .frame(width: width)
            Stepper("", value: value, in: range, step: step)
                .labelsHidden()
        }
    }
}

/// A label on the left with controls after it, so rows line up down a card.
struct FormRow<Content: View>: View {
    let label: String
    let content: Content

    init(_ label: String, @ViewBuilder content: () -> Content) {
        self.label = label
        self.content = content()
    }

    var body: some View {
        HStack(spacing: 6) {
            Text(label).frame(width: 104, alignment: .leading)
            content
            Spacer(minLength: 0)
        }
    }
}

/// Histogram of the waits the current settings will produce.
struct RhythmChart: View {
    let data: PreviewData

    private static let barColor = Color(red: 0.20, green: 0.66, blue: 0.44)
    private static let pauseColor = Color(red: 0.85, green: 0.47, blue: 0.02)

    var body: some View {
        VStack(spacing: 3) {
            GeometryReader { geo in
                HStack(alignment: .bottom, spacing: 8) {
                    HStack(alignment: .bottom, spacing: 1) {
                        ForEach(0..<data.share.count, id: \.self) { i in
                            Rectangle()
                                .fill(RhythmChart.barColor)
                                .frame(height: barHeight(data.share[i], in: geo.size.height))
                        }
                    }
                    if data.pauseShare > 0 {
                        Rectangle()
                            .fill(RhythmChart.pauseColor)
                            .frame(width: geo.size.width / 8, height: barHeight(data.pauseShare, in: geo.size.height))
                    }
                }
                .frame(width: geo.size.width, height: geo.size.height, alignment: .bottom)
            }
            .frame(height: 50)

            HStack {
                Text(PreviewData.secs(data.lo) + " s")
                Spacer()
                Text(PreviewData.secs(data.hi) + " s")
                if data.pauseShare > 0 {
                    Text("pauses").frame(width: 52, alignment: .trailing)
                }
            }
            .font(.system(size: 10))
            .foregroundColor(.secondary)
        }
    }

    private func barHeight(_ share: Double, in available: CGFloat) -> CGFloat {
        if share <= 0 { return 0 }
        return max(1, CGFloat(share / data.maxShare) * available)
    }
}

struct ContentView: View {
    @EnvironmentObject var m: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if !m.trusted {
                permissionBanner
            }
            HStack(alignment: .top, spacing: 12) {
                VStack(spacing: 12) {
                    whereCard
                    clickCard
                    keysCard
                }
                .frame(width: 366)
                VStack(spacing: 12) {
                    timingCard
                    stopCard
                }
                .frame(width: 366)
            }
            .disabled(m.running)

            Text(m.summary)
                .font(.system(size: 13))
                .fixedSize(horizontal: false, vertical: true)

            startButton

            HStack {
                Text(m.statusLine)
                    .font(.system(size: 12))
                    .foregroundColor(.secondary)
                    .lineLimit(1)
                Spacer()
                Button("Session details...") { m.showDetails() }
                    .help("Every click of this run: when it happened, the wait before it, how long it was held and where it landed, plus totals.")
            }
        }
        .padding(14)
        .frame(width: 772)
        .fixedSize(horizontal: false, vertical: true)
    }

    private var permissionBanner: some View {
        HStack(spacing: 10) {
            Text("SteadyClick needs Accessibility permission before macOS will let it click for you.")
                .fixedSize(horizontal: false, vertical: true)
            Spacer()
            Button("Open Settings") {
                MouseIO.promptForTrust()
                MouseIO.openAccessibilitySettings()
            }
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color.orange.opacity(0.18)))
    }

    private var startButton: some View {
        Button(action: { m.toggle() }) {
            Text((m.running ? "Stop" : "Start") + "   (\(m.o.toggleKey))")
                .font(.system(size: 15, weight: .bold))
                .foregroundColor(.white)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 11)
                .background(RoundedRectangle(cornerRadius: 8).fill(m.running
                    ? Color(red: 0.78, green: 0.22, blue: 0.18)
                    : Color(red: 0.09, green: 0.50, blue: 0.31)))
        }
        .buttonStyle(.plain)
    }

    private var whereCard: some View {
        Card("Where to click") {
            Picker("", selection: $m.o.fixedPoint) {
                Text("A fixed spot").tag(true)
                Text("Wherever the cursor already is").tag(false)
            }
            .pickerStyle(.radioGroup)
            .labelsHidden()

            Group {
                HStack(spacing: 6) {
                    Text("X")
                    NumberField(value: $m.o.x, range: -100000...100000, step: 1)
                    Text("Y").padding(.leading, 8)
                    NumberField(value: $m.o.y, range: -100000...100000, step: 1)
                }
                HStack(spacing: 8) {
                    Button("Pick on screen") { m.pickOnScreen() }
                        .help("Dims the screen so you can point at the exact spot you want.")
                    Button("Show me") { m.flashSpot() }
                        .help("Flashes a ring on the current spot. The ring's size is the random offset area.")
                }
                Text("Or point at any spot and press \(m.o.pickKey). Works from any app.")
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
                HStack(spacing: 6) {
                    Text("Random offset, up to")
                    NumberField(value: $m.o.jitterRadius, range: 0...500, step: 1, width: 56)
                        .help("Each click lands on a random point within this distance of the spot. 0 = the exact same point every time.")
                    Text("pt")
                }
            }
            .disabled(!m.o.fixedPoint)
        }
    }

    private var clickCard: some View {
        Card("Click") {
            HStack(spacing: 6) {
                Text("Button")
                Picker("", selection: $m.o.button) {
                    Text("Left").tag(0)
                    Text("Right").tag(1)
                    Text("Middle").tag(2)
                }
                .labelsHidden()
                .frame(width: 100)
                Text("Type").padding(.leading, 10)
                Picker("", selection: $m.o.doubleClick) {
                    Text("Single click").tag(false)
                    Text("Double click").tag(true)
                }
                .labelsHidden()
                .frame(width: 130)
            }
            Toggle("Put the cursor back after each click", isOn: $m.o.restoreCursor)
                .disabled(!m.o.fixedPoint)
                .help("After each click the cursor jumps back to where you had it, so you can keep using the mouse between clicks.")
        }
    }

    private var keysCard: some View {
        Card("Hotkeys and window") {
            HStack(spacing: 6) {
                Text("Start / stop")
                hotkeyPicker($m.o.toggleKey)
                Text("Pick spot").padding(.leading, 10)
                hotkeyPicker($m.o.pickKey)
            }
            Toggle("Keep this window on top", isOn: $m.o.alwaysOnTop)
        }
    }

    private func hotkeyPicker(_ selection: Binding<String>) -> some View {
        Picker("", selection: selection) {
            ForEach(HotkeyChoice.all, id: \.label) { choice in
                Text(choice.label).tag(choice.label)
            }
        }
        .labelsHidden()
        .frame(width: 84)
        .help("Works from any app, even when this window is hidden.")
    }

    private var timingCard: some View {
        Card("Timing") {
            FormRow("Click every") {
                NumberField(value: $m.o.minInterval, range: 0.01...86400, step: 0.1, decimals: 2)
                Text("to")
                NumberField(value: $m.o.maxInterval, range: 0.01...86400, step: 0.1, decimals: 2)
                Text("seconds")
            }
            FormRow("Hold each click") {
                NumberField(value: $m.o.holdMinMs, range: 1...10000, step: 5)
                Text("to")
                NumberField(value: $m.o.holdMaxMs, range: 1...10000, step: 5)
                Text("ms")
            }
            FormRow("Start delay") {
                NumberField(value: $m.o.startDelay, range: 0...3600, step: 0.5, decimals: 1)
                Text("seconds")
            }
            FormRow("Variation") {
                Picker("", selection: $m.o.humanize) {
                    Text("Human-like rhythm").tag(true)
                    Text("Even spread").tag(false)
                }
                .labelsHidden()
                .frame(width: 180)
                .help("Human-like rhythm: waits bunch around a typical pace with a tail of slower ones, and the pace drifts faster and slower over several clicks. Hold times and the random offset vary the same way. Even spread: every value in the range is equally likely.")
            }
            Toggle("Take a longer pause now and then", isOn: $m.o.humanPauses)
                .disabled(!m.o.humanize)
                .padding(.leading, 110)
                .help("About 1 wait in 30 runs well past your maximum, like someone glancing away for a moment.")

            RhythmChart(data: m.preview)
            Text(m.preview.caption)
                .font(.system(size: 11))
                .foregroundColor(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private var stopCard: some View {
        Card("Stop automatically") {
            HStack(spacing: 6) {
                Text("After")
                NumberField(value: $m.o.maxClicks, range: 0...1_000_000_000, step: 10, width: 90)
                Text("clicks  (0 = no limit)")
            }
            HStack(spacing: 6) {
                Text("After")
                NumberField(value: $m.o.maxMinutes, range: 0...100000, step: 1, decimals: 1, width: 90)
                Text("minutes  (0 = no limit)")
            }
            Toggle("Emergency stop: cursor in a screen's top-left corner", isOn: $m.o.cornerFailsafe)
                .help("Shove the cursor into the top-left corner of a screen to stop instantly, even if the hotkey is not getting through.")
        }
    }
}
