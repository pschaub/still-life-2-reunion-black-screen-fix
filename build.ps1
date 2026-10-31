$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = '0'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build-home'
foreach ($project in @('src/App/StillLife2Patcher.csproj', 'src/Checks/StillLife2Patcher.Checks.csproj')) {
    dotnet restore (Join-Path $PSScriptRoot $project) --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed. The .NET 10 SDK and Windows Desktop reference packs are required.' }
    dotnet build (Join-Path $PSScriptRoot $project) -c Release --no-restore -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
dotnet publish (Join-Path $PSScriptRoot 'src/App/StillLife2Patcher.csproj') -c Release --no-restore --self-contained false -o (Join-Path $PSScriptRoot 'dist') -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
