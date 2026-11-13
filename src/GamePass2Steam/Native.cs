using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using GamePass2Steam.Core;
using Microsoft.Win32.SafeHandles;

namespace GamePass2Steam;

internal static class Native
{
    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivationManager
    {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
    }
    public static uint Activate(string id)
    {
        var manager = (IActivationManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"), true)!)!;
        try { Marshal.ThrowExceptionForHR(manager.ActivateApplication(id, "", 0, out uint pid)); return pid; }
        finally { Marshal.FinalReleaseComObject(manager); }
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFamilyName(SafeProcessHandle process, ref uint size, StringBuilder name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint size, uint flags);
    private delegate bool WindowCallback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, WindowCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    private static HashSet<int> VisibleProcesses()
    {
        HashSet<int> ids = [];
        bool Collect(IntPtr window, IntPtr state)
        {
            if (IsWindowVisible(window)) { GetWindowThreadProcessId(window, out uint id); ids.Add((int)id); }
            return true;
        }
        EnumWindows((window, state) => { if (IsWindowVisible(window)) { Collect(window, state); EnumChildWindows(window, Collect, IntPtr.Zero); } return true; }, IntPtr.Zero);
        return ids;
    }
    public static string CanonicalPath(string path)
    {
        try
        {
            using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new StringBuilder(32768);
            uint length = GetFinalPathNameByHandle(file, buffer, (uint)buffer.Capacity, 0);
            if (length > 0 && length < buffer.Capacity)
            {
                string result = buffer.ToString();
                return result.StartsWith(@"\\?\UNC\") ? @"\\" + result[8..] : result.StartsWith(@"\\?\") ? result[4..] : result;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return Path.GetFullPath(path);
    }
    public static List<int> GameProcesses(Game game)
    {
        List<int> matches = [];
        var visible = game.Kind == "UWP" ? VisibleProcesses() : [];
        var path = new StringBuilder(32768);
        foreach (string name in game.Executables.Select(e => Path.GetFileNameWithoutExtension(e)).Distinct(StringComparer.OrdinalIgnoreCase))
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            using (var handle = OpenProcess(0x1000, false, process.Id))
            {
                if (handle.IsInvalid) continue;
                path.Clear(); uint size = (uint)path.Capacity;
                if (!QueryFullProcessImageName(handle, 0, path, ref size)) continue;
                string value = path.ToString();
                bool exact = game.Executables.Contains(value, StringComparer.OrdinalIgnoreCase);
                if (!exact)
                {
                    // A versioned WindowsApps alias and the custom Content path can name the same executable.
                    if (!game.Executables.Any(e => Path.GetFileName(e).Equals(Path.GetFileName(value), StringComparison.OrdinalIgnoreCase))) continue;
                    var family = new StringBuilder(1024); uint count = (uint)family.Capacity;
                    exact = GetPackageFamilyName(handle, ref count, family) == 0 && family.ToString() == game.Family;
                    if (!exact) exact = game.Executables.Contains(CanonicalPath(value), StringComparer.OrdinalIgnoreCase);
                }
                if (SessionTracker.IsRunning(game.Kind, exact, visible.Contains(process.Id))) matches.Add(process.Id);
            }
        }
        return matches;
    }
}
