namespace AfmHsa.Data;

// Popula os dados iniciais (seed) apenas se a pasta ainda estiver vazia.
// Cada registro do seed é gravado como um Parquet, igual a qualquer edição.
//
// Nesta primeira versão da migração o AFM_HSA NASCE COM A BASE VAZIA: não há
// entidades semeadas. Conforme cada módulo for implementado, o seed de cada
// entidade entra aqui (mesmo padrão do Licencas_HSA), por exemplo:
//
//     if (store.IsEmpty("categoriasDespesa"))
//         foreach (var c in DespesaSeed.Categorias)
//             store.WriteRow("categoriasDespesa", ...);
public static class DbInitializer
{
    public static void Initialize(ParquetStore store)
    {
        // Login é geral (appsettings Auth:Usuario/Auth:Senha) — não há tabela de usuários.
        // Base de dados inicia vazia; os dados reais entram pelas telas de importação/cadastro.
    }
}
