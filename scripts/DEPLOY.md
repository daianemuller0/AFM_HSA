# Publicar o AFM HSA na rede

O app roda **por usuário**, em `localhost:5090`, e lê a base Parquet do caminho
de rede configurado em `appsettings.json` (`Data:Folder`). Publicar significa
copiar o app (binários) para a pasta de rede do programa:

```
\\BZVCPFIL003\proj_ramires$\HSA_AFM
```

> Atenção: essa pasta é a do **APP**. A **base de dados** fica noutra pasta
> (`...\DB\AFM_HSA`) e não é tocada pela publicação.

## Como publicar

1. Peça para **todos fecharem o AFM HSA** (a janela preta do `AfmHsa.exe`):
   com ele aberto o arquivo fica travado e a cópia falha — o script avisa.
2. No **PowerShell**, na raiz do projeto, traga a versão mais nova e publique:

```powershell
git pull
powershell -ExecutionPolicy Bypass -File .\scripts\publicar-na-rede.ps1
```

O script:

1. compila em Release, **self-contained win-x64** (embute o .NET 8 — os PCs dos
   usuários não precisam ter runtime instalado);
2. gera um atalho `Abrir AFM HSA.bat`;
3. espelha tudo para `\\BZVCPFIL003\proj_ramires$\HSA_AFM` (via `robocopy /MIR`).

## Pré-requisitos

- .NET 8 SDK instalado na **sua** máquina (só de quem publica).
- Acesso de escrita na pasta de rede do app.

## Como os usuários abrem

Duplo clique em:

```
\\BZVCPFIL003\proj_ramires$\HSA_AFM\Abrir AFM HSA.bat
```

(ou executando `AfmHsa.exe` na mesma pasta). O navegador abre sozinho em
`localhost:5090`. O ícone/summary usa o logo Howden (`wwwroot/favicon.png`).

## O que cada PC precisa

- Nada de .NET (vai embutido).
- **Outlook** (para os e-mails de aprovação e de envio abrirem direto).
- **Microsoft Word** (ou LibreOffice) para o botão **PDF** da proposta: o
  PDF é o Word convertido no próprio computador. Sem nenhum dos dois, o
  Word continua saindo e a tela explica.

## Atualizações futuras

É só rodar o script de novo — o `robocopy /MIR` substitui a versão antiga na
rede pela nova. Peça aos usuários para fechar o app antes, para o `AfmHsa.exe`
não ficar bloqueado.
