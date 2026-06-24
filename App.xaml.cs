using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;
using Application = System.Windows.Application;
using Forms = System.Windows.Forms;

namespace DeepLTranslator;

public partial class App : Application
{
    const string PipeName = "DeepL-Translator-Auth";

    static Mutex?              _mutex;
    internal Forms.NotifyIcon? TrayIcon;
    internal HotkeyManager?   Hotkey;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // If launched with a deepl:// callback URL, forward it to the running instance
        if (e.Args.Length > 0 && e.Args[0].StartsWith("deepl://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(2000);
                using var w = new StreamWriter(client) { AutoFlush = true };
                w.Write(e.Args[0]);
            }
            catch { }
            Shutdown();
            return;
        }

        // Single instance guard
        _mutex = new Mutex(true, "DeepL-Translator-7F3A", out bool first);
        if (!first) { Shutdown(); return; }

        // Listen for OAuth callbacks from second instances
        _ = Task.Run(ListenForCallbacks);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Tray icon
        TrayIcon = new Forms.NotifyIcon
        {
            Text    = "Better DeepL Übersetzer",
            Icon    = LoadIcon(),
            Visible = true,
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Better DeepL Übersetzer", null, (_, _) => ShowMain());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Beenden",           null, (_, _) => ExitApp());
        TrayIcon.ContextMenuStrip  = menu;
        TrayIcon.DoubleClick      += (_, _) => ShowMain();

        // Global Ctrl+C+C hotkey
        Hotkey = new HotkeyManager();
        Hotkey.CtrlCCPressed += () => ((MainWindow?)MainWindow)?.OnCtrlCC();

        var win = new MainWindow();
        win.Show();
    }

    async Task ListenForCallbacks()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In,
                    maxNumberOfServerInstances: 1, transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync();
                using var reader = new StreamReader(server);
                var url = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(url))
                    Dispatcher.Invoke(() => AuthManager.HandleCallback(url));
            }
            catch { await Task.Delay(500); }
        }
    }

    internal void ShowMain()
    {
        if (MainWindow is MainWindow w)
        {
            w.Show();
            w.WindowState = WindowState.Normal;
            w.Activate();
        }
    }

    void ExitApp()
    {
        TrayIcon?.Dispose();
        Hotkey?.Dispose();
        _mutex?.ReleaseMutex();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TrayIcon?.Dispose();
        Hotkey?.Dispose();
        base.OnExit(e);
    }

    static System.Drawing.Icon LoadIcon()
    {
        var uri  = new Uri("pack://application:,,,/icon.ico");
        var info = Application.GetResourceStream(uri);
        return info is not null
            ? new System.Drawing.Icon(info.Stream)
            : System.Drawing.SystemIcons.Application;
    }
}

