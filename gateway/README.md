# StormPT Gateway — integração LAB (ainda sem clientes conectados)

API REST de prova de conceito, feita com .NET 8, sem dependências NuGet externas.
Ela **não** faz ponte de pacotes de jogo nem altera o servidor StormPT.

## Segurança e isolamento

- Escuta somente em `http://127.0.0.1:18878`, sem porta pública.
- Autenticação real desativada por padrão: `POST /v1/sessions` responde 503.
- Para habilitar somente em LAB, definir `STORMPT_GATEWAY_ENABLE_LAB_AUTH=1` e `STORMPT_LAB_PORTAL_ORIGIN` como origem HTTPS com certificado válido (HTTP permitido apenas em loopback da mesma VM).
- A URL atual do portal no LAB usa HTTP num endereço privado remoto; **não habilitar** login remoto por ela até configurar TLS. Não trafegar senhas em texto simples por essa conexão.
- Tokens aleatórios de 256 bits e 15 minutos, somente hashes armazenados em memória; logout revoga o token; reinício invalida sessões.
- Limite de corpo de 4 KB e rate limiting de login; não envia credenciais para logs; não configura CORS.
- O gateway nunca acessa o SQL diretamente e nunca aceita `username` vindo da URL como identidade.

## Rotas (restritas a loopback)

| Método | Caminho | Comportamento |
|---|---|---|
| GET | `/health` | Saúde e estado de configuração do backend |
| POST | `/v1/sessions` | Recebe `username` e `password`, consulta portal autorizado, emite Bearer |
| GET | `/v1/me` | Retorna conta da sessão, exige Bearer |
| GET | `/v1/characters` | Consulta personagens apenas da conta do Bearer, devolve nome, classe e nível |
| POST | `/v1/mobile/role-list` | Recebe `application/x-protobuf` com `NFMsg.ReqRoleList`; exige Bearer; confere conta; devolve resposta REST JSON com personagens filtrados (não é um pacote nativo ACK do jogo) |
| POST | `/v1/logout` | Revoga Bearer |

## Compilar e testar com dados fictícios

Requer SDK .NET 8 e Python 3. Não exige conta ou banco de dados real:

```bash
dotnet build gateway/StormPT.Gateway/StormPT.Gateway.csproj -c Release
python gateway/tests/smoke_gateway.py
```

O teste sobe um portal fictício apenas em loopback, verifica login desabilitado por padrão, login negado, identidade vinculada ao token, acesso aos personagens, filtragem de campos do perfil, e revogação de token. A automação está em `.github/workflows/test-gateway.yml`.

## Restrições atuais

- O portal antigo possui `/api/login` e `/api/profile`, porém o segundo não valida sessão por conta própria. Restringir a origem da API e resolver isso antes de disponibilizar qualquer serviço público.
- O portal consulta bancos sem prefixo `MountLab_20261102_` e arquivos em `C:\Servidor\DataServer`; verificar isolação da instância antes de testar contas reais.
- Ainda é necessária uma ponte específica para o protocolo nativo Desktop e para `NFMsg` do PTM.
- O APK Mobile assinado é experimental e ainda não passou por testes de inicialização.
- O cliente Web é uma prévia e ainda precisa conectar login e estado do mundo.
- As sessões são locais e efêmeras; para produção será necessário armazenamento compartilhado, auditoria, restrições de origem, autorização mais abrangente e TLS.

**Nenhuma mudança foi feita nos servidores de produção. Nenhuma porta pública foi aberta.**
## Estado de verificação em 10/11/2026

A compilação .NET 8 e os testes sintéticos de login/lista de personagens/ponte Mobile passaram no GitHub Actions; a versão foi compilada na VM do Ziel. Em loopback, o endpoint Mobile sem sessão retornou 401. O processo foi desligado após o teste. O script `tools/Test-StormPTLabIsolation.ps1` foi executado por leitura na VM LAB e retornou bloqueio: processo do jogo e portal referenciam bancos sem prefixo `MountLab_20261102_`, o portal usa DataServer padrão e a VM tem menos de 300MB disponíveis em C:. Nenhuma conta real foi testada.


## Reserva exclusiva de personagens (prova de conceito)

O Gateway implementa três novas rotas, todas exigindo Bearer válido:

| Método | Caminho | Função |
|---|---|---|
| POST | `/v1/characters/reserve` | Valida o personagem na lista autorizada da conta e cria uma reserva de **90 segundos** |
| POST | `/v1/characters/renew` | Renova a reserva somente pelo dono da sessão |
| POST | `/v1/characters/release` | Libera a reserva somente pelo dono da sessão |

Corpo: `{"name":"PERSONAGEM_DE_LAB"}`. A segunda sessão que tenta reservar o mesmo personagem recebe **HTTP 409**; a reserva é liberada na saída da sessão e expira sem renovação. Para o protótipo, uma mesma sessão não reserva mais de um personagem simultaneamente.

**ATENÇÃO:** a resposta inclui `"worldAccessGranted":false`. A reserva é uma estrutura **local e efêmera** de prevenção de conflito entre Web e Mobile, não um login no servidor do jogo. Não controla o cliente Desktop, não bloqueia escrita no servidor `Server33.exe` e não deve ser usada isoladamente em produção distribuída.

Os testes do GitHub Actions cobrem duas sessões com uma mesma conta, conflito de reservas, renovação, liberação, logout e tentativa de reservar personagens não pertencentes à sessão.
