namespace AfmHsa.Data;

/// <summary>Uma condição comercial que a equipe incluiu na base pelo "+" da proposta.</summary>
public sealed record CondicaoComercial(string Id, string Tipo, string Texto);

/// <summary>
/// As condições de pagamento e os locais de entrega que não estão no modelo
/// e foram incluídos pela proposta (o "+" ao lado da lista). Ficam na base e
/// aparecem nas listas de todas as propostas, depois das opções do modelo.
/// </summary>
public sealed class CondicoesComerciaisRepository
{
    public const string Pagamento = "pagamento";
    public const string Entrega = "entrega";
    private const string Entidade = "condicoes_comerciais";

    private readonly ParquetStore _store;
    public CondicoesComerciaisRepository(ParquetStore store) => _store = store;

    public List<CondicaoComercial> Do(string tipo) => _store
        .ReadLatest(Entidade, "id, tipo, texto", r => new CondicaoComercial(Fmt.S(r, 0), Fmt.S(r, 1), Fmt.S(r, 2)))
        .Where(c => c.Tipo == tipo && c.Texto.Trim().Length > 0)
        .OrderBy(c => c.Texto, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>Inclui o texto (se ainda não existe igual) e devolve a condição gravada.</summary>
    public CondicaoComercial Incluir(string tipo, string texto)
    {
        var t = (texto ?? "").Trim();
        var existente = Do(tipo).FirstOrDefault(c => string.Equals(c.Texto.Trim(), t, StringComparison.OrdinalIgnoreCase));
        if (existente is not null) return existente;
        var nova = new CondicaoComercial(Guid.NewGuid().ToString("N"), tipo, t);
        _store.WriteRow(Entidade, new KeyValuePair<string, object?>[] { new("id", nova.Id), new("tipo", nova.Tipo), new("texto", nova.Texto) });
        return nova;
    }

    public void Apagar(string id) => _store.WriteRow(Entidade, new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);
}
