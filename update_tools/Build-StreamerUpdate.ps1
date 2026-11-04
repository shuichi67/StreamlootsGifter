param(
    [string]$Version = "",
    [switch]$IncludeFullDownload,
    [string]$SigningKeyPath = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot "StreamlootsManager.csproj"
$extensionSource = Join-Path $projectRoot "streamloots_extension"
$baselineRoot = Join-Path $projectRoot "StreamerShare"
$distRoot = Join-Path $projectRoot "dist"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$workRoot = Join-Path $distRoot "update-build-$stamp"
$publishRoot = Join-Path $workRoot "publish"
$packageRoot = Join-Path $workRoot "package"
$payloadRoot = Join-Path $packageRoot "payload"

if (-not (Test-Path -LiteralPath $projectFile)) { throw "Project file not found: $projectFile" }
if (-not (Test-Path -LiteralPath $baselineRoot)) { throw "StreamerShare baseline folder not found: $baselineRoot" }
if (-not (Test-Path -LiteralPath $extensionSource)) { throw "Extension source folder not found: $extensionSource" }
if ([string]::IsNullOrWhiteSpace($Version)) {
    $manifest = Get-Content -LiteralPath (Join-Path $extensionSource "manifest.json") -Raw | ConvertFrom-Json
    $Version = [string]$manifest.version
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null

Write-Host "Publishing Streamloots Gifter $Version for Windows x64..."
$publishArgs = @(
    "publish", $projectFile,
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=false",
    "-p:Version=$Version",
    "--output", $publishRoot
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Copy-Item -LiteralPath $extensionSource -Destination (Join-Path $publishRoot "streamloots_extension") -Recurse -Force
foreach ($fileName in @("README.txt", "TWITCH_SETUP.txt", ".env.example")) {
    $sourceFile = Join-Path $baselineRoot $fileName
    if (Test-Path -LiteralPath $sourceFile) { Copy-Item -LiteralPath $sourceFile -Destination (Join-Path $publishRoot $fileName) -Force }
}
Set-Content -LiteralPath (Join-Path $publishRoot "VERSION.txt") -Value $Version -Encoding UTF8

$fullArchivePath = $null
if ($IncludeFullDownload) {
    # Build a clean first-install package only when explicitly requested. Never
    # reuse developer credentials, settings, history, queue, or Bridge key.
    $fullPackageRoot = Join-Path $workRoot "full-package"
    New-Item -ItemType Directory -Path $fullPackageRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $publishRoot "*") -Destination $fullPackageRoot -Recurse -Force
    foreach ($protectedName in @("config.json", "gift_queue.json", "gift_queue.json.tmp", "gift_queue.json.journal", ".streamloots_bridge_key")) {
        $protectedPath = Join-Path $fullPackageRoot $protectedName
        if (Test-Path -LiteralPath $protectedPath -PathType Leaf) { Remove-Item -LiteralPath $protectedPath -Force }
    }
    $fullArchivePath = Join-Path $distRoot "StreamlootsGifter-Windows-x64-v$Version.zip"
    Compress-Archive -Path (Join-Path $fullPackageRoot "*") -DestinationPath $fullArchivePath -CompressionLevel Optimal -Force
}

# These are installation-specific data. They must never be placed in an update ZIP.
$protectedTopLevelFiles = @(".env", "config.json", "gift_queue.json", "gift_queue.json.tmp", "gift_queue.json.journal", ".streamloots_bridge_key")
$changedCount = 0
foreach ($sourceFile in Get-ChildItem -LiteralPath $publishRoot -File -Recurse) {
    $relativePath = $sourceFile.FullName.Substring($publishRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $relativePath = $relativePath.Replace([IO.Path]::DirectorySeparatorChar, "/")
    if ($relativePath -notmatch "/" -and $protectedTopLevelFiles -contains $relativePath) { continue }

    $baselineFile = Join-Path $baselineRoot ($relativePath.Replace("/", [IO.Path]::DirectorySeparatorChar))
    $isChanged = -not (Test-Path -LiteralPath $baselineFile)
    if (-not $isChanged) {
        $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
        $baselineHash = (Get-FileHash -LiteralPath $baselineFile -Algorithm SHA256).Hash
        $isChanged = $sourceHash -ne $baselineHash
    }
    if (-not $isChanged) { continue }

    $payloadFile = Join-Path $payloadRoot ($relativePath.Replace("/", [IO.Path]::DirectorySeparatorChar))
    $payloadDirectory = Split-Path -Parent $payloadFile
    New-Item -ItemType Directory -Path $payloadDirectory -Force | Out-Null
    Copy-Item -LiteralPath $sourceFile.FullName -Destination $payloadFile -Force
    $changedCount++
}

if ($changedCount -eq 0) { throw "No files differ from StreamerShare. The update package would be empty." }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Apply-Update.ps1") -Destination (Join-Path $packageRoot "Apply-Update.ps1") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Apply-Update.cmd") -Destination (Join-Path $packageRoot "Apply-Update.cmd") -Force
$readme = @"
STREAMLOOTS GIFTER UPDATE $Version

1. Close Streamloots Gifter.
2. Extract this ZIP to a temporary folder.
3. Double-click Apply-Update.cmd.
4. Select the existing Streamloots Gifter folder (the one containing StreamlootsGifter.exe).
5. After the update finishes, reload the unpacked extension at chrome://extensions and refresh the Streamloots Activity Feed tab.

The updater backs up changed application files and keeps your local config.json, gift_queue.json, and .streamloots_bridge_key in place. Twitch tokens are stored separately, encrypted for your Windows account.
Keep the update ZIP until you have opened the app and confirmed the update worked.
"@
Set-Content -LiteralPath (Join-Path $packageRoot "UPDATE_README.txt") -Value $readme -Encoding UTF8

$archivePath = Join-Path $distRoot "StreamlootsGifter-Update-v$Version-$stamp.zip"
Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $archivePath -CompressionLevel Optimal

if ([string]::IsNullOrWhiteSpace($SigningKeyPath)) {
    $SigningKeyPath = $env:STREAMLOOTS_UPDATE_SIGNING_KEY
}
if ([string]::IsNullOrWhiteSpace($SigningKeyPath)) {
    $SigningKeyPath = Join-Path $projectRoot ".update-signing-private.pem"
}
if (-not (Test-Path -LiteralPath $SigningKeyPath -PathType Leaf)) {
    throw "Update signing key not found. Keep the private PEM key out of GitHub and pass -SigningKeyPath or set STREAMLOOTS_UPDATE_SIGNING_KEY."
}
$pinnedPublicKeyPath = Join-Path $PSScriptRoot "update_signing_public.pem"
if (-not (Test-Path -LiteralPath $pinnedPublicKeyPath -PathType Leaf)) {
    throw "Pinned update verification key not found: $pinnedPublicKeyPath"
}
$signingTool = Join-Path $PSScriptRoot "UpdateSigningTool\UpdateSigningTool.csproj"
if (-not (Test-Path -LiteralPath $signingTool -PathType Leaf)) { throw "Update signing helper not found: $signingTool" }
& dotnet run --project $signingTool --configuration Release --no-launch-profile -- $archivePath $SigningKeyPath $pinnedPublicKeyPath
if ($LASTEXITCODE -ne 0) { throw "Update signing failed with exit code $LASTEXITCODE." }

Write-Host "Update package created: $archivePath"
Write-Host "Publisher signature created: $archivePath.sig (upload both assets to the GitHub release)"
if ($fullArchivePath) { Write-Host "First-download package created: $fullArchivePath" }
Write-Host "Changed payload files: $changedCount"
Write-Host "The update package excludes Twitch tokens, settings, claim history, queue, and Bridge key."
if ($IncludeFullDownload) { Write-Host "The full download package contains no developer or streamer credentials." }

$resolvedDistRoot = [IO.Path]::GetFullPath($distRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedWorkRoot = [IO.Path]::GetFullPath($workRoot)
if ($resolvedWorkRoot.StartsWith($resolvedDistRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolvedWorkRoot).StartsWith("update-build-")) {
    Remove-Item -LiteralPath $resolvedWorkRoot -Recurse -Force
}
