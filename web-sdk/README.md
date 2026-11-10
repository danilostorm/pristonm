# StormPT Web — módulo de integração com Gateway

Este módulo ESM é uma **biblioteca cliente**, não o jogo Web completo. Ele oferece a conexão inicial com a API do LAB sem acesso direto ao SQL Server e sem manipular as senhas legadas do StormPT.

## Estado

- `StormPTGatewayClient.mjs` implementado.
- Os testes offline com Node.js passaram no GitHub Actions, sem servidor nem conta reais.
- Login, identidade, listagem de personagens e logout são compatíveis com as rotas atuais do Gateway.
- Ainda não foi inserido na prévia Three.js/Vite do StormPT Web. A integração do jogo em tempo real permanece pendente.

## Exemplo mínimo

```js
import { StormPTGatewayClient } from './StormPTGatewayClient.mjs';

const gateway = new StormPTGatewayClient(); // mesmo domínio, HTTPS
await gateway.login('CONTA_DE_LAB', 'SENHA_DE_LAB');
const { username } = await gateway.me();
const { characters } = await gateway.characters();
console.log(username, characters.map(c => c.name)); // não imprima tokens/senhas
await gateway.logout();
```

A página Web deve utilizar **HTTPS e o mesmo domínio** para `/v1/*`, encaminhado por proxy ao Gateway privado. O código não habilita CORS e nunca salva o token em `localStorage`, cookies ou `sessionStorage`; ao recarregar a página a sessão do cliente precisa de nova autenticação. Os caracteres pertencem à identidade confirmada pelo servidor, nunca a um usuário escolhido por parâmetro.

## Rodar testes

```bash
node --test web-sdk/tests/client.test.mjs
```

## Limites

O Gateway está preparado somente em loopback e seu backend real está desligado. A auditoria do StormPT LAB identificou conexões do processo a bancos sem o prefixo `MountLab_20261102_` e espaço C: insuficiente; por isso não conecte contas reais ou exponha um proxy agora.

O módulo não implementa movimentação, inventário, combate, seleção de sessão no mundo nem conexões TCP; essas operações exigem um protocolo/adaptador adicional no servidor.