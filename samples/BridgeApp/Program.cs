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

            // Prepare frontend (URL + protocol if embedded/disk) so we can pass at create time
            var options = new WryWindowCreateOptions();
            options.SetFrontend(devUrl: devUrl, assembly: Assembly.GetExecutingAssembly(), loggerFactory: loggerFactory);
            options.Title = "Wry.NET Bridge React App";
            options.Width = 1024;
            options.Height = 800;
            options.DefaultContextMenus = false;
            var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
                options.IconPath = iconPath;

            // Inject bridge init script into the window options before creation
            bridge.PrepareWindowOptions(options);

            using var app = new WryApp();
            var window = app.CreateWindow(null, options);

            app.WindowCreated += (_, e) =>
            {
                
            };

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
                    /*
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
                    */
                    case "quit":
                        foreach (var w in app.Windows)
                            w.Dispatch(win => win.Close());
                        break;
                }
            };

            app.Run();
        }

    }
}
