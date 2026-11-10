#requires -version 5.1
<#
.SYNOPSIS
  Testes offline de NFMsg.ReqRoleList. Dados sintéticos; zero rede.
#>
[CmdletBinding()]
param([string]$BridgeDir = "$PSScriptRoot\..\bridge")
$ErrorActionPreference = 'Stop'
Add-Type -Path @(
    (Resolve-Path -LiteralPath (Join-Path $BridgeDir 'MobileLoginProbe.cs')).Path,
    (Resolve-Path -LiteralPath (Join-Path $BridgeDir 'MobileRoleListProbe.cs')).Path
)
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw "FALHOU: $Message"}}
function Reject([byte[]]$Data,[string]$Case){
    $rejected=$false
    try{[StormPT.MobileLab.MobileRoleListProbe]::Inspect($Data)|Out-Null}catch{$rejected=$true}
    Assert $rejected $Case
}
# game_id=1, account='TEST1', account_type='QA' (campos 1, 2, 3).
[byte[]]$message=@(0x08,0x01,0x12,0x05,0x54,0x45,0x53,0x54,0x31,0x1A,0x02,0x51,0x41)
$r=[StormPT.MobileLab.MobileRoleListProbe]::Inspect($message)
Assert ($r.GameId -eq 1) 'game_id'
Assert ($r.ClaimedAccount -eq 'TEST1') 'account'
Assert ($r.HasAccountType) 'account_type'
Reject ([byte[]]@(0x12,0x01,0x41)) 'missing game id'
Reject ([byte[]]@(0x08,0x01)) 'missing account'
Reject ([byte[]]@(0x08,0x00,0x12,0x01,0x41)) 'game id zero'
Reject ([byte[]]@(0x08,0x01,0x12,0x05,0x54)) 'truncated account'
Reject ([byte[]]@(0x08,0x01,0x12,0x01,0x41,0x12,0x01,0x42)) 'duplicate account'
Reject ([byte[]]@(0x08,0x01,0x12,0x01,0x41,0x0F)) 'unsupported wire'
Write-Output 'PASS: 3 verificacoes positivas de ReqRoleList'
Write-Output 'PASS: 6 payloads invalidos rejeitados'
Write-Output 'AVISO: claimedAccount deve ser comparado com a conta da sessao autenticada pelo servidor'
