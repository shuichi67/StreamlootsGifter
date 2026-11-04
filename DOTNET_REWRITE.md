# .NET desktop rewrite

The C# version is a native WPF desktop app with no third-party NuGet packages. It keeps the existing Chromium extension and its loopback protocol (`127.0.0.1:8765`), and reads the existing `.env`, `config.json`, `gift_queue.json`, and bridge key from the project folder.

## Run

Install the **.NET 8 SDK** for Windows (the .NET runtime alone cannot build this project), then run from this folder:

```powershell
dotnet run --project .\StreamlootsManager.csproj
```

The registered Twitch OAuth redirect remains `http://localhost:17563`; ensure that redirect URL is registered for the same Twitch application as the client ID and secret in `.env`.

The Python entry point remains available as a rollback until the .NET app has been built and its Twitch and gifting flows have been exercised. The current development environment has .NET runtimes but no SDK/compiler, so this project has not yet been compiled or launched here.
