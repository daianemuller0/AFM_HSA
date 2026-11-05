using System.Globalization;

namespace AfmHsa.Data;

/// <summary>
/// O custo que uma proposta usou para um part number: quem colocar o mesmo PN
/// depois vê a referência (valor, data, modelo do equipamento e serial number).
/// </summary>
public sealed class ReferenciaDeCusto
{
    /// <summary>O id do item da proposta: salvar de novo atualiza a mesma referência.</summary>
    public string Id { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string Descricao { get; set; } = "";
    /// <summary>Custo unitário em R$ (sem o TA).</summary>
    public decimal Valor { get; set; }
    /// <summary>TA usado na proposta para chegar no preço meta (R$, unitário); 0 = sem TA.</summary>
    public decimal Ta { get; set; }
    /// <summary>yyyy-MM-dd.</summary>
    public string Data { get; set; } = "";
    public string Modelo { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    /// <summary>"digitado" ou "importação".</summary>
    public string Origem { get; set; } = "";
    public string PropostaId { get; set; } = "";
    /// <summary>"OF-…-00".</summary>
    public string Proposta { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string Usuario { get; set; } = "";

    public DateTime? DataComoData =>
        DateTime.TryParseExact(Data, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}

/// <summary>As referências de custo (entidade "referencias_custo"), gravadas ao salvar a proposta.</summary>
public sealed class ReferenciaDeCustoRepository
{
    private const string Entidade = "referencias_custo";
    private readonly ParquetStore _store;
    public ReferenciaDeCustoRepository(ParquetStore store) => _store = store;

    public List<ReferenciaDeCusto> Todas() => _store
        .ReadLatest(Entidade,
            "id, part_number, descricao, valor, ta, data, modelo, serial_number, origem, proposta_id, proposta, cliente, usuario",
            r => new ReferenciaDeCusto
            {
                Id = Fmt.S(r, 0), PartNumber = Fmt.S(r, 1), Descricao = Fmt.S(r, 2), Valor = Fmt.Dec(Fmt.S(r, 3)),
                Ta = Fmt.Dec(Fmt.S(r, 4)), Data = Fmt.S(r, 5), Modelo = Fmt.S(r, 6), SerialNumber = Fmt.S(r, 7),
                Origem = Fmt.S(r, 8), PropostaId = Fmt.S(r, 9), Proposta = Fmt.S(r, 10), Cliente = Fmt.S(r, 11),
                Usuario = Fmt.S(r, 12),
            })
        .OrderByDescending(r => r.Data).ThenByDescending(r => r.Proposta)
        .ToList();

    /// <summary>A referência mais recente do part number, de outra proposta (null = nenhuma).</summary>
    public static ReferenciaDeCusto? MaisRecente(IEnumerable<ReferenciaDeCusto> todas, string? partNumber, string propostaAtual)
    {
        var pn = IntelligenceService.NormPn(partNumber);
        if (pn.Length == 0) return null;
        return todas.Where(r => r.PropostaId != propostaAtual && IntelligenceService.NormPn(r.PartNumber) == pn && r.Valor > 0)
            .OrderByDescending(r => r.Data).ThenByDescending(r => r.Proposta).FirstOrDefault();
    }

    public void Gravar(ReferenciaDeCusto r) => _store.WriteRow(Entidade, new KeyValuePair<string, object?>[]
    {
        new("id", r.Id), new("part_number", r.PartNumber), new("descricao", r.Descricao),
        new("valor", r.Valor.ToString(CultureInfo.InvariantCulture)), new("ta", r.Ta.ToString(CultureInfo.InvariantCulture)),
        new("data", r.Data), new("modelo", r.Modelo), new("serial_number", r.SerialNumber), new("origem", r.Origem),
        new("proposta_id", r.PropostaId), new("proposta", r.Proposta), new("cliente", r.Cliente), new("usuario", r.Usuario),
    });

    public void Apagar(string id) => _store.WriteRow(Entidade, new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);
}
