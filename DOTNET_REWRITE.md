# .NET desktop rewrite

The C# version is a native WPF desktop app with no third-party NuGet packages. It keeps the existing Chromium extension and its loopback protocol (`127.0.0.1:8765`). App settings, queue history, and the Bridge key stay beside the app; Twitch access and refresh tokens are stored under the current Windows user's Local AppData and encrypted with DPAPI.

## Run

Install the **.NET 8 SDK** for Windows (the .NET runtime alone cannot build this project), then run from this folder:

```powershell
dotnet run --project .\StreamlootsManager.csproj
```

Twitch sign-in uses Device Code Grant. The streamer authorizes the app at Twitch's activation page; no Client Secret or redirect URL is required on the user's PC. The published app uses its public Twitch Client ID and requests `channel:read:redemptions` and `user:read:chat`.

The Python entry point remains available as a rollback until the .NET app has been built and its Twitch and gifting flows have been exercised.
