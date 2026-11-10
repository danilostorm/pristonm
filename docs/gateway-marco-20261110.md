# Marco: API do StormPT Gateway no LAB — 10/11/2026

## Confirmado

- Projeto `gateway/StormPT.Gateway/` compilado em **.NET 8** no GitHub Actions e na VM **Priston WEB (Ziel)**: **zero erros e zero avisos**.
- SDK .NET 8 instalado de forma portátil no Ziel em `C:\PristonM-Lab\tools\dotnet8\` (versão 8.0.427). Nenhuma instalação no AEROCOOL.
- Código do gateway também compilado no Ziel em `C:\PristonM-Lab\gateway\StormPT.Gateway\`.
- Teste local em loopback no Ziel: `/health` retornou 200 com login real desativado; `/v1/me` e `/v1/characters` retornaram 401 sem token; `/v1/sessions` retornou 503 sem backend autorizado. **Processo local foi encerrado após o teste**.
- GitHub Actions validou a integração usando servidor fictício em 127.0.0.1, conta e personagens simulados. Compilação e teste automático concluídos com sucesso no run [42753403341](https://github.com/danilostorm/pristonm/actions/runs/42753403341).
- Fluxo simulado coberto: configuração fechada por padrão, rejeição de senha incorreta, rejeição de identidade divergente, emissão de Bearer, leitura de identidade, lista filtrada de personagens, isolamento entre contas e revogação de token.

## API inicial

| Endpoint | Controle |
|---|---|
| `GET /health` | Loopback, não divulga segredos |
| `POST /v1/sessions` | Rate limit, chama apenas portal interno explicitamente configurado, emite sessão de 15 min |
| `GET /v1/me` | Bearer obrigatório |
| `GET /v1/characters` | Bearer obrigatório; a conta vem somente da sessão, não da URL |
| `POST /v1/logout` | Revoga token |

## Bloqueios de integração com contas reais

- O portal legado em `C:\StormPTPortal\src\Program.cs` usa origem HTTP privada na rede e conexão SQL sem prefixo `MountLab_20261102_`. Não habilitar o gateway contra esse endpoint remoto sem TLS válido e validação de isolamento do banco.
- A rota antiga `POST /api/profile` não possui autorização por sessão no próprio portal. Proteger o endpoint de origem antes de publicação.
- A API ainda **não** implementa tradução do protocolo Desktop/Android, movimentação, combate, inventário nem entrada em mapa.
- APK Mobile legado ainda não inicializado com sucesso. Web atual ainda é prévia.
- LAB StormPT no AEROCOOL está praticamente sem espaço C:, logo compilação ocorreu apenas no Ziel.

## Produção

**Nenhuma alteração nos serviços de produção, nenhum acesso a contas reais e nenhuma porta pública aberta.**
Próximo passo técnico: criar a configuração de portal **LAB com TLS e isolamento verificável**, validar conta de teste e conectar a prévia Web ao gateway seguro, mantendo Android em homologação.