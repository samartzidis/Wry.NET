using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wry.NET;

namespace Wry.NET.Bridge;

/// <summary>
/// Serves frontend assets embedded as .NET resources via a custom URI scheme (e.g. <c>app://</c>),
/// enabling single-file publishing — the entire frontend is baked into the executable.
/// <para>
/// When the WebView navigates to <c>app://localhost/index.html</c>, the server intercepts the request
/// and returns the matching embedded resource stream directly from memory. No files are written to disk.
/// </para>
/// <para>
/// Supports SPA (Single Page Application) routing: requests for paths that don't match an embedded
/// resource and have no file extension are served the entry file (e.g. <c>index.html</c>), allowing
/// client-side routers (React Router, Vue Router, etc.) to handle navigation.
/// </para>
/// </summary>
internal sealed class EmbeddedAssetServer
{
    /// <summary>
    /// Default resource prefix used by the MSBuild targets when embedding frontend assets.
    /// </summary>
    public const string DefaultResourcePrefix = "frontend/";

    /// <summary>
    /// Default custom scheme name.
    /// </summary>
    public const string DefaultScheme = "app";

    private readonly Assembly _assembly;
    private readonly string _scheme;
    private readonly string _entryFile;
    private readonly ILogger<EmbeddedAssetServer> _logger;

    // Maps normalized path (forward slashes, e.g. "assets/index.js") to actual resource name.
    // Needed because MSBuild %(RecursiveDir) uses backslashes on Windows, producing resource
    // names like "frontend/assets\index.js" which won't match URL-derived forward-slash paths.
    private readonly Dictionary<string, string> _resourceMap;

    /// <summary>
    /// The custom scheme name used by this server (e.g. "app").
    /// </summary>
    public string Scheme => _scheme;

    /// <summary>
    /// The entry URL to load in the window (e.g. "app://localhost/index.html").
    /// </summary>
    public string EntryUrl { get; }

    /// <summary>
    /// Number of embedded frontend assets available.
    /// </summary>
    public int AssetCount { get; }

    /// <summary>
    /// Creates an EmbeddedAssetServer that serves resources via a custom URI scheme.
    /// </summary>
    /// <param name="assembly">Assembly containing embedded frontend resources.</param>
    /// <param name="resourcePrefix">Resource name prefix to match (default: "frontend/").</param>
    /// <param name="entryFile">Name of the entry HTML file (default: "index.html").</param>
    /// <param name="scheme">Custom scheme name (default: "app").</param>
    /// <param name="logger">Optional logger. Pass <c>null</c> to disable logging.</param>
    public EmbeddedAssetServer(
        Assembly assembly,
        string resourcePrefix = DefaultResourcePrefix,
        string entryFile = "index.html",
        string scheme = DefaultScheme,
        ILogger<EmbeddedAssetServer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        _assembly = assembly;
        _scheme = scheme;
        _entryFile = entryFile;
        _logger = logger ?? NullLogger<EmbeddedAssetServer>.Instance;
        _resourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            // Strip prefix and normalize to forward slashes for consistent URL-based lookup
            var relativePath = name[resourcePrefix.Length..].Replace('\\', '/');
            _resourceMap[relativePath] = name;
        }

        AssetCount = _resourceMap.Count;
        EntryUrl = $"{scheme}://localhost/{entryFile}";
    }

    /// <summary>
    /// Registers the custom protocol handler on the WryWindow.
    /// Call this before <c>WryApp.Run()</c>.
    /// </summary>
    /// <param name="window">The WryWindow to register the handler on.</param>
    /// <returns>The window, for fluent chaining.</returns>
    public WryWindow Register(WryWindow window)
    {
        window.AddCustomProtocol(_scheme, HandleSchemeRequest);
        return window;
    }

    /// <summary>
    /// Creates an EmbeddedAssetServer if the assembly contains embedded frontend assets.
    /// Returns <c>null</c> if no assets are found, allowing easy fallback to file-based loading.
    /// </summary>
    public static EmbeddedAssetServer? CreateIfAvailable(
        Assembly assembly,
        string resourcePrefix = DefaultResourcePrefix,
        string entryFile = "index.html",
        string scheme = DefaultScheme,
        ILogger<EmbeddedAssetServer>? logger = null)
    {
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (name.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return new EmbeddedAssetServer(assembly, resourcePrefix, entryFile, scheme, logger);
            }
        }

        return null;
    }

    private ProtocolResponse HandleSchemeRequest(string url)
    {
        // Parse the path from the URL: "app://localhost/path/to/file" -> "path/to/file"
        var uri = new Uri(url);
        var path = uri.AbsolutePath.TrimStart('/');

        // Directory requests (empty path or trailing slash) -> serve entry file
        if (string.IsNullOrEmpty(path) || path.EndsWith('/'))
            path = _entryFile;

        _logger.LogDebug("Request: {Url} -> path: {Path}", url, path);

        // Try exact match first
        if (TryServeResource(path, out var contentType, out var data))
            return new ProtocolResponse { StatusCode = 200, ContentType = contentType, Data = data };

        // Try appending .html (e.g. "/about" -> "about.html")
        if (!Path.HasExtension(path) && TryServeResource(path + ".html", out contentType, out data))
        {
            _logger.LogDebug("Resolved {Path} -> {Path}.html", path, path);
            return new ProtocolResponse { StatusCode = 200, ContentType = contentType, Data = data };
        }

        // SPA fallback: if the path has no file extension, serve the entry file (index.html)
        // so client-side routers (React Router, Vue Router, etc.) can handle the route.
        if (!Path.HasExtension(path) && TryServeResource(_entryFile, out contentType, out data))
        {
            _logger.LogDebug("SPA fallback: {Path} -> {EntryFile}", path, _entryFile);
            return new ProtocolResponse { StatusCode = 200, ContentType = contentType, Data = data };
        }

        // Resource not found
        _logger.LogWarning("Not found: {Path}", path);
        return new ProtocolResponse
        {
            StatusCode = 404,
            ContentType = "text/plain; charset=utf-8",
            Data = System.Text.Encoding.UTF8.GetBytes($"Not found: {path}")
        };
    }

    private bool TryServeResource(string path, out string contentType, out byte[] data)
    {
        if (_resourceMap.TryGetValue(path, out var resourceName))
        {
            using var resourceStream = _assembly.GetManifestResourceStream(resourceName);
            if (resourceStream != null)
            {
                contentType = GetContentType(path);
                using var ms = new MemoryStream();
                resourceStream.CopyTo(ms);
                data = ms.ToArray();
                _logger.LogDebug("Serving: {ResourceName} ({ContentType}, {Length} bytes)", resourceName, contentType, data.Length);
                return true;
            }
        }

        contentType = "";
        data = Array.Empty<byte>();
        return false;
    }

    private static string GetContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            // HTML
            ".html" or ".htm" => "text/html; charset=utf-8",
            // Stylesheets
            ".css" => "text/css; charset=utf-8",
            // JavaScript / TypeScript
            ".js" or ".mjs" or ".jsx" => "text/javascript; charset=utf-8",
            ".ts" or ".tsx" => "application/x-typescript; charset=utf-8",
            // Data formats
            ".json" or ".map" => "application/json",
            ".xml" => "text/xml; charset=utf-8",
            ".yaml" or ".yml" => "text/yaml; charset=utf-8",
            ".toml" => "text/toml; charset=utf-8",
            // Images
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            // Fonts
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".eot" => "application/vnd.ms-fontobject",
            // Audio
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            ".aac" => "audio/aac",
            ".flac" => "audio/flac",
            ".opus" => "audio/opus",
            // Video
            ".mp4" or ".m4v" => "video/mp4",
            ".webm" => "video/webm",
            ".ogv" => "video/ogg",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            // Documents
            ".pdf" => "application/pdf",
            ".txt" => "text/plain; charset=utf-8",
            ".md" => "text/markdown; charset=utf-8",
            // Archives
            ".zip" => "application/zip",
            ".gz" => "application/gzip",
            ".tar" => "application/x-tar",
            // WebAssembly
            ".wasm" => "application/wasm",
            // Default
            _ => "application/octet-stream",
        };
    }
}
