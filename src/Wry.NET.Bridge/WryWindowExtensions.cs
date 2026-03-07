using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Wry.NET.Bridge;

/// <summary>
/// Extension methods for <see cref="WryWindow"/> that provide frontend loading strategies.
/// </summary>
public static class WryWindowExtensions
{
    /// <summary>
    /// Fills this options instance with frontend URL and optional protocol so you can pass it at window creation.
    /// Use with <c>app.CreateWindow(null, options)</c> so the window shows content.
    /// </summary>
    public static void SetFrontend(
        this WryWindowCreateOptions options,
        string? devUrl = null,
        string diskFallback = "wwwroot/index.html",
        Assembly? assembly = null,
        string resourcePrefix = "frontend/",
        string entryFile = "index.html",
        string scheme = "app",
        string? pathFragment = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var log = loggerFactory?.CreateLogger(nameof(WryWindowExtensions))
               ?? NullLogger.Instance;
        var assetLogger = loggerFactory?.CreateLogger<EmbeddedAssetServer>();
        var pathSuffix = pathFragment ?? "";

        // 1. Dev server
        if (!string.IsNullOrEmpty(devUrl))
        {
            log.LogInformation("Dev mode: loading from {Url}", devUrl + pathSuffix);
            options.Url = devUrl + pathSuffix;
            return;
        }

        // 2. Embedded assets
        if (assembly != null)
        {
            var server = EmbeddedAssetServer.CreateIfAvailable(assembly, resourcePrefix, entryFile, scheme, assetLogger);
            if (server != null)
            {
                log.LogInformation("Serving {AssetCount} embedded frontend assets via '{Scheme}://' scheme", server.AssetCount, server.Scheme);
                options.Url = server.EntryUrl + pathSuffix;
                options.Protocols = [(server.Scheme, server.HandleRequest)];
                return;
            }
        }

        // 3. Disk fallback
        var basePath = AppContext.BaseDirectory;
        var diskDir = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(basePath, diskFallback)))!;
        var server2 = new DiskAssetServer(diskDir, entryFile, scheme, loggerFactory?.CreateLogger<DiskAssetServer>());
        log.LogInformation("Serving frontend from disk via '{Scheme}://' scheme ({Path})", scheme, diskDir);
        options.Url = server2.EntryUrl + pathSuffix;
        options.Protocols = [(server2.Scheme, server2.HandleRequest)];
    }

    /// <summary>
    /// Adds this bridge's init script to the window options and registers a created-hook that
    /// automatically calls <see cref="WryBridge.Attach"/> when the window materializes.
    /// </summary>
    public static void AddBridge(this WryWindowCreateOptions options, WryBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(bridge);
        options.InitScripts ??= [];
        options.InitScripts.Add(WryBridge.GetBridgeInitScript());
        options.WindowCreatedActions ??= [];
        options.WindowCreatedActions.Add(bridge.Attach);
    }
}
