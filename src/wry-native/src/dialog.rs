//! Native dialog API: message, ask, confirm, open file/folder, save file.
//! Uses rfd for cross-platform file and message dialogs.

#![allow(clippy::missing_safety_doc)]

use std::ffi::{c_char, c_int, CString};
use std::path::Path;

use rfd::{FileDialog, MessageButtons, MessageDialog, MessageDialogResult, MessageLevel};

use crate::c_str_to_string;

// ---------------------------------------------------------------------------
// Constants (C API)
// ---------------------------------------------------------------------------

/// Dialog kind: 0 = Info, 1 = Warning, 2 = Error
fn level_from_int(kind: c_int) -> MessageLevel {
    match kind {
        1 => MessageLevel::Warning,
        2 => MessageLevel::Error,
        _ => MessageLevel::Info,
    }
}

/// Message dialog buttons: 0 = Ok, 1 = OkCancel, 2 = YesNo, 3 = YesNoCancel
fn buttons_from_int(buttons: c_int) -> MessageButtons {
    match buttons {
        1 => MessageButtons::OkCancel,
        2 => MessageButtons::YesNo,
        3 => MessageButtons::YesNoCancel,
        _ => MessageButtons::Ok,
    }
}

fn result_to_string(r: MessageDialogResult) -> String {
    match r {
        MessageDialogResult::Yes => "Yes".into(),
        MessageDialogResult::No => "No".into(),
        MessageDialogResult::Ok => "Ok".into(),
        MessageDialogResult::Cancel => "Cancel".into(),
        MessageDialogResult::Custom(s) => s,
    }
}

// ---------------------------------------------------------------------------
// Message - show message dialog, return which button was pressed
// ---------------------------------------------------------------------------

/// Show a message dialog.
/// - `title`: dialog title (nullable)
/// - `message`: dialog body (nullable)
/// - `kind`: 0 = Info, 1 = Warning, 2 = Error
/// - `buttons`: 0 = Ok, 1 = OkCancel, 2 = YesNo, 3 = YesNoCancel
/// Returns a new C string (Ok/Cancel/Yes/No); caller must free with `wry_string_free`. Returns null on error.
#[no_mangle]
pub extern "C" fn wry_dialog_message(
    title: *const c_char,
    message: *const c_char,
    kind: c_int,
    buttons: c_int,
) -> *mut c_char {
    let title_s = unsafe { c_str_to_string(title) };
    let message_s = unsafe { c_str_to_string(message) };
    let level = level_from_int(kind);
    let btns = buttons_from_int(buttons);

    let mut dlg = MessageDialog::new()
        .set_level(level)
        .set_description(if message_s.is_empty() { " " } else { &message_s });
    if !title_s.is_empty() {
        dlg = dlg.set_title(title_s);
    }
    dlg = dlg.set_buttons(btns);

    let result = dlg.show();
    CString::new(result_to_string(result).as_bytes())
        .ok()
        .map(|cs| cs.into_raw())
        .unwrap_or(std::ptr::null_mut())
}

// ---------------------------------------------------------------------------
// Ask - Yes/No dialog, returns true for Yes
// ---------------------------------------------------------------------------

/// Show a Yes/No dialog. Returns true if user chose Yes, false for No or Cancel.
#[no_mangle]
pub extern "C" fn wry_dialog_ask(
    title: *const c_char,
    message: *const c_char,
    kind: c_int,
) -> bool {
    let title_s = unsafe { c_str_to_string(title) };
    let message_s = unsafe { c_str_to_string(message) };
    let level = level_from_int(kind);

    let mut dlg = MessageDialog::new()
        .set_level(level)
        .set_buttons(MessageButtons::YesNo)
        .set_description(if message_s.is_empty() { " " } else { &message_s });
    if !title_s.is_empty() {
        dlg = dlg.set_title(title_s);
    }

    matches!(dlg.show(), MessageDialogResult::Yes)
}

// ---------------------------------------------------------------------------
// Confirm - Ok/Cancel dialog, returns true for Ok
// ---------------------------------------------------------------------------

/// Show an Ok/Cancel dialog. Returns true if user chose Ok, false for Cancel.
#[no_mangle]
pub extern "C" fn wry_dialog_confirm(
    title: *const c_char,
    message: *const c_char,
    kind: c_int,
) -> bool {
    let title_s = unsafe { c_str_to_string(title) };
    let message_s = unsafe { c_str_to_string(message) };
    let level = level_from_int(kind);

    let mut dlg = MessageDialog::new()
        .set_level(level)
        .set_buttons(MessageButtons::OkCancel)
        .set_description(if message_s.is_empty() { " " } else { &message_s });
    if !title_s.is_empty() {
        dlg = dlg.set_title(title_s);
    }

    matches!(dlg.show(), MessageDialogResult::Ok)
}

// ---------------------------------------------------------------------------
// Open - file or folder picker
// ---------------------------------------------------------------------------

/// Open file or folder picker.
/// - `title`: dialog title (nullable)
/// - `default_path`: starting directory or file (nullable)
/// - `directory`: true = pick folder(s), false = pick file(s)
/// - `multiple`: true = allow multiple selection
/// - `filter_name`: optional filter label (nullable)
/// - `filter_extensions`: comma-separated extensions e.g. "png,jpg" (nullable); used only if filter_name non-null
/// Returns a new C string: single path, or newline-separated paths if multiple; caller frees with `wry_string_free`. Returns null if cancelled.
#[no_mangle]
pub extern "C" fn wry_dialog_open(
    title: *const c_char,
    default_path: *const c_char,
    directory: bool,
    multiple: bool,
    filter_name: *const c_char,
    filter_extensions: *const c_char,
) -> *mut c_char {
    let title_s = unsafe { c_str_to_string(title) };
    let default_s = unsafe { c_str_to_string(default_path) };
    let filter_name_s = unsafe { c_str_to_string(filter_name) };
    let filter_ext_s = unsafe { c_str_to_string(filter_extensions) };

    let mut dlg = FileDialog::new();
    if !title_s.is_empty() {
        dlg = dlg.set_title(&title_s);
    }
    if !default_s.is_empty() {
        let p = Path::new(&default_s);
        if p.is_dir() {
            dlg = dlg.set_directory(p);
        } else if let Some(parent) = p.parent() {
            dlg = dlg.set_directory(parent);
            if let Some(name) = p.file_name() {
                dlg = dlg.set_file_name(name.to_string_lossy().as_ref());
            }
        }
    }
    if !filter_name_s.is_empty() && !filter_ext_s.is_empty() {
        let exts: Vec<&str> = filter_ext_s.split(',').map(|s| s.trim()).filter(|s| !s.is_empty()).collect();
        if !exts.is_empty() {
            dlg = dlg.add_filter(&filter_name_s, &exts);
        }
    }

    let result = if directory {
        if multiple {
            dlg.pick_folders().map(|v| v.into_iter().map(|p| p.to_string_lossy().into_owned()).collect::<Vec<_>>().join("\n"))
        } else {
            dlg.pick_folder().map(|p| p.to_string_lossy().into_owned())
        }
    } else {
        if multiple {
            dlg.pick_files().map(|v| v.into_iter().map(|p| p.to_string_lossy().into_owned()).collect::<Vec<_>>().join("\n"))
        } else {
            dlg.pick_file().map(|p| p.to_string_lossy().into_owned())
        }
    };

    match result {
        Some(s) => CString::new(s).ok().map(|cs| cs.into_raw()).unwrap_or(std::ptr::null_mut()),
        None => std::ptr::null_mut(),
    }
}

// ---------------------------------------------------------------------------
// Save - save file dialog
// ---------------------------------------------------------------------------

/// Save file dialog.
/// - `title`: dialog title (nullable)
/// - `default_path`: starting directory or suggested filename (nullable)
/// - `filter_name`: optional filter label (nullable)
/// - `filter_extensions`: comma-separated extensions (nullable)
/// Returns a new C string path; caller frees with `wry_string_free`. Returns null if cancelled.
#[no_mangle]
pub extern "C" fn wry_dialog_save(
    title: *const c_char,
    default_path: *const c_char,
    filter_name: *const c_char,
    filter_extensions: *const c_char,
) -> *mut c_char {
    let title_s = unsafe { c_str_to_string(title) };
    let default_s = unsafe { c_str_to_string(default_path) };
    let filter_name_s = unsafe { c_str_to_string(filter_name) };
    let filter_ext_s = unsafe { c_str_to_string(filter_extensions) };

    let mut dlg = FileDialog::new();
    if !title_s.is_empty() {
        dlg = dlg.set_title(&title_s);
    }
    if !default_s.is_empty() {
        let p = Path::new(&default_s);
        if p.is_dir() {
            dlg = dlg.set_directory(p);
        } else {
            if let Some(parent) = p.parent() {
                dlg = dlg.set_directory(parent);
            }
            if let Some(name) = p.file_name() {
                dlg = dlg.set_file_name(name.to_string_lossy().as_ref());
            }
        }
    }
    if !filter_name_s.is_empty() && !filter_ext_s.is_empty() {
        let exts: Vec<&str> = filter_ext_s.split(',').map(|s| s.trim()).filter(|s| !s.is_empty()).collect();
        if !exts.is_empty() {
            dlg = dlg.add_filter(&filter_name_s, &exts);
        }
    }

    match dlg.save_file() {
        Some(p) => CString::new(p.to_string_lossy().as_ref()).ok().map(|cs| cs.into_raw()).unwrap_or(std::ptr::null_mut()),
        None => std::ptr::null_mut(),
    }
}

// ---------------------------------------------------------------------------
// Unit tests (pure mappings)
// ---------------------------------------------------------------------------

#[cfg(test)]
mod tests {
    use super::{buttons_from_int, level_from_int, result_to_string};
    use rfd::{MessageButtons, MessageDialogResult, MessageLevel};

    #[test]
    fn level_from_int_maps_correctly() {
        assert!(matches!(level_from_int(0), MessageLevel::Info));
        assert!(matches!(level_from_int(1), MessageLevel::Warning));
        assert!(matches!(level_from_int(2), MessageLevel::Error));
        assert!(matches!(level_from_int(-1), MessageLevel::Info));
        assert!(matches!(level_from_int(3), MessageLevel::Info));
    }

    #[test]
    fn buttons_from_int_maps_correctly() {
        assert!(matches!(buttons_from_int(0), MessageButtons::Ok));
        assert!(matches!(buttons_from_int(1), MessageButtons::OkCancel));
        assert!(matches!(buttons_from_int(2), MessageButtons::YesNo));
        assert!(matches!(buttons_from_int(3), MessageButtons::YesNoCancel));
        assert!(matches!(buttons_from_int(-1), MessageButtons::Ok));
        assert!(matches!(buttons_from_int(4), MessageButtons::Ok));
    }

    #[test]
    fn result_to_string_maps_correctly() {
        assert_eq!(result_to_string(MessageDialogResult::Yes), "Yes");
        assert_eq!(result_to_string(MessageDialogResult::No), "No");
        assert_eq!(result_to_string(MessageDialogResult::Ok), "Ok");
        assert_eq!(result_to_string(MessageDialogResult::Cancel), "Cancel");
        assert_eq!(
            result_to_string(MessageDialogResult::Custom("Custom".into())),
            "Custom"
        );
    }
}
