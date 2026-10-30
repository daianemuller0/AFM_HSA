using System.Globalization;
using AfmHsa.Models;

namespace AfmHsa.Data;

public class NacionalizacaoRepository
{
    private readonly ParquetStore _s;
    public NacionalizacaoRepository(ParquetStore s) => _s = s;

    private const string Entidade = "nacionalizacao";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public List<Nacionalizacao> All() => _s.ReadLatest(Entidade,
        "id, data, projeto, cod_hsa, part_number, descricao_alterada, quantidade, " +
        "valor_intercompany, valor_total_intercompany, valor_br, total_br, ja_vendido",
        r => new Nacionalizacao
        {
            Id = Fmt.S(r, 0), Data = Fmt.S(r, 1), Projeto = Fmt.S(r, 2), CodHsa = Fmt.S(r, 3),
            PartNumber = Fmt.S(r, 4), DescricaoAlterada = Fmt.S(r, 5),
            Quantidade = Fmt.Dec(Fmt.S(r, 6)),
            ValorIntercompany = Fmt.Dec(Fmt.S(r, 7)), ValorTotalIntercompany = Fmt.Dec(Fmt.S(r, 8)),
            ValorBr = Fmt.Dec(Fmt.S(r, 9)), TotalBr = Fmt.Dec(Fmt.S(r, 10)),
            JaVendido = Fmt.Bool(Fmt.S(r, 11)),
        }, orderBy: "data DESC");

    /// <summary>Partnumbers nacionalizados, já normalizados para comparação.</summary>
    public HashSet<string> PartNumbers() =>
        All().Select(n => IntelligenceService.NormPn(n.PartNumber))
             .Where(pn => pn.Length > 0)
             .ToHashSet(StringComparer.Ordinal);

    public void Save(Nacionalizacao n) => _s.WriteRow(Entidade, Linha(n));
    public void Delete(Nacionalizacao n) => _s.WriteRow(Entidade, Linha(n), deleted: true);
    public void Clear() => _s.Clear(Entidade);

    public static IReadOnlyList<KeyValuePair<string, object?>> Linha(Nacionalizacao n) =>
        new KeyValuePair<string, object?>[]
        {
            new("id", n.Id), new("data", n.Data), new("projeto", n.Projeto), new("cod_hsa", n.CodHsa),
            new("part_number", n.PartNumber), new("descricao_alterada", n.DescricaoAlterada),
            new("quantidade", n.Quantidade.ToString(Inv)),
            new("valor_intercompany", n.ValorIntercompany.ToString(Inv)),
            new("valor_total_intercompany", n.ValorTotalIntercompany.ToString(Inv)),
            new("valor_br", n.ValorBr.ToString(Inv)), new("total_br", n.TotalBr.ToString(Inv)),
            new("ja_vendido", n.JaVendido ? "true" : "false"),
        };
}
