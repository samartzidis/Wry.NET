using System.Collections.Generic;
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

        var resDir = Path.Combine(AppContext.BaseDirectory, "res");
        var icons = new List<byte[]>();
        for (int i = 1; i <= 12; i++)
        {
            var path = Path.Combine(resDir, $"{i}.ico");
            if (File.Exists(path))
                icons.Add(File.ReadAllBytes(path));
        }

        var tray = app.CreateTrayIcon(new WryTrayIconCreateOptions
        {
            Tooltip = "TrayApp",
            IconData = icons.Count > 0 ? icons[0] : null,
            Menu = menu,
        });

        int index = 0;
        using var timer = new System.Threading.Timer(_ =>
        {
            if (icons.Count == 0) return;
            index = (index + 1) % icons.Count;
            tray.SetIconFromBytes(icons[index]);
        }, null, 100, 100);

        tray.MenuItemClicked += (_, e) =>
        {
            switch (e.ItemId)
            {
                case "about":
                    WryDialog.Message("TrayApp - Wry.NET sample.\nTray-only app with a dialog.", "About TrayApp", kind: WryDialogKind.Info, parent: null);
                    break;
                case "quit":
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                    app.Exit(0);
                    break;
            }
        };

        app.Run();
    }
}
