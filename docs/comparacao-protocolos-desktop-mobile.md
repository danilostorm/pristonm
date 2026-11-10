# Comparação inicial dos protocolos — StormPT Desktop x PTM Mobile

Verificação realizada **somente em leitura** no LAB StormPT e sobre arquivos PTM já extraídos. Não modifica produção.

## StormPT Desktop

O código do servidor Desktop contém um protocolo de rede próprio, baseado em C/C++ com enumeração `OpCode` em `Source/Shared/smPacket.h`. Há operações para autenticação/estado, posição, chat, combate e inventário, como `OPCODE_POSITION`, `OPCODE_CHATMESSAGE`, `OPCODE_ATTACKDATA` e `OPCODE_WAREHOUSE`.

## PTM Mobile

Os esquemas `NFDataCfg/ProtoFile/*.proto` definem mensagens Protocol Buffers no namespace `NFMsg`. Um exemplo é `ReqAccountLogin`, cuja estrutura de campos é diferente do protocolo de pacotes Desktop. A biblioteca `libgame.so` é ARMv7 e ainda não foi executada.

## Conclusão

**Os protocolos de rede não são idênticos**. Apontar o cliente Mobile diretamente para a porta de jogo Desktop não basta; seria preciso implementar e testar uma **ponte de protocolo/estado**, ou desenvolver um novo cliente mobile sobre uma API moderna.

As etapas verificáveis são: login no LAB, consulta de personagens, seleção, mapa, posição, inventário, combate e desconexão. Um adaptador só é viável após comparar o formato de enquadramento, criptografia/compressão, estado de sessão e semântica de cada mensagem; nenhuma ponte está implementada no momento.

A fonte de verdade continuará sendo o servidor StormPT. Não expor SQL diretamente a Android/Web; evitar que clientes alterem dados de personagem sem validação no servidor.

## Outras dependências

O projeto Web descrito em `danilostorm/pristonweb` ainda precisa integrar autenticação e multiplayer reais. O APK PTM foi remontado e assinado para LAB, mas faltam pacotes `.pak`, há permissões antigas e `targetSdkVersion=12`.

**Nenhuma conexão entre clientes foi ativada. Nenhum serviço público foi criado.**
