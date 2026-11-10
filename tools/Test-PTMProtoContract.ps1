#requires -version 5.1
<#
.SYNOPSIS
  Confere campos usados pelos inspetores contra os .proto locais extraidos.
  Nao le credenciais, arquivos de contas nem faz conexoes externas.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ProtoFile,
    [string]$ReportPath
)
$ErrorActionPreference = 'Stop'
$f=(Resolve-Path -LiteralPath $ProtoFile).Path
$raw=[IO.File]::ReadAllText($f)
function Get-Fields([string]$MessageName){
    $pattern='(?ms)^\s*message\s+' + [regex]::Escape($MessageName) + '\b[^{]*\{(?<body>.*?)(?=^\s*\}\s*$)'
    $m=[regex]::Match($raw,$pattern)
    if(-not $m.Success){throw "Mensagem ausente: $MessageName"}
    $fields=@{}
    foreach($f in [regex]::Matches($m.Groups['body'].Value,'(?m)^\s*(?:required|optional|repeated)\s+[\w.]+\s+(\w+)\s*=\s*(\d+)\s*(?:;|\[)')){
        $fields[$f.Groups[1].Value]=[int]::Parse($f.Groups[2].Value)
    }
    return $fields
}
function Assert-Field($fields,[string]$name,[int]$number,[string]$message){
    if(-not $fields.ContainsKey($name) -or $fields[$name] -ne $number){
        throw "Schema divergente ${message}.${name}: esperado $number"
    }
}
$login=Get-Fields 'ReqAccountLogin'
$role=Get-Fields 'ReqRoleList'
$roleList=Get-Fields 'AckRoleLiteInfoList'
Assert-Field $login 'account' 2 'ReqAccountLogin'
Assert-Field $login 'password' 3 'ReqAccountLogin'
Assert-Field $login 'clientVersion' 6 'ReqAccountLogin'
Assert-Field $role 'game_id' 1 'ReqRoleList'
Assert-Field $role 'account' 2 'ReqRoleList'
Assert-Field $role 'account_type' 3 'ReqRoleList'
Assert-Field $roleList 'char_data' 1 'AckRoleLiteInfoList'
$summary=[ordered]@{
    proto_file = $f
    proto_sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $f).Hash
    login_fields=@{ account=2; password=3; clientVersion=6 }
    role_list_request_fields=@{ game_id=1; account=2; account_type=3 }
    role_list_response_fields=@{ char_data=1 }
    verified_fields_count=7
    network_requests=0
    secrets_read=0
    result='PASS'
}
if($ReportPath){
    $output=[IO.Path]::GetFullPath($ReportPath)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output)|Out-Null
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $output -Encoding UTF8
}
Write-Output 'PASS: ReqAccountLogin.account/password/clientVersion'
Write-Output 'PASS: ReqRoleList.game_id/account/account_type'
Write-Output 'PASS: AckRoleLiteInfoList.char_data'
Write-Output 'PASS: 7 campos conferidos com arquivo .proto local'
