# Publicar o AFM HSA na rede

Mesma arquitetura do axiaisvent: o programa é uma **janela própria**
(WinForms + WebView2, o Edge embutido) com o servidor rodando **dentro dela** —
não há janela preta, e fechar a janela encerra tudo. Na rede fica só um
**lançador** e as versões:

```
\\BZVCPFIL003\proj_ramires$\HSA_AFM\
  AFM_HSA.exe                              <- lançador (o atalho aponta pra ele)
  AFM HSA - Aftermarket Intelligence.lnk   <- atalho com a logo
  app\versao.txt                           <- a versão atual
  app\v2026-11-09_1530\                    <- os arquivos de cada versão
```

> A **base de dados** fica noutra pasta (`\\BZVCPFIL003\proj_ramires$\DB\AFM_HSA`,
> `Data:Folder` do `appsettings.json`) e não é tocada pela publicação.

## Como abre no computador de cada um

```
AFM_HSA.exe (lançador, na rede)
 ├─ lê app\versao.txt
 ├─ versão nova? copia app\<versão>\ para %LOCALAPPDATA%\HowdenAFM\<versão>\ (barra de %)
 ├─ na primeira vez: instala o lançador no computador e cria o atalho
 │   "AFM HSA - Aftermarket Intelligence" na área de trabalho
 └─ abre o AfmHsaApp.exe local e se fecha

AfmHsaApp.exe (o programa)
 ├─ tela de abertura com a %
 ├─ servidor interno em 127.0.0.1 (porta 5090, ou a próxima livre)
 └─ janela com o sistema; fechar a janela encerra o servidor
```

- Ninguém roda nada direto da rede: **nenhum arquivo fica travado** e dá para
  publicar a qualquer hora. Quem está com o programa aberto recebe a versão
  nova ao fechar e abrir.
- Sem rede, o lançador abre a última versão instalada no computador.

## Como publicar

1. **Na primeira vez:** apague o conteúdo antigo de `\\BZVCPFIL003\proj_ramires$\HSA_AFM`
   (o `AfmHsa.exe`, as `.dll`, o `Abrir AFM HSA.bat` etc. da publicação antiga).
2. No **PowerShell**, na raiz do projeto:

```powershell
git pull
powershell -ExecutionPolicy Bypass -File .\scripts\publicar-na-rede.ps1
```

O script compila a janela e o lançador (auto-contidos: os PCs não precisam de
.NET), confere se nada ficou faltando, copia a versão para `app\<versão>`, ativa
a versão, atualiza o `AFM_HSA.exe` e o atalho, e mantém só as 5 últimas
versões na rede. Tudo o que aparece na tela fica em `scripts\publicar.log` — se
algo falhar, é só mandar esse arquivo.

## Pré-requisitos

- **Quem publica:** .NET 8 SDK (Windows) e acesso de escrita na pasta de rede.
- **Cada computador:**
  - **WebView2 Runtime** (já vem com o Windows 10/11 atualizado ou o Edge; se
    faltar, o programa avisa e indica o download);
  - **Outlook**, para os e-mails abrirem direto;
  - **Microsoft Word** (ou LibreOffice), para o botão **PDF** da proposta.

## Arquivos baixados

O Word, o PDF e as planilhas caem na pasta **Downloads** do usuário (o menu
"Arquivos baixados" da janela abre essa pasta).

## Modo navegador (desenvolvimento / servidor central)

O projeto principal continua rodando como site: `dotnet run` abre no navegador
em `localhost:5090`. Para um servidor central: `AfmHsa.exe --urls http://0.0.0.0:5090`
com `"OpenBrowser": false` no `appsettings.json`.
