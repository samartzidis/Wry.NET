using Wry.NET;
using Wry.NET.Bridge;

namespace Wry.NET.Bridge.Services;

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
    public string? Save(
        string? title = null,
        string? defaultPath = null,
        string? filterName = null,
        string? filterExtensions = null)
    {
        return WryDialog.Save(title, defaultPath, filterName, filterExtensions);
    }
}
