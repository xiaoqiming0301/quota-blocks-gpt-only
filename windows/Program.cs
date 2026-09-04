using System.Runtime.InteropServices;
using System.Text;

namespace QuotaBlocks;

internal static class Program
{
    private const string MutexName = "GPTVersion.SingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--probe")) return Probe();

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance) return 0;

        Application.ThreadException += (_, e) => Log.Error($"UI exception: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error($"fatal: {e.ExceptionObject}");

        ApplicationConfiguration.Initialize();
        Application.Run(new OverlayForm());
        return 0;
    }

    /// <summary>Headless Codex read for checking the data path without the UI.</summary>
    private static int Probe()
    {
        if (AttachConsole(-1))
        {
            Console.OutputEncoding = Encoding.UTF8;
        }

        Report("GPT / Codex", () => CodexQuotaService.FetchAsync(CancellationToken.None));
        Console.WriteLine($"  executable: {CodexQuotaService.LocateExecutable() ?? "not found"}");
        return 0;

        static void Report(string name, Func<Task<QuotaSnapshot>> fetch)
        {
            Console.WriteLine($"== {name} ==");
            try
            {
                var snapshot = fetch().GetAwaiter().GetResult();
                Write("weekly", snapshot.Weekly);
                if (snapshot.Session is { } session) Write("session", session);
                if (snapshot.Extra is { } extra) Write(snapshot.ExtraLabel ?? "extra", extra);
                for (var i = 0; i < snapshot.AvailableResetCredits.Count; i++)
                {
                    Console.WriteLine($"  reset {i + 1,-3} expires {snapshot.AvailableResetCredits[i].ExpiresAt:yyyy-MM-dd HH:mm}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"  FAILED: {e.Message}");
            }
        }

        static void Write(string label, QuotaWindow window) => Console.WriteLine(
            $"  {label,-8} remaining {window.RemainingPercent,3}%  blocks {window.FilledBlockCount}/5  " +
            $"resets {(window.ResetsAt is { } r ? r.ToString("yyyy-MM-dd HH:mm") : "—")}");

    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);
}
