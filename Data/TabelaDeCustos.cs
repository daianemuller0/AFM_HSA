using System.Globalization;
using System.Text;
using ExcelDataReader;

namespace AfmHsa.Data;

/// <summary>
/// As tabelas de custo (BD) que a planilha de ventiladores usa
/// (tabela_rmzBD_2024: ID, Cond_0…Cond_5, MP, MO, MP+MO, PC, data_alteracao),
/// lidas dos arquivos Excel de uma pasta da rede (Custos:PastaTabelas no
/// appsettings). A busca é a mesma da planilha:
/// <list type="bullet">
/// <item>material: a primeira linha com ID = material → coluna MP
///   (=INDEX(FILTER(tabela!H:H, tabela!A:A = material), 1));</item>
/// <item>mão de obra: ID = "MO_Howden", Cond_0 = recurso, Cond_2 = BU → coluna MP+MO
///   (=FILTER(tabela!J:J, (A = "MO_Howden") * (B = recurso) * (D = "HSA"))).</item>
/// </list>
/// Relê sozinha quando um arquivo da pasta muda.
/// </summary>
public sealed class TabelaDeCustos
{
    public const string PastaPadrao = @"H:\ENG_APLICACAO\HSA\02 MODELOS\Custos\teste";

    public sealed record Linha(string Id, string[] Cond, decimal? Mp, decimal? Mo, decimal? MpMo, decimal? Pc, string Arquivo);

    private sealed record Carga(string Assinatura, List<Linha> Linhas, List<string> Arquivos, string? Erro);

    private readonly string _pasta;
    private Carga? _carga;
    private readonly object _trava = new();

    public TabelaDeCustos(string? pasta) => _pasta = string.IsNullOrWhiteSpace(pasta) ? PastaPadrao : pasta.Trim();

    public string Pasta => _pasta;

    /// <summary>Situação para a tela: de onde veio e quantas linhas, ou por que não carregou.</summary>
    public (bool Ok, string Texto) Situacao()
    {
        var c = Carregar();
        if (c.Erro is not null) return (false, c.Erro);
        return (true, $"{c.Linhas.Count} linhas de {string.Join(", ", c.Arquivos.Select(Path.GetFileName))}");
    }

    /// <summary>O R$/kg (coluna MP) do material; null = não está na tabela.</summary>
    public decimal? Material(string? id)
    {
        var chave = (id ?? "").Trim();
        if (chave.Length == 0 || chave == "-") return null;
        var l = Carregar().Linhas.FirstOrDefault(x => Igual(x.Id, chave));
        return l?.Mp ?? l?.MpMo;
    }

    /// <summary>O valor da hora (coluna MP+MO) de MO_Howden para o recurso e a BU; null = não está na tabela.</summary>
    public decimal? MaoDeObra(string? recurso, string bu = "HSA")
    {
        var r = (recurso ?? "").Trim();
        if (r.Length == 0) return null;
        return Carregar().Linhas.FirstOrDefault(x => Igual(x.Id, "MO_Howden") && Igual(Cond(x, 0), r) && Igual(Cond(x, 2), bu))?.MpMo;
    }

    /// <summary>Os recursos de mão de obra da BU (Cond_0 das linhas MO_Howden).</summary>
    public List<string> Recursos(string bu = "HSA") =>
        Carregar().Linhas.Where(x => Igual(x.Id, "MO_Howden") && Igual(Cond(x, 2), bu)).Select(x => Cond(x, 0))
            .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Os materiais com valor de matéria-prima (as sugestões do campo Material).</summary>
    public List<string> Materiais() =>
        Carregar().Linhas.Where(x => (x.Mp ?? x.MpMo) is > 0 && !Igual(x.Id, "MO_Howden")).Select(x => x.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    private static string Cond(Linha l, int i) => i < l.Cond.Length ? l.Cond[i] : "";

    private static bool Igual(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    // ================= leitura da pasta =================

    private Carga Carregar()
    {
        string assinatura;
        List<FileInfo> arquivos;
        try
        {
            if (!Directory.Exists(_pasta))
                return new Carga("", new(), new(), $"A pasta das tabelas de custo não foi encontrada: {_pasta}");
            arquivos = new DirectoryInfo(_pasta).EnumerateFiles()
                .Where(f => !f.Name.StartsWith("~$") && f.Extension.ToLowerInvariant() is ".xlsx" or ".xlsm" or ".xls" or ".csv")
                .OrderByDescending(f => f.LastWriteTimeUtc)   // o arquivo mais novo manda
                .ToList();
            assinatura = string.Join("|", arquivos.Select(f => $"{f.Name}:{f.Length}:{f.LastWriteTimeUtc.Ticks}"));
        }
        catch (Exception ex) { return new Carga("", new(), new(), $"Não deu para abrir a pasta das tabelas de custo ({_pasta}): {ex.Message}"); }

        lock (_trava)
        {
            if (_carga is { } c && c.Assinatura == assinatura) return c;
            var linhas = new List<Linha>();
            var lidos = new List<string>();
            var erros = new List<string>();
            foreach (var f in arquivos)
            {
                try
                {
                    var n = Ler(f.FullName, linhas);
                    if (n > 0) lidos.Add(f.FullName);
                }
                catch (Exception ex) { erros.Add($"{f.Name}: {ex.Message}"); }
            }
            string? erro = null;
            if (linhas.Count == 0)
                erro = arquivos.Count == 0
                    ? $"Nenhuma planilha na pasta das tabelas de custo ({_pasta})."
                    : $"Nenhuma tabela de custo (colunas ID, Cond_0…, MP, MO, MP+MO, PC) nas planilhas de {_pasta}" + (erros.Count > 0 ? $" — {string.Join("; ", erros)}" : ".");
            _carga = new Carga(assinatura, linhas, lidos, erro);
            return _carga;
        }
    }

    /// <summary>Lê as abas da planilha que têm o cabeçalho da tabela BD (ID … MP, MO, MP+MO, PC).</summary>
    private static int Ler(string caminho, List<Linha> destino)
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
        // cópia em memória: o arquivo pode estar aberto no Excel de alguém
        byte[] bytes;
        using (var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var ms = new MemoryStream()) { fs.CopyTo(ms); bytes = ms.ToArray(); }
        using var mem = new MemoryStream(bytes);
        using var reader = caminho.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? ExcelReaderFactory.CreateCsvReader(mem)
            : ExcelReaderFactory.CreateReader(mem);
        var total = 0;
        do
        {
            int? iId = null, iMp = null, iMo = null, iMpMo = null, iPc = null;
            var conds = new List<int>();
            var cabecalho = false;
            while (reader.Read())
            {
                if (!cabecalho)
                {
                    // o cabeçalho pode não estar na 1ª linha: procura nas primeiras
                    for (var c = 0; c < reader.FieldCount; c++)
                    {
                        var t = Texto(reader.GetValue(c)).ToUpperInvariant();
                        if (t == "ID") iId = c;
                        else if (t == "MP") iMp = c;
                        else if (t == "MO") iMo = c;
                        else if (t == "MP+MO") iMpMo = c;
                        else if (t == "PC") iPc = c;
                        else if (t.StartsWith("COND_")) conds.Add(c);
                    }
                    cabecalho = iId is not null && (iMp is not null || iMpMo is not null);
                    if (!cabecalho) { iId = iMp = iMo = iMpMo = iPc = null; conds.Clear(); if (reader.Depth > 20) break; }
                    continue;
                }
                var id = Texto(reader.GetValue(iId!.Value));
                if (id.Length == 0) continue;
                destino.Add(new Linha(id, conds.Select(c => Texto(reader.GetValue(c))).ToArray(),
                    Numero(iMp, reader), Numero(iMo, reader), Numero(iMpMo, reader), Numero(iPc, reader), caminho));
                total++;
            }
        } while (reader.NextResult());
        return total;
    }

    private static string Texto(object? v) => v switch
    {
        null => "",
        double d => d.ToString(CultureInfo.InvariantCulture),
        _ => (v.ToString() ?? "").Trim(),
    };

    private static decimal? Numero(int? i, IExcelDataReader r)
    {
        if (i is null) return null;
        var v = r.GetValue(i.Value);
        return v switch
        {
            null => null,
            double d => (decimal)d,
            int n => n,
            _ => decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x
               : decimal.TryParse(v.ToString(), NumberStyles.Any, new CultureInfo("pt-BR"), out x) ? x : null,
        };
    }
}
