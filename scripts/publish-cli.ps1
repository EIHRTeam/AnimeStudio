[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier,

    [string]$Version,

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'AnimeStudio.CLI\AnimeStudio.CLI.csproj'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts'
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $versionOutput = & dotnet msbuild $project `
        -nologo `
        -getProperty:Version `
        -p:TargetFramework=net10.0
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to read the CLI version.'
    }

    $Version = ($versionOutput | Where-Object { $_.Trim() } | Select-Object -Last 1).Trim()
}

if ([string]::IsNullOrWhiteSpace($Version) -or
    $Version.Contains('/') -or
    $Version.Contains('\')) {
    throw "Invalid package version: $Version"
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$stageDirectory = Join-Path $OutputDirectory "publish\$RuntimeIdentifier"
$packageBase = "AnimeStudio.CLI-$Version-$RuntimeIdentifier"

if (Test-Path $stageDirectory) {
    Remove-Item $stageDirectory -Recurse -Force
}
New-Item -ItemType Directory $stageDirectory -Force | Out-Null

& dotnet publish $project `
    --configuration Release `
    --framework net10.0 `
    --runtime $RuntimeIdentifier `
    --self-contained false `
    --output $stageDirectory `
    "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to publish AnimeStudio.CLI for $RuntimeIdentifier."
}

switch ($RuntimeIdentifier) {
    'win-x64' {
        $archive = Join-Path $OutputDirectory "$packageBase.zip"
        if (Test-Path $archive) {
            Remove-Item $archive -Force
        }
        Compress-Archive -Path (Join-Path $stageDirectory '*') -DestinationPath $archive
    }
    { $_ -in 'linux-x64', 'osx-arm64' } {
        $archive = Join-Path $OutputDirectory "$packageBase.tar.gz"
        if (Test-Path $archive) {
            Remove-Item $archive -Force
        }
        & tar -czf $archive -C $stageDirectory .
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create $archive."
        }
    }
}

Remove-Item $stageDirectory -Recurse -Force
Write-Output "Created $archive"
