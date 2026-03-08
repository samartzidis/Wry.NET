using Wry.NET;

class Program
{
    [STAThread]
    static void Main()
    {
        using var app = new WryApp();

        // Keep running when "last window closed" (tray-only has no windows); allow programmatic Exit()
        app.ExitRequested += (_, e) => { if (e.ExitCode is null) e.Cancel = true; };

        var menu = new WryTrayMenu();
        menu.AddItem("about", "About...");
        menu.AddSeparator();
        menu.AddItem("quit", "Quit");

        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        var tray = app.CreateTrayIcon(new WryTrayIconCreateOptions
        {
            Tooltip = "TrayApp",
            IconData = File.Exists(iconPath) ? File.ReadAllBytes(iconPath) : null,
            Menu = menu,
        });

        tray.MenuItemClicked += (_, e) =>
        {
            switch (e.ItemId)
            {
                case "about":
                    WryDialog.Message("TrayApp - Wry.NET sample.\nTray-only app with a dialog.", "About TrayApp", kind: WryDialogKind.Info, parent: null);
                    break;
                case "quit":
                    app.Exit(0);
                    break;
            }
        };

        app.Run();
    }
}
