# PristonM — laboratório de análise do PTM

Este repositório documenta a investigação dos arquivos **PTM.rar** (Priston Mobile / Urho3D). **Não há evidência ainda de que seja uma instalação completa e jogável.**

## Origem

- Arquivo: https://www.mediafire.com/file/wxs1cvqqnjec10k/PTM.rar/file
- Tamanho: `1.735.407.539` bytes
- SHA-256 confirmado na VM (igual ao hash publicado pelo MediaFire):
  `BCEC56C169AADD655BFD6BFF963FE1EB9ED81E1480C6C5071EB79E8A4D6CA0CB`
- Ferramenta de leitura de pacotes Urho: https://github.com/morsm/UrhoPackageExtract

## Ambiente isolado

A VM **Priston WEB**, no host Ziel, é usada apenas como área de trabalho. A instalação existente do Priston WEB **não deve ser alterada**.

- Arquivo original: `C:\Users\Administrador\Downloads\PTM.rar`
- Projeto: `C:\PristonM-Lab\`
- Relatórios: `C:\PristonM-Lab\reports\`
- Descompactação: `C:\PristonM-Lab\extracted\`
- Ferramentas: `C:\PristonM-Lab\tools\`

Instalado **7-Zip 26.06** a partir do domínio oficial `www.7-zip.org` para leitura do arquivo RAR. Não executar arquivos do pacote antes da análise.

## Descriptografia XOR (possível etapa após extrair o RAR)

Código informado para os recursos criptografados:

```cpp
static const std::vector<int> PTM_DECRYPTKEYS = { 0xDE, 0xAD, 0xFA, 0xCE };
for (unsigned int i = 0; i < iFileSize; i++)
    pBuffer[i] ^= PTM_DECRYPTKEYS[i % PTM_DECRYPTKEYS.size()];
```

A chave é repetida byte a byte, **não é uma senha de RAR**. Em pacotes Urho3D íntegros, o cabeçalho esperado após decifrar é `UPAK` ou `ULZ4`. Validar com amostra antes de decifrar em lote.

O extrator de `morsm` lê arquivos `.pak` Urho `UPAK` e `ULZ4`, com dependência `lz4net`; sua documentação avisa que o programa foi pouco testado. Não use diretamente sobre dados não confiáveis sem validar caminhos de saída.

## Etapas

1. Conferir hash e inventariar os arquivos do `PTM.rar`.
2. Extrair para `C:\PristonM-Lab\extracted\` **sem executar** binários.
3. Identificar executáveis, bibliotecas, modelos, texturas, scripts, pacotes Urho e eventuais arquivos de servidor.
4. Testar decifração XOR em cópias de arquivos candidatos.
5. Decidir se existe cliente/server instalável ou somente assets reaproveitáveis.
6. Registrar resultados antes de qualquer tentativa de inicialização.

**Não enviar `PTM.rar` ou assets de terceiros para este repositório público sem verificar direitos de distribuição.**
