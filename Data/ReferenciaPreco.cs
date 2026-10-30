using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>
/// Referências para apoiar a precificação de um item da proposta. Tudo vem da
/// base: lista de Nacionalização (custo) e histórico Aftermarket (última venda).
/// Nada é estimado ou inventado — o que não existe fica null e a tela diz isso.
/// As referências são só informativas: nunca preenchem o preço da proposta.
/// </summary>
public record ReferenciaPreco(
    bool Nacionalizado,
    decimal? Custo, string CustoData,
    decimal? UltimaVenda, string UltimaVendaData, bool UltimaVendaDoCliente)
{
    public static readonly ReferenciaPreco Vazia = new(false, null, "", null, "", false);

    /// <param name="partNumber">Partnumber do item.</param>
    /// <param name="clientUnitId">Planta da proposta: a última venda dela tem prioridade.</param>
    public static ReferenciaPreco Para(string? partNumber, string? clientUnitId,
        IReadOnlyList<Nacionalizacao> nacionais, IReadOnlyList<SalesRecord> vendas)
    {
        var pn = IntelligenceService.NormPn(partNumber);
        if (pn.Length == 0) return Vazia;

        // --- custo: só para peça nacionalizada --------------------------------
        var doPn = nacionais.Where(n => IntelligenceService.NormPn(n.PartNumber) == pn).ToList();
        var nacionalizado = doPn.Count > 0;
        decimal? custo = null;
        var custoData = "";
        if (nacionalizado)
        {
            // registro mais recente que tenha valor BR (unitário; ou total ÷ qnt.)
            var comValor = doPn
                .Select(n => (n, unit: n.ValorBr > 0 ? n.ValorBr
                                     : n.TotalBr > 0 && n.Quantidade > 0 ? n.TotalBr / n.Quantidade : 0m))
                .Where(x => x.unit > 0)
                .OrderByDescending(x => IntelligenceService.DataDaVenda(x.n.Data) ?? DateTime.MinValue)
                .FirstOrDefault();
            if (comValor.n is not null)
            {
                custo = Math.Round(comValor.unit, 2);
                custoData = IntelligenceService.DataDaVenda(comValor.n.Data) is DateTime d
                    ? d.ToString("yyyy-MM-dd") : "";
            }
        }

        // --- última venda: deste cliente primeiro; senão, de qualquer cliente --
        static decimal Unitario(SalesRecord v) =>
            v.ValorUnitario > 0 ? v.ValorUnitario : v.Quantidade > 0 && v.Valor > 0 ? v.Valor / v.Quantidade : 0m;

        var vendasPn = vendas.Where(v => IntelligenceService.NormPn(v.PartNumber) == pn && Unitario(v) > 0)
                             .OrderByDescending(v => IntelligenceService.DataDaVenda(v.DataVenda) ?? DateTime.MinValue)
                             .ToList();
        var doCliente = string.IsNullOrWhiteSpace(clientUnitId) ? null
            : vendasPn.FirstOrDefault(v => string.Equals((v.ClientUnitId ?? "").Trim(), clientUnitId.Trim(),
                                                         StringComparison.OrdinalIgnoreCase));
        var ultima = doCliente ?? vendasPn.FirstOrDefault();

        return new ReferenciaPreco(
            nacionalizado, custo, custoData,
            ultima is null ? null : Math.Round(Unitario(ultima), 2),
            ultima is null ? "" : IntelligenceService.DataDaVenda(ultima.DataVenda) is DateTime dv ? dv.ToString("yyyy-MM-dd") : "",
            doCliente is not null);
    }
}
