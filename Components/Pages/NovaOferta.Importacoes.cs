using AfmHsa.Data;
using AfmHsa.Models;
using Microsoft.AspNetCore.Components;

namespace AfmHsa.Components.Pages;

/// <summary>
/// Importação por origem: cada item importado diz de que BU de fora vem
/// (junta da França, rotor da Alemanha, mancal da China…), e cada origem é
/// uma importação, com a moeda e a taxa dela. "Por total" = uma importação
/// por origem; "por item" = uma por item. A origem vazia é a importação das
/// propostas antigas (moeda e taxa da proposta).
/// </summary>
public partial class NovaOferta
{
    private static string ChaveDaOrigem(string? origem) => (origem ?? "").Trim();

    private string OrigemDe(CommercialOfferItem it) => ChaveDaOrigem(it.OrigemImportacao);

    private static string NomeDaOrigem(string origem) => origem.Length > 0 ? origem : "origem não informada";

    /// <summary>As origens dos itens importados, na ordem em que aparecem.</summary>
    private List<string> OrigensDosImportados => _items.Where(Imp).Select(OrigemDe)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private ImportacaoDaOrigem? ConfigDe(string origem) =>
        _o.Importacoes.FirstOrDefault(c => string.Equals(ChaveDaOrigem(c.Origem), origem, StringComparison.OrdinalIgnoreCase));

    private string MoedaDe(string origem) =>
        origem.Length == 0 ? _o.Moeda : ConfigDe(origem)?.Moeda ?? ImportacoesDaProposta.MoedaProvavel(origem);

    private decimal TaxaDe(string origem) => origem.Length == 0 ? _o.TaxaCambio : ConfigDe(origem)?.Taxa ?? 0m;

    /// <summary>
    /// A importação da origem (criada na primeira vez): a moeda pela origem
    /// (França, Alemanha… → EUR; Reino Unido → GBP; o resto → USD) e, se
    /// outra importação da proposta já tem taxa nessa moeda, a mesma taxa.
    /// </summary>
    private ImportacaoDaOrigem GarantirImportacao(string origem, string? moeda = null)
    {
        if (ConfigDe(origem) is { } c) return c;
        var m = moeda ?? ImportacoesDaProposta.MoedaProvavel(origem);
        var taxa = _o.Importacoes.Where(x => x.Moeda == m && x.Taxa > 0m).Select(x => x.Taxa).FirstOrDefault();
        if (taxa == 0m && _o.Moeda == m && _o.TaxaCambio > 0m) taxa = _o.TaxaCambio;
        c = new ImportacaoDaOrigem { Origem = origem, Moeda = m, Taxa = taxa };
        _o.Importacoes.Add(c);
        return c;
    }

    private void MudarOrigemDe(CommercialOfferItem it, ChangeEventArgs e)
    {
        it.OrigemImportacao = ChaveDaOrigem(e.Value?.ToString());
        if (it.OrigemImportacao.Length > 0) GarantirImportacao(it.OrigemImportacao);
    }

    private void MudarMoedaDaOrigem(string origem, ChangeEventArgs e)
    {
        var m = e.Value?.ToString() ?? "EUR";
        if (origem.Length == 0) _o.Moeda = m;
        else GarantirImportacao(origem).Moeda = m;
    }

    private void MudarTaxaDaOrigem(string origem, ChangeEventArgs e)
    {
        var t = Numero(e) is decimal v && v > 0 ? v : 0m;
        if (origem.Length == 0) _o.TaxaCambio = t;
        else GarantirImportacao(origem).Taxa = t;
    }

    /// <summary>As importações que não têm mais item saem (ao salvar).</summary>
    private void LimparImportacoes()
    {
        var usadas = OrigensDosImportados.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _o.Importacoes.RemoveAll(c => !usadas.Contains(ChaveDaOrigem(c.Origem)));
    }

    /// <summary>As sugestões de origem: as unidades Howden de fora, as já digitadas e as desta proposta.</summary>
    private IEnumerable<string> OrigensConhecidas() => PedidoIntercompany.Unidades
        .Concat(_listas.Where(l => l.Lista is ListasCompartilhadasRepository.UnidadeIntercompany or ListasCompartilhadasRepository.OrigemImportacao).Select(l => l.Valor))
        .Concat(_items.Select(i => i.OrigemImportacao))
        .Select(ChaveDaOrigem).Where(o => o.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(o => o, StringComparer.OrdinalIgnoreCase);

    /// <summary>Uma importação (uma origem) na tela: itens, valor na moeda, e o custo nos dois modos.</summary>
    private sealed record GrupoDeImportacao(string Origem, string Moeda, decimal Taxa, int Itens, decimal ValorMoeda,
        List<string> Pendencias, decimal? CustoPorTotal, decimal? CustoPorItem);
}
