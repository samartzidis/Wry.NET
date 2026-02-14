using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wry.NET;

namespace Wry.Bridge;

/// <summary>
/// Extension methods for <see cref="WryWindow"/> that provide frontend loading strategies.
/// </summary>
public static class WryWindowExtensions
{
    /// <summary>
    /// Loads frontend content into the window using the best available strategy:
    /// <list type="number">
    ///   <item><b>Dev server</b> — if <paramref name="devUrl"/> is provided, the WebView loads from
    ///   that URL (e.g. Vite dev server with HMR). The library does not decide <em>how</em> you
    ///   determine the dev URL; pass it from a CLI argument, environment variable, config file, or
    ///   any other source.</item>
    ///   <item><b>Embedded assets</b> — if the assembly contains embedded frontend resources,
    ///   a custom protocol handler is registered and the entry file is loaded via <c>app://</c>.</item>
    ///   <item><b>Disk fallback</b> — loads from the <paramref name="diskFallback"/> path on disk.</item>
    /// </list>
    /// </summary>
    /// <param name="window">The WryWindow to configure.</param>
    /// <param name="devUrl">Optional dev server URL (e.g. <c>http://localhost:5173</c>).
    /// When set, the WebView loads from this URL and embedded/disk assets are ignored.</param>
    /// <param name="diskFallback">Path to load from disk when no embedded assets are available
    /// (default: <c>wwwroot/index.html</c>).</param>
    /// <param name="assembly">Optional assembly to scan for embedded frontend resources.
    /// Pass <c>null</c> to skip embedded asset loading (only dev server or disk fallback will be used).</param>
    /// <param name="resourcePrefix">Resource name prefix (default: <c>frontend/</c>).</param>
    /// <param name="entryFile">Entry HTML file name (default: <c>index.html</c>).</param>
    /// <param name="scheme">Custom scheme name (default: <c>app</c>).</param>
    /// <param name="loggerFactory">Optional logger factory. When provided, loggers are created
    /// internally for the asset server and loading diagnostics.</param>
    /// <returns>The window, for fluent chaining.</returns>
    public static WryWindow LoadFrontend(
        this WryWindow window,
        string? devUrl = null,
        string diskFallback = "wwwroot/index.html",
        Assembly? assembly = null,
        string resourcePrefix = "frontend/",
        string entryFile = "index.html",
        string scheme = "app",
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        var log = loggerFactory?.CreateLogger(nameof(WryWindowExtensions))
               ?? NullLogger.Instance;
        var assetLogger = loggerFactory?.CreateLogger<EmbeddedAssetServer>();

        // 1. Dev server
        if (!string.IsNullOrEmpty(devUrl))
        {
            window.Url = devUrl;
            log.LogInformation("Dev mode: loading from {Url}", devUrl);
            return window;
        }

        // 2. Embedded assets
        if (assembly != null)
        {
            var server = EmbeddedAssetServer.CreateIfAvailable(assembly, resourcePrefix, entryFile, scheme, assetLogger);
            if (server != null)
            {
                server.Register(window);
                window.Url = server.EntryUrl;
                log.LogInformation("Serving {AssetCount} embedded frontend assets via '{Scheme}://' scheme",
                    server.AssetCount, server.Scheme);
                return window;
            }
        }

        // 3. Disk fallback — serve via custom protocol to avoid file:// CORS issues
        //    (ES modules with type="module" are blocked by CORS on file:// URLs)
        // Always use AppContext.BaseDirectory — Assembly.Location returns empty in
        // single-file published apps (IL3000).
        var basePath = AppContext.BaseDirectory;

        var diskDir = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(basePath, diskFallback)))!;
        var server2 = new DiskAssetServer(diskDir, entryFile, scheme,
            loggerFactory?.CreateLogger<DiskAssetServer>());
        server2.Register(window);
        window.Url = server2.EntryUrl;
        log.LogInformation("Serving frontend from disk via '{Scheme}://' scheme ({Path})", scheme, diskDir);
        return window;
    }
}
