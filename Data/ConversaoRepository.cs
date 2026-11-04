using System.Globalization;

namespace AfmHsa.Data;

/// <summary>
/// As taxas que a equipe digita na aba Conversão, escritas do jeito que se
/// fala — quanto vale <b>uma</b> unidade da moeda mais forte: "1 USD = 5,40 R$",
/// "1 EUR = 6,20 R$", "1 PEN = 1,45 R$" e "1 R$ = 175 CLP".
/// Mesma ideia da aba Conversão do axiaisvent (branch "Proposta v2"): lá a
/// taxa é CLP por USD; aqui o custo é em real, então as taxas são do real
/// para as moedas em que o preço sai.
/// </summary>
public sealed class TaxasDeConversao
{
    public decimal UsdEmReais { get; set; }
    public decimal EurEmReais { get; set; }
    public decimal PenEmReais { get; set; }
    public decimal ClpPorReal { get; set; }

    /// <summary>Quando as taxas foram gravadas (ISO, para ordenar e ler sem cultura).</summary>
    public string AtualizadaEm { get; set; } = "";

    public DateTime? Data => DateTime.TryParse(AtualizadaEm, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>
    /// A taxa de uma moeda do preço, no sentido em que ela é escrita: as
    /// moedas mais fortes que o real em "1 moeda = X R$" (VendaEmCusto), o peso
    /// chileno em "1 R$ = X CLP" (CustoEmVenda). Null quando não há taxa.
    /// </summary>
    public (decimal Valor, string Direcao)? Para(Moeda m) => m switch
    {
        Moeda.Usd when UsdEmReais > 0 => (UsdEmReais, "VendaEmCusto"),
        Moeda.Eur when EurEmReais > 0 => (EurEmReais, "VendaEmCusto"),
        Moeda.Pen when PenEmReais > 0 => (PenEmReais, "VendaEmCusto"),
        Moeda.Clp when ClpPorReal > 0 => (ClpPorReal, "CustoEmVenda"),
        _ => null,
    };
}

/// <summary>
/// Guarda as taxas da aba Conversão (entidade "taxas_conversao", uma linha só,
/// como no axiaisvent). As propostas guardam a taxa delas: mudar aqui não mexe
/// em proposta salva — a tela da proposta só oferece a taxa nova.
/// </summary>
public sealed class ConversaoRepository
{
    private const string Entidade = "taxas_conversao";
    private const string IdUnico = "atual";

    /// <summary>Depois de tantos dias a taxa trazida pede conferência (DiasDaTaxa do axiaisvent).</summary>
    public const int DiasDaTaxa = 7;

    private readonly ParquetStore _store;
    public ConversaoRepository(ParquetStore store) => _store = store;

    private static string D(decimal v) => v.ToString(CultureInfo.InvariantCulture);

    public TaxasDeConversao Ler() => _store
        .ReadLatest(Entidade, "id, usd_em_reais, eur_em_reais, pen_em_reais, clp_por_real, atualizada_em", r => new TaxasDeConversao
        {
            UsdEmReais = Fmt.Dec(Fmt.S(r, 1)), EurEmReais = Fmt.Dec(Fmt.S(r, 2)),
            PenEmReais = Fmt.Dec(Fmt.S(r, 3)), ClpPorReal = Fmt.Dec(Fmt.S(r, 4)), AtualizadaEm = Fmt.S(r, 5),
        })
        .FirstOrDefault() ?? new TaxasDeConversao();

    public void Salvar(TaxasDeConversao t)
    {
        t.AtualizadaEm = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        _store.WriteRow(Entidade, new KeyValuePair<string, object?>[]
        {
            new("id", IdUnico),
            new("usd_em_reais", D(t.UsdEmReais)), new("eur_em_reais", D(t.EurEmReais)),
            new("pen_em_reais", D(t.PenEmReais)), new("clp_por_real", D(t.ClpPorReal)),
            new("atualizada_em", t.AtualizadaEm),
        });
    }

    /// <summary>A taxa gravada tem mais de <see cref="DiasDaTaxa"/> dias: o câmbio pode ter andado.</summary>
    public static bool Velha(TaxasDeConversao t) => t.Data is { } d && (DateTime.Now - d).TotalDays > DiasDaTaxa;
}
