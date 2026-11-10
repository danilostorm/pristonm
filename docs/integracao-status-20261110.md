# Integração StormPT Desktop / Web / Mobile — progresso LAB

Atualizado em 2026-11-10. **Não existe integração online ativa**; trabalho executado sem alterações na produção.

## Máquinas e segurança

- **StormPT-Mount-Lab-20261102** (AEROCOOL): servidor `Server33` e SQL Express ativos, código-fonte do Desktop encontrado em `C:\Elemental-Source\Source\`.
- O disco C: do LAB estava com cerca de **340 MB livres** na inspeção. Nenhuma alteração nem instalação foi realizada nele.
- **Priston WEB** (Ziel): trabalho isolado em `C:\PristonM-Lab\`, sem modificar o Priston WEB existente.
- No laboratório StormPT há bancos com nomes normais e bancos com prefixo `MountLab_20261102_`. **Não presumir que o processo do servidor use o banco com prefixo até verificar a configuração da instância.**

## Resultados comprovados no Ziel

| Item | Resultado |
|---|---|
| Preflight do Android (script `tools/Test-PTMPreflight.ps1`) | PASS |
| Script do cliente `assets/Scripts/PTGame.as` | bytecode `ASBC`, não código-fonte AngelScript editável |
| Esquemas Mobile na árvore Android | 843 declarações `message` encontradas |
| Campos de login `ReqAccountLogin` | conta (2), campo senha (3), versão cliente (6) |
| Campos de personagem `ReqRoleList` | game_id (1), account (2), account_type (3) |
| Resposta de lista `AckRoleLiteInfoList` | char_data (1) |
| Campos conferidos contra `NFMsgPreGame.proto` | 7 de 7 PASS |
| Testes do leitor login `bridge/MobileLoginProbe.cs` | PASS: verificações positivas e seis casos malformados |
| Testes do leitor lista `bridge/MobileRoleListProbe.cs` | PASS: verificações positivas e seis casos malformados |
| Arquivos .pak mencionados no manifesto original mas ausentes | 22 referências distintas |
| Arquivos .pak efetivamente presentes na árvore Android | 0 |
| `Base/CommandLine.txt` | referência a `Scripts/Editor.as` ausente nessa árvore |
| `assets/sdkconf.ini` | contém endereço de SDK externo legado, que deve ser bloqueado em testes de execução |
| APK assinado anteriormente no LAB | assinatura técnica válida, **sem teste de inicialização** |

Os inspetores Protobuf **não fazem login**, não recebem credenciais reais, não conectam ao banco, não emitem tokens e não validam todas as regras do protocolo. Eles verificam apenas a estrutura inicial das mensagens para apoiar o desenvolvimento de um adaptador. Em particular, os campos `account` enviados pelo cliente **não são confiáveis** e deverão ser confrontados com uma identidade autenticada no servidor.

### Reprodução (PowerShell 5.1 no Ziel)

```powershell
& C:\PristonM-Lab\tools\Test-PTMPreflight.ps1 `
  -SourceDir C:\PristonM-Lab\clients\Client_PTMobile\main_install `
  -ReportPath C:\PristonM-Lab\reports\ptm-preflight.json

& C:\PristonM-Lab\tests\Test-MobileLoginProbe.ps1
& C:\PristonM-Lab\tests\Test-MobileRoleListProbe.ps1

& C:\PristonM-Lab\tools\Test-PTMProtoContract.ps1 `
  -ProtoFile C:\PristonM-Lab\clients\Client_PTMobile\main_install\assets\NFDataCfg\ProtoFile\NFMsgPreGame.proto `
  -ReportPath C:\PristonM-Lab\reports\proto-contract.json
```

## Problemas encontrados no portal StormPT LAB

Inspeção somente-leitura de `C:\StormPTPortal\src\Program.cs`:

1. `POST /api/register` já existe.
2. `POST /api/login` já existe, valida credenciais e retorna `{ authenticated: true, username }`, **não** uma sessão assinada para clientes Web/Mobile.
3. A rota de perfil é **POST /api/profile** (e não GET). Aceita `username` e no próprio mapeamento não exige token ou sessão autenticada. Não expor essa operação diretamente a clientes públicos.
4. O login do portal consulta **`AccountDB`**, sem prefixo `MountLab_20261102_`. O leitor de personagens em `PlayerProfiles.cs` também referencia bancos sem prefixo e recursos em `C:\Servidor\DataServer`. Antes de qualquer teste de integração com contas, a origem dos dados deve ser verificada e isolada.
5. O código de rede Desktop em `Source/Shared/smPacket.h` usa enum `OpCode`, diferente das mensagens `NFMsg` do Mobile.

Nenhum teste foi executado com usuário real e nenhuma senha foi copiada para GitHub.

## Arquitetura desejada (ainda não implementada)

```
Desktop (protocolo nativo) -----+
                                +--> StormPT LAB (única fonte de verdade do mundo)
Mobile (NFMsg -> adaptador) ----+          |
                                |          +--> personagens, inventário, mapas e regras
Web (HTTPS/WSS -> gateway) -----+          |
                                +--> camada de sessão segura, token curto e autorização
```

**Regra essencial:** o gateway não aceitará `username` como prova de identidade, não acessará senhas diretamente do Web/Mobile e não gravará mudanças de jogo diretamente no SQL por pedido do cliente.

## Próximos bloqueios para resolver no LAB

1. Criar ambiente Android isolado com rede externa bloqueada; testar abertura e coletar logs do APK. Os arquivos faltantes podem ou não impedir o boot; ainda não foi comprovado.
2. Validar a configuração real do SQL e separar a autenticação de produção das bases LAB, incluindo bloqueio de consulta não autenticada de perfil.
3. Implementar autenticação com sessão/token em serviço LAB, respeitando contas banidas e o sistema nativo.
4. Associar personagens pela sessão validada, não por nome de usuário enviado livremente.
5. Mapear a camada de enquadramento de pacotes do Mobile (socket, cabeçalho, compactação) e o fluxo de conexão da biblioteca Android.
6. Ligar a prévia Web à mesma camada de autenticação/estado, depois implementar as ações do mundo.
7. Resolver o espaço insuficiente na VM do LAB antes de instalações e build.
8. Apenas após homologação com as três interfaces considerar publicação, manutenção e atualização via launcher.

**Sem implantação em produção ou exposição de novas portas nesta etapa.**
