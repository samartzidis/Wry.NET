using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wry.NET;

namespace Wry.NET.Bridge;

/// <summary>
/// Serves frontend assets from a directory on disk via a custom URI scheme (e.g. <c>app://</c>).
/// <para>
/// This avoids <c>file://</c> CORS restrictions that block ES module scripts
/// (e.g. Vite-built bundles with <c>&lt;script type="module"&gt;</c>).
/// </para>
/// <para>
/// Supports SPA routing: requests for paths without a file extension that don't
/// match an existing file are served the entry file (e.g. <c>index.html</c>).
/// </para>
/// </summary>
internal sealed class DiskAssetServer
{
    private readonly string _rootDir;
    private readonly string _entryFile;
    private readonly string _scheme;
    private readonly ILogger<DiskAssetServer> _logger;

    /// <summary>
    /// The entry URL to load in the window (e.g. "app://localhost/index.html").
    /// </summary>
    public string EntryUrl { get; }

    public DiskAssetServer(
        string rootDir,
        string entryFile = "index.html",
        string scheme = "app",
        ILogger<DiskAssetServer>? logger = null)
    {
        _rootDir = Path.GetFullPath(rootDir);
        _entryFile = entryFile;
        _scheme = scheme;
        _logger = logger ?? NullLogger<DiskAssetServer>.Instance;
        EntryUrl = $"{scheme}://localhost/{entryFile}";
    }

    /// <summary>
    /// Registers the custom protocol handler on the WryWindow.
    /// Call this before <c>WryApp.Run()</c>.
    /// </summary>
    public WryWindow Register(WryWindow window)
    {
        window.AddCustomProtocol(_scheme, HandleSchemeRequest);
        return window;
    }

    private ProtocolResponse HandleSchemeRequest(string url)
    {
        var uri = new Uri(url);
        var path = uri.AbsolutePath.TrimStart('/');

        // Directory requests -> serve entry file
        if (string.IsNullOrEmpty(path) || path.EndsWith('/'))
            path = _entryFile;

        _logger.LogDebug("Request: {Url} -> path: {Path}", url, path);

        // Try exact match
        if (TryServeFile(path, out var contentType, out var data))
            return new ProtocolResponse { StatusCode = 200, ContentType = contentType, Data = data };

        // Try appending .html
        if (!Path.HasExtension(path) && TryServeFile(path + ".html", out contentType, out data))
        {
            _logger.LogDebug("Resolved {Path} -> {Path}.html", path, path);
            return new ProtocolResponse { StatusCode = 200, ContentType = contentType, Data = data };
        }

        // SPA fallback
        if (!Path.HasExtension(path) && TryServeFile(_entryFile, out contentType, out data))
        {
            _logger.LogDebug("SPA fallback: {Path} -> {EntryFile}", path, _entryFile);
            return new ProtocolResponse { StatusCode = 200, ContentType = contentType, Data = data };
        }

        _logger.LogWarning("Not found: {Path}", path);
        return new ProtocolResponse
        {
            StatusCode = 404,
            ContentType = "text/plain; charset=utf-8",
            Data = System.Text.Encoding.UTF8.GetBytes($"Not found: {path}")
        };
    }

    private bool TryServeFile(string relativePath, out string contentType, out byte[] data)
    {
        // Normalize and prevent path traversal
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_rootDir, normalized));

        if (!fullPath.StartsWith(_rootDir, StringComparison.OrdinalIgnoreCase))
        {
            contentType = "";
            data = Array.Empty<byte>();
            return false;
        }

        if (File.Exists(fullPath))
        {
            contentType = GetContentType(relativePath);
            data = File.ReadAllBytes(fullPath);
            _logger.LogDebug("Serving: {Path} ({ContentType}, {Length} bytes)", fullPath, contentType, data.Length);
            return true;
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
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" or ".mjs" or ".jsx" => "text/javascript; charset=utf-8",
            ".ts" or ".tsx" => "application/x-typescript; charset=utf-8",
            ".json" or ".map" => "application/json",
            ".xml" => "text/xml; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".mp4" or ".m4v" => "video/mp4",
            ".webm" => "video/webm",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain; charset=utf-8",
            ".wasm" => "application/wasm",
            _ => "application/octet-stream",
        };
    }
}
