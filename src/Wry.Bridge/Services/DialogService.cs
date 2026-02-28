using Wry.NET;

namespace Wry.Bridge.Services;

/// <summary>
/// Bridge service exposing native dialogs (message, ask, confirm, open, save).
/// Prefer <c>bridge.RegisterDialogService()</c> to register; or <c>bridge.RegisterService(new DialogService())</c>.
/// </summary>
[BridgeService]
public class DialogService
{
    /// <summary>
    /// Show a message dialog and return which button was pressed.
    /// </summary>
    /// <param name="message">Dialog body text.</param>
    /// <param name="title">Optional dialog title.</param>
    /// <param name="kind">Dialog icon/severity (Info, Warning, Error).</param>
    /// <param name="buttons">Button set (Ok, OkCancel, YesNo, YesNoCancel).</param>
    /// <returns>The label of the button pressed (e.g. "Ok", "Cancel", "Yes", "No"), or null on error.</returns>
    public string? Message(
        string message,
        string? title = null,
        WryDialogKind kind = WryDialogKind.Info,
        WryDialogButtons buttons = WryDialogButtons.Ok)
    {
        return WryDialog.Message(message, title, kind, buttons); 
    }

    /// <summary>
    /// Show a Yes/No dialog.
    /// </summary>
    /// <param name="message">Dialog body text.</param>
    /// <param name="title">Optional dialog title.</param>
    /// <param name="kind">Dialog icon/severity.</param>
    /// <returns>True if the user chose Yes, false for No or Cancel.</returns>
    public bool Ask(
        string message,
        string? title = null,
        WryDialogKind kind = WryDialogKind.Info)
    {
        return WryDialog.Ask(message, title, kind);
    }

    /// <summary>
    /// Show an Ok/Cancel dialog.
    /// </summary>
    /// <param name="message">Dialog body text.</param>
    /// <param name="title">Optional dialog title.</param>
    /// <param name="kind">Dialog icon/severity.</param>
    /// <returns>True if the user chose Ok, false for Cancel.</returns>
    public bool Confirm(
        string message,
        string? title = null,
        WryDialogKind kind = WryDialogKind.Info)
    {
        return WryDialog.Confirm(message, title, kind);
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
    /// <returns>Selected path(s), or null if cancelled.</returns>
    public string[]? Open(
        bool directory = false,
        bool multiple = false,
        string? title = null,
        string? defaultPath = null,
        string? filterName = null,
        string? filterExtensions = null)
    {
        return WryDialog.Open(directory, multiple, title, defaultPath, filterName, filterExtensions);
    }

    /// <summary>
    /// Show a save file dialog.
    /// </summary>
    /// <param name="title">Optional dialog title.</param>
    /// <param name="defaultPath">Optional starting directory or suggested filename.</param>
    /// <param name="filterName">Optional filter label.</param>
    /// <param name="filterExtensions">Optional comma-separated extensions.</param>
    /// <returns>Selected path, or null if cancelled.</returns>
    public string? Save(
        string? title = null,
        string? defaultPath = null,
        string? filterName = null,
        string? filterExtensions = null)
    {
        return WryDialog.Save(title, defaultPath, filterName, filterExtensions);
    }
}
