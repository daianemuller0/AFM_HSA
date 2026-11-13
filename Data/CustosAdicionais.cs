using System.Text.Json;
using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>
/// Os custos adicionais da proposta — embalagens, engenharia, exportação e
/// outros. Cada um tem um padrão (aba Dados → Custos padrão): um valor fixo
/// em R$ ou um % do custo dos itens. A proposta nasce com o padrão do dia e
/// dá para trocar o valor nela. Eles entram na tabela CÁLCULO DE CUSTO do
/// pricing: engenharia na linha 26 (ENGENHARIA/INSPEÇÕES), embalagens,
/// exportação e outros nas linhas 29, 30 e 31 (livres no modelo).
/// </summary>
public static class CustosAdicionaisDaProposta
{
    /// <summary>O código é o da linha de custo do pricing onde o valor entra.</summary>
    public static readonly (string Codigo, string Nome)[] Itens =
    {
        ("EMBALAGEM", "Embalagens"),
        ("ENGENHARIA", "Engenharia"),
        ("EXPORTACAO", "Exportação"),
        ("OUTROS", "Outros"),
    };

    public static string NomeDe(string codigo) => Itens.FirstOrDefault(i => i.Codigo == codigo).Nome ?? codigo;

    /// <summary>Os padrões, um por item, na ordem da tela (o que faltar no gravado entra zerado).</summary>
    public static List<CustoAdicional> Completos(IEnumerable<CustoAdicional>? gravados)
    {
        var lista = gravados?.ToList() ?? new();
        return Itens.Select(i => lista.FirstOrDefault(c => c.Codigo == i.Codigo) is { } c
                ? new CustoAdicional { Codigo = c.Codigo, Tipo = c.Tipo == CustoAdicional.Percentual ? CustoAdicional.Percentual : CustoAdicional.Fixo, Padrao = c.Padrao, Valor = c.Valor }
                : new CustoAdicional { Codigo = i.Codigo })
            .ToList();
    }

    /// <summary>A proposta nova (ou a revisão de uma de antes destes custos) leva os padrões do dia.</summary>
    public static List<CustoAdicional> ParaProposta(IEnumerable<CustoAdicional> padroes) =>
        Completos(padroes).Select(c => { c.Valor = null; return c; }).ToList();

    /// <summary>
    /// O padrão já em dinheiro: % sobre o custo dos itens; o valor fixo é em
    /// R$ e só vale quando o custo da proposta é em R$ (null = não dá para
    /// usar; a pessoa digita o valor na moeda do custo).
    /// </summary>
    public static decimal? PadraoEmDinheiro(CommercialOffer o, CustoAdicional c, decimal custoDosItens) =>
        c.Tipo == CustoAdicional.Percentual ? Math.Round(c.Padrao * custoDosItens, 2)
        : Cambio.MoedaDoCusto(o) == Moeda.Brl ? c.Padrao
        : c.Padrao == 0m ? 0m : null;

    /// <summary>O valor que entra no pricing: o digitado na proposta, ou o padrão.</summary>
    public static decimal Valor(CommercialOffer o, CustoAdicional c, decimal custoDosItens) =>
        c.Valor ?? PadraoEmDinheiro(o, c, custoDosItens) ?? 0m;

    /// <summary>O total dos custos adicionais que caem nesta linha do pricing.</summary>
    public static decimal NaLinha(CommercialOffer o, string codigoDaLinha, decimal custoDosItens) =>
        o.CustosAdicionais.Where(c => c.Codigo == codigoDaLinha).Sum(c => Valor(o, c, custoDosItens));
}

/// <summary>Os custos padrão (entidade "custos_padrao", uma linha só).</summary>
public sealed class CustosPadraoRepository
{
    private const string Entidade = "custos_padrao";
    private const string Id = "padrao";
    private readonly ParquetStore _s;
    public CustosPadraoRepository(ParquetStore s) => _s = s;

    public List<CustoAdicional> Ler()
    {
        var json = _s.ReadLatest(Entidade, "id, dados", r => (Id: Fmt.S(r, 0), Dados: Fmt.S(r, 1)))
            .FirstOrDefault(x => x.Id == Id).Dados;
        List<CustoAdicional>? lista = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try { lista = JsonSerializer.Deserialize<List<CustoAdicional>>(json); }
            catch (JsonException) { lista = null; }
        }
        return CustosAdicionaisDaProposta.Completos(lista).Select(c => { c.Valor = null; return c; }).ToList();
    }

    public void Gravar(IEnumerable<CustoAdicional> padroes) =>
        _s.WriteRow(Entidade, new KeyValuePair<string, object?>[]
        {
            new("id", Id),
            new("dados", JsonSerializer.Serialize(CustosAdicionaisDaProposta.Completos(padroes).Select(c => new { c.Codigo, c.Tipo, c.Padrao }))),
        });
}
