#requires -version 5.1
<#
.SYNOPSIS
  Testes offline do inspetor de envelope Protobuf do login Mobile.
  Nenhuma rede, nenhuma conta real.
#>
[CmdletBinding()]
param([string]$SourceFile = "$PSScriptRoot\..\bridge\MobileLoginProbe.cs")
$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $SourceFile).Path
function Assert-True([bool]$Condition, [string]$Message) {
    if(-not $Condition) { throw "FALHOU: $Message" }
}
function Expect-Failure([byte[]]$Data, [string]$Message) {
    $rejected = $false
    try { [StormPT.MobileLab.MobileLoginProbe]::Inspect($Data) | Out-Null }
    catch { $rejected = $true }
    Assert-True $rejected $Message
}
# Simula a serializacao do message NFMsg.ReqAccountLogin (somente campos 2,3,6).
# Identificador 'TEST1' e senha ficticia 'dummy' nao sao contas reais.
[byte[]]$payload = @(0x12,0x05,0x54,0x45,0x53,0x54,0x31,0x1A,0x05,0x64,0x75,0x6D,0x6D,0x79,0x30,0x7B)
$result = [StormPT.MobileLab.MobileLoginProbe]::Inspect($payload)
Assert-True ($result.Account -eq 'TEST1') 'campo 2 / account'
Assert-True $result.HasPasswordField 'campo 3 / password (presenca somente)'
Assert-True ($result.ClientVersion -eq 123) 'campo 6 / clientVersion'
Assert-True ($result.ParsedFields -eq 3) 'contagem de campos'
Expect-Failure ([byte[]]@(0x12,0x02,0x41)) 'campo de tamanho truncado'
Expect-Failure ([byte[]]@(0x12,0x01,0x41,0x30,0x01)) 'senha ausente'
Expect-Failure ([byte[]]@(0x12,0x01,0x41,0x12,0x01,0x42,0x1A,0x01,0x78)) 'usuario duplicado'
Expect-Failure ([byte[]]@(0x12,0x01,0x21,0x1A,0x01,0x78)) 'usuario com caractere invalido'
Expect-Failure ([byte[]]@(0x00,0x1A,0x01,0x78)) 'numero de campo zero'
Expect-Failure ([byte[]]@(0xFF)) 'varint incompleto'
Write-Output 'PASS: 4 testes positivos de campo'
Write-Output 'PASS: 6 casos negativos corretamente rejeitados'
Write-Output 'PASS: analise de envelope totalmente offline'
Write-Output 'AVISO: este componente NAO autentica nem implementa protocolo completo'
