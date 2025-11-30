# BrainrotTracker

A WinUI 3 companion for tracking your app usage and surfacing "brainrot" categories.

## Projects
- **Brainrot.Core** – shared models and tracking logic.
- **Brainrot.Console** – minimal console harness for exercising the core logic.
- **Brainrot.UI** – WinUI 3 desktop experience.

## Prerequisites
- .NET 8 SDK (tested with 8.0.121)
- Windows 10/11 for building the WinUI 3 app (XAML compiler requires Windows).

## Building
Use the solution file to restore and build all projects:

```bash
# Restore & build (Windows)
dotnet build BrainrotTracker.sln

# Restore & build on non-Windows (enables Windows targeting, but WinUI still requires Windows)
dotnet build BrainrotTracker.sln -p:EnableWindowsTargeting=true
```

On non-Windows hosts, the **Brainrot.UI** project fails because the WinUI XAML compiler only ships for Windows. You can still build the other projects individually:

```bash
# Build shared library and console harness
 dotnet build Brainrot.Core/Brainrot.Core.csproj
 dotnet build Brainrot.Console/Brainrot.Console.csproj
```

Run the console app from the repository root:

```bash
dotnet run --project Brainrot.Console/Brainrot.Console.csproj
```
