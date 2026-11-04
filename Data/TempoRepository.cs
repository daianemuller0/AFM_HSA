using System.Globalization;
using System.Text.Json;

namespace AfmHsa.Data;

/// <summary>
/// Quanto tempo de trabalho uma proposta levou até virar documento — o mesmo
/// cronômetro do axiaisvent (HowdenAxiais.Poc.Data.TempoProposta, "Proposta v2").
///
/// A tela Gerar Proposta soma o tempo entre uma interação e outra (pausas de
/// mais de 3 minutos não contam) e, a cada Word gerado, grava uma linha aqui.
/// É com estes números, e não com estimativa, que se mostra se a meta de
/// reduzir o tempo da proposta foi batida — e em que etapa ela ainda demora.
/// </summary>
public sealed class TempoProposta
{
    public string Id { get; set; } = "";
    public string PropostaId { get; set; } = "";
    public string Numero { get; set; } = "";
    public string Revisao { get; set; } = "";
    public string Usuario { get; set; } = "";

    /// <summary>Quando a pessoa começou a mexer nesta proposta (ISO, hora local).</summary>
    public string Inicio { get; set; } = "";

    /// <summary>Quando o documento foi gerado (ISO, hora local).</summary>
    public string Fim { get; set; } = "";

    /// <summary>Segundos de trabalho, sem as pausas.</summary>
    public int SegundosAtivos { get; set; }

    /// <summary>Quantas vezes a tela respondeu a um clique ou campo.</summary>
    public int Interacoes { get; set; }

    /// <summary>O que foi gerado: "word".</summary>
    public string Evento { get; set; } = "";

    /// <summary>Os segundos em cada etapa, em JSON ({"2": 140, "3": 310…}).</summary>
    public string PorEtapa { get; set; } = "";

    /// <summary>De onde a proposta partiu: "nova", "copia", "aberta" ou "revisao".</summary>
    public string Origem { get; set; } = "";

    /// <summary>Quantos itens a proposta tinha ao gerar.</summary>
    public int Itens { get; set; }

    public Dictionary<string, int> PorEtapaComoMapa => Mapa(PorEtapa);

    public static Dictionary<string, int> Mapa(string json)
    {
        if (json.Trim().Length == 0) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public DateTime? FimComoData =>
        DateTime.TryParse(Fim, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}

/// <summary>Os tempos gravados (entidade "tempos_proposta"), uma linha por Word gerado.</summary>
public sealed class TempoRepository
{
    private const string Entidade = "tempos_proposta";
    private const string EntidadeReferencia = "tempo_referencia";

    private readonly ParquetStore _store;
    public TempoRepository(ParquetStore store) => _store = store;

    public List<TempoProposta> Todos() => _store
        .ReadLatest(Entidade,
            "id, proposta_id, numero, revisao, usuario, inicio, fim, segundos_ativos, interacoes, evento, " +
            "por_etapa, origem, itens",
            r => new TempoProposta
            {
                Id = Fmt.S(r, 0), PropostaId = Fmt.S(r, 1), Numero = Fmt.S(r, 2), Revisao = Fmt.S(r, 3),
                Usuario = Fmt.S(r, 4), Inicio = Fmt.S(r, 5), Fim = Fmt.S(r, 6),
                SegundosAtivos = Fmt.Int(Fmt.S(r, 7)), Interacoes = Fmt.Int(Fmt.S(r, 8)), Evento = Fmt.S(r, 9),
                PorEtapa = Fmt.S(r, 10), Origem = Fmt.S(r, 11), Itens = Fmt.Int(Fmt.S(r, 12)),
            })
        .OrderByDescending(t => t.Fim)
        .ToList();

    public void Registrar(TempoProposta t)
    {
        if (t.Id.Length == 0) t.Id = Guid.NewGuid().ToString("n");
        _store.WriteRow(Entidade, new KeyValuePair<string, object?>[]
        {
            new("id", t.Id), new("proposta_id", t.PropostaId), new("numero", t.Numero),
            new("revisao", t.Revisao), new("usuario", t.Usuario),
            new("inicio", t.Inicio), new("fim", t.Fim),
            new("segundos_ativos", t.SegundosAtivos.ToString(CultureInfo.InvariantCulture)),
            new("interacoes", t.Interacoes.ToString(CultureInfo.InvariantCulture)),
            new("evento", t.Evento), new("por_etapa", t.PorEtapa), new("origem", t.Origem),
            new("itens", t.Itens.ToString(CultureInfo.InvariantCulture)),
        });
    }

    /// <summary>
    /// O tempo de cada proposta: a PRIMEIRA geração do Word de cada número e
    /// revisão. Gerar de novo depois (para corrigir uma vírgula) não conta
    /// como outra proposta montada.
    /// </summary>
    public static List<TempoProposta> PrimeiraDeCada(IEnumerable<TempoProposta> tempos) => tempos
        .Where(t => t.Numero.Length > 0)
        .GroupBy(t => (t.Numero, t.Revisao.Trim()))
        .Select(g => g.OrderBy(t => t.Fim).First())
        .OrderByDescending(t => t.Fim)
        .ToList();

    /// <summary>
    /// O tempo de uma proposta ANTES da tela por etapas, em minutos — a base
    /// da meta (a tela antiga não era medida). 0 = ainda não informado.
    /// </summary>
    public int MinutosDeReferencia() => _store
        .ReadLatest(EntidadeReferencia, "id, minutos", r => Fmt.Int(Fmt.S(r, 1)))
        .FirstOrDefault();

    public void SalvarReferencia(int minutos) => _store.WriteRow(EntidadeReferencia, new KeyValuePair<string, object?>[]
    {
        new("id", "atual"), new("minutos", Math.Max(0, minutos).ToString(CultureInfo.InvariantCulture)),
    });

    /// <summary>A meta: metade do tempo de referência.</summary>
    public const decimal Meta = 0.5m;

    public static double Mediana(IEnumerable<double> valores)
    {
        var v = valores.OrderBy(x => x).ToList();
        if (v.Count == 0) return 0;
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }
}
