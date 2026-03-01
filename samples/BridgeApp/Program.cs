using System.Reflection;
using Microsoft.Extensions.Logging;
using Wry.NET.Bridge;
using SampleApp.Services;
using Wry.NET;

namespace SampleApp
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Set up logging
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            // Set up the bridge and register services
            var bridge = new WryBridge(loggerFactory.CreateLogger<WryBridge>());
            bridge.RegisterDialogService();
            bridge.RegisterService(new BackendService(bridge));

            // Resolve dev URL from CLI arg (--dev-url=...) or environment variable.
            var devUrl = args.FirstOrDefault(a => a.StartsWith("--dev-url="))?.Split('=', 2)[1]
                      ?? Environment.GetEnvironmentVariable("WRY_DEV_URL");

            // Create the Wry.NET application and window
            using var app = new WryApp();
            var window = app.CreateWindow();

            window.Title = "Wry.NET Bridge React App";
            window.Size = (1024, 800);
            window.Center();
            window.DefaultContextMenus = false;

            // Set window icon from the shared app.ico
            var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
                window.SetIconFromFile(iconPath);

            // Attach the bridge (registers IPC handler + init script shims)
            bridge.Attach(window);

            // Hide window until first page load finishes to avoid white flash
            window.Visible = false;
            window.PageLoad += (_, e) =>
            {
                if (e.Event == WryPageLoadEvent.Finished)
                    window.Visible = true;
            };

            // --- Tray icon ---
            var tray = app.CreateTrayIcon();
            tray.Tooltip = "Wry.NET Bridge App";

            if (File.Exists(iconPath))
                tray.SetIconFromBytes(File.ReadAllBytes(iconPath));

            var menu = new WryTrayMenu();
            menu.AddItem("show", "Show Window");
            menu.AddItem("hide", "Hide Window");
            menu.AddSeparator();
            menu.AddItem("new_window", "New Window");
            menu.AddSeparator();
            menu.AddItem("quit", "Quit");
            tray.Menu = menu;

            tray.MenuItemClicked += (_, e) =>
            {
                switch (e.ItemId)
                {
                    case "show":
                        window.Dispatch(w => w.Visible = true);
                        break;
                    case "hide":
                        window.Dispatch(w => w.Visible = false);
                        break;
                    case "new_window":
                        // Dynamic child window: same SPA, route ?window=child. Owner = main (stays on top, closes with main).
                        // URL and protocol are applied to the queued window (Tauri-style: config at create time).
                        var child = app.CreateWindow(owner: window);
                        child.Title = "Child Window (dynamic)";
                        child.Size = (600, 400);
                        child.Visible = false;
                        child.LoadFrontend(
                            assembly: Assembly.GetExecutingAssembly(),
                            devUrl: devUrl,
                            pathFragment: "#/child",
                            loggerFactory: loggerFactory);
                        bridge.Attach(child);
                        child.PageLoad += (_, pe) =>
                        {
                            if (pe.Event == WryPageLoadEvent.Finished)
                                child.Visible = true;
                        };
                        break;
                    case "quit":
                        foreach (var w in app.Windows)
                            w.Dispatch(win => win.Close());
                        break;
                }
            };

            // Configure frontend loading for main window: dev server -> embedded assets -> disk fallback
            window.LoadFrontend(
                assembly: Assembly.GetExecutingAssembly(),
                devUrl: devUrl,
                loggerFactory: loggerFactory);

            app.Run();
        }

    }
}
