#Requires -Version 7.0
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$rid = 'win-x64'
$publishDir = "publish/$rid"
$releasesDir = 'Releases'
$version = '1.3.0'

Write-Host "==> Limpiando $publishDir y $releasesDir"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $releasesDir) { Remove-Item $releasesDir -Recurse -Force }

Write-Host "==> Publicando $rid"
dotnet publish src/GitProfileSwitcher.Desktop -c Release -r $rid --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallo con codigo $LASTEXITCODE." }

function Resolve-Vpk {
    $onPath = Get-Command vpk -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $exe = Join-Path $env:USERPROFILE '.dotnet/tools/vpk.exe'
    if (Test-Path $exe) { return $exe }

    Write-Host "==> vpk no encontrado; instalando dotnet tool global"
    dotnet tool install --global vpk
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool install --global vpk fallo con codigo $LASTEXITCODE." }

    if (Test-Path $exe) { return $exe }
    throw "vpk se instalo pero no esta en $exe. Reinicia la terminal para refrescar PATH y vuelve a correr el script."
}

$vpk = Resolve-Vpk

Write-Host "==> Empaquetando con vpk ($vpk)"
& $vpk pack --packId GitProfileSwitcher --packVersion $version --packDir $publishDir `
    --mainExe GitProfileSwitcher.exe --packTitle "Git Profile Switcher" `
    --packAuthors "Canopus44" --runtime win-x64 --icon assets/icon.ico -o $releasesDir
if ($LASTEXITCODE -ne 0) { throw "vpk pack fallo con codigo $LASTEXITCODE." }

$setup = Join-Path $releasesDir 'Setup.exe'
$producedSetup = Get-ChildItem $releasesDir -Filter '*Setup.exe' | Select-Object -First 1
if ($null -eq $producedSetup) { throw "vpk termino pero no genero ningun *Setup.exe en $releasesDir." }
if (-not (Test-Path $setup)) { Move-Item $producedSetup.FullName $setup }

Write-Host ""
Write-Host "==> Archivos generados en ${releasesDir}:"
Get-ChildItem $releasesDir -File | Sort-Object Name | ForEach-Object {
    '{0,-48} {1,12:N2} MB' -f $_.Name, ($_.Length / 1MB)
}

Write-Host ""
Write-Host ("Setup.exe: {0:N2} MB" -f ((Get-Item $setup).Length / 1MB))
