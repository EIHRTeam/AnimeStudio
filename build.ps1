$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$configuration = 'Release'

dotnet build AnimeStudio.Patcher -c $configuration -f net9.0
if ($LASTEXITCODE -ne 0) {
    throw "Failed to build AnimeStudio.Patcher."
}

$patcher = "AnimeStudio.Patcher\bin\$configuration\net9.0\AnimeStudio.Patcher.exe"

foreach ($tfm in 'net8.0-windows', 'net9.0-windows') {
    $outputDir = ".\dist\$tfm"
    $guiOut = "AnimeStudio.GUI\bin\$configuration\$tfm"
    $guiExe = "$guiOut\AnimeStudio.GUI.exe"

    dotnet build AnimeStudio.GUI -c $configuration -f $tfm
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build AnimeStudio.GUI for $tfm."
    }

    & $patcher $guiExe -d bin
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to patch AnimeStudio.GUI for $tfm."
    }

    if (Test-Path $outputDir) {
        Remove-Item $outputDir -Recurse -Force
    }

    New-Item -ItemType Directory "$outputDir\bin" | Out-Null
    Copy-Item "$guiOut\*" "$outputDir\bin" -Recurse -Force
    Move-Item "$outputDir\bin\AnimeStudio.GUI.exe" $outputDir
    Move-Item "$outputDir\bin\LICENSE" $outputDir
}
