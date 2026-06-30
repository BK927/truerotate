using Microsoft.UI.Xaml;
using TrueRotate;
using Windows.Win32;

namespace TrueRotate;

// DISABLE_XAML_GENERATED_MAIN is defined in the csproj so the XAML toolchain
// does not emit its own Main.  We own the entry point.
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            // Attach to the parent console so output appears in the launching terminal.
            PInvoke.AttachConsole(unchecked((uint)-1));  // ATTACH_PARENT_PROCESS
            RunCli(args);
            return;
        }

        // ── Tray mode ──────────────────────────────────────────────────────
        bool createdNew;
        using var mutex = new Mutex(initiallyOwned: true, @"Global\TrueRotate_singleton", out createdNew);
        if (!createdNew)
            return;  // Another instance is already running.

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(static (p) =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);

            _ = new App();
        });
    }

    // ── CLI commands ───────────────────────────────────────────────────────────

    private static void RunCli(string[] args)
    {
        if (args.Length == 0 || args[0] == "list")
        {
            ListMonitors();
            return;
        }

        if (args[0] == "set" && args.Length == 3
            && int.TryParse(args[1], out int setIdx)
            && uint.TryParse(args[2], out uint setDeg))
        {
            var monitors = DisplayService.EnumerateMonitors();
            if (setIdx < 0 || setIdx >= monitors.Count)
            {
                Console.Error.WriteLine($"Error: index {setIdx} out of range (0–{monitors.Count - 1}).");
                return;
            }
            DisplayService.SetRotation(monitors[setIdx], setDeg);
            Console.WriteLine($"Set monitor {setIdx} ({monitors[setIdx].FriendlyName}) to {setDeg}°.");
            return;
        }

        if (args[0] == "test" && args.Length == 2 && int.TryParse(args[1], out int testIdx))
        {
            var monitors = DisplayService.EnumerateMonitors();
            if (testIdx < 0 || testIdx >= monitors.Count)
            {
                Console.Error.WriteLine($"Error: index {testIdx} out of range (0–{monitors.Count - 1}).");
                return;
            }

            var mon = monitors[testIdx];
            uint originalRotation = DisplayService.GetRotation(mon);
            Console.WriteLine($"Testing monitor {testIdx}: {mon.FriendlyName}");
            Console.WriteLine($"Original rotation: {originalRotation}°");
            Console.WriteLine();

            uint[] targets = [90, 180, 270, 0];
            int pass = 0, fail = 0;

            foreach (uint target in targets)
            {
                DisplayService.SetRotation(mon, target);
                uint actual = DisplayService.GetRotation(mon);
                bool ok = actual == target;
                Console.WriteLine($"  {target,3}° → read back {actual,3}° : {(ok ? "PASS" : "FAIL")}");
                if (ok) pass++; else fail++;
            }

            DisplayService.SetRotation(mon, originalRotation);
            Console.WriteLine();
            Console.WriteLine($"Result: {pass} PASS, {fail} FAIL. Restored to {originalRotation}°.");
            return;
        }

        if (args[0] == "diag")
        {
            RunDiag();
            return;
        }

        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  TrueRotate list");
        Console.Error.WriteLine("  TrueRotate set <index> <0|90|180|270>");
        Console.Error.WriteLine("  TrueRotate test <index>");
        Console.Error.WriteLine("  TrueRotate diag");
    }

    private static void RunDiag()
    {
        Console.WriteLine("TrueRotate — diagnostics");
        Console.WriteLine($"OS:      {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")})");
        Console.WriteLine($"Runtime: .NET {Environment.Version}");
        Console.WriteLine($"Exe:     {Environment.ProcessPath}");
        string cfg = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrueRotate", "config.json");
        Console.WriteLine($"Config:  {cfg} ({(File.Exists(cfg) ? "present" : "missing")})");
        Console.WriteLine();
        ListMonitors();
        if (File.Exists(cfg))
        {
            Console.WriteLine();
            Console.WriteLine("--- config.json ---");
            try { Console.WriteLine(File.ReadAllText(cfg)); }
            catch (Exception ex) { Console.WriteLine($"(read failed: {ex.Message})"); }
        }
    }

    private static void ListMonitors()
    {
        var monitors = DisplayService.EnumerateMonitors();
        Console.WriteLine($"{"IDX",-4} {"FRIENDLY NAME",-32} {"ROT",5}  {"GDI",-12}  DEVICE PATH");
        Console.WriteLine(new string('-', 110));
        foreach (var m in monitors)
        {
            string path = m.DevicePath.Length > 50
                ? "…" + m.DevicePath[^49..]
                : m.DevicePath;
            Console.WriteLine($"{m.Index,-4} {m.FriendlyName,-32} {m.Rotation,3}°  {m.GdiDeviceName,-12}  {path}");
        }
    }

}
