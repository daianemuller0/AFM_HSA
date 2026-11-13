using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AfmHsa.Data;

/// <summary>Um valor de uma lista suspensa que alguém já digitou.</summary>
/// <param name="Chave">o que ele acompanha (o modelo para serial e TAG, o PN normalizado para a descrição); "" = solto</param>
/// <param name="Extra">um dado a mais (a descrição do PN, o e-mail da unidade)</param>
public sealed record ValorDeLista(string Lista, string Chave, string Valor, string Extra, string Usuario, string Data);

/// <summary>
/// As listas suspensas que aprendem: o que alguém digita uma vez (modelo do
/// compressor, part number com a descrição, serial e TAG de um modelo,
/// unidade Intercompany com o e-mail) fica gravado e aparece na lista para
/// todos (entidade "listas_compartilhadas"). O mesmo valor de novo só
/// atualiza o que já existe.
/// </summary>
public sealed class ListasCompartilhadasRepository
{
    public const string Modelo = "modelo";
    public const string PartNumber = "pn";
    public const string Serial = "serial";
    public const string Tag = "tag";
    public const string UnidadeIntercompany = "unidade_ic";
    /// <summary>A BU de fora de onde vem um item importado.</summary>
    public const string OrigemImportacao = "origem_imp";

    private const string Entidade = "listas_compartilhadas";
    private readonly ParquetStore _store;
    public ListasCompartilhadasRepository(ParquetStore store) => _store = store;

    public List<ValorDeLista> Todas() => _store
        .ReadLatest(Entidade, "id, lista, chave, valor, extra, usuario, data",
            r => new ValorDeLista(Fmt.S(r, 1), Fmt.S(r, 2), Fmt.S(r, 3), Fmt.S(r, 4), Fmt.S(r, 5), Fmt.S(r, 6)))
        .Where(v => v.Valor.Trim().Length > 0)
        .OrderByDescending(v => v.Data).ToList();

    /// <summary>Grava os valores (os vazios e os iguais aos que já estão gravados ficam de fora).</summary>
    public void Gravar(IEnumerable<(string Lista, string Chave, string Valor, string Extra)> valores, IReadOnlyCollection<ValorDeLista> existentes, string usuario)
    {
        var jaTem = existentes.ToDictionary(v => Id(v.Lista, v.Chave, v.Valor), v => v.Extra);
        var hoje = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var linhas = new List<IReadOnlyList<KeyValuePair<string, object?>>>();
        var vistos = new HashSet<string>();
        foreach (var (lista, chave, valor, extra) in valores)
        {
            var v = (valor ?? "").Trim();
            if (v.Length == 0) continue;
            var c = (chave ?? "").Trim();
            var x = (extra ?? "").Trim();
            var id = Id(lista, c, v);
            if (!vistos.Add(id)) continue;
            if (jaTem.TryGetValue(id, out var extraGravado) && (x.Length == 0 || extraGravado == x)) continue;
            linhas.Add(new KeyValuePair<string, object?>[]
            {
                new("id", id), new("lista", lista), new("chave", c), new("valor", v), new("extra", x),
                new("usuario", usuario ?? ""), new("data", hoje),
            });
        }
        if (linhas.Count > 0) _store.WriteBatch(Entidade, linhas);
    }

    private static string Id(string lista, string chave, string valor) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{lista}|{Textos.Simples(chave)}|{Textos.Simples(valor)}")))[..24].ToLowerInvariant();
}
