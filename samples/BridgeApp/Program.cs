using System.Reflection;
using Microsoft.Extensions.Logging;
using Wry.Bridge;
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
            bridge.RegisterService(new GreetService(bridge));

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

            // Resolve dev URL from CLI arg (--dev-url=...) or environment variable.
            // When set, the WebView loads from the dev server (e.g. Vite HMR) instead of
            // embedded/disk assets. Any approach works — this is the sample's choice, not the library's.
            var devUrl = args.FirstOrDefault(a => a.StartsWith("--dev-url="))?.Split('=', 2)[1]
                      ?? Environment.GetEnvironmentVariable("WRY_DEV_URL");

            // Configure frontend loading: dev server -> embedded assets -> disk fallback
            window.LoadFrontend(
                assembly: Assembly.GetExecutingAssembly(),
                devUrl: devUrl,
                loggerFactory: loggerFactory);

            app.Run();
        }

    }
}
