# Guia Arquitetural — AFM_HSA

**Howden Aftermarket Intelligence · AFM HSA**
Migração do app original (TypeScript/React — projeto *Leonardo*) para a stack
aprovada internamente: **C# (.NET 8, Blazor Server)** com persistência em
**DuckDB/Parquet** numa pasta de rede — a **mesma arquitetura do Licencas_HSA**.

> Estado atual: **fundação + esqueleto de todos os módulos**. A base de dados
> nasce **vazia**; cada módulo já tem sua tela pronta para receber os dados e a
> lógica conforme forem implementados.

---

## 1. Visão geral

O AFM_HSA é a aplicação web interna de **inteligência comercial de aftermarket**:
gestão de vendas (dashboard executivo, carteira, oportunidades, previsão, visitas,
alertas), clientes e base instalada, ofertas, vendedores, controle de despesas e
integrações (Salesforce).

É a **migração** do sistema original em TypeScript/React para a stack padrão da
equipe, seguindo exatamente o desenho do projeto **Licencas_HSA**.

### Stack

| Camada | Tecnologia |
|---|---|
| Runtime | .NET 8 (`net8.0`), ASP.NET Core |
| UI | Blazor Server (Razor Components, render interativo no servidor) |
| Dados | DuckDB em memória sobre arquivos Parquet (`DuckDB.NET.Data.Full` 1.1.3) |
| Importação | Leitura de `.xlsx` com `ClosedXML` 0.102.2 |
| Autenticação | Cookie (ASP.NET Core Cookie Authentication), credencial única da equipe |
| Estilo | CSS puro em `wwwroot/app.css` (sem framework de front-end) |

---

## 2. Estrutura do projeto

```
AFM_HSA/
├── Program.cs                     # Composição: DI, autenticação, endpoints, seed
├── appsettings.json               # Pasta de dados (rede), credencial, OpenBrowser
├── AfmHsa.csproj                  # net8.0 + DuckDB.NET + ClosedXML
├── Icons.cs                       # Ícones SVG inline (menu e telas)
├── Components/
│   ├── App.razor                  # Documento HTML raiz
│   ├── Routes.razor               # Router + AuthorizeRouteView (tudo exige login)
│   ├── RedirectToLogin.razor      # Não autenticado → /login
│   ├── _Imports.razor
│   ├── Layout/
│   │   ├── MainLayout.razor       # Layout padrão (sidebar + topbar + conteúdo)
│   │   ├── NavMenu.razor          # Menu lateral (todos os módulos) + usuário/logout
│   │   └── EmptyLayout.razor      # Layout sem menu (usado no login)
│   ├── Shared/
│   │   └── ModuleScaffold.razor   # Cabeçalho + estado "em construção" reutilizável
│   └── Pages/                      # Uma página por rota (Home, Login, Error + módulos)
├── Data/
│   ├── ParquetStore.cs            # Núcleo da persistência (DuckDB sobre Parquet)
│   └── DbInitializer.cs           # Seed na 1ª execução (hoje: base vazia)
├── Models/
│   └── AppInfo.cs                 # Namespace de modelos (cresce com as entidades)
└── wwwroot/
    ├── app.css                    # Estilos (sidebar, grid, cards, badges, empty-state)
    └── app.js                     # Utilitários (print/download via JS interop)
```

---

## 3. Camada de dados — o padrão Parquet + DuckDB

Idêntico ao Licencas_HSA. O `ParquetStore` (`Data/ParquetStore.cs`) evita um banco
compartilhado na rede usando **escrita append-only** e **consolidação na leitura**:

- **Escrita** (`WriteRow`): cada adição/edição/exclusão gera um **novo arquivo
  Parquet pequeno** na subpasta da entidade (ex.: `clientes/`), nomeado
  `"{ticks}_{guid}.parquet"`. Cada linha carrega `_ts` (timestamp) e `_deleted`
  (exclusão lógica). Todos os valores são gravados como `VARCHAR`.
- **Leitura** (`ReadLatest`): o DuckDB abre **em memória** (`Data Source=:memory:`),
  lê a pasta com `read_parquet('pasta/*.parquet', union_by_name=true)` e consolida
  com `row_number() OVER (PARTITION BY id ORDER BY _ts DESC)` → mantém só a versão
  mais recente de cada `id` e descarta os `_deleted`.
- **Concorrência**: cada gravação é um arquivo novo e independente, então vários
  usuários gravam ao mesmo tempo na pasta de rede sem travar.
- **Substituição** (`Clear`): apaga todos os Parquet da entidade (usado por
  importações em modo "substituir").

A pasta de dados vem de `Data:Folder` no `appsettings.json`:
**`\\BZVCPFIL003\proj_ramires$\DB\AFM_HSA`** (sem configuração, usa `data/` local).

### Como adicionar uma entidade (padrão do Licencas_HSA)

1. Criar o **modelo** em `Models/` (propriedades como texto).
2. Criar o **repositório** em `Data/` com `All()` (via `ReadLatest`) e `Save()`
   (via `WriteRow`) — ver `Data/RenewalRepository.cs` do Licencas_HSA.
3. Registrar no `Program.cs`: `builder.Services.AddScoped<XRepository>();`
4. (Opcional) semear na 1ª execução no `DbInitializer`.
5. Consumir na página do módulo, substituindo o `ModuleScaffold` pela UI real.

---

## 4. Autenticação e autorização

- **Cookie authentication** com expiração deslizante de 7 dias.
- **Credencial única da equipe** em `appsettings.json` (`Auth:Usuario` /
  `Auth:Senha`) — não há tabela de usuários. O login cria uma identidade fixa
  ("Equipe Howden", papel `admin`).
- Endpoints minimal API `POST /auth/login` e `POST /auth/logout` em `Program.cs`.
- `Routes.razor` usa `AuthorizeRouteView`: toda rota exige login; não autenticado
  cai em `RedirectToLogin` → `/login` (com `EmptyLayout`).

> Nota de POC: a senha fica em texto plano no `appsettings.json`. Aceitável para
> uso interno; para produção, mover para um segredo.

---

## 5. Módulos (telas)

Espelham o app original. Todas as rotas exigem login e usam, por ora, o
`ModuleScaffold` (estado "em construção"), prontas para receber a UI real.

| Seção | Rota | Tela |
|---|---|---|
| Gestão de Vendas | `/dashboard` | Central de Inteligência |
| | `/diretoria` | Painel Executivo |
| | `/carteira` | Plano de Ação Comercial |
| | `/oportunidades` (+ `/{id}`) | Oportunidades |
| | `/previsao` | Previsão |
| | `/planejamento` | Planejamento |
| | `/historico` | Histórico Aftermarket |
| | `/visitas` | Ações em Campo |
| | `/alertas` | Alertas Preditivos |
| Clientes & Base | `/clientes` (+ `/{id}`) | Clientes e Unidades |
| | `/base-instalada` | Base Instalada |
| | `/equipamentos` (+ `/{id}`) | Equipamentos |
| | `/pecas` | Regras Técnicas de Troca |
| Comercial | `/ofertas`, `/ofertas/nova` | Ofertas |
| | `/vendedores` (+ `/{id}`) | Vendedores |
| Despesas | `/despesas` | Visão Geral |
| | `/despesas/minhas` | Minhas Despesas |
| | `/despesas/relatorios` (+ `/{id}`) | Relatórios |
| | `/despesas/aprovacoes` | Aprovações |
| | `/despesas/reembolsos` | Reembolsos |
| | `/despesas/projetos` (+ `/{id}`) | Projetos |
| | `/despesas/categorias` | Categorias |
| | `/despesas/analise` | Análise |
| Integrações | `/integracoes` | Integrações |
| | `/integracoes/salesforce` | Salesforce |
| | `/integracoes/salesforce/oportunidades` (+ `/{id}`) | Oportunidades (SF) |
| | `/integracoes/salesforce/importar` | Importar (SF) |
| | `/integracoes/salesforce/mapeamento` | Mapeamento |
| | `/integracoes/salesforce/logs` | Logs |
| Dados & Marca | `/dados-marca` | Dados e Importação |
| | `/importar` | Importar |
| | `/marca` | Identidade Visual |

> O módulo de **Licenças** não faz parte do AFM_HSA — ele vive no seu próprio
> app (**Licencas_HSA**), com a mesma arquitetura.

---

## 6. Configuração e execução

`appsettings.json`:

| Chave | Função |
|---|---|
| `Data:Folder` | Pasta dos Parquet (rede: `\\BZVCPFIL003\proj_ramires$\DB\AFM_HSA`) |
| `Auth:Usuario` / `Auth:Senha` | Credencial única da equipe (padrão `howden` / `howden2026`) |
| `OpenBrowser` | `true` abre o navegador ao iniciar (modo por-usuário) |

Dois modos de operação:

- **Por usuário** (padrão): sobe em `http://localhost:5090` e abre o navegador
  automaticamente — cada pessoa roda o `.exe` na sua máquina, todas compartilhando
  a mesma pasta de rede.
- **Servidor central**: `AfmHsa.exe --urls http://0.0.0.0:5090` com
  `"OpenBrowser": false` — uma instância única servindo a equipe.

### Rodar no VS Code / dotnet

```bash
dotnet restore
dotnet run
# abre http://localhost:5090  (login padrão: howden / howden2026)
```
