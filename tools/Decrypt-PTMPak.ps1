#requires -version 5.1
<#
.SYNOPSIS
  Decifra uma cópia de pacote Urho3D com a chave XOR PTM.
.DESCRIPTION
  Não altera a origem, não substitui o destino e exige que o cabeçalho
  decifrado seja UPAK ou ULZ4. A chave XOR não é senha do arquivo RAR.
.EXAMPLE
  .\Decrypt-PTMPak.ps1 -InputFile "C:\PristonM-Lab\extracted\data.pak"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$InputFile,
    [string]$OutputFile
)
$ErrorActionPreference='Stop'
$source=(Resolve-Path -LiteralPath $InputFile).Path
if (-not $OutputFile) { $OutputFile="$source.decrypted.pak" }
if ([System.IO.Path]::GetFullPath($source) -eq [System.IO.Path]::GetFullPath($OutputFile)) {
    throw 'O destino deve ser diferente da origem.'
}
if (Test-Path -LiteralPath $OutputFile) { throw "Destino existente: $OutputFile" }
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
public static class PTMDecrypt
{
    public static long Decode(string input, string output)
    {
        byte[] key={0xDE,0xAD,0xFA,0xCE};
        using(var src=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            if(src.Length<4) throw new InvalidDataException("Arquivo curto");
            byte[] prefix=new byte[4];
            if(src.Read(prefix,0,4)!=4) throw new EndOfStreamException();
            string original=Encoding.ASCII.GetString(prefix);
            if(original=="UPAK" || original=="ULZ4")
                throw new InvalidDataException("Ja e um pacote Urho legivel. Nao decifrar.");
            for(int i=0;i<4;i++) prefix[i]^=key[i];
            string magic=Encoding.ASCII.GetString(prefix);
            if(magic!="UPAK" && magic!="ULZ4")
                throw new InvalidDataException("Cabecalho decifrado nao e UPAK ou ULZ4. Confira a origem e o algoritmo.");
            src.Position=0;
            byte[] buffer=new byte[1024*1024];
            long offset=0;
            using(var dst=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                int count;
                while((count=src.Read(buffer,0,buffer.Length))>0)
                {
                    for(int i=0;i<count;i++) buffer[i]^=key[(int)((offset+i)%4)];
                    dst.Write(buffer,0,count);
                    offset+=count;
                }
                dst.Flush(true);
            }
            return offset;
        }
    }
}
'@
$bytes=[PTMDecrypt]::Decode($source,[System.IO.Path]::GetFullPath($OutputFile))
Write-Output "Decifrado: $bytes bytes -> $OutputFile"
