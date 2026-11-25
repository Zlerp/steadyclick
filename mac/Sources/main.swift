import AppKit

// SteadyClick --selftest : checks the timing model, the run record and the click loop, then exits.
if CommandLine.arguments.contains("--selftest") {
    ClickLog.autoSaveEnabled = false
    exit(SelfTest.run() ? 0 : 1)
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.regular)
app.run()
