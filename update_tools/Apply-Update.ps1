param(
    [int]$WaitForPid = 0,
    [switch]$Restart
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms

function Show-UpdateError([string]$Message) {
    [System.Windows.Forms.MessageBox]::Show($Message, "Streamloots Gifter update", "OK", "Error") | Out-Null
}

try {
    $payloadRoot = Join-Path $PSScriptRoot "payload"
    if (-not (Test-Path -LiteralPath $payloadRoot -PathType Container)) { throw "The update package is incomplete: payload folder is missing." }

    # In-app updates provide this through the child process environment, not
    # command-line arguments. Manual ZIP installs can still choose a folder.
    $InstallRoot = $env:STREAMLOOTS_GIFTER_INSTALL_ROOT

    if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        $dialog.Description = "Select the existing Streamloots Gifter folder containing StreamlootsGifter.exe."
        $dialog.ShowNewFolderButton = $false
        if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { exit 2 }
        $InstallRoot = $dialog.SelectedPath
    }

    if (-not [IO.Path]::IsPathRooted($InstallRoot)) { throw "The install folder path must be absolute." }
    $InstallRoot = (Get-Item -LiteralPath $InstallRoot -ErrorAction Stop).FullName
    if (-not (Test-Path -LiteralPath (Join-Path $InstallRoot "StreamlootsGifter.exe") -PathType Leaf)) {
        throw "That folder does not contain StreamlootsGifter.exe. Select the current installation folder."
    }
    if ($WaitForPid -gt 0) {
        $parentProcess = Get-Process -Id $WaitForPid -ErrorAction SilentlyContinue
        if ($parentProcess -and -not $parentProcess.WaitForExit(120000)) { throw "The app did not close in time. Close it and try the update again." }
    }
    $running = Get-Process -Name "StreamlootsGifter" -ErrorAction SilentlyContinue
    if ($running) { throw "Close Streamloots Gifter before applying the update, then run this updater again." }

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupRoot = Join-Path $InstallRoot ".update-backup-$stamp"
    $backupFiles = Join-Path $backupRoot "files"
    New-Item -ItemType Directory -Path $backupFiles -Force | Out-Null

    # Protect local credentials and state even if an update archive was assembled incorrectly.
    $protected = @(".env", "config.json", "gift_queue.json", "gift_queue.json.tmp", ".streamloots_bridge_key")
    foreach ($name in $protected) {
        $source = Join-Path $InstallRoot $name
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $backupRoot $name) -Force
        }
    }

    $filesToCopy = @(Get-ChildItem -LiteralPath $payloadRoot -File -Recurse)
    if ($filesToCopy.Count -eq 0) { throw "The update package contains no application files." }
    $applied = New-Object System.Collections.Generic.List[string]
    try {
        foreach ($file in $filesToCopy) {
            $relative = $file.FullName.Substring($payloadRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
            $segments = @($relative -split "[\\/]")
            if ($segments.Count -eq 0 -or $segments -contains "" -or $segments -contains "." -or $segments -contains "..") {
                throw "Unsafe relative path in update package: $relative"
            }
            $invalidNameChars = [IO.Path]::GetInvalidFileNameChars()
            foreach ($segment in $segments) {
                if ($segment.IndexOfAny($invalidNameChars) -ge 0) { throw "Invalid file name in update package: $relative" }
            }
            $target = Join-Path $InstallRoot ($segments -join [IO.Path]::DirectorySeparatorChar)
            $rootPrefix = $InstallRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            if (-not $target.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe path in update package: $relative" }
            if (($relative -notmatch "[\\/]" -and $protected -contains $relative)) { throw "Update package unexpectedly includes protected user data: $relative" }

            $targetDirectory = Split-Path -Parent $target
            New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
            if (Test-Path -LiteralPath $target -PathType Leaf) {
                $backupTarget = Join-Path $backupFiles $relative
                New-Item -ItemType Directory -Path (Split-Path -Parent $backupTarget) -Force | Out-Null
                Copy-Item -LiteralPath $target -Destination $backupTarget -Force
            }
            $applied.Add($target)
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
    }
    catch {
        foreach ($target in $applied) {
            $relative = $target.Substring($InstallRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
            $previous = Join-Path $backupFiles $relative
            if (Test-Path -LiteralPath $previous -PathType Leaf) { Copy-Item -LiteralPath $previous -Destination $target -Force }
            else { Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue }
        }
        throw
    }

    [System.Windows.Forms.MessageBox]::Show("Update applied successfully.`r`n`r`nYour settings, Twitch credentials, claim history, queue, and Bridge key were preserved.`r`n`r`nIf you use a locally loaded extension, reload it in the browser's extensions page.", "Streamloots Gifter update", "OK", "Information") | Out-Null
    # Let the user read and dismiss the completion notice before the app starts;
    # otherwise the restarted window can cover the update instructions.
    if ($Restart) { Start-Process -FilePath (Join-Path $InstallRoot "StreamlootsGifter.exe") -WorkingDirectory $InstallRoot }
    exit 0
}
catch {
    $line = if ($_.InvocationInfo.ScriptLineNumber) { " (line $($_.InvocationInfo.ScriptLineNumber))" } else { "" }
    Show-UpdateError ($_.Exception.Message + $line)
    Write-Error $_
    exit 1
}
