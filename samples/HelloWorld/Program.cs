using Wry.NET;

class Program
{
    [STAThread]
    static void Main()
    {
        // Print the WebView engine version
        var version = WryApp.GetWebViewVersion();
        Console.WriteLine($"WebView version: {version}");

        // Create app and window
        using var app = new WryApp();
        var window = app.CreateWindow();

        // Configure window
        window.Title = "Wry.NET Sample";
        window.Size = (1024, 768);
        window.Center();
        window.DefaultContextMenus = false;

        // Set window icon from the shared app.ico
        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(iconPath))
            window.SetIconFromFile(iconPath);

        // Load a simple HTML page with IPC demo
        window.Html = """
            <!DOCTYPE html>
            <html>
            <head>
                <style>
                    * { margin: 0; padding: 0; box-sizing: border-box; }
                    body {
                        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
                        background: #1a1a2e;
                        color: #e0e0e0;
                        display: flex;
                        align-items: center;
                        justify-content: center;
                        min-height: 100vh;
                    }
                    .container {
                        text-align: center;
                        max-width: 600px;
                        padding: 2rem;
                    }
                    h1 {
                        font-size: 2.5rem;
                        background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
                        -webkit-background-clip: text;
                        -webkit-text-fill-color: transparent;
                        margin-bottom: 0.5rem;
                    }
                    .subtitle {
                        color: #888;
                        margin-bottom: 2rem;
                    }
                    .card {
                        background: #16213e;
                        border-radius: 12px;
                        padding: 1.5rem;
                        margin-bottom: 1rem;
                        border: 1px solid #2a2a4a;
                    }
                    button {
                        background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
                        color: white;
                        border: none;
                        padding: 0.75rem 1.5rem;
                        border-radius: 8px;
                        font-size: 1rem;
                        cursor: pointer;
                        margin: 0.25rem;
                        transition: opacity 0.2s;
                    }
                    button:hover { opacity: 0.85; }
                    #messages {
                        text-align: left;
                        font-family: 'Cascadia Code', 'Fira Code', monospace;
                        font-size: 0.85rem;
                        color: #a0e0a0;
                        min-height: 80px;
                        white-space: pre-wrap;
                    }
                </style>
            </head>
            <body>
                <div class="container">
                    <h1>Wry.NET</h1>
                    <p class="subtitle">Cross-platform webview powered by wry + tao</p>

                    <div class="card">
                        <p>Send a message to C# via IPC:</p>
                        <br/>
                        <button onclick="sendMessage('Hello from JavaScript!')">Say Hello</button>
                        <button onclick="sendMessage(JSON.stringify({action:'ping', time: Date.now()}))">Send Ping</button>
                    </div>

                    <div class="card">
                        <div id="messages">Waiting for messages...</div>
                    </div>
                </div>

                <script>
                    function sendMessage(msg) {
                        window.ipc.postMessage(msg);
                    }

                    function appendMessage(text) {
                        const el = document.getElementById('messages');
                        el.textContent += '\n' + text;
                        el.scrollTop = el.scrollHeight;
                    }
                </script>
            </body>
            </html>
            """;

        // IPC: receive messages from JavaScript
        window.IpcMessageReceived += (sender, e) =>
        {
            Console.WriteLine($"[IPC] {e.Message}");

            // Echo back to the webview
            var win = (WryWindow)sender!;
            win.EvalJs($"appendMessage('C# received: {e.Message.Replace("'", "\\'")}')");
        };

        // Window events
        window.CloseRequested += (sender, e) =>
        {
            Console.WriteLine("[Event] Close requested");
            // e.Cancel = true; // uncomment to prevent closing
        };

        window.Resized += (sender, e) =>
        {
            Console.WriteLine($"[Event] Resized to {e.Width}x{e.Height}");
        };

        window.FocusChanged += (sender, e) =>
        {
            Console.WriteLine($"[Event] Focus: {e.Focused}");
        };

        Console.WriteLine("Starting app... close the window to exit.");

        // Run the event loop (blocks until all windows close)
        app.Run();

        Console.WriteLine("App finished.");
    }
}
