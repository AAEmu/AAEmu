using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AAEmu.World.Core.ZoneHost;

/// <summary>
/// Starts <c>AAEmu.ZoneHost.exe</c> without inheriting World's console.
/// ZoneHost is a console PE that calls AllocConsole; a child of World otherwise
/// inherits that console and AllocConsole fails immediately.
/// </summary>
public static class ZoneHostWin32
{
    public const int DetachedProcess = 0x00000008;
    public const int CreateUnicodeEnvironment = 0x00000400;

    public static int CreationFlags => DetachedProcess | CreateUnicodeEnvironment;

    public static string BuildCommandLine(string executable, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        var parts = new List<string> { Quote(executable) };
        if (arguments != null)
        {
            foreach (var argument in arguments)
                parts.Add(Quote(argument ?? string.Empty));
        }

        return string.Join(' ', parts);
    }

    public static byte[] BuildEnvironmentBlock(IReadOnlyDictionary<string, string> overlay)
    {
        var map = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key)
                map[key] = entry.Value as string ?? string.Empty;
        }

        if (overlay != null)
        {
            foreach (var (key, value) in overlay)
                map[key] = value ?? string.Empty;
        }

        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
        using var ms = new MemoryStream();
        foreach (var (key, value) in map)
        {
            var line = key + "=" + value + "\0";
            var bytes = encoding.GetBytes(line);
            ms.Write(bytes, 0, bytes.Length);
        }

        ms.Write(encoding.GetBytes("\0"), 0, 2);
        return ms.ToArray();
    }

    public static Process StartDetached(ZoneHostLaunchSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var commandLine = BuildCommandLine(spec.Executable, spec.Arguments);
        var environment = BuildEnvironmentBlock(spec.Environment);
        var pin = GCHandle.Alloc(environment, GCHandleType.Pinned);
        try
        {
            var startup = new StartupInfoW { Cb = Marshal.SizeOf<StartupInfoW>() };
            var cmd = new StringBuilder(commandLine);
            if (!CreateProcessW(
                    spec.Executable,
                    cmd,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    bInheritHandles: false,
                    CreationFlags,
                    pin.AddrOfPinnedObject(),
                    spec.WorkingDirectory,
                    ref startup,
                    out var processInfo))
            {
                throw new InvalidOperationException(
                    $"CreateProcess failed for AAEmu.ZoneHost (win32 {Marshal.GetLastWin32Error()}).");
            }

            try
            {
                var process = Process.GetProcessById(unchecked((int)processInfo.DwProcessId));
                process.EnableRaisingEvents = true;
                return process;
            }
            finally
            {
                if (processInfo.HThread != IntPtr.Zero)
                    CloseHandle(processInfo.HThread);
                if (processInfo.HProcess != IntPtr.Zero)
                    CloseHandle(processInfo.HProcess);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    internal static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";
        var needsQuotes = false;
        foreach (var ch in value)
        {
            if (ch is ' ' or '\t' or '"')
            {
                needsQuotes = true;
                break;
            }
        }

        if (!needsQuotes)
            return value;
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref StartupInfoW lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfoW
    {
        public int Cb;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Ptr;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr HProcess;
        public IntPtr HThread;
        public uint DwProcessId;
        public uint DwThreadId;
    }
}
