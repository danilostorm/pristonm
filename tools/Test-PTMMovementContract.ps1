#requires -version 5.1
<#
.SYNOPSIS
  Confere o contrato de posicao do PTM contra .proto originais do LAB.
.DESCRIPTION
  Somente leitura: sem execucao dos binarios do jogo, senhas ou rede.
  Verifica os numeros/categorias dos campos, nao a semantica dos mapas.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ProtoDirectory,
    [string]$ResultPath
)
$ErrorActionPreference = 'Stop'
$dir = (Resolve-Path -LiteralPath $ProtoDirectory).Path

function Get-MessageBody([string]$FileName, [string]$MessageName) {
    $file = Join-Path $dir $FileName
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Schema nao encontrado: $FileName"
    }
    $content = [IO.File]::ReadAllText($file)
    $pattern = '(?ms)^\s*message\s+' + [regex]::Escape($MessageName) + '\s*\{(?<body>.*?)^\s*\}\s*$'
    $found = [regex]::Match($content, $pattern)
    if(-not $found.Success) {
        throw "Mensagem ausente ou invalida: $MessageName"
    }
    return $found.Groups['body'].Value
}

function Verify-Field(
    [string]$Body,
    [string]$Mode,
    [string]$Type,
    [string]$Name,
    [int]$Tag,
    [string]$Message
) {
    $pat = '(?m)^\s*' + $Mode + '\s+' + $Type + '\s+' + $Name +
        '\s*=\s*' + $Tag + '\s*;'
    if(-not [regex]::IsMatch($Body, $pat)) {
        throw ("Contrato divergente: " + $Message + "." + $Name +
               " [expected " + $Mode + " " + $Type + " #" + $Tag + "]")
    }
}

$pos = Get-MessageBody 'NFMsgBase.proto' 'Position'
Verify-Field $pos 'required' 'float' 'x' 1 'Position'
Verify-Field $pos 'required' 'float' 'y' 2 'Position'
Verify-Field $pos 'required' 'float' 'z' 3 'Position'

$move = Get-MessageBody 'NFMsgShare.proto' 'ReqAckPlayerMove'
Verify-Field $move 'required' 'Ident' 'mover' 1 'ReqAckPlayerMove'
Verify-Field $move 'required' 'int32' 'moveType' 2 'ReqAckPlayerMove'
Verify-Field $move 'repeated' 'Position' 'target_pos' 3 'ReqAckPlayerMove'
Verify-Field $move 'repeated' 'Position' 'source_pos' 4 'ReqAckPlayerMove'

$ident = Get-MessageBody 'NFMsgBase.proto' 'Ident'
Verify-Field $ident 'required' 'int64' 'svrid' 1 'Ident'
Verify-Field $ident 'required' 'int64' 'index' 2 'Ident'

$defs = [IO.File]::ReadAllText((Join-Path $dir 'NFDefine.proto'))
foreach($op in @(
    @{Name='EGMI_REQ_MOVE';Value=230},
    @{Name='EGMI_ACK_MOVE';Value=231}
)) {
    $pat = '(?m)^\s*' + $op.Name + '\s*=\s*' + $op.Value + '\s*;'
    if(-not [regex]::IsMatch($defs, $pat)) {
        throw ("Opcode Mobile diferente: " + $op.Name)
    }
}

$summary = [ordered]@{
    verified_utc = [DateTimeOffset]::UtcNow.ToString('o')
    source = 'PTM LAB extracted local proto files'
    fields_verified = 9
    move_ids_verified = 2
    proto_position = 'NFMsg.Position: required float x1 y2 z3'
    proto_move = 'NFMsg.ReqAckPlayerMove: mover Ident1; moveType int32 2; target_pos Position3; source_pos Position4'
    movement_messages = @('EGMI_REQ_MOVE=230','EGMI_ACK_MOVE=231')
    network_requests = 0
    account_rows_read = 0
    result = 'PASS'
}
if($ResultPath) {
    $target = [IO.Path]::GetFullPath($ResultPath)
    New-Item -Path (Split-Path -Parent $target) -ItemType Directory -Force | Out-Null
    $summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $target -Encoding UTF8
}
Write-Output 'PASS: NFMsg.Position float x/y/z tags 1,2,3'
Write-Output 'PASS: ReqAckPlayerMove mover, type, target_pos, source_pos tags 1..4'
Write-Output 'PASS: NFMsg.Ident server id and index int64 tags 1..2'
Write-Output 'PASS: EGMI_REQ_MOVE=230, EGMI_ACK_MOVE=231'
Write-Output 'NOTE: no online client, session, map semantics or gameplay packet generated.'
