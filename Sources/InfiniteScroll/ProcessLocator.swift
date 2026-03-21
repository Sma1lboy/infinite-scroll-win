import Foundation

enum ProcessLocator {
    static func shellEnvironment() -> [String: String] {
        var env = ProcessInfo.processInfo.environment
        let extraPaths = [
            "/usr/local/bin",
            "/opt/homebrew/bin",
            "\(NSHomeDirectory())/.local/bin",
            "\(NSHomeDirectory())/.npm-global/bin",
            "\(NSHomeDirectory())/.nvm/versions/node",
        ]
        let currentPath = env["PATH"] ?? "/usr/bin:/bin"
        env["PATH"] = (extraPaths + [currentPath]).joined(separator: ":")
        env["TERM"] = "xterm-256color"
        // Advertise as iTerm.app so apps like Claude Code activate the kitty keyboard
        // protocol (CSI-u). SwiftTerm + our ShiftEnterMonitor handle the actual sequences.
        env["TERM_PROGRAM"] = "iTerm.app"
        env["LANG"] = env["LANG"] ?? "en_US.UTF-8"
        // Ensure terminfo database is found (needed for bundled tmux)
        if env["TERMINFO_DIRS"] == nil {
            let terminfoDirs = [
                "/usr/share/terminfo",
                "/opt/homebrew/share/terminfo",
                "\(NSHomeDirectory())/.terminfo",
            ]
            env["TERMINFO_DIRS"] = terminfoDirs.joined(separator: ":")
        }
        return env
    }
}
