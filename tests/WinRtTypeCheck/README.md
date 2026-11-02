# WinRT type check

Compiles the Home Assistant link's WinRT-facing sources (`HomeAssistantLinkService`, the Settings
controller, `UserSettings`) with C# 7.3 against the Windows SDK 10.0.19041 metadata from the
`Microsoft.Windows.SDK.Contracts` package. It runs on Linux, so code written off Windows gets a real
type check before the Windows build. `Stubs.cs` stands in for `App` and `LogService` only.

    dotnet build tests/WinRtTypeCheck

It does not compile XAML, run .NET Native or prove anything about the device; the ARM Release build
on Windows and the Lumia tests remain required.
