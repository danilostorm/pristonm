# StormPT multiplataforma — resultado da etapa de 10/11/2026 (Web + reserva de personagem)

## Implementado e testado

- Novo protótipo Web PT-BR na branch isolada [`lab/gateway-login-prototype-20261110`](https://github.com/danilostorm/pristonweb/tree/lab/gateway-login-prototype-20261110), com login, identidade, listagem e botões de reservar/liberar personagem; [PR rascunho #1](https://github.com/danilostorm/pristonweb/pull/1). Não foi mesclado ao `main`, publicado ou executado em produção.
- Biblioteca Web atualizada no `pristonm` para `/v1/characters/reserve`, `/renew`, `/release`. Nenhuma reserva concede entrada ao mapa.
- `gateway/StormPT.Gateway/LabCharacterLeases.cs`: reserva local e exclusiva por sessão, TTL 90 segundos, liberação após logout. Segunda sessão que disputa personagem reservado recebe 409.
- Validado em GitHub Actions: [Gateway com contas fictícias](https://github.com/danilostorm/pristonm/actions/runs/42832397416), [SDK Web](https://github.com/danilostorm/pristonm/actions/runs/42832681753) e [protótipo Web](https://github.com/danilostorm/pristonweb/actions/runs/42832942571), todos aprovados.
- Build do Gateway .NET 8 novamente realizado no Ziel (`C:\PristonM-Lab\`): **zero erros e zero avisos**. Ao executar a API na VM, `/health` foi 200, login real permaneceu desativado e os três endpoints de reserva retornaram 401 sem sessão. Processo foi encerrado após o teste.

## Auditoria SQL do StormPT LAB (leitura somente)

- A VM `StormPT-Mount-Lab-20261102` no AEROCOOL executa `Server33.exe` usando SQL Server Express local.
- Conexões de sessão identificadas como `Priston Tale` utilizam `AccountDB`, `ServerDB` e outros bancos sem prefixo `MountLab_20261102_`.
- Os **arquivos físicos** de `AccountDB` e `ServerDB` estão no diretório SQL local dessa VM (`C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\DATA\`).
- As cópias prefixadas `MountLab_20261102_AccountDB` e `MountLab_20261102_ServerDB` também existem, mas estão numa pasta separada em `C:\StormPTMaintenance\MountNativeInventory-Lab-20261102\DbFiles\` e não são utilizadas pelas conexões vistas.
- A consulta a `sys.servers` não encontrou servidores SQL vinculados externos; somente a instância local `Priston\SQLEXPRESS`.
- **Interpretação importante:** usar nomes de bancos sem prefixo dentro de uma VM LAB separada não é prova de que os bancos sejam os de produção. É preciso confirmar snapshots, origens dos dados e ausência de sincronização antes de criar contas descartáveis. O auditor estrito anterior continua bloqueando a integração automaticamente por não ter essa comprovação.
- O C: do LAB mostrou **225 MB livres** na verificação mais recente. Nenhum arquivo SQL nem configuração de serviços foi modificado.

## Fonte Web anterior não localizada

- O repositório `danilostorm/pristonweb` em `main` possui apenas README/documentação; não inclui o código Three.js/Vite citado no README.
- A pasta documentada `C:\Elemental Priston Tale\web-client` não existe na VM StormPT LAB ativa do AEROCOOL.
- A antiga VM `StormPT-Mount-Lab-20261102` do Ziel foi inspecionada desligada, em **montagem de disco somente leitura**: o caminho documentado também não estava presente. Nenhuma alteração no disco antigo.
- Por isso o novo frontend é **protótipo independente**, não uma atualização da prévia 3D desaparecida.

## Ainda NÃO concluído

1. Não há autenticação real: Gateway continua fail-closed por padrão e portal antigo usa HTTP privado sem TLS validado.
2. O sistema de reservas ainda não está integrado ao servidor de jogo `Server33.exe`; não impede que o Desktop jogue o mesmo personagem. São reservas apenas dentro da única instância de Gateway LAB.
3. Falta recuperar o código do cliente Web 3D para conectar login/seleção à renderização do mundo.
4. O APK Android permanece experimental: falta resolver pacotes `.pak`, inicialização, rede nativa e atualização de APIs antigas.
5. Falta espaço de disco e verificação de isolamento/comportamento com snapshots antes de testar login de contas descartáveis.

**Nenhum login real, deploy público, porta aberta, banco escrito ou serviço de produção alterado.**