# Agent Rules

## Visual Studio File Visibility

After adding, moving, or generating files/folders, make the changes visible in Visual Studio before considering the task complete.

- Add new projects to `DesktopTemplate.slnx` with stable project `Id` values.
- For SDK-style projects, do not rely only on implicit file inclusion when Visual Studio visibility may be unclear.
- Add explicit `<Folder Include="...\" />` entries for new source folders when useful for Solution Explorer.
- For new Avalonia XAML views, add designer metadata such as `<None Update="...axaml"><SubType>Designer</SubType></None>` when the surrounding project uses that pattern.
- Verify with `dotnet build DesktopTemplate.slnx --no-restore` after project/solution metadata changes.
- If a build is blocked by a running `.NET Host`, stop the running app/debug session and rebuild.
