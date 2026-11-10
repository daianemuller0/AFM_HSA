using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AfmHsa.Data;

/// <summary>
/// Uma referência de preço: o que a Howden mandou ao cliente para um part
/// number — vendido ou não, o cliente recebeu, e isso vale como referência na
/// próxima proposta. Nasce de duas formas: ao salvar uma proposta (um registro
/// por item) ou importada de uma planilha (o histórico de propostas enviadas).
/// Leva também o custo e o TA usados, para quem montar a próxima proposta.
/// </summary>
public sealed class ReferenciaDeCusto
{
    /// <summary>O id do item da proposta (salvar de novo atualiza a mesma); na planilha, um código da linha.</summary>
    public string Id { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string Descricao { get; set; } = "";
    /// <summary>Custo unitário em R$ (sem o TA).</summary>
    public decimal Valor { get; set; }
    /// <summary>TA usado na proposta para chegar no preço meta (R$, unitário); 0 = sem TA.</summary>
    public decimal Ta { get; set; }
    /// <summary>yyyy-MM-dd: a data da proposta.</summary>
    public string Data { get; set; } = "";
    public string Modelo { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    /// <summary>De onde veio o custo ("digitado", "importação", "nacionalização") ou "planilha".</summary>
    public string Origem { get; set; } = "";
    public string PropostaId { get; set; } = "";
    /// <summary>O código da proposta (sem a revisão).</summary>
    public string Proposta { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string Usuario { get; set; } = "";

    public string Revisao { get; set; } = "";
    /// <summary>As plantas do item (ou da proposta), como saem no Word.</summary>
    public string Plantas { get; set; } = "";
    /// <summary>UF do cliente.</summary>
    public string Estado { get; set; } = "";
    /// <summary>A unidade Howden de onde a proposta sai (HSA-SP, HSA-ES, HCHL, HPU).</summary>
    public string Intercompany { get; set; } = "";
    public decimal Quantidade { get; set; }
    /// <summary>Preço unitário enviado ao cliente, sem impostos, na <see cref="Moeda"/>.</summary>
    public decimal PrecoUnitLiquido { get; set; }
    public decimal PrecoTotalLiquido { get; set; }
    /// <summary>BRL, USD, EUR...; vazio = BRL.</summary>
    public string Moeda { get; set; } = "";

    /// <summary>Número da AF (autorização de fornecimento): preenchido = o item foi vendido.</summary>
    public string NumeroAf { get; set; } = "";
    /// <summary>Como foi vendido: "item" (preço do item) ou "pacote" (no total de um pacote).</summary>
    public string VendaPor { get; set; } = "";
    /// <summary>O valor total do pacote vendido (na <see cref="Moeda"/>), quando a venda foi por pacote.</summary>
    public decimal ValorPacote { get; set; }
    /// <summary>De onde veio o custo: "cotação" (temos cotação de fornecedor / intercompany) ou "estimado".</summary>
    public string BaseDoCusto { get; set; } = "";
    /// <summary>A moeda do custo e do TA (o custo fica na moeda em que foi feito: EUR, USD...); vazio = R$ (as antigas).</summary>
    public string MoedaCusto { get; set; } = "";
    /// <summary>A quantidade total de itens (peças) da proposta — o tamanho do pacote em que o preço foi dado.</summary>
    public decimal QtdItensPacote { get; set; }

    public bool Vendido => NumeroAf.Trim().Length > 0;

    public DateTime? DataComoData =>
        DateTime.TryParseExact(Data, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public Moeda MoedaDoPreco => Moedas.Ler(Moeda);
    public Moeda MoedaDoCusto => Moedas.Ler(MoedaCusto);
}

/// <summary>As referências de preço e custo (entidade "referencias_custo").</summary>
public sealed class ReferenciaDeCustoRepository
{
    private const string Entidade = "referencias_custo";
    public const string OrigemPlanilha = "planilha";
    private readonly ParquetStore _store;
    public ReferenciaDeCustoRepository(ParquetStore store) => _store = store;

    public List<ReferenciaDeCusto> Todas() => _store
        .ReadLatest(Entidade,
            "id, part_number, descricao, valor, ta, data, modelo, serial_number, origem, proposta_id, proposta, cliente, usuario, " +
            "revisao, plantas, estado, intercompany, quantidade, preco_unit_liquido, preco_total_liquido, moeda, " +
            "numero_af, venda_por, valor_pacote, base_custo, moeda_custo, qtd_itens_pacote",
            r => new ReferenciaDeCusto
            {
                Id = Fmt.S(r, 0), PartNumber = Fmt.S(r, 1), Descricao = Fmt.S(r, 2), Valor = Fmt.Dec(Fmt.S(r, 3)),
                Ta = Fmt.Dec(Fmt.S(r, 4)), Data = Fmt.S(r, 5), Modelo = Fmt.S(r, 6), SerialNumber = Fmt.S(r, 7),
                Origem = Fmt.S(r, 8), PropostaId = Fmt.S(r, 9), Proposta = Fmt.S(r, 10), Cliente = Fmt.S(r, 11),
                Usuario = Fmt.S(r, 12), Revisao = Fmt.S(r, 13), Plantas = Fmt.S(r, 14), Estado = Fmt.S(r, 15),
                Intercompany = Fmt.S(r, 16), Quantidade = Fmt.Dec(Fmt.S(r, 17)), PrecoUnitLiquido = Fmt.Dec(Fmt.S(r, 18)),
                PrecoTotalLiquido = Fmt.Dec(Fmt.S(r, 19)), Moeda = Fmt.S(r, 20),
                NumeroAf = Fmt.S(r, 21), VendaPor = Fmt.S(r, 22), ValorPacote = Fmt.Dec(Fmt.S(r, 23)), BaseDoCusto = Fmt.S(r, 24),
                MoedaCusto = Fmt.S(r, 25), QtdItensPacote = Fmt.Dec(Fmt.S(r, 26)),
            })
        .Select(SepararRevisao)
        .OrderByDescending(r => r.Data).ThenByDescending(r => r.Proposta)
        .ToList();

    /// <summary>As referências antigas gravavam "CÓDIGO-C2" na proposta: vira proposta + revisão.</summary>
    private static ReferenciaDeCusto SepararRevisao(ReferenciaDeCusto r)
    {
        if (r.Revisao.Length == 0 && System.Text.RegularExpressions.Regex.Match(r.Proposta, @"^(.+)-C(\d+)$") is { Success: true } m)
        {
            r.Proposta = m.Groups[1].Value;
            r.Revisao = m.Groups[2].Value;
        }
        return r;
    }

    /// <summary>O custo mais recente do part number, de outra proposta (null = nenhum).</summary>
    public static ReferenciaDeCusto? MaisRecente(IEnumerable<ReferenciaDeCusto> todas, string? partNumber, string propostaAtual)
    {
        var pn = IntelligenceService.NormPn(partNumber);
        if (pn.Length == 0) return null;
        return todas.Where(r => DeOutra(r, propostaAtual) && IntelligenceService.NormPn(r.PartNumber) == pn && r.Valor > 0)
            .OrderByDescending(r => r.Data).ThenByDescending(r => r.Proposta).FirstOrDefault();
    }

    /// <summary>
    /// O último preço enviado para o part number, de outra proposta: primeiro
    /// o deste cliente, senão o de qualquer um (null = nenhum).
    /// </summary>
    public static ReferenciaDeCusto? UltimoPreco(IEnumerable<ReferenciaDeCusto> todas, string? partNumber, string propostaAtual, string? cliente)
    {
        var pn = IntelligenceService.NormPn(partNumber);
        if (pn.Length == 0) return null;
        var doPn = todas.Where(r => DeOutra(r, propostaAtual) && r.PrecoUnitLiquido > 0 && IntelligenceService.NormPn(r.PartNumber) == pn)
            .OrderByDescending(r => r.Data).ThenByDescending(r => r.Proposta).ThenByDescending(r => r.Revisao).ToList();
        return doPn.FirstOrDefault(r => Textos.Igual(r.Cliente, cliente) && (cliente ?? "").Trim().Length > 0) ?? doPn.FirstOrDefault();
    }

    /// <summary>
    /// A referência não é desta proposta. As importadas do Excel não têm
    /// proposta (id vazio) e valem sempre — também na proposta nova, que
    /// ainda não tem id.
    /// </summary>
    private static bool DeOutra(ReferenciaDeCusto r, string propostaAtual) =>
        r.PropostaId.Length == 0 || r.PropostaId != propostaAtual;

    public void Gravar(ReferenciaDeCusto r) => _store.WriteRow(Entidade, Linha(r));

    public void GravarVarias(IEnumerable<ReferenciaDeCusto> refs) => _store.WriteBatch(Entidade, refs.Select(Linha).ToList());

    private static IReadOnlyList<KeyValuePair<string, object?>> Linha(ReferenciaDeCusto r)
    {
        string N(decimal v) => v.ToString(CultureInfo.InvariantCulture);
        return new KeyValuePair<string, object?>[]
        {
            new("id", r.Id), new("part_number", r.PartNumber), new("descricao", r.Descricao),
            new("valor", N(r.Valor)), new("ta", N(r.Ta)),
            new("data", r.Data), new("modelo", r.Modelo), new("serial_number", r.SerialNumber), new("origem", r.Origem),
            new("proposta_id", r.PropostaId), new("proposta", r.Proposta), new("cliente", r.Cliente), new("usuario", r.Usuario),
            new("revisao", r.Revisao), new("plantas", r.Plantas), new("estado", r.Estado), new("intercompany", r.Intercompany),
            new("quantidade", N(r.Quantidade)), new("preco_unit_liquido", N(r.PrecoUnitLiquido)),
            new("preco_total_liquido", N(r.PrecoTotalLiquido)), new("moeda", r.Moeda),
            new("numero_af", r.NumeroAf), new("venda_por", r.VendaPor), new("valor_pacote", N(r.ValorPacote)), new("base_custo", r.BaseDoCusto),
            new("moeda_custo", r.MoedaCusto), new("qtd_itens_pacote", N(r.QtdItensPacote)),
        };
    }

    public void Apagar(string id) => _store.WriteRow(Entidade, new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);

    /// <summary>Apaga as referências gravadas por essas propostas (as importadas do Excel ficam). Devolve quantas.</summary>
    public int ApagarDasPropostas(IReadOnlyCollection<string> propostas)
    {
        var conjunto = propostas.ToHashSet();
        var ids = Todas().Where(r => r.PropostaId.Length > 0 && conjunto.Contains(r.PropostaId)).Select(r => r.Id).ToList();
        if (ids.Count > 0)
            _store.WriteBatch(Entidade, ids.Select(id => (IReadOnlyList<KeyValuePair<string, object?>>)new KeyValuePair<string, object?>[] { new("id", id) }).ToList(), deleted: true);
        return ids.Count;
    }

    // ================= planilha =================

    /// <summary>As colunas da planilha, na ordem da tela; os apelidos aceitos vêm depois do nome.</summary>
    public static readonly (string Nome, string[] Apelidos)[] Colunas =
    {
        ("Proposta", new[] { "proposta", "nossa referencia", "codigo", "codigo da proposta" }),
        ("Revisão", new[] { "revisao", "rev" }),
        ("Cliente", new[] { "cliente" }),
        ("Plantas", new[] { "plantas", "planta", "unidade do cliente" }),
        ("Estado", new[] { "estado", "uf" }),
        ("Intercompany", new[] { "intercompany", "unidade", "bu", "unidade howden" }),
        ("Part Number", new[] { "part number", "partnumber", "pn", "codigo da peca" }),
        ("Quantidade", new[] { "quantidade", "qtd", "qtde", "quant" }),
        ("Valor unitário líquido", new[] { "valor unitario liquido", "valor unit liquido", "preco unitario liquido", "preco unitario", "valor unitario", "unitario liquido" }),
        ("Valor total líquido", new[] { "valor total liquido", "preco total liquido", "valor total", "preco total", "total liquido" }),
        ("Data", new[] { "data", "data da proposta" }),
        ("Custo unit.", new[] { "custo unit (r$)", "custo unit", "custo unitario", "custo unit r$", "custo" }),
        ("TA", new[] { "ta (r$)", "ta", "ta r$" }),
        ("Descrição", new[] { "descricao", "descricao da peca" }),
        ("Nº AF", new[] { "n af", "numero da af", "no af", "af", "n da af", "numero af" }),
        ("Vendido por", new[] { "vendido por", "venda por", "item ou total", "item/pacote", "por item ou total" }),
        ("Valor total do pacote", new[] { "valor total do pacote", "valor do pacote", "pacote" }),
        ("Qtd. itens do pacote", new[] { "qtd itens do pacote", "quantidade de itens do pacote", "qtd total de itens", "quantidade total de itens", "itens do pacote" }),
        ("Base do custo", new[] { "base do custo", "cotacao ou estimado", "custo cotado ou estimado", "cotado/estimado" }),
        ("Moeda", new[] { "moeda", "moeda do preco", "moeda da venda" }),
        ("Moeda do custo", new[] { "moeda do custo", "moeda custo", "moeda do custo e ta" }),
    };

    private static string ChaveDaColuna(string s) =>
        new(Textos.Simples(s).Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());

    /// <summary>
    /// Lê a planilha (xlsx, xls ou csv) e grava as referências. A mesma linha
    /// (proposta, revisão, cliente e part number) importada de novo atualiza
    /// a que já existe, em vez de duplicar.
    /// </summary>
    public (int Gravadas, List<string> Avisos) Importar(byte[] arquivo, string nome, string usuario)
    {
        var avisos = new List<string>();
        var (cab, linhas) = ImportService.LerPlanilha(arquivo, nome);
        var indice = new Dictionary<string, int>();
        for (var i = 0; i < cab.Length; i++)
        {
            var h = ChaveDaColuna(cab[i]);
            foreach (var (col, apelidos) in Colunas)
                if (!indice.ContainsKey(col) && apelidos.Any(a => ChaveDaColuna(a) == h)) { indice[col] = i; break; }
        }
        if (!indice.ContainsKey("Part Number"))
            return (0, new List<string> { "A planilha não tem a coluna \"Part Number\". Baixe o modelo para ver as colunas." });
        foreach (var (col, _) in Colunas.Where(c => c.Nome is not ("Moeda" or "Moeda do custo") && !indice.ContainsKey(c.Nome)))
            avisos.Add($"Coluna \"{col}\" não encontrada (fica vazia).");

        string C(string[] l, string col) => indice.TryGetValue(col, out var i) && i < l.Length ? (l[i] ?? "").Trim() : "";
        var novas = new List<ReferenciaDeCusto>();
        var n = 1;
        foreach (var l in linhas)
        {
            n++;
            var pn = C(l, "Part Number");
            if (pn.Length == 0) continue;
            var r = new ReferenciaDeCusto
            {
                Proposta = C(l, "Proposta"), Revisao = C(l, "Revisão"), Cliente = C(l, "Cliente"), Plantas = C(l, "Plantas"),
                Estado = C(l, "Estado"), Intercompany = C(l, "Intercompany"), PartNumber = pn,
                Quantidade = Numero(C(l, "Quantidade")) ?? 0m, PrecoUnitLiquido = Numero(C(l, "Valor unitário líquido")) ?? 0m,
                PrecoTotalLiquido = Numero(C(l, "Valor total líquido")) ?? 0m, Data = DataIso(C(l, "Data")),
                Valor = Numero(C(l, "Custo unit.")) ?? 0m, Ta = Numero(C(l, "TA")) ?? 0m,
                MoedaCusto = Moedas.Ler(C(l, "Moeda do custo")).Codigo(),
                Descricao = C(l, "Descrição"), Moeda = Moedas.Ler(C(l, "Moeda")).Codigo(),
                NumeroAf = C(l, "Nº AF"), VendaPor = VendaPorDe(C(l, "Vendido por")), ValorPacote = Numero(C(l, "Valor total do pacote")) ?? 0m,
                QtdItensPacote = Numero(C(l, "Qtd. itens do pacote")) ?? 0m,
                BaseDoCusto = BaseDe(C(l, "Base do custo")),
                Origem = OrigemPlanilha, Usuario = usuario,
            };
            if (r.PrecoTotalLiquido == 0m && r.PrecoUnitLiquido > 0m && r.Quantidade > 0m) r.PrecoTotalLiquido = r.PrecoUnitLiquido * r.Quantidade;
            if (r.PrecoUnitLiquido == 0m && r.PrecoTotalLiquido > 0m && r.Quantidade > 0m) r.PrecoUnitLiquido = Math.Round(r.PrecoTotalLiquido / r.Quantidade, 2);
            if (C(l, "Data").Length > 0 && r.Data.Length == 0) avisos.Add($"Linha {n}: data \"{C(l, "Data")}\" não entendida.");
            r.Id = "pl-" + Hash($"{r.Proposta}|{r.Revisao}|{r.Cliente}|{IntelligenceService.NormPn(pn)}|{r.Plantas}");
            novas.Add(r);
        }
        // a mesma linha duas vezes na planilha: fica a última
        novas = novas.GroupBy(r => r.Id).Select(g => g.Last()).ToList();
        if (novas.Count > 0) GravarVarias(novas);
        return (novas.Count, avisos);
    }

    /// <summary>O modelo da planilha de importação, com uma linha de exemplo.</summary>
    public static byte[] Modelo() => Planilha.Xlsx("Referências", Colunas.Select(c => c.Nome).ToList(), new[]
    {
        (IReadOnlyList<string>)new[] { "HSA2026-0147", "2", "Vale", "São Luís", "MA", "HSA-SP", "HW-NAC-A", "3", "3853,22", "11559,66", "05/11/2026", "2100,00", "0", "Rolamento", "AF-12345", "pacote", "25000,00", "8", "cotação", "BRL", "BRL" },
    });

    public const string Cotacao = "cotação";
    public const string Estimado = "estimado";

    /// <summary>"item", "por item", "unitário" → item; "total", "pacote", "kit" → pacote.</summary>
    public static string VendaPorDe(string? texto)
    {
        var t = Textos.Simples(texto);
        if (t.Length == 0) return "";
        return t.Contains("pacote") || t.Contains("total") || t.Contains("kit") || t.Contains("lote") ? "pacote" : "item";
    }

    /// <summary>"cotação", "cotado", "quote" → cotação; "estimado", "estimativa" → estimado.</summary>
    public static string BaseDe(string? texto)
    {
        var t = Textos.Simples(texto);
        if (t.Length == 0) return "";
        return t.StartsWith("cot") || t.Contains("quot") ? Cotacao : t.StartsWith("est") ? Estimado : "";
    }

    private static string Hash(string s) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s.ToUpperInvariant())))[..20].ToLowerInvariant();

    /// <summary>"1.234,56", "1234.56", "R$ 1.234,56" ou o número do Excel.</summary>
    public static decimal? Numero(string? texto)
    {
        var t = new string((texto ?? "").Where(c => char.IsDigit(c) || c is ',' or '.' or '-').ToArray());
        if (t.Length == 0) return null;
        var virgula = t.LastIndexOf(',');
        var ponto = t.LastIndexOf('.');
        if (virgula >= 0 && ponto >= 0)
            t = virgula > ponto ? t.Replace(".", "").Replace(',', '.') : t.Replace(",", "");
        else if (virgula >= 0)
            t = t.Replace(',', '.');
        else if (ponto >= 0 && t.Count(c => c == '.') > 1)
            t = t.Replace(".", "");
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>dd/MM/yyyy, yyyy-MM-dd ou o número de série do Excel → yyyy-MM-dd ("" = não entendida).</summary>
    public static string DataIso(string? texto)
    {
        var t = (texto ?? "").Trim();
        if (t.Length == 0) return "";
        string[] formatos = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "dd-MM-yyyy", "dd.MM.yyyy", "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy HH:mm:ss" };
        if (DateTime.TryParseExact(t, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (double.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var serie) && serie is > 20000 and < 80000)
            return DateTime.FromOADate(serie).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return "";
    }
}
