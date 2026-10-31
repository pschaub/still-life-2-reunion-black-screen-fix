param(
    [ValidatePattern('^\d{4}-\d{2}-\d{2}$')][string]$Date = (Get-Date -Format 'yyyy-MM-dd'),
    [ValidateRange(1, 65535)][int]$Build = 1
)
$ErrorActionPreference = 'Stop'
$parsedDate = [DateTime]::ParseExact($Date, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
$commit = (git -C $PSScriptRoot rev-parse --short=7 HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{7,40}$') { throw 'A committed Git checkout is required.' }
$changes = @(git -C $PSScriptRoot status --porcelain)
if ($LASTEXITCODE -ne 0 -or $changes.Count -ne 0) { throw 'Commit the source changes before building a release.' }
$releaseId = "$Date-$commit"
if ($Build -gt 1) { $releaseId += "-b$Build" }
$numericVersion = "$($parsedDate.Year).$($parsedDate.Month).$($parsedDate.Day).$Build"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = '0'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build-home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.build-packages'
$project = Join-Path $PSScriptRoot 'src/App/StillLife2Patcher.csproj'
$output = Join-Path $PSScriptRoot "artifacts/StillLife2Fix-$releaseId-win-x64"
$archive = "$output.zip"
if ((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath $archive)) { throw 'Release output already exists. Use a different build number.' }
dotnet publish $project -c Release -r win-x64 --self-contained true --configfile (Join-Path $PSScriptRoot 'NuGet.Release.Config') -o $output -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false "-p:Version=$numericVersion" "-p:FileVersion=$numericVersion" "-p:InformationalVersion=$releaseId" -p:IncludeSourceRevisionInInformationalVersion=false -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }
$readme = @'
Still Life 2 - Reunion Black Screen Fix
Build: {BUILD_ID}

Fixes the black screen when returning from the cellar to the hallway in Reunion.
Windows x64 portable app. No separate .NET installation needed.

1. Close Still Life 2 and run StillLife2Patcher.exe.
2. Choose Repair a savegame and Find my saves, or Patch the game EXE
   and select your own SL2.exe.
3. Choose Fix original with backup or Save fixed copy.
   For an untested file, choose Continue anyway in the warning dialog.
4. Start the game normally and load your save.

Choose one method:
- Savegame repair fixes one stuck save. The game EXE stays unchanged.
- The EXE patch recovers this transition when affected saves are loaded.
  Existing save files stay unchanged.

Both methods recover this one Reunion transition. The original cause of timer
loss remains unknown. Other timers and black screens are outside this fix.

Untested files are allowed when the required patch structures match.
Both methods were tested with GOG 1.0 (spanish); Steam launch is untested.
Steam EXE build stamp: Apr 6 2009 18:31:35. Its game version is unreported.

To undo, restore the .bak file under the original filename.
A save copy needs a valid slot filename in EXTDataSave to appear in-game.

Choose System, Light or Dark appearance.
Languages: english, german, french, spanish, italian, portuguese, polish,
russian, simplified chinese, japanese and korean. The app selects the Windows
language automatically and lets you change it. The fallback is english.

https://github.com/pschaub/still-life-2-reunion-black-screen-fix
'@
$readme.Replace('{BUILD_ID}', $releaseId) | Set-Content -LiteralPath (Join-Path $output 'README.txt') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $output 'LICENSE.txt')
$deps = Get-Content (Join-Path $PSScriptRoot 'src/App/bin/Release/net10.0-windows/win-x64/StillLife2Patcher.deps.json') -Raw | ConvertFrom-Json
$notices = foreach ($library in $deps.libraries.PSObject.Properties.Name) {
    if (!$library.StartsWith('runtimepack.')) { continue }
    $package, $packageVersion = $library.Substring(12).Split('/', 2)
    $packageRoot = Join-Path $env:NUGET_PACKAGES "$($package.ToLowerInvariant())/$packageVersion"
    foreach ($name in @('LICENSE', 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
        $source = Join-Path $packageRoot $name
        if (Test-Path -LiteralPath $source) { "$package $packageVersion - $name"; Get-Content -LiteralPath $source -Raw }
    }
}
if (!$notices) { throw 'Bundled runtime license notices were not found.' }
$notices -join "`r`n`r`n" | Set-Content -LiteralPath (Join-Path $output 'THIRD-PARTY-NOTICES.txt') -Encoding utf8
Compress-Archive -LiteralPath (Join-Path $output 'StillLife2Patcher.exe'),(Join-Path $output 'README.txt'),(Join-Path $output 'LICENSE.txt'),(Join-Path $output 'THIRD-PARTY-NOTICES.txt') -DestinationPath $archive -CompressionLevel Optimal
Write-Output $archive
