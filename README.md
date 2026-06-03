# DekstopTemplate

## Dialog host convention

- Global modal dialogs are shown in the root host identified by `DialogHostIds.Root`.
- The root host is declared in `MainWindow.axaml`; all open/close operations must use the same identifier constant.
- Multiple parallel dialogs are enabled for the root host (`IsMultipleDialogsEnabled="True"`).
