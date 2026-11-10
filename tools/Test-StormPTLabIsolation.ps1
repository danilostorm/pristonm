#requires -version 5.1
<#
.SYNOPSIS
  Verifica se a VM StormPT LAB pode receber uma API de contas SEM acessar dados de producao.
.DESCRIPTION
  Read-only: SQL metadata only, file paths/config detection only.
  Does not query player rows, read passwords, modify settings, open ports or write files.
  Deliberately FAILS CLOSED if SQL sessions or portal paths cannot be confirmed.
#>
[CmdletBinding()]
param(
    [string]$SqlInstance = '.\SQLEXPRESS',
    [string]$ExpectedLabPrefix = 'MountLab_20261102_',
    [string]$PortalSourceRoot = 'C:\StormPTPortal\src',
    [int]$MinimumFreeMB = 2048
)
$ErrorActionPreference = 'Stop'
$issues = New-Object 'System.Collections.Generic.List[string]'
$activeDatabases = New-Object 'System.Collections.Generic.List[string]'
$portalDatabases = New-Object 'System.Collections.Generic.List[string]'
$sqlOk = $false
$portalOk = $false

try {
    $cn = New-Object System.Data.SqlClient.SqlConnection(
        "Server=$SqlInstance;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connection Timeout=5;Application Name=StormPTLAB-ReadOnlySafetyGate")
    $cn.Open()
    try {
        $q = $cn.CreateCommand()
        $q.CommandText = @'
SELECT DISTINCT DB_NAME(database_id)
FROM sys.dm_exec_sessions
WHERE is_user_process = 1
  AND program_name = N'Priston Tale'
  AND DB_NAME(database_id) IS NOT NULL
'@
        $r = $q.ExecuteReader()
        try {
            while ($r.Read()) { $activeDatabases.Add($r.GetString(0)) }
        } finally { $r.Close() }
        $sqlOk = $true
    } finally { $cn.Dispose() }
} catch {
    $issues.Add('Nao foi possivel confirmar as conexoes SQL do jogo no LAB.')
}
if (!$sqlOk -or $activeDatabases.Count -eq 0) {
    $issues.Add('Nenhuma conexao de banco do servidor Priston Tale foi confirmada.')
} else {
    foreach($db in $activeDatabases) {
        if(-not $db.StartsWith($ExpectedLabPrefix,[StringComparison]::OrdinalIgnoreCase)) {
            $issues.Add('Servidor conectado a banco sem prefixo LAB: ' + $db)
        }
    }
}

if (Test-Path -LiteralPath $PortalSourceRoot -PathType Container) {
    foreach ($filename in @('Program.cs','PlayerProfiles.cs')) {
        $path = Join-Path $PortalSourceRoot $filename
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            $issues.Add('Fonte do portal ausente: ' + $filename)
            continue
        }
        $raw = [IO.File]::ReadAllText($path)
        foreach ($m in [regex]::Matches($raw,'(?i)\bDatabase\s*=\s*([A-Za-z][A-Za-z0-9_]*)')) {
            $dbname = $m.Groups[1].Value
            if (-not $portalDatabases.Contains($dbname)) { $portalDatabases.Add($dbname) }
        }
        if ($raw.Contains('C:\Servidor\DataServer')) {
            $issues.Add('Portal usa DataServer padrao: ' + $filename)
        }
    }
    $portalOk = $portalDatabases.Count -gt 0
    if (-not $portalOk) {
        $issues.Add('Banco do portal nao foi identificado com seguranca.')
    }
    foreach ($db in $portalDatabases) {
        if (-not $db.StartsWith($ExpectedLabPrefix,[StringComparison]::OrdinalIgnoreCase)) {
            $issues.Add('Portal referencia banco sem prefixo LAB: ' + $db)
        }
    }
} else { $issues.Add('Fonte do portal nao localizada.') }

$freeMB = [math]::Round((Get-PSDrive -Name C).Free / 1MB,0)
if ($freeMB -lt $MinimumFreeMB) {
    $issues.Add('Espaco C insuficiente para instalar/compilar com seguranca.')
}

$verdict = [ordered]@{
    mode = 'READ_ONLY'
    target = 'StormPT LAB compatibility gate'
    expected_prefix = $ExpectedLabPrefix
    active_game_databases = @($activeDatabases.ToArray() | Sort-Object)
    portal_referenced_databases = @($portalDatabases.ToArray() | Sort-Object)
    c_free_megabytes = $freeMB
    minimum_free_megabytes = $MinimumFreeMB
    real_account_integration_safe = ($issues.Count -eq 0)
    blockers = @($issues.ToArray() | Sort-Object -Unique)
    credentials_examined = $false
    player_rows_read = 0
    files_modified = 0
}
$verdict | ConvertTo-Json -Depth 5
if ($issues.Count -gt 0) { exit 3 }
