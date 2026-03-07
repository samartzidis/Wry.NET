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

            options.AddBridge(bridge);
            options.Visible = false; // hidden until first page load finishes to avoid white flash

            using var app = new WryApp();

            // --- Tray icon (set up before Run so it's ready) ---
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

            app.CreateWindow(options: options, onCreated: window =>
            {
                window.PageLoad += (_, e) =>
                {
                    if (e.Event == WryPageLoadEvent.Finished)
                        window.Visible = true;
                };

                tray.MenuItemClicked += (_, e) =>
                {
                    switch (e.ItemId)
                    {
                        case "show":
                            window.Visible = true;
                            break;
                        case "hide":
                            window.Visible = false;
                            break;
                        case "new_window":
                            var childOptions = new WryWindowCreateOptions
                            {
                                Title = "Child Window (dynamic)",
                                Width = 600,
                                Height = 400,
                                Visible = false,
                            };
                            childOptions.SetFrontend(
                                devUrl: devUrl,
                                assembly: Assembly.GetExecutingAssembly(),
                                pathFragment: "#/child",
                                loggerFactory: loggerFactory);
                            childOptions.AddBridge(bridge);
                            app.CreateWindow(owner: window, options: childOptions, onCreated: child =>
                            {
                                child.PageLoad += (_, pe) =>
                                {
                                    if (pe.Event == WryPageLoadEvent.Finished)
                                        child.Visible = true;
                                };
                            });
                            break;
                        case "quit":
                            foreach (var w in app.Windows)
                                w.Close();
                            break;
                    }
                };
            });

            app.Run();
        }

    }
}
