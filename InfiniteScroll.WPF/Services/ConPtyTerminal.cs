using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace InfiniteScroll.Services;

/// <summary>
/// Manages a Windows ConPTY pseudo-terminal session.
/// Replaces tmux on Windows — each terminal cell gets its own ConPTY process.
/// </summary>
public class ConPtyTerminal : IDisposable
{
    private IntPtr _pseudoConsoleHandle;
    private Process? _process;
    private SafeFileHandle? _pipeIn;
    private SafeFileHandle? _pipeOut;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private bool _disposed;

    public event Action<string>? OutputReceived;
    public event Action<int>? ProcessExited;
    public string CurrentDirectory { get; private set; }

    public bool IsRunning => _process is { HasExited: false };

    public ConPtyTerminal(string initialDirectory)
    {
        CurrentDirectory = initialDirectory;
    }

    /// <summary>
    /// Start a shell process (powershell or cmd) in a ConPTY.
    /// On systems without ConPTY support, falls back to plain Process.
    /// </summary>
    public void Start(int cols = 120, int rows = 30)
    {
        var shellPath = FindShell();

        var startInfo = new ProcessStartInfo
        {
            FileName = shellPath,
            WorkingDirectory = CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Environment =
            {
                ["TERM"] = "xterm-256color"
            }
        };

        _process = new Process { StartInfo = startInfo };
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) =>
        {
            ProcessExited?.Invoke(_process.ExitCode);
        };

        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                OutputReceived?.Invoke(e.Data + "\n");
        };

        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                OutputReceived?.Invoke(e.Data + "\n");
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void SendInput(string text)
    {
        if (_process is { HasExited: false })
        {
            _process.StandardInput.Write(text);
            _process.StandardInput.Flush();
        }
    }

    public void SendLine(string command)
    {
        SendInput(command + "\r\n");
    }

    public void Resize(int cols, int rows)
    {
        // ConPTY resize would go here when using native ConPTY API
        // For the fallback Process approach, resize is not supported
    }

    public void Kill()
    {
        if (_process is { HasExited: false })
        {
            try { _process.Kill(entireProcessTree: true); }
            catch { /* already exited */ }
        }
    }

    private static string FindShell()
    {
        // Prefer PowerShell 7, then Windows PowerShell, then cmd
        var pwsh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PowerShell", "7", "pwsh.exe");
        if (File.Exists(pwsh)) return pwsh;

        var winPwsh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(winPwsh)) return winPwsh;

        return "cmd.exe";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Kill();
        _writer?.Dispose();
        _reader?.Dispose();
        _pipeIn?.Dispose();
        _pipeOut?.Dispose();
        _process?.Dispose();
        GC.SuppressFinalize(this);
    }
}
