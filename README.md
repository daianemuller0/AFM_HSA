# AFM_HSA

**Howden Aftermarket Intelligence · AFM HSA** — inteligência comercial de
aftermarket para base instalada industrial.

Migração do app original (TypeScript/React) para a stack aprovada internamente:
**C# / .NET 8 / Blazor Server** com base de dados em **arquivos Parquet + DuckDB**
numa pasta de rede — a **mesma arquitetura do projeto Licencas_HSA**.

## Stack

- .NET 8 (ASP.NET Core) + **Blazor Server**
- **DuckDB** (motor, em memória) sobre **Parquet** (`DuckDB.NET.Data.Full`)
- Importação de Excel com `ClosedXML`
- Autenticação por cookie (credencial única da equipe)
- CSS puro (sem framework de front-end)

## Rodar localmente

Pré-requisito: **.NET 8 SDK**.

```bash
dotnet restore
dotnet run
```

- App: `http://localhost:5090` (abre o navegador sozinho no modo por-usuário)
- Login padrão: **howden / howden2026** (altere em `appsettings.json`)

## Base de dados

Arquivos Parquet numa pasta de rede, definida em `appsettings.json`:

```
Data:Folder = \\BZVCPFIL003\proj_ramires$\DB\AFM_HSA
```

Sem configuração, usa a pasta local `data/`. A base **nasce vazia**; os dados
entram pelas telas de importação/cadastro conforme cada módulo é implementado.

## Documentação

Veja **[ARQUITETURA.md](./ARQUITETURA.md)** para o desenho completo (camada de
dados Parquet/DuckDB, autenticação, lista de módulos e como adicionar entidades).
