using System.Threading;
using System.Windows;

namespace GeminiWalletWidget;

public partial class App : System.Windows.Application
{
    public const string SingleInstanceMutexName = "AntigravityGeminiWalletWidget_SingleInstance";
    public const string WakeEventName = "AntigravityGeminiWalletWidget_WakeEvent";
    public const string QuitEventName = "AntigravityGeminiWalletWidget_QuitEvent";

    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool isAuto = e.Args.Any(a => string.Equals(a, "--auto", StringComparison.OrdinalIgnoreCase));
        bool isQuit = e.Args.Any(a => string.Equals(a, "--quit", StringComparison.OrdinalIgnoreCase));

        if (isQuit)
        {
            SignalNamedEvent(QuitEventName);
            Shutdown(0);
            return;
        }

        bool createdNew;
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
        if (!createdNew)
        {
            // Existing instance is already running: silently wake/show it (no modal popup!)
            SignalNamedEvent(WakeEventName);
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown(0);
            return;
        }

        // Launch MainWindow cleanly
        var main = new MainWindow(launchedAutomatically: isAuto);
        MainWindow = main;
        main.Show();
        base.OnStartup(e);
    }

    private static void SignalNamedEvent(string eventName)
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(eventName);
            ev.Set();
        }
        catch
        {
            // No running instance listening; ignore cleanly
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
