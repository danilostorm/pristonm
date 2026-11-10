# StormPT Desktop — envelope TCP nativo (inspeção offline no LAB)

**Status em 10/11/2026:** leitor estrutural implementado, testado e **sem comunicação com servidores**. Branch de prova de conceito; não implantar em produção.

## Evidência observada no código C++

O arquivo `C:\Elemental-Source\Source\SrcServer\src\smwsock.cpp` foi inspecionado no disco da VM antiga do StormPT no Ziel, montado somente para leitura, e seus padrões de cabeçalho foram conferidos no código-fonte da VM LAB ativa no AEROCOOL (sem alterá-la).

- O recebedor lê os primeiros **8 bytes** como dois `int` de 32 bits: tamanho do pacote (incluindo cabeçalho) e código de operação (`RecvLen` e `RecvCode`). No Windows x86/x64 as representações desse código C++ são little-endian.
- `IsValidPacketHeader` exige comprimento de **8 a `smSOCKBUFF_SIZE`**, que vale **8192** no cabeçalho `smwsock.h` dessa cópia de código.
- O código de rede pode receber fragmentos TCP menores do que um pacote ou vários pacotes na mesma leitura; o leitor acumula e delimita as mensagens.
- `smPacket.h` define `smTRANS_COMMAND` com **6 campos `int` de 32 bits** (`size`, `code`, `LParam`, `WParam`, `SParam`, `EParam`), somando 24 bytes nesta estrutura de código.
- `smPacket.h` inclui o `enum OpCode`, com operações de login, posição, chat, combate e inventário. A codificação numérica e as estruturas exatas de cada opcode ainda devem ser verificadas com a configuração de build utilizada no processo em execução.
- A macro de porta condicional do cabeçalho indica 10012 para `YWEENZY_PC` e 1997 caso contrário; **não confundir essas constantes com uma porta do servidor confirmada**.
- Existem rotinas de encode/decode e referências a `OPCODE_ENCODE_PACKET`; ainda **não está provado** em quais fases da sessão e pacotes do executável em execução ocorre codificação. O leitor atual não trata criptografia, compressão nem handshake.

## Implementação

- [`bridge/DesktopWireFramer.cs`](../bridge/DesktopWireFramer.cs) — recebe `ReadOnlySpan<byte>` e retorna **apenas metadados** dos quadros, sem copiar payloads de jogadores para relatórios.
- Verifica cabeçalhos inválidos ou excessivos, suporta cabeçalhos e corpos fragmentados, e concatenação de vários pacotes no mesmo bloco TCP.
- Zera o buffer quando há erro e após o processamento de cada quadro; limita cada bloco de entrada a 64 KiB.
- Inclui `DecodeSimpleCommand`, que interpreta somente a estrutura dos seis inteiros de `smTRANS_COMMAND` **se e somente se** o opcode já tiver sido identificado por um interpretador autenticado superior. O comprimento de 24 bytes, sozinho, não prova o tipo do pacote.
- [`bridge-tests/DesktopWireFramer.Tests.csproj`](../bridge-tests/DesktopWireFramer.Tests.csproj) — 26 verificações usando bytes sintéticos: 8 e 8192 bytes, 24 bytes, endianness, fragmentação de 1 byte, concatenação de quadros, tamanho inválido, recuperação e ausência de exposição de payload.

## Resultados reproduzíveis

```bash
dotnet run --project bridge-tests/DesktopWireFramer.Tests.csproj -c Release
```

- [CI GitHub Actions: build e 26 verificações aprovados](https://github.com/danilostorm/pristonm/actions/runs/42835364605).
- Executado também na VM Windows Priston WEB do Ziel, dentro de `C:\PristonM-Lab\` com o SDK .NET 8 portátil: **26 de 26 PASS; exit code 0**.
- Nenhum pacote foi enviado para o LAB ou para a produção; nenhuma conta ou senha foi consultada.

## Comparação com o Android

O PTM usa o esquema `NFMsg` Protocol Buffers no login e na lista de personagens, não o mesmo envelope C++ do Desktop. Os testes existentes `MobileLoginProbe` e `MobileRoleListProbe` verificam somente a estrutura dessas mensagens. A rota `POST /v1/mobile/role-list` do Gateway é uma adaptação HTTP/JSON de laboratório e **não** o protocolo nativo do aplicativo Android.

## Não implementado

1. Conversão de opcode e payloads C++ para um modelo de mundo canônico.
2. Handshake, login, proteção contra replay, codificação e verificações nativas de sessão.
3. Representação de posição, entidade, mapa, ataque, itens e comandos de movimento.
4. Validação e bloqueio autoritativo de personagem no `Server33.exe` em execução.
5. Integração efetiva dos clientes Desktop, Web e Android na mesma sessão de jogo.

**Bloqueio operacional:** o LAB do AEROCOOL possui pouco espaço no C: e ainda não foi comprovado o isolamento das bases de contas com relação às cópias prefixadas, por isso nenhuma alteração de servidor é autorizada nesta etapa.