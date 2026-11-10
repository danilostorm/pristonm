#requires -version 5.1
<#
.SYNOPSIS
  Confere, SOMENTE EM LEITURA, pontos de observacao do Server33 LAB.
.DESCRIPTION
  Nao injeta DLL, nao altera binario ou fonte, nao le contas reais
  e nao cria arquivo no LAB. Verifica apenas a existencia e o hash
  dos pontos de instrumentacao candidatos.
#>
[CmdletBinding()]
param(
    [string]$SourceRoot = 'C:\Elemental-Source\Source\SrcServer\src'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $SourceRoot).Path
$server = Join-Path $root 'Server\OnSever.cpp'
$record = Join-Path $root 'record.cpp'
$serverNet = Join-Path $root 'netplay.cpp'
foreach($file in @($server,$record,$serverNet)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){
        throw 'Fonte do LAB nao localizada: ' + [IO.Path]::GetFileName($file)
    }
}

function Check-Source([string]$Path,[string]$Pattern,[string]$Check){
    $raw = [IO.File]::ReadAllText($Path)
    $match = [regex]::Match($raw,$Pattern)
    return [pscustomobject]@{
        id = $Check
        exists = $match.Success
        source_line = if($match.Success){
            @($raw.Substring(0,$match.Index).Split([char]10)).Count
        }else{$null}
        source_file = [IO.Path]::GetFileName($Path)
    }
}

$checks = @(
    (Check-Source $server '(?m)^\s*DWORD\s+GetNewObjectSerial\s*\(' 'native_serial_generator'),
    (Check-Source $server '(?m)^\s*int\s+DisconnectUser\s*\(smWINSOCK\s*\*lpsmSock\)' 'socket_disconnect_handler'),
    (Check-Source $server 'tpInfo->getAccount\(\)\.c_str\(\)' 'server_account_field_present'),
    (Check-Source $server 'tpInfo->getName\(\)\.c_str\(\)' 'server_character_field_present'),
    (Check-Source $server 'lpsmSock->ExtData1' 'server_session_socket_binding'),
    (Check-Source $record 'lpTransRecord->smCharInfo\.dwObjectSerial\s*=\s*GetNewObjectSerial\(\)' 'login_character_serial_generation'),
    (Check-Source $serverNet 'TransPlayerInfo\.dwObjectSerial\s*=\s*lpCurPlayer->dwObjectSerial' 'player_serial_network_broadcast')
)
$free = [math]::Round((Get-PSDrive C).Free/1MB,0)
$all = @($checks | Where-Object {-not $_.exists}).Count -eq 0
$report = [ordered]@{
    target = 'StormPT LAB source ONLY'
    evidence = 'Source patterns, not runtime hooks'
    read_only = $true
    binary_patched = $false
    event_emitter_installed = $false
    passwords_read = 0
    player_rows_read = 0
    source_hashes = [ordered]@{
        OnSever_cpp_SHA256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $server).Hash
        record_cpp_SHA256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $record).Hash
        netplay_cpp_SHA256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $serverNet).Hash
    }
    candidate_hooks = $checks
    all_source_patterns_found = $all
    c_free_megabytes = $free
    safe_to_install_on_full_lab_disk = $false
    status = if($all){'SOURCE_PATTERNS_FOUND_ONLY'}else{'NEEDS_REVIEW'}
}
$report|ConvertTo-Json -Depth 6
if(-not $all){exit 3}
