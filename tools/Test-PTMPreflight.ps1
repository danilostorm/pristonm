#requires -version 5.1
<#
.SYNOPSIS
  Inventario somente-leitura de uma arvore Android do PTM para QA.
.DESCRIPTION
  Nao executa binarios, nao faz conexoes externas e nao altera o cliente.
  Grava um relatorio JSON separado da arvore do cliente.
.EXAMPLE
  .\Test-PTMPreflight.ps1 -SourceDir 'C:\PristonM-Lab\clients\Client_PTMobile\main_install' -ReportPath 'C:\PristonM-Lab\reports\ptm-preflight.json'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SourceDir,
    [Parameter(Mandatory=$true)][string]$ReportPath
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $SourceDir).Path
$reportFull = [IO.Path]::GetFullPath($ReportPath)
if ($reportFull.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'O relatorio nao pode ser salvo dentro da arvore do cliente.'
}
$required = @('AndroidManifest.xml','classes.dex','resources.arsc','lib\armeabi-v7a\libgame.so')
$found = @{}
foreach($name in $required) { $found[$name] = Test-Path -LiteralPath (Join-Path $root $name) }
$compiledPath = Join-Path $root 'assets\Scripts\PTGame.as'
$magic = $null
if(Test-Path -LiteralPath $compiledPath) {
    $fs = [IO.File]::OpenRead($compiledPath)
    try {
        $bytes = New-Object byte[] 4
        if($fs.Read($bytes,0,4) -eq 4) { $magic = [Text.Encoding]::ASCII.GetString($bytes) }
    } finally { $fs.Dispose() }
}
$manifestPath = Join-Path $root 'META-INF\MANIFEST.MF'
$pakRefs = @()
if(Test-Path -LiteralPath $manifestPath) {
    # Extensoes .pak devem ser parte da linha Name no manifesto JAR.
    $manifest = [IO.File]::ReadAllText($manifestPath)
    $matches = [regex]::Matches($manifest, '(?m)^Name:\s*([^\r\n]+\.pak)\s*$')
    foreach($m in $matches) { $pakRefs += $m.Groups[1].Value.Trim() }
}
$pakRefs = @($pakRefs | Sort-Object -Unique)
$missing = @()
foreach($path in $pakRefs) {
    $normalized = $path.Replace('/', '\')
    if($normalized -match '^\w:|(^|\\)\.\.(\\|$)' -or $normalized.StartsWith('\')) {
        throw "Caminho suspeito no manifesto; abortando leitura: $path"
    }
    if(-not (Test-Path -LiteralPath (Join-Path $root $normalized))) { $missing += $path }
}
$actualPaks = @(Get-ChildItem -LiteralPath (Join-Path $root 'assets') -Filter '*.pak' -Recurse -File -ErrorAction SilentlyContinue)
$protoRoot = Join-Path $root 'assets\NFDataCfg\ProtoFile'
$protoNames = @()
if(Test-Path -LiteralPath $protoRoot) {
    foreach($proto in @(Get-ChildItem -LiteralPath $protoRoot -Filter '*.proto' -File)) {
        $raw = [IO.File]::ReadAllText($proto.FullName)
        foreach($m in [regex]::Matches($raw,'(?m)^\s*message\s+(\w+)\s*(?:\/\/[^\r\n]*)?$')) {
            $protoNames += $m.Groups[1].Value
        }
    }
}
$protoNames = @($protoNames | Sort-Object -Unique)
$conf = Join-Path $root 'assets\sdkconf.ini'
$legacyHosts = @()
if(Test-Path -LiteralPath $conf) {
    foreach($line in [IO.File]::ReadAllLines($conf)) {
        if($line -match '^\s*(?:SERVER_URL|CDN_SERVER_URL)\s*=\s*(\S+)') {
            $uri = $null
            if([Uri]::TryCreate($Matches[1],[UriKind]::Absolute,[ref]$uri)) { $legacyHosts += $uri.Host }
        }
    }
}
$legacyHosts = @($legacyHosts | Sort-Object -Unique)
$warnings = @()
if($magic -eq 'ASBC') { $warnings += 'PTGame.as e bytecode AngelScript (ASBC), nao texto-fonte editavel.' }
if($missing.Count -gt 0) { $warnings += 'O manifesto de assinatura original referencia pacotes .pak ausentes.' }
if($legacyHosts.Count -gt 0) { $warnings += 'SDK legado aponta para servico de terceiros. Bloquear conexao antes de testes de execucao.' }
if($actualPaks.Count -eq 0) { $warnings += 'Nao ha arquivos .pak nesta arvore de assets.' }
if(@($found.Values | Where-Object { -not $_ }).Count -gt 0) { $warnings += 'Faltam componentes Android obrigatorios.' }
$report = [ordered]@{
    generated_utc = [DateTime]::UtcNow.ToString('o')
    target = 'PTM Android LAB - inventory only'
    source_directory = $root
    android_required_present = $found
    ptgame_header = $magic
    ptgame_compiled_angelscript = ($magic -eq 'ASBC')
    original_manifest_pak_references_count = $pakRefs.Count
    original_manifest_missing_paks_count = $missing.Count
    original_manifest_missing_paks = $missing
    assets_pak_files_count = $actualPaks.Count
    proto_message_names_count = $protoNames.Count
    proto_has_account_login = ($protoNames -contains 'ReqAccountLogin')
    proto_has_character_list = ($protoNames -contains 'ReqRoleList')
    old_sdk_domains = $legacyHosts
    warnings = $warnings
    action = 'NENHUM executavel foi iniciado; nenhum endpoint foi acessado.'
}
$parent = Split-Path -Parent $reportFull
if(-not (Test-Path -LiteralPath $parent)){ New-Item -ItemType Directory -Force -Path $parent | Out-Null }
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportFull -Encoding UTF8
Write-Output "REPORT=$reportFull"
Write-Output "ANGELSCRIPT_COMPILED=$($report.ptgame_compiled_angelscript)"
Write-Output "MISSING_PAKS=$($missing.Count)"
Write-Output "ACTUAL_PAKS=$($actualPaks.Count)"
Write-Output "PROTO_MESSAGES=$($protoNames.Count)"
Write-Output "HAS_LOGIN=$($report.proto_has_account_login)"
Write-Output "LEGACY_SDK_HOST_COUNT=$($legacyHosts.Count)"
Write-Output "WARNINGS=$($warnings.Count)"
