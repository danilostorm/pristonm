# Inventário do PTM.rar — 2026-11-10 (horário de Brasília)

## Verificações realizadas

- VM usada: **Priston WEB** (Windows Server 2022), sem alterar a aplicação Priston WEB existente.
- Original: `C:\Users\Administrador\Downloads\PTM.rar`
- Tamanho: `1.735.407.539` bytes.
- SHA-256: `BCEC56C169AADD655BFD6BFF963FE1EB9ED81E1480C6C5071EB79E8A4D6CA0CB` — **confere com a API do MediaFire**.
- 7-Zip: 26.06, obtido no domínio oficial `www.7-zip.org`.
- Formato: RAR5, não criptografado.
- Extração primária concluída com exit code 0: `C:\PristonM-Lab\extracted\`, **15.806 arquivos** + diretórios.
- Listas de arquivo estão na VM em `C:\PristonM-Lab\reports\`, não no GitHub devido ao tamanho.

## Arquivos dentro do RAR principal

| Nome | Tamanho compactado | Entradas | Conteúdo identificado |
| --- | ---: | ---: | --- |
| `Client_PTMobile.rar` | 520.200.002 bytes | 36.038 | `main_install/`, `auto_update/`, recursos Android e `main_install/lib/armeabi-v7a/libgame.so` |
| `Client_PTMobile-Beta.rar` | 180.053.885 bytes | 7.073 | recursos e `NFDataCfg/Excel_Ini/Server.xlsx` / `SqlServer.xlsx` |
| `Client_PristonMobile.rar` | 606.967.283 bytes | 19.086 | `Clients/android/`, `Clients/game/`, `Clients/game_taptap/`, `TextureUnPacker.jar` |

RAR principal: 16.415 entradas, aprox. 2.026.581.105 bytes descompactados incluindo os três RAR internos. As listagens dos RAR internos não exibiram caminhos absolutos nem referências `..` (checagem de travessia básica).

### Binários encontrados nas listas dos três arquivos internos

- `Client_PTMobile.rar`: `main_install/assets/converter.exe`, `main_install/assets/TexturePackage/converter.exe`, `main_install/lib/armeabi-v7a/libgame.so`.
- `Client_PTMobile-Beta.rar`: nenhum com extensão `.exe`, `.dll`, `.apk`, `.aab`, `.ipa`, `.jar`, `.so`, `.msi`, `.pak`, `.cs`, `.cpp`, `.sql`, `.db`, `.bat`, `.cmd`, `.ps1`.
- `Client_PristonMobile.rar`: `TexturePackage/etcpack.exe`, `TextureUnPacker.jar`.

Essas extensões de executáveis encontradas são ferramentas de conversão ou um **componente ARM Android**, não um binário do jogo para Windows. **Não foi encontrado APK completo nem servidor de jogo executável** nessas listas. Planilhas sobre servidor e SQL **não são** servidor implementado.

## Consequência técnica

O pacote oferece uma base rica para recuperação de assets e estudo do cliente, porém **não basta para instalar um jogo online jogável**. Antes disso, será necessário encontrar o aplicativo instalável Android e o código/binário de backend compatível, ou avaliar tecnicamente o trabalho de reconstrução (cliente, protocolo e servidor).

A chave XOR fornecida é para alguns recursos, **não** para o RAR. O script `tools/Decrypt-PTMPak.ps1` só aceita pacotes cuja decifração produza cabeçalho `UPAK` ou `ULZ4`.

**Segurança:** não executar `converter.exe`, `etcpack.exe`, `TextureUnPacker.jar` nem `libgame.so` sem análise prévia. Não publicar os assets ou as ferramentas de terceiros sem verificar licenças e direitos.
