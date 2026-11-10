# StormPT LAB — canal de eventos confiáveis do servidor

**Status de 10/11/2026: receptor implementado e testado apenas com eventos sintéticos. Nenhum evento real do `Server33.exe` é emitido ou recebido.** O código permanece na branch `lab/trusted-world-events-20261110`; não há implantação de produção.

## Objetivo

Preparar o Gateway para receber observações de inicialização (`boot`), surgimento de personagem (`spawn`) e saída (`despawn`) de um futuro adaptador **autorizado dentro do próprio servidor de jogo**. O receptor não aceita eventos como autorização para movimentar, salvar personagem ou entrar no mundo.

## Implementação

- `gateway/StormPT.Gateway/LabWorldEventReceiver.cs`: receptor interno com HMAC-SHA256, assinaturas calculadas sobre bytes exatos, relógio com janela de ±20 segundos, nonce aleatório único, contador de sequência estritamente crescente e limite de memória.
- `gateway/StormPT.Gateway/LabIdentityCrosswalk.cs`: registro temporário de IDs nativos e Mobile. `despawn` só remove uma associação quando a conta, personagem, sessão do servidor, época, serial nativo e Ident Mobile coincidirem **exatamente**.
- `POST /__lab/world-events`: recebe exclusivamente `application/json` e exige conexão por loopback. O Gateway escuta em `127.0.0.1:18878`, sem CORS público nem serviço publicado.
- `GET /health` informa apenas se o receptor está configurado; não expõe a chave nem a lista de entidades.
- **Desligado por padrão.** Para um ambiente de testes cuidadosamente isolado exige `STORMPT_LAB_ENABLE_WORLD_EVENTS=1` e `STORMPT_LAB_WORLD_EVENT_KEY_B64` com segredo aleatório de 32–64 bytes codificado em Base64. Não colocar o segredo no repositório, em URL nem nos logs.

### Contrato de envio (somente documentação, não ativar no servidor real)

O emissor futuro deve enviar um corpo JSON UTF-8 com os campos camelCase `eventType`, `serverEpoch`, `sequence`, `serverSession`, `account`, `character`, `nativeObjectSerial`, `mobileServerId` e `mobileIndex`. Eventos `boot` necessitam de época UUID inédita e sequência crescente. Os eventos `spawn/despawn` necessitam de todos os IDs preenchidos; exemplos com dados fictícios estão em `gateway/tests/smoke_world_events.py`.

Assinatura HMAC calculada **sobre bytes**, em ordem:

```text
HMAC_SHA256(KEY, ASCII(unixTimeSeconds + '\n' + nonce32Hex + '\n') + rawJsonUtf8)
```

Cabeçalhos obrigatórios: `X-StormPT-Lab-Time`, `X-StormPT-Lab-Nonce`, `X-StormPT-Lab-Signature` (digest hexadecimal de 64 caracteres). A conexão só é válida em loopback mesmo quando assinada. O receptor limita o corpo a 4096 bytes e bloqueia eventos sem assinatura, repetidos, fora de ordem, com epoch antiga, serial conflitante ou conteúdo inválido.

**Atenção:** a sequência é monotônica entre eventos de uma instância do Gateway, inclusive entre novos `boot`. O mecanismo atual é **volátil**: reiniciar o Gateway elimina a cache de nonces e as associações. Ainda não é adequado para produção; antes da integração real é necessário armazenamento durável, ressincronização, ciclo de vida da chave, certificados, recuperação de perda de eventos e tratamento de eventos fora de ordem. O segredo de eventos não deve ser reutilizado para contas, login, launcher ou outros serviços.

## Evidência do código-fonte da VM AEROCOOL

Executado o auditor somente-leitura `tools/Test-StormPTEventHookPoints.ps1` dentro da VM `StormPT-Mount-Lab-20261102`, sem instalar ferramentas. Foram encontrados **7/7 padrões de código**, entre eles:

| Arquivo | Ponto de estudo | Risco/observação |
|---|---|---|
| `Server/OnSever.cpp` | `GetNewObjectSerial()` | Gera serial também para NPCs e monstros; **NÃO é um evento de login** |
| `Server/OnSever.cpp` | `DisconnectUser(smWINSOCK*)` | Localização central de desconexão; deve preservar a sessão antes da limpeza; não bloquear a thread de jogo |
| `Server/OnSever.cpp` | `tpInfo->getAccount()/getName()` | Campos associados ao jogador disponíveis no contexto do processo; não publicar valores reais |
| `Server/OnSever.cpp` | `lpsmSock->ExtData1` | Vínculo inicial do socket com rsPLAYINFO; **não prova autenticação concluída** |
| `record.cpp` | `smCharInfo.dwObjectSerial = GetNewObjectSerial()` | Atribuição observada durante preparação de registro; é preciso determinar o ponto seguro *após* autenticação e carregamento |
| `netplay.cpp` | `TransPlayerInfo.dwObjectSerial = lpCurPlayer->dwObjectSerial` | O cliente recebe um serial, mas não comprova uma sessão de confiança |

O auditor calculou hashes dos três arquivos-fonte para registrar a versão investigada. **Ver a fonte não comprova que o executável do LAB tenha sido recompilado com aquele código.** Nenhum hook foi aplicado ao executável, nenhuma DLL foi injetada, nenhum arquivo foi modificado e nenhuma senha ou registro de jogador foi lido.

## Validação automática

- `dotnet build gateway/StormPT.Gateway/StormPT.Gateway.csproj -c Release`: aprovado no Ziel, 0 erros e 0 avisos.
- `python gateway/tests/smoke_gateway.py`: os testes anteriores de login e personagens com portal fictício continuam aprovados.
- `python gateway/tests/smoke_world_events.py`: boot, spawn, despawn assinados, HMAC inválido, relógio fora da janela, nonce repetido, sequência antiga, época diferente, conflito de IDs, tentativa de despawn de outra entidade, tipos inválidos e tentativa de obter acesso a personagens após os eventos.
- [GitHub Actions, execução do Gateway](https://github.com/danilostorm/pristonm/actions/runs/42841297682): compilação e ambas as suítes concluídas com sucesso.

## Bloqueios para conectar de verdade

1. **Armazenamento:** C: do StormPT LAB foi medido com apenas **224 MB livres**. Não instalar componentes novos até resolver o espaço com backup/snapshot e plano de manutenção.
2. **Banco de contas:** o jogo ativo usa bancos sem prefixo `MountLab_20261102_`. A instância parece local à VM, mas o isolamento/linhagem dos dados ainda exige validação antes de testes com contas descartáveis.
3. **Transporte:** o Gateway está numa VM no Ziel e o servidor StormPT LAB em outra VM, no AEROCOOL. O endpoint usa apenas loopback do Gateway. **Não abrir a porta na rede para fazê-los conversar**. Quando for hora de integrar, hospedar o emissor e receptor na mesma máquina ou usar um canal privado com autenticação mútua e TLS verificado.
4. **Eventos de origem:** `Server33.exe` ainda não publica eventos confiáveis. Deve-se identificar e instrumentar, em uma compilação separada do LAB, o ponto **após autenticação** e de entrada no mundo, saída/desconexão e reinício de processo. Emissão assíncrona, sem segredos em logs, sem bloquear o loop do jogo e com fila limitada.
5. **Identidade:** `serverSession` pertence ao servidor do jogo e não é o token do navegador. O vínculo com contas Web/Mobile precisa ser comprovado pelo servidor. Nunca aceitar `dwObjectSerial`/`Ident` enviados por clientes como prova de posse.
6. **Movimentação:** nenhuma instrução de posicionamento, HP ou ataque será enviada ao `Server33.exe` até existir validação autoritativa de mapa, velocidade, colisão, frequência e integridade de sessão.

**Nenhum serviço de produção foi modificado, nenhum endpoint de evento público foi ativado e nenhuma conta real entrou nos testes.**