<#
  Makes the installer and the update files of the desktop program (Velopack):

      .\installer\build.ps1 -Version 1.0.0

  In artifacts\releases:
    - MahalaCNC-win-Setup.exe     what users download and run (installs without an administrator, .NET inside);
    - MahalaCNC-win-Portable.zip  the same without installing;
    - releases.win.json, *.nupkg  the update files: upload them (with Setup.exe) to the update address,
                                  AppSettings.UpdateUrl (https://mahalaworkshop.com/cnc/releases), and installed
                                  programs offer the new version in the bar at the bottom of the page.
  Every version needs a higher number than the last one uploaded.
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$UpdateUrl = "https://mahalaworkshop.com/cnc/releases"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root "artifacts\publish"
$releases = Join-Path $root "artifacts\releases"

Push-Location $root
try {
    # vpk is a tool of this repo (dotnet-tools.json), the same version as the Velopack package.
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed." }

    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    # Self-contained: .NET (with ASP.NET Core for the local network page) goes with the program.
    dotnet publish src\CncController.WinForms -c Release -r win-x64 --self-contained -p:Version=$Version -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

    # The versions already uploaded, so the new one also comes as a small delta. None yet: nothing to download.
    dotnet vpk download http --url $UpdateUrl --outputDir $releases --channel win --allowEmptyChannel
    if ($LASTEXITCODE -ne 0) { Write-Warning "Earlier versions not downloaded from $UpdateUrl; the update will be the full package." }

    dotnet vpk pack --packId MahalaCNC --packVersion $Version --runtime win-x64 --packDir $publish --mainExe CncController.WinForms.exe `
        --packTitle "The Mahala Workshop - CNC Controller" --packAuthors "The Mahala Workshop" `
        --icon src\CncController.WinForms\app.ico --outputDir $releases
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed." }

    Write-Host ""
    Write-Host "Done: $releases"
}
finally {
    Pop-Location
}
