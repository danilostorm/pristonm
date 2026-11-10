# StormPT — identidade de entidades e movimento Mobile/ Desktop

**10/11/2026 — laboratório, sem integração online.** Desenvolvimento na branch `lab/desktop-wire-framing-20261110`; PR #1 permanece em rascunho.

## Identificadores: por que não converter um número no outro

Inspeção somente-leitura do código `Elemental-Source/Source/SrcServer/src/Server/OnSever.cpp` no disco da VM antiga no Ziel:

```cpp
DWORD ObjectSerialCnt = 0x2000;
DWORD GetNewObjectSerial() {
    DWORD code = (ObjectSerialCnt & 0x0FFFFFFF) | (rsServerConfig.ServerCode << 28);
    ObjectSerialCnt++;
    return code;
}
```

Essa rotina gera um **identificador de entidade da instância**, não uma chave permanente da conta. O mesmo código é utilizado para entidades como NPCs/monstros; o contador pode reiniciar e reutilizar valores. Não usar `dwObjectSerial` como chave única global, nem como identidade de jogador ou item persistido.

Já o PTM usa `NFMsg.Ident`, declarado em `NFMsgBase.proto` com `required int64 svrid = 1` e `required int64 index = 2`. **Não há correspondência numérica comprovada** entre `Ident` e `dwObjectSerial`.

## Registro de identidade offline

[`bridge/LabIdentityCrosswalk.cs`](../bridge/LabIdentityCrosswalk.cs) mantém pares de **identidades verificadas pelo servidor** em memória, como `ServerEpoch + nativeObjectSerial ⇔ (svrid,index)`, associados à identidade da sessão. Este registro:

- só permite uma associação por sessão, por número nativo e por par `(svrid,index)`;
- não considera o conhecimento de um número suficiente para acessar outra entidade; a busca exige também sessão e época da instância;
- rejeita conflitos, campos inválidos e sessões com tamanho excessivo;
- usa no máximo 1.024 associações (limite defensivo do protótipo);
- limpa a associação ao desconectar/revogar a sessão e invalida todos os IDs em cada reinício do servidor.

**Aviso crítico:** `BindVerified` é chamado apenas pelas rotinas de teste. Ainda falta uma fonte real de eventos do servidor que confirme conta, personagem, sessão, spawn e IDs. Um cliente Web ou Mobile nunca deve poder chamar diretamente essa função nem definir IDs para si. O registro não autentica o jogador, não controla o servidor e não garante uma relação real entre os dois jogos.

## Mensagens de movimentação Mobile

Os `.proto` originais extraídos no Ziel indicam:

| Campo | `NFMsg.ReqAckPlayerMove` |
|---|---|
| `mover` | `Ident`, campo 1, required |
| `moveType` | `int32`, campo 2, required |
| `target_pos` | `Position`, campo 3, repeated |
| `source_pos` | `Position`, campo 4, repeated |

`NFMsg.Position` tem `float x=1`, `float y=2`, `float z=3`. O arquivo `NFDefine.proto` usa os códigos `EGMI_REQ_MOVE=230` e `EGMI_ACK_MOVE=231`. O formato completo de transporte e handshake do cliente Android permanece desconhecido.

[`bridge/MobileMovementProtoProbe.cs`](../bridge/MobileMovementProtoProbe.cs) inspeciona apenas **o payload Protobuf** de uma mensagem completa de movimento do Mobile. Ele confere `mover`, `moveType`, campos obrigatórios, coordenadas finitas e o número de posições, com limites de 4 KiB por mensagem, no máximo 16 posições em cada lista e 64 campos. Depois pode conferir um vínculo já estabelecido pelo servidor na `LabIdentityCrosswalk`.

Isso não decodifica o framing de rede do Android, não monta uma sessão NFMsg, não implementa a lógica de colisão, não calcula velocidade permitida nem transforma pedidos do Mobile em ações do jogo. É um decodificador defensivo **sem efeito no mundo**.

## Testes executados

- Suite `.NET 8` em [`bridge-tests/`](../bridge-tests/): **117 verificações PASS** executadas no Ziel com o SDK portátil, exit code 0.
- GitHub Actions: [execução aprovada](https://github.com/danilostorm/pristonm/actions/runs/42839432503).
- Os testes abrangem pacotes Desktop, conversão X/Y/Z Desktop→Mobile, IDs de sessão, conflitos, limite de memória, reinicialização, campo `Ident`, mensagem completa `ReqAckPlayerMove`, tentativas de representar outro jogador e Protobuf malformado.
- Sem rede de jogo, sem autenticação real, sem acesso a registros de jogadores e sem alterações em arquivos do servidor AEROCOOL.

## Próxima integração real necessária

1. Confirmar o isolamento dos bancos e diretórios do StormPT LAB; corrigir o espaço quase esgotado de C: antes de instalar serviços.
2. Preparar transporte autenticado e TLS interno, preservando o processo Games de produção.
3. Criar um adaptador do lado do `Server33.exe` que observe **eventos autoritativos de login/spawn/despawn**, em vez de aceitar serials enviados pelo cliente.
4. Associar identidade de sessão e personagem às entidades reais; decidir como criar e atribuir os pares `(svrid,index)` na futura compatibilidade Mobile.
5. Validar escala, origem, eixos, mapas, velocidade, colisão, teleporte e estado do HP no servidor. Nunca encaminhar coordenadas/HP arbitrários a `OPCODE_POSITION`.
6. Só depois desenvolver o transporte Mobile e publicar estados autorizados por WebSocket para o Web e Android.

**A integração real Desktop + Web + Mobile no mesmo mundo continua pendente. Produção não foi modificada.**