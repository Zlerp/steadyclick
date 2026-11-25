import AppKit
import SwiftUI

/// Live totals and the click-by-click record of the current (or most recent) run.
struct DetailsView: View {
    @EnvironmentObject var m: AppModel

    private static let clock: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "HH:mm:ss.SSS"
        return f
    }()

    private static func seconds(_ v: Double) -> String {
        return String(format: v < 100 ? "%.3f s" : "%.1f s", v)
    }

    private static func duration(_ seconds: Double) -> String {
        let total = Int(max(0, seconds))
        let h = total / 3600, mins = (total % 3600) / 60, s = total % 60
        return h > 0 ? String(format: "%d:%02d:%02d", h, mins, s) : String(format: "%d:%02d", mins, s)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            totals
            header
            ScrollView {
                LazyVStack(spacing: 0) {
                    ForEach(m.recent) { record in
                        row(record)
                    }
                }
            }
            .background(Color(nsColor: .controlBackgroundColor))
            .overlay(Rectangle().stroke(Color(nsColor: .separatorColor), lineWidth: 1))

            HStack {
                Text(footer)
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
                    .lineLimit(1)
                    .truncationMode(.middle)
                Spacer()
                Button("Open log folder") { m.openLogFolder() }
                Button("Save as CSV...") { m.saveCsv() }
                    .disabled(m.stats.clicks == 0)
            }
        }
        .padding(14)
        .frame(minWidth: 640, minHeight: 420)
    }

    private var footer: String {
        if !m.detailsNote.isEmpty { return m.detailsNote }
        if m.running { return "Recording. The run is saved automatically when it stops." }
        if !m.savedPath.isEmpty { return "Saved automatically to \(m.savedPath)" }
        if m.stats.clicks == 0 { return "Nothing recorded yet. Press Start in the main window." }
        return m.stats.clicks > 500 ? "Showing the newest 500 clicks. The CSV has all of them." : ""
    }

    private var totals: some View {
        let s = m.stats
        let dash = "-"
        return HStack(alignment: .top, spacing: 30) {
            VStack(alignment: .leading, spacing: 6) {
                stat("Clicks", String(s.clicks))
                stat("Run time", s.clicks > 0 ? DetailsView.duration(s.elapsed) : dash)
                stat("Clicks per minute", s.waits > 0 ? String(format: "%.1f", s.clicksPerMinute) : dash)
                stat("Average hold", s.clicks > 0 ? String(format: "%.0f ms", s.averageHoldMs) : dash)
                stat("Furthest from spot", s.clicks > 0 ? String(format: "%.1f pt", s.furthestOffset) : dash)
            }
            VStack(alignment: .leading, spacing: 6) {
                stat("Average wait", s.waits > 0 ? DetailsView.seconds(s.averageWait) : dash)
                stat("Typical (median) wait", s.waits > 0 ? DetailsView.seconds(s.medianWait) : dash)
                stat("Shortest wait", s.waits > 0 ? DetailsView.seconds(s.shortestWait) : dash)
                stat("Longest wait", s.waits > 0 ? DetailsView.seconds(s.longestWait) : dash)
                stat("Longer pauses", s.clicks > 0 ? String(s.pauses) : dash)
            }
            Spacer(minLength: 0)
        }
        .padding(12)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color(nsColor: .controlBackgroundColor)))
    }

    private func stat(_ name: String, _ value: String) -> some View {
        HStack(spacing: 8) {
            Text(name)
                .foregroundColor(.secondary)
                .frame(width: 150, alignment: .leading)
            Text(value).bold()
        }
    }

    private var header: some View {
        HStack(spacing: 0) {
            cell("#", 56, .trailing)
            cell("Time", 104, .leading)
            cell("Wait before", 92, .trailing)
            cell("Held", 64, .trailing)
            cell("X", 60, .trailing)
            cell("Y", 60, .trailing)
            cell("Off spot", 70, .trailing)
            cell("Note", 90, .leading)
            Spacer(minLength: 0)
        }
        .font(.system(size: 11, weight: .semibold))
        .foregroundColor(.secondary)
    }

    private func row(_ r: ClickRecord) -> some View {
        var note = r.pause ? "long pause" : ""
        if r.doubleClick { note = note.isEmpty ? "double" : note + ", double" }
        return HStack(spacing: 0) {
            cell(String(r.index), 56, .trailing)
            cell(DetailsView.clock.string(from: r.time), 104, .leading)
            cell(r.index == 1 ? "-" : String(format: "%.3f s", r.wait), 92, .trailing)
            cell("\(r.holdMs) ms", 64, .trailing)
            cell(String(r.x), 60, .trailing)
            cell(String(r.y), 60, .trailing)
            cell(String(format: "%.1f pt", r.offset), 70, .trailing)
            cell(note, 90, .leading)
            Spacer(minLength: 0)
        }
        .font(.system(size: 12).monospacedDigit())
        .foregroundColor(r.pause ? Color(red: 0.71, green: 0.33, blue: 0.04) : Color.primary)
        .padding(.vertical, 3)
    }

    private func cell(_ text: String, _ width: CGFloat, _ alignment: Alignment) -> some View {
        Text(text)
            .lineLimit(1)
            .frame(width: width, alignment: alignment)
            .padding(.horizontal, 5)
    }
}
