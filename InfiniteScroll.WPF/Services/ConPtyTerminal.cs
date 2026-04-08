using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace InfiniteScroll.Services;

/// <summary>
/// Manages a Windows ConPTY pseudo-terminal session using native APIs.
/// Each terminal cell gets its own ConPTY + shell process.
/// </summary>
public class ConPtyTerminal : IDisposable
{
    private IntPtr _hPC;
    private SafeFileHandle? _hPipeIn;   // write end → pty input
    private SafeFileHandle? _hPipeOut;  // read end  ← pty output
    private SafeFileHandle? _hPipePtyIn;
    private SafeFileHandle? _hPipePtyOut;
    private Process? _process;
    private Thread? _readThread;
    private bool _disposed;

    public event Action<byte[]>? DataReceived;
    public event Action<int>? ProcessExited;
    public string CurrentDirectory { get; set; }
    public bool IsRunning => _process is { HasExited: false };

    public ConPtyTerminal(string initialDirectory)
    {
        CurrentDirectory = initialDirectory;
    }

    public void Start(int cols = 120, int rows = 30)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                StartWithConPty(cols, rows);
                return;
            }
            catch
            {
                // Fall back to plain process if ConPTY unavailable
            }
        }

        StartFallback();
    }

    private void StartWithConPty(int cols, int rows)
    {
        // Create pipes: app writes to hPipeIn → pty reads from hPipePtyIn
        //               pty writes to hPipePtyOut → app reads from hPipeOut
        CreatePipe(out _hPipePtyIn, out _hPipeIn);
        CreatePipe(out _hPipeOut, out _hPipePtyOut);

        // Create pseudo console
        var size = new NativeMethods.COORD { X = (short)cols, Y = (short)rows };
        var hr = NativeMethods.CreatePseudoConsole(
            size,
            _hPipePtyIn!.DangerousGetHandle(),
            _hPipePtyOut!.DangerousGetHandle(),
            0,
            out _hPC);

        if (hr != 0)
            throw new Win32Exception(hr, "CreatePseudoConsole failed");

        // Close PTY-side pipe handles (now owned by pseudo console)
        _hPipePtyIn.Dispose();
        _hPipePtyIn = null;
        _hPipePtyOut.Dispose();
        _hPipePtyOut = null;

        // Start process attached to the pseudo console
        var shellPath = FindShell();
        _process = StartProcessWithPseudoConsole(shellPath, _hPC, CurrentDirectory);
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => ProcessExited?.Invoke(_process.ExitCode);

        // Start reading output
        _readThread = new Thread(ReadOutputLoop) { IsBackground = true, Name = "ConPTY-Read" };
        _readThread.Start();
    }

    private void StartFallback()
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
        };
        startInfo.Environment["TERM"] = "xterm-256color";

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.Exited += (_, _) => ProcessExited?.Invoke(_process.ExitCode);

        _process.Start();

        // Read stdout and stderr on background threads
        _readThread = new Thread(() =>
        {
            var buffer = new byte[4096];
            var stream = _process.StandardOutput.BaseStream;
            while (true)
            {
                int bytesRead;
                try { bytesRead = stream.Read(buffer, 0, buffer.Length); }
                catch { break; }
                if (bytesRead == 0) break;
                var data = new byte[bytesRead];
                Array.Copy(buffer, data, bytesRead);
                DataReceived?.Invoke(data);
            }
        }) { IsBackground = true, Name = "Fallback-Read" };
        _readThread.Start();

        // Also read stderr
        Task.Run(async () =>
        {
            var buffer = new byte[4096];
            var stream = _process.StandardError.BaseStream;
            while (true)
            {
                int bytesRead;
                try { bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length); }
                catch { break; }
                if (bytesRead == 0) break;
                var data = new byte[bytesRead];
                Array.Copy(buffer, data, bytesRead);
                DataReceived?.Invoke(data);
            }
        });
    }

    private void ReadOutputLoop()
    {
        var buffer = new byte[4096];
        using var stream = new FileStream(_hPipeOut!, FileAccess.Read, 4096, false);
        while (!_disposed)
        {
            int bytesRead;
            try { bytesRead = stream.Read(buffer, 0, buffer.Length); }
            catch { break; }
            if (bytesRead == 0) break;

            var data = new byte[bytesRead];
            Array.Copy(buffer, data, bytesRead);
            DataReceived?.Invoke(data);
        }
    }

    public void SendInput(byte[] data)
    {
        if (_hPipeIn != null && !_hPipeIn.IsClosed)
        {
            using var stream = new FileStream(_hPipeIn, FileAccess.Write, 4096, false);
            stream.Write(data, 0, data.Length);
            stream.Flush();
        }
        else if (_process is { HasExited: false } && _process.StartInfo.RedirectStandardInput)
        {
            _process.StandardInput.BaseStream.Write(data, 0, data.Length);
            _process.StandardInput.BaseStream.Flush();
        }
    }

    public void SendInput(string text)
    {
        SendInput(System.Text.Encoding.UTF8.GetBytes(text));
    }

    public void Resize(int cols, int rows)
    {
        if (_hPC != IntPtr.Zero)
        {
            var size = new NativeMethods.COORD { X = (short)cols, Y = (short)rows };
            NativeMethods.ResizePseudoConsole(_hPC, size);
        }
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

        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        var winPwsh = Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(winPwsh)) return winPwsh;

        return Path.Combine(systemRoot, "System32", "cmd.exe");
    }

    private static void CreatePipe(out SafeFileHandle readSide, out SafeFileHandle writeSide)
    {
        if (!NativeMethods.CreatePipe(out readSide, out writeSide, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe failed");
    }

    private static Process StartProcessWithPseudoConsole(string command, IntPtr hPC, string workingDir)
    {
        var startupInfo = new NativeMethods.STARTUPINFOEX();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<NativeMethods.STARTUPINFOEX>();

        // Initialize proc thread attribute list with the pseudo console
        var attrListSize = IntPtr.Zero;
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrListSize);
        startupInfo.lpAttributeList = Marshal.AllocHGlobal(attrListSize.ToInt32());

        if (!NativeMethods.InitializeProcThreadAttributeList(
                startupInfo.lpAttributeList, 1, 0, ref attrListSize))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        if (!NativeMethods.UpdateProcThreadAttribute(
                startupInfo.lpAttributeList,
                0,
                (IntPtr)NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                hPC,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var processInfo = new NativeMethods.PROCESS_INFORMATION();
        if (!NativeMethods.CreateProcess(
                null,
                command,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                NativeMethods.EXTENDED_STARTUPINFO_PRESENT,
                IntPtr.Zero,
                workingDir,
                ref startupInfo,
                out processInfo))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return Process.GetProcessById(processInfo.dwProcessId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Kill();

        if (_hPC != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(_hPC);
            _hPC = IntPtr.Zero;
        }

        _hPipeIn?.Dispose();
        _hPipeOut?.Dispose();
        _hPipePtyIn?.Dispose();
        _hPipePtyOut?.Dispose();
        _process?.Dispose();

        GC.SuppressFinalize(this);
    }
}

internal static class NativeMethods
{
    internal const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    internal const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    [StructLayout(LayoutKind.Sequential)]
    internal struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int CreatePseudoConsole(
        COORD size,
        IntPtr hInput,
        IntPtr hOutput,
        uint dwFlags,
        out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        IntPtr lpPipeAttributes,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CreateProcess(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);
}
