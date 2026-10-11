param([switch]$FrameworkDependent, [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64', [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hardwareRelativePath = 'Assets\Hardware\PawnIO\PawnIO_setup.exe'
$hardwareSourcePath = Join-Path $projectRoot (Join-Path 'src\WalleFences.App' $hardwareRelativePath)
$hardwareManifestPath = Join-Path $projectRoot 'src\WalleFences.App\Assets\Hardware\PawnIO\manifest.json'
$hardwareExpectedHash = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
if (!(Test-Path -LiteralPath $hardwareSourcePath -PathType Leaf) -or !(Test-Path -LiteralPath $hardwareManifestPath -PathType Leaf)) {
    throw 'В проекте отсутствует офлайн-комплект PawnIO. Восстановите Assets\Hardware\PawnIO из исходников: сборка драйверы не скачивает.'
}
if ((Get-FileHash -LiteralPath $hardwareSourcePath -Algorithm SHA256).Hash -ne $hardwareExpectedHash) {
    throw 'Установщик PawnIO отличается от закреплённого официального выпуска 2.2.0. Сборка остановлена.'
}
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
Push-Location -LiteralPath $projectRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
    $outputFolder = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory, $projectRoot) } elseif ($Runtime -eq 'win-x64') { Join-Path $projectRoot 'dist\WalleFences' } else { Join-Path $projectRoot 'dist\WalleFences-arm64' }
    & $dotnetCommand publish 'src\WalleFences.App\WalleFences.App.csproj' -c Release -r $Runtime --self-contained $selfContained -p:PublishSingleFile=true -p:DebugType=None -o $outputFolder --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Сборка не выполнена.' }
    $hardwarePublishedPath = Join-Path $outputFolder $hardwareRelativePath
    $requiredPackageFiles = @(
        $hardwareRelativePath,
        'Assets\Hardware\PawnIO\manifest.json',
        'Assets\Licenses\PawnIO-LICENSE.txt',
        'Assets\Licenses\PawnIO-NOTICES.txt',
        'LibreHardwareMonitorLib.dll'
    )
    foreach ($packageRelativePath in $requiredPackageFiles) {
        if (!(Test-Path -LiteralPath (Join-Path $outputFolder $packageRelativePath) -PathType Leaf)) {
            throw "В релизе отсутствует обязательный компонент датчиков: $packageRelativePath"
        }
    }
    if ((Get-FileHash -LiteralPath $hardwarePublishedPath -Algorithm SHA256).Hash -ne $hardwareExpectedHash) {
        throw 'Офлайн-установщик PawnIO в релизе повреждён или изменён.'
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'START-HERE.txt') -Destination (Join-Path $outputFolder 'START-HERE.txt') -Force
    Get-Item -LiteralPath (Join-Path $outputFolder 'WalleFences.exe') | Select-Object FullName,Length
} finally { Pop-Location }
