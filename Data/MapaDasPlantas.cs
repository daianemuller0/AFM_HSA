using System.Globalization;
using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>
/// Onde fica cada planta, para o mapa de Clientes e Unidades. Pela ordem:
/// a posição marcada à mão no mapa; senão a da cidade (municípios do IBGE,
/// em wwwroot/dados/municipios.csv); senão o centro do estado (aproximado).
/// </summary>
public static class MapaDasPlantas
{
    public const string CaminhoDasCidades = "wwwroot/dados/municipios.csv";

    public const string Manual = "manual";
    public const string Cidade = "cidade";
    public const string Estado = "estado";

    private static readonly Lazy<(Dictionary<string, (double, double)> Cidades, Dictionary<string, List<(string Uf, double Lat, double Lon)>> PorNome, Dictionary<string, (double, double)> Estados)> _dados = new(Ler);

    private static (Dictionary<string, (double, double)>, Dictionary<string, List<(string, double, double)>>, Dictionary<string, (double, double)>) Ler()
    {
        var cidades = new Dictionary<string, (double, double)>();
        var porNome = new Dictionary<string, List<(string, double, double)>>();
        var estados = new Dictionary<string, (double, double)>();
        var caminho = Path.Combine(AppContext.BaseDirectory, CaminhoDasCidades);
        if (!File.Exists(caminho)) caminho = CaminhoDasCidades;
        if (!File.Exists(caminho)) return (cidades, porNome, estados);
        foreach (var linha in File.ReadLines(caminho))
        {
            if (linha.StartsWith('#')) continue;
            var p = linha.Split(';');
            if (p.Length < 4) continue;
            if (!double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) continue;
            if (p[0] == "UF") { estados[p[1].ToUpperInvariant()] = (lat, lon); continue; }
            var nome = Textos.Simples(p[1]);
            cidades[$"{p[0].ToUpperInvariant()}|{nome}"] = (lat, lon);
            if (!porNome.TryGetValue(nome, out var l)) porNome[nome] = l = new();
            l.Add((p[0].ToUpperInvariant(), lat, lon));
        }
        return (cidades, porNome, estados);
    }

    /// <summary>"Itatiba - SP", "Itatiba/SP" e "Itatiba (SP)" → ("itatiba", "SP").</summary>
    private static (string Nome, string Uf) Separar(string? cidade, string? estado)
    {
        var c = (cidade ?? "").Trim();
        var uf = (estado ?? "").Trim().ToUpperInvariant();
        foreach (var sep in new[] { " - ", "/", " – ", "-" })
        {
            var i = c.LastIndexOf(sep, StringComparison.Ordinal);
            if (i > 0 && c[(i + sep.Length)..].Trim() is { Length: 2 } sigla && sigla.All(char.IsLetter))
            {
                if (uf.Length == 0) uf = sigla.ToUpperInvariant();
                c = c[..i].Trim();
                break;
            }
        }
        if (c.EndsWith(')') && c.LastIndexOf('(') is var a and > 0 && c[(a + 1)..^1].Trim() is { Length: 2 } s2)
        {
            if (uf.Length == 0) uf = s2.ToUpperInvariant();
            c = c[..a].Trim();
        }
        return (Textos.Simples(c), uf.Length == 2 ? uf : "");
    }

    /// <summary>A posição da planta e de onde ela veio; null = sem cidade conhecida (marque no mapa).</summary>
    public static (double Lat, double Lon, string Origem)? Localizar(ClientUnit u, IReadOnlyDictionary<string, PosicaoDaPlanta> manuais)
    {
        if (manuais.TryGetValue(u.Id, out var m)) return (m.Lat, m.Lon, Manual);
        var (cidades, porNome, estados) = _dados.Value;
        var (nome, uf) = Separar(u.Cidade, u.Estado);
        if (nome.Length > 0)
        {
            if (uf.Length > 0 && cidades.TryGetValue($"{uf}|{nome}", out var c)) return (c.Item1, c.Item2, Cidade);
            // sem UF: só se o nome for de uma cidade só
            if (uf.Length == 0 && porNome.TryGetValue(nome, out var l) && l.Count == 1) return (l[0].Lat, l[0].Lon, Cidade);
        }
        if (uf.Length > 0 && estados.TryGetValue(uf, out var e)) return (e.Item1, e.Item2, Estado);
        return null;
    }
}

/// <summary>Uma posição marcada à mão no mapa.</summary>
public sealed record PosicaoDaPlanta(string Id, double Lat, double Lon, string Usuario, string Data);

/// <summary>
/// As posições marcadas à mão (entidade "posicoes_plantas"), à parte do
/// cadastro da planta: reimportar a base instalada não apaga o que foi ajustado.
/// </summary>
public sealed class PosicaoDaPlantaRepository
{
    private const string Entidade = "posicoes_plantas";
    private readonly ParquetStore _store;
    public PosicaoDaPlantaRepository(ParquetStore store) => _store = store;

    public Dictionary<string, PosicaoDaPlanta> Todas() => _store
        .ReadLatest(Entidade, "id, lat, lon, usuario, data", r => new PosicaoDaPlanta(
            Fmt.S(r, 0),
            double.TryParse(Fmt.S(r, 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var la) ? la : double.NaN,
            double.TryParse(Fmt.S(r, 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var lo) ? lo : double.NaN,
            Fmt.S(r, 3), Fmt.S(r, 4)))
        .Where(p => !double.IsNaN(p.Lat) && !double.IsNaN(p.Lon))
        .GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.Last());

    public void Gravar(string id, double lat, double lon, string usuario) => _store.WriteRow(Entidade, new KeyValuePair<string, object?>[]
    {
        new("id", id), new("lat", Math.Round(lat, 6).ToString(CultureInfo.InvariantCulture)),
        new("lon", Math.Round(lon, 6).ToString(CultureInfo.InvariantCulture)), new("usuario", usuario),
        new("data", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
    });

    public void Apagar(string id) => _store.WriteRow(Entidade, new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);
}
