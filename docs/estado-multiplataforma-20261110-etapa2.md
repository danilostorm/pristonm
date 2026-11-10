# StormPT multiplataforma — andamento 10/11/2026 (segunda etapa)

**Meta:** Desktop, Web e Mobile compartilharem contas, personagens e mundo, sempre validados no LAB antes de qualquer produção.

## Entregas desta etapa

- `web-sdk/StormPTGatewayClient.mjs`: cliente ESM para o StormPT Web com login, identidade, listagem de personagens e logout; token na memória, sem armazenamento persistente. Validação HTTPS na página remota e nenhum CORS público.
- `web-sdk/tests/client.test.mjs`: testes offline aprovados em [GitHub Actions](https://github.com/danilostorm/pristonm/actions/runs/42827277200).
- `gateway/StormPT.Gateway/Program.cs`: novo endpoint `POST /v1/mobile/role-list`, exigindo Bearer válido e recebendo corpo binário `application/x-protobuf` de `NFMsg.ReqRoleList`. Valida conta reivindicada contra identidade da sessão, devolve somente dados autorizados de personagens em **JSON**. Este endpoint **NÃO** implementa o framing TCP do jogo nem um `AckRoleLiteInfoList` nativo.
- `gateway/tests/smoke_gateway.py`: testes com conta fictícia cobrem pacote binário válido, pacote malformado, ausência de token e tentativa de acessar personagens de outra conta. [GitHub Actions aprovado](https://github.com/danilostorm/pristonm/actions/runs/42827023874).
- Compilação do Gateway no Ziel, usando SDK .NET 8 portátil: 0 erros, 0 avisos.
- Teste local no Ziel: `GET /health` HTTP 200 (backend de login real desativado), `POST /v1/mobile/role-list` sem token HTTP 401. Serviço encerrado após o teste.
- `tools/Test-StormPTLabIsolation.ps1`: auditor de segurança **somente de leitura** executado no LAB AEROCOOL. Retornou falha preventiva (exit 3) por ausência de isolamento verificável e disco cheio. Nenhum jogador foi lido.
- `danilostorm/pristonweb/docs/integração-gateway-lab.md`: instruções de encaixe da API na prévia Web, sem modificar o jogo Web publicado.

## Fatos importantes da auditoria

- VM `StormPT-Mount-Lab-20261102` ligada no AEROCOOL; a VM `Games` de produção permanece ligada e não foi alterada.
- Processo `Server33.exe` do LAB roda a partir de `C:\StormPTMaintenance\MountNativeInventory-Lab-20261102\Runtime\Server33.exe`.
- Conexões reais do processo `Priston Tale` ao SQL Server usam nomes **sem prefixo**, como `AccountDB`, `ServerDB`, `ClanDB`, etc. Bancos `MountLab_20261102_*` existem mas não aparecem nessas sessões de jogo. Isso **não comprova que os dados sejam de produção**; significa que ainda não há evidência suficiente do isolamento que exigimos.
- Portal do LAB consulta `AccountDB` sem prefixo e usa `C:\Servidor\DataServer` para arquivos de personagens.
- C: do LAB mediu 228 MB livres na execução do auditor. A VM possui disco GPT de 100 GB com partição de recuperação depois da partição C:, portanto expansão direta de C: requer planejamento; **nenhum disco foi alterado**.
- VM Priston WEB no Ziel permaneceu em funcionamento, com todas as novas ferramentas e builds limitadas a `C:\PristonM-Lab\`.

## Bloqueios ainda abertos

1. Definir e comprovar um ambiente de jogo/portal/SQL realmente isolado, incluindo diretório de personagens, com backup e espaço livre adequado.
2. Configurar backend de autenticação de LAB por TLS validado (ou loopback no mesmo host); nunca trafegar senhas por HTTP remoto.
3. Testar a autenticação com contas descartáveis **somente depois** da aprovação da auditoria.
4. Integrar o SDK ao código da prévia Web e mapear uma sessão real de personagem no servidor.
5. No Mobile, investigar os `.pak` ausentes, bytecode ASBC e protocolos de framing; o APK experimental não é jogável nem está conectado ao StormPT.
6. Desenvolver movimento, combate e inventário compartilhados e regras para uma sessão por personagem antes da homologação multicliente.

**Nenhuma porta pública foi aberta, nenhuma credencial foi publicada, nenhuma configuração de produção foi alterada e nenhum usuário real foi autenticado.**