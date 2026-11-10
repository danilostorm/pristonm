# StormPT multiplataforma — plano de integração no LAB

Data: 2026-11-10. Status: **pesquisa e prova de conceito; nenhuma integração online ativada**.

## Objetivo

Disponibilizar **Desktop**, **Web** e **Android** para o mesmo servidor de jogo, compartilhando autenticação, personagens, inventário e estado do mundo. A única fonte autorizada a gravar dados persistentes e validar ações de jogo deve ser o servidor StormPT; não conectar aplicativos móveis ou páginas web diretamente ao SQL Server.

## Constatações verificadas

- O LAB `StormPT-Mount-Lab-20261102` no AEROCOOL está em execução e contém processo de servidor do jogo e SQL Server Express.
- Há bancos independentes de LAB com prefixo `MountLab_20261102_`. Durante os testes, usar apenas recursos homologados como LAB. A presença de bancos com e sem prefixo exige validação cuidadosa da configuração real antes de qualquer gravação.
- A VM Windows do LAB reportou **zero bytes livres na unidade C:**. Não gravar, instalar SDKs ou modificar configurações nela antes de resolver o espaço.
- O repositório `danilostorm/pristonweb` descreve o Web como **prévia visual em Three.js/TypeScript/Vite**; login persistente, multiplayer e combate ainda não estão implementados.
- O cliente PTM possui uma árvore de APK extraída em `Client_PTMobile/main_install` com `AndroidManifest.xml`, `classes.dex`, `resources.arsc`, `lib/armeabi-v7a/libgame.so` e `META-INF`. Essa estrutura permite tentar reempacotar o APK, mas não garante assinatura válida, instalação, inicialização ou conectividade.
- Os arquivos `.proto` do PTM definem mensagens com namespace `NFMsg`, por exemplo `ReqAccountLogin`. O protocolo não foi confirmado como compatível com o do StormPT desktop.

## Arquitetura pretendida

```
StormPT Desktop ───────────────┐
                              │ protocolo atual
StormPT Web ── HTTPS/WSS ─────┼── [Gateway autenticado LAB] ── [StormPT LAB]
                              │                             ├─ personagens
StormPT Mobile ── adaptador ──┘                             ├─ inventario
                                                            └─ SQL/file stores
```

A arquitetura acima é **objetivo**, não implementação existente. O gateway nunca deve confiar em posição, inventário, dinheiro, dano ou permissões enviados pelo cliente. O servidor aplica todas as regras.

## Fases e critérios

1. **Inventário e segurança:** confirmar backend principal, fontes de personagem, sessão e porta do LAB, espaço livre e backups; não ler valores de senhas ou expor segredos no GitHub.
2. **APK:** reempacotar em máquina isolada e testar integridade ZIP; validar assinatura/zipalign com SDK Android, inspecionar versão e permissões e testar inicialização num aparelho/emulador de teste. Um APK reempacotado ainda não é um aplicativo funcional.
3. **Autenticação:** definir contrato de sessão no gateway, reutilizar método de autenticação seguro do StormPT (sem validar senha diretamente pelo SQL do navegador), aplicar TLS, rate limit e tokens de curta duração. Testar usuário de LAB.
4. **Personagens:** definir leituras e gravações por operações autenticadas no servidor. Não duplicar database nem permitir duas sessões modificarem o mesmo personagem simultaneamente.
5. **Protocolo:** mapear estados de login, listagem de personagens, movimento, combate, inventário e desconexão nos arquivos PTM `.proto` versus a implementação real StormPT. Se incompatível, adaptar ou criar um cliente Mobile sobre a API/SDK do gateway.
6. **Web:** conectar o visualizador atual à mesma camada de autenticação e estado, completar sistemas de jogo.
7. **Homologação:** PC + Android + Web simultâneos em contas/personagens de teste, regras de sessão e reconciliação; testes de carga e de recuperação de sessão.
8. **Produção:** somente após validação completa e aprovação explícita, com manutenção e atualização via launcher. Nunca implantar automaticamente.

## Avisos

- Os dados do LAB são para ensaios; não presumir que o banco com prefixo esteja automaticamente configurado no processo atual sem confirmar a ligação.
- Não publicar APKs, assets, certificados nem segredos do cliente de terceiros neste repositório público antes de verificar direitos e licenças.
- O programa foi originalmente montado para Android ARMv7; compatibilidade com aparelhos atuais e bibliotecas de SDK precisa de teste independente.
- **O que existe agora é pesquisa e infraestrutura de testes. Não há login cruzado, mundo compartilhado nem servidor Mobile online.**
