using Wry.NET;
using Wry.NET.Bridge;

namespace Wry.NET.Bridge.Services;

/// <summary>
/// Bridge service exposing native dialogs (message, ask, confirm, open, save).
/// Dialogs are automatically modal to the calling window via <see cref="CallContext"/>.
/// Prefer <c>bridge.RegisterDialogService()</c> to register; or <c>bridge.RegisterService(new DialogService())</c>.
/// </summary>
[BridgeService]
public class DialogService
{
    /// <summary>
    /// Show a message dialog and return which button was pressed.
    /// </summary>
    public string? Message(
        CallContext ctx,
        string message,
        string? title = null,
        WryDialogKind kind = WryDialogKind.Info,
        WryDialogButtons buttons = WryDialogButtons.Ok)
    {
        return WryDialog.Message(message, title, kind, buttons, parent: ctx.Window);
    }

    /// <summary>
    /// Show a Yes/No dialog.
    /// </summary>
    public bool Ask(
        CallContext ctx,
        string message,
        string? title = null,
        WryDialogKind kind = WryDialogKind.Info)
    {
        return WryDialog.Ask(message, title, kind, parent: ctx.Window);
    }

    /// <summary>
    /// Show an Ok/Cancel dialog.
    /// </summary>
    public bool Confirm(
        CallContext ctx,
        string message,
        string? title = null,
        WryDialogKind kind = WryDialogKind.Info)
    {
        return WryDialog.Confirm(message, title, kind, parent: ctx.Window);
    }

    /// <summary>
    /// Show an open file or folder dialog.
    /// </summary>
    public string[]? Open(
        CallContext ctx,
        bool directory = false,
        bool multiple = false,
        string? title = null,
        string? defaultPath = null,
        string? filterName = null,
        string? filterExtensions = null)
    {
        return WryDialog.Open(directory, multiple, title, defaultPath, filterName, filterExtensions, parent: ctx.Window);
    }

    /// <summary>
    /// Show a save file dialog.
    /// </summary>
    public string? Save(
        CallContext ctx,
        string? title = null,
        string? defaultPath = null,
        string? filterName = null,
        string? filterExtensions = null)
    {
        return WryDialog.Save(title, defaultPath, filterName, filterExtensions, parent: ctx.Window);
    }
}
