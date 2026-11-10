#requires -version 5.1
<#
.SYNOPSIS
  Remonta um arquivo APK de pesquisa a partir da pasta main_install extraida do PTM.
.DESCRIPTION
  Produz um ZIP com estrutura Android. NAO assina nem garante instalacao.
  Nunca modifica o cliente original ou a pasta do servidor StormPT.
.EXAMPLE
  .\Build-Mobile-APK-Structure.ps1 -SourceDir 'C:\PristonM-Lab\clients\Client_PTMobile\main_install'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SourceDir,
    [string]$BuildDir = 'C:\PristonM-Lab\build',
    [string]$SevenZip = 'C:\Program Files\7-Zip\7z.exe'
)
$ErrorActionPreference = 'Stop'
$src = (Resolve-Path -LiteralPath $SourceDir).Path
foreach ($relative in @('AndroidManifest.xml', 'classes.dex', 'resources.arsc', 'lib\armeabi-v7a\libgame.so')) {
    if (-not (Test-Path -LiteralPath (Join-Path $src $relative))) {
        throw "Falta componente obrigatorio: $relative"
    }
}
if (-not (Test-Path -LiteralPath $SevenZip)) { throw "7-Zip nao encontrado: $SevenZip" }
New-Item -ItemType Directory -Path $BuildDir -Force | Out-Null
$apk = Join-Path $BuildDir 'StormPT-Mobile-repacked-unverified.apk'
if (Test-Path -LiteralPath $apk) { throw "Arquivo ja existe. Sem sobrescrita: $apk" }

Push-Location $src
try {
    & $SevenZip a '-tzip' '-mx=0' '-y' '-bso0' '-bsp0' $apk '.\*'
    if ($LASTEXITCODE -ne 0) { throw "Falha ao empacotar (codigo $LASTEXITCODE)." }
} finally { Pop-Location }

& $SevenZip t '-bso0' '-bsp0' $apk
if ($LASTEXITCODE -ne 0) { throw "Falha no teste do arquivo ZIP (codigo $LASTEXITCODE)." }
$f = Get-Item -LiteralPath $apk
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $apk).Hash
Write-Output "APK_STRUCTURE=$($f.FullName)"
Write-Output "BYTES=$($f.Length)"
Write-Output "SHA256=$hash"
Write-Output 'STATUS=ARQUIVO_REEMPACOTADO_NAO_ASSINADO_NAO_VALIDADO_NO_ANDROID'
Write-Output 'Para testar no Android: validar com zipalign/apksigner (Android SDK oficial) e um dispositivo/emulador.'
