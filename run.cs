using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class Program
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
    const int SW_SHOWNORMAL = 1;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    static int Main(string[] args)
    {
        string input = args.Length > 0 ? string.Join(" ", args) : ReadInteractive();
        if (string.IsNullOrWhiteSpace(input)) return 1;
        var (file, parameters) = SplitCommand(input);
        string resolved = ResolveAppPath(file) ?? file;
        bool ok = LaunchWithShellExecuteEx(resolved, parameters);
        return ok ? 0 : 2;
    }

    static string ReadInteractive()
    {
        Console.Write("run: ");
        return Console.ReadLine()?.Trim() ?? "";
    }

    static (string file, string args) SplitCommand(string input)
    {
        input = input.Trim();
        if (input.Length == 0) return (input, "");
        if (input[0] == '"')
        {
            int idx = input.IndexOf('"', 1);
            if (idx > 0)
            {
                string file = input.Substring(1, idx - 1);
                string rest = input.Substring(idx + 1).Trim();
                return (file, rest);
            }
        }
        int firstSpace = input.IndexOf(' ');
        if (firstSpace < 0) return (input, "");
        string first = input.Substring(0, firstSpace);
        string restArgs = input.Substring(firstSpace + 1).Trim();
        return (first, restArgs);
    }

    static string ResolveAppPath(string file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        if (Path.IsPathRooted(file) || file.Contains(Path.DirectorySeparatorChar.ToString()) || file.Contains(Path.AltDirectorySeparatorChar.ToString()))
            return file;
        string[] candidates = file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? new[] { file } : new[] { file, file + ".exe" };
        foreach (string candidate in candidates)
        {
            string fromUser = LookupAppPath(Registry.CurrentUser, candidate);
            if (!string.IsNullOrEmpty(fromUser)) return fromUser;
            string fromLocal = LookupAppPath(Registry.LocalMachine, candidate);
            if (!string.IsNullOrEmpty(fromLocal)) return fromLocal;
        }
        return null;
    }

    static string LookupAppPath(RegistryKey hive, string name)
    {
        try
        {
            using (var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + name, false))
            {
                if (key != null)
                {
                    object def = key.GetValue(null);
                    if (def is string s && s.Length > 0) return Environment.ExpandEnvironmentVariables(s);
                }
            }
        }
        catch { }
        return null;
    }

    static bool LaunchWithShellExecuteEx(string file, string parameters)
    {
        var sei = new SHELLEXECUTEINFO();
        sei.cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO));
        sei.fMask = SEE_MASK_NOCLOSEPROCESS;
        sei.hwnd = IntPtr.Zero;
        sei.lpVerb = "open";
        sei.lpFile = file;
        sei.lpParameters = string.IsNullOrWhiteSpace(parameters) ? null : parameters;
        sei.lpDirectory = null;
        sei.nShow = SW_SHOWNORMAL;
        sei.hProcess = IntPtr.Zero;
        bool res = false;
        try
        {
            res = ShellExecuteEx(ref sei);
            if (!res)
            {
                int err = Marshal.GetLastWin32Error();
                Console.Error.WriteLine("ShellExecuteEx failed (GetLastError = " + err + ")");
                return false;
            }
            if (sei.hProcess != IntPtr.Zero)
            {
                CloseHandle(sei.hProcess);
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Exception: " + ex.Message);
            return false;
        }
    }

}
