# Wry.NET

Managed .NET 8 wrapper for the **wry-native** C API: cross-platform webview windows using [wry](https://github.com/tauri-apps/wry) (WebView2 on Windows, WebKit on macOS/Linux) and [tao](https://github.com/tauri-apps/tao) (windowing).

## Main types

- **`WryApp`** — App lifecycle: create windows, register callbacks, then `Run()` to enter the event loop.
- **`WryWindow`** — Configure (title, size, URL, etc.) before run; after run, use properties, methods, events, and `Dispatch()` for cross-thread calls.

## Getting started

See the [root README](../../README.md) for setup, Quick Start, and samples.

## Requirements

- .NET 8+
- **Windows:** WebView2 (typically pre-installed)
- **macOS:** WebKit (built-in)
- **Linux:** WebKitGTK (`libgtk-3-dev`, `libwebkit2gtk-4.1-dev`)

## API coverage

For the mapping from wry’s Rust API to the native C API wrapped here, see [wry-native api-coverage.md](../wry-native/api-coverage.md).
