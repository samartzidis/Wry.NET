namespace Wry.NET;

/// <summary>
/// Native dialog API: message, ask, confirm, open file/folder, save file.
/// Uses the wry-native dialog implementation (rfd) on all platforms.
/// Pass a <see cref="WryWindow"/> as <paramref name="parent"/> to make the dialog modal to that window.
/// </summary>
public static class WryDialog
{
    /// <summary>
    /// Show a message dialog and return which button was pressed.
    /// </summary>
    /// <param name="message">Dialog body text.</param>
    /// <param name="title">Optional dialog title.</param>
    /// <param name="kind">Dialog icon/severity (Info, Warning, Error).</param>
    /// <param name="buttons">Button set (Ok, OkCancel, YesNo, YesNoCancel).</param>
    /// <param name="parent">Optional parent window; when set the dialog is modal to this window.</param>
    /// <returns>The label of the button pressed (e.g. "Ok", "Cancel", "Yes", "No"), or null on error.</returns>
    public static string? Message(
        string message,
        string? title = null,
        WryDialogKind kind = WryDialogKind.Info,
        WryDialogButtons buttons = WryDialogButtons.Ok,
        WryWindow? parent = null)
    {
        return NativeMethods.ReadAndFreeNativeString(
            NativeMethods.wry_dialog_message(parent?.NativePtr ?? 0, title, message, (int)kind, (int)buttons));
    }

    /// <summary>
    /// Show a Yes/No dialog.
    /// </summary>
    /// <param name="message">Dialog body text.</param>
    /// <param name="title">Optional dialog title.</param>
    /// <param name="kind">Dialog icon/severity.</param>
    /// <param name="parent">Optional parent window; when set the dialog is modal to this window.</param>
    /// <returns>True if the user chose Yes, false for No or Cancel.</returns>
    public static bool Ask(
        string message,
        string? title = null,
        WryDialogKind kind = WryDialogKind.Info,
        WryWindow? parent = null)
    {
        return NativeMethods.wry_dialog_ask(parent?.NativePtr ?? 0, title, message, (int)kind);
    }

    /// <summary>
    /// Show an Ok/Cancel dialog.
    /// </summary>
    /// <param name="message">Dialog body text.</param>
    /// <param name="title">Optional dialog title.</param>
    /// <param name="kind">Dialog icon/severity.</param>
    /// <param name="parent">Optional parent window; when set the dialog is modal to this window.</param>
    /// <returns>True if the user chose Ok, false for Cancel.</returns>
    public static bool Confirm(
        string message,
        string? title = null,
        WryDialogKind kind = WryDialogKind.Info,
        WryWindow? parent = null)
    {
        return NativeMethods.wry_dialog_confirm(parent?.NativePtr ?? 0, title, message, (int)kind);
    }

    /// <summary>
    /// Show an open file or folder dialog.
    /// </summary>
    /// <param name="directory">If true, pick folder(s); otherwise pick file(s).</param>
    /// <param name="multiple">If true, allow multiple selection.</param>
    /// <param name="title">Optional dialog title.</param>
    /// <param name="defaultPath">Optional starting directory or file path.</param>
    /// <param name="filterName">Optional filter label (e.g. "Images").</param>
    /// <param name="filterExtensions">Optional comma-separated extensions (e.g. "png,jpg,gif"). Used only if filterName is set.</param>
    /// <param name="parent">Optional parent window; when set the dialog is modal to this window.</param>
    /// <returns>Selected path(s), or null if cancelled. Single path as one element; multiple paths as multiple elements.</returns>
    public static string[]? Open(
        bool directory = false,
        bool multiple = false,
        string? title = null,
        string? defaultPath = null,
        string? filterName = null,
        string? filterExtensions = null,
        WryWindow? parent = null)
    {
        var raw = NativeMethods.ReadAndFreeNativeString(
            NativeMethods.wry_dialog_open(parent?.NativePtr ?? 0, title, defaultPath, directory, multiple, filterName, filterExtensions));
        if (raw == null) return null;
        if (string.IsNullOrEmpty(raw)) return [];
        var paths = raw.Split('\n');
        return paths.Length == 0 ? null : paths;
    }

    /// <summary>
    /// Show a save file dialog.
    /// </summary>
    /// <param name="title">Optional dialog title.</param>
    /// <param name="defaultPath">Optional starting directory or suggested filename.</param>
    /// <param name="filterName">Optional filter label.</param>
    /// <param name="filterExtensions">Optional comma-separated extensions.</param>
    /// <param name="parent">Optional parent window; when set the dialog is modal to this window.</param>
    /// <returns>Selected path, or null if cancelled.</returns>
    public static string? Save(
        string? title = null,
        string? defaultPath = null,
        string? filterName = null,
        string? filterExtensions = null,
        WryWindow? parent = null)
    {
        return NativeMethods.ReadAndFreeNativeString(
            NativeMethods.wry_dialog_save(parent?.NativePtr ?? 0, title, defaultPath, filterName, filterExtensions));
    }
}
