using System.Text.Json;

namespace AfmHsa.Data;

/// <summary>Uma linha dos dados de faturamento: "RUT:" + "76.274.455-4" (o valor em negrito ou não).</summary>
public sealed class LinhaDeFaturamento
{
    public string Rotulo { get; set; } = "";
    public string Valor { get; set; } = "";
    public bool Negrito { get; set; } = true;

    public LinhaDeFaturamento() { }
    public LinhaDeFaturamento(string rotulo, string valor, bool negrito = true) { Rotulo = rotulo; Valor = valor; Negrito = negrito; }

    public bool Vazia => Valor.Trim().Length == 0;
}

/// <summary>
/// Os dados para faturamento de uma BU: a empresa (razão social e as linhas
/// de endereço, registro fiscal…) e uma conta bancária por moeda. São
/// editados em Dados e Importação → Dados bancários.
/// </summary>
public sealed class DadosBancariosDaBu
{
    public string Bu { get; set; } = "";
    public string Nome { get; set; } = "";
    public List<LinhaDeFaturamento> Linhas { get; set; } = new();

    /// <summary>A conta de cada moeda (código: BRL, USD, EUR, CLP, PEN).</summary>
    public Dictionary<string, List<LinhaDeFaturamento>> Contas { get; set; } = new();

    public List<LinhaDeFaturamento> ContaEm(Moeda m) =>
        Contas.TryGetValue(m.Codigo(), out var c) ? c : new List<LinhaDeFaturamento>();

    public bool TemConta(Moeda m) => ContaEm(m).Any(l => !l.Vazia);
}

/// <summary>O que vai no "Dados para faturamento" de uma proposta: a empresa da BU e a conta na moeda do preço.</summary>
public sealed record FaturamentoDaProposta(DadosBancariosDaBu Dados, Moeda Moeda, IReadOnlyList<LinhaDeFaturamento>? Conta);

/// <summary>
/// As BUs no documento da proposta: o imposto local das BUs de fora ("+ IVA"
/// no Chile, "+ IGV" no Peru), as moedas de conta de cada BU e os dados
/// iniciais (os que a equipe enviou e os do modelo do Word).
/// </summary>
public static class FaturamentoDaBu
{
    /// <summary>O imposto que vai somado ao preço líquido: IVA (Chile), IGV (Peru); null nas BUs do Brasil.</summary>
    public static string? ImpostoLocal(string? bu)
    {
        var b = (bu ?? "").Trim();
        if (b.StartsWith("HCHL", StringComparison.OrdinalIgnoreCase)) return "IVA";
        if (b.Equals("HPU", StringComparison.OrdinalIgnoreCase)) return "IGV";
        return null;
    }

    /// <summary>O nome da Howden na faixa da capa: "Howden Chile" (HCHL), "Howden Peru" (HPU); null = o do modelo (Howden South America).</summary>
    public static string? NomeNaCapa(string? bu)
    {
        var b = (bu ?? "").Trim();
        if (b.StartsWith("HCHL", StringComparison.OrdinalIgnoreCase)) return "Howden Chile";
        if (b.Equals("HPU", StringComparison.OrdinalIgnoreCase)) return "Howden Peru";
        return null;
    }

    /// <summary>
    /// A venda é para um país fora do Brasil, do Chile e do Peru (Colômbia,
    /// Panamá, Rep. Dominicana, outro): nenhum imposto se soma ao preço.
    /// </summary>
    public static bool OutroPais(string? estado) =>
        ListasDoPricing.PaisesExportacao.Any(p => Textos.Igual(p, estado))
        && !new[] { "Brasil", "Chile", "Peru" }.Any(p => Textos.Igual(p, estado));

    /// <summary>As BUs com dados bancários e as moedas de conta de cada uma.</summary>
    public static readonly (string Bu, string Nome, Moeda[] Moedas)[] Bus =
    {
        ("HSA-SP", "HSA-SP — Itatiba", new[] { Moeda.Brl, Moeda.Eur, Moeda.Usd }),
        ("HSA-ES", "HSA-ES — Serra", new[] { Moeda.Brl, Moeda.Eur, Moeda.Usd }),
        ("HCHL", "HCHL — Chile", new[] { Moeda.Usd, Moeda.Eur, Moeda.Clp }),
        ("HPU", "HPU — Peru", new[] { Moeda.Usd, Moeda.Eur, Moeda.Pen }),
    };

    /// <summary>A BU dos dados bancários (HCHL_Custo_USD usa os do HCHL); "" = BU sem dados.</summary>
    public static string ChaveDaBu(string? bu)
    {
        var b = (bu ?? "").Trim();
        if (b.StartsWith("HCHL", StringComparison.OrdinalIgnoreCase)) return "HCHL";
        return Bus.FirstOrDefault(x => x.Bu.Equals(b, StringComparison.OrdinalIgnoreCase)).Bu ?? "";
    }

    /// <summary>As contas preenchidas da BU, a da moeda do preço primeiro (só as linhas com valor).</summary>
    public static List<(Moeda Moeda, List<LinhaDeFaturamento> Linhas)> ContasParaOWord(DadosBancariosDaBu d, Moeda moedaDoPreco)
    {
        var ordem = (Bus.FirstOrDefault(b => b.Bu == d.Bu).Moedas ?? Array.Empty<Moeda>())
            .OrderBy(m => m == moedaDoPreco ? 0 : 1).ToList();
        return ordem.Where(d.TemConta).Select(m => (m, d.ContaEm(m).Where(l => !l.Vazia).ToList())).ToList();
    }

    /// <summary>O título da tabela da conta ("Dólar Americano - USD").</summary>
    public static string TituloDaConta(Moeda m) => m switch
    {
        Moeda.Usd => "Dólar Americano - USD",
        Moeda.Eur => "Euro - EUR",
        Moeda.Clp => "Peso Chileno - CLP",
        Moeda.Pen => "Sol Peruano - PEN",
        _ => "Real - BRL",
    };

    /// <summary>As linhas de uma conta nova (para preencher): as do Brasil em R$; as internacionais nas outras moedas.</summary>
    public static List<LinhaDeFaturamento> LinhasDeContaNova(string bu, Moeda m)
    {
        var brasil = bu.StartsWith("HSA", StringComparison.OrdinalIgnoreCase);
        if (m == Moeda.Brl)
            return new() { new("Banco:", ""), new("Agência:", ""), new("Conta:", "") };
        return brasil
            ? new() { new("Beneficiário:", ""), new("Banco:", ""), new("Conta:", ""), new("SWIFT:", ""), new("IBAN:", ""), new("Banco intermediário:", ""), new("SWIFT intermediário:", "") }
            : new() { new("Beneficiario:", ""), new("Banco Beneficiario:", ""), new("Cuenta", ""), new("Swift Banco Beneficiario:", ""),
                      new("Banco intermediario:", ""), new("Cuenta/IBAN:", ""), new("Swift Banco Intermediario:", "") };
    }

    /// <summary>Os dados iniciais da BU: os do modelo do Word (HSA) e os que a equipe enviou (Chile, Peru).</summary>
    public static DadosBancariosDaBu Padrao(string bu)
    {
        var d = bu switch
        {
            "HSA-SP" => new DadosBancariosDaBu
            {
                Nome = "Howden South America Ventiladores e Compressores Indústria e Comércio Ltda.",
                Linhas = new()
                {
                    new("", "Av. Osvaldo Berto, 475, Distrito Industrial Alfredo Rela, 13255-405 – Itatiba – SP", false),
                    new("CNPJ:", "01.094.363/0001-04 – Inscrição Estadual: 382.062.017.112", false),
                },
                Contas = new() { ["BRL"] = new() { new("Banco:", "Banco Itaú"), new("Agência:", "4892", false), new("Conta:", "28480-5", false) } },
            },
            "HSA-ES" => new DadosBancariosDaBu
            {
                Nome = "Howden South America Ventiladores e Compressores Indústria e Comércio Ltda.",
                Linhas = new()
                {
                    new("", "Rua 4E, 135 - Bairro Civit II – Distrito de Carapina, 29168-082 – Serra – ES", false),
                    new("CNPJ:", "", false),
                },
            },
            "HCHL" => new DadosBancariosDaBu
            {
                Nome = "HOWDEN CHILE SpA",
                Linhas = new() { new("Dirección:", "Calle Cordillera 575, Pudahuel - Santiago"), new("RUT:", "76.274.455-4"), new("Banco:", "HSBC Bank Chile") },
                Contas = new()
                {
                    ["EUR"] = new()
                    {
                        new("Beneficiario:", "Howden Chile SpA", false), new("Banco Beneficiario:", "HSBC Bank Chile", false),
                        new("Cuenta", "000-059261-061", false), new("Swift Banco Beneficiario:", "BLICCLRM", false),
                        new("Banco intermediario:", "HSBC Pls", false), new("Cuenta/IBAN:", "GB92MIDL40051557202815", false),
                        new("Swift Banco Intermediario:", "MIDLGB22", false),
                    },
                },
            },
            "HPU" => new DadosBancariosDaBu
            {
                Nome = "HPU - Howden Perú S.R.L.",
                Linhas = new()
                {
                    new("Dirección:", "Calle Guillermo Marconi 386, oficina 101, San Isidro, Lima, Peru - CP: 150131"), new("RUC:", "20606553618"),
                    new("Banco:", "Scotiabank (EUR)", false), new("Banco:", "Santander (Dólar Americano – USD)", false),
                },
                Contas = new()
                {
                    ["USD"] = new()
                    {
                        new("Beneficiario:", "Howden Perú S.R.L.", false), new("Banco Beneficiario:", "Banco Santander", false),
                        new("Cuenta", "056-001-000008103356-15", false), new("Swift Banco Beneficiario:", "-", false),
                        new("Banco intermediario:", "-", false), new("Cuenta:", "-", false),
                        new("Swift Banco Intermediario:", "-", false), new("ABA:", "-", false),
                    },
                },
            },
            _ => new DadosBancariosDaBu(),
        };
        d.Bu = bu;
        foreach (var m in Bus.FirstOrDefault(x => x.Bu == bu).Moedas ?? Array.Empty<Moeda>())
            if (!d.Contas.ContainsKey(m.Codigo())) d.Contas[m.Codigo()] = LinhasDeContaNova(bu, m);
        return d;
    }
}

/// <summary>Grava os dados bancários de cada BU (entidade "dados_bancarios": um JSON por BU).</summary>
public class DadosBancariosRepository
{
    private const string Entidade = "dados_bancarios";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly ParquetStore _s;
    public DadosBancariosRepository(ParquetStore s) => _s = s;

    /// <summary>Os dados de todas as BUs: os gravados, ou os iniciais.</summary>
    public List<DadosBancariosDaBu> Todos()
    {
        var gravados = _s.ReadLatest(Entidade, "id, dados", r => (Bu: Fmt.S(r, 0), Dados: Fmt.S(r, 1)))
            .Where(x => x.Bu.Length > 0).ToDictionary(x => x.Bu, x => x.Dados);
        return FaturamentoDaBu.Bus.Select(b => Ler(b.Bu, gravados.GetValueOrDefault(b.Bu))).ToList();
    }

    public DadosBancariosDaBu? Da(string? bu)
    {
        var chave = FaturamentoDaBu.ChaveDaBu(bu);
        return chave.Length == 0 ? null : Todos().FirstOrDefault(d => d.Bu == chave);
    }

    /// <summary>O que vai no Word da proposta: a empresa da BU dela e a conta na moeda do preço (null = sem conta nessa moeda).</summary>
    public FaturamentoDaProposta? ParaProposta(Models.CommercialOffer o)
    {
        if (Da(o.Bu) is not { } d) return null;
        var moeda = Cambio.MoedaDoPreco(o);
        return new FaturamentoDaProposta(d, moeda, d.TemConta(moeda) ? d.ContaEm(moeda) : null);
    }

    public void Gravar(DadosBancariosDaBu d)
    {
        d.Nome = d.Nome.Trim();
        foreach (var l in d.Linhas.Concat(d.Contas.Values.SelectMany(c => c))) { l.Rotulo = l.Rotulo.Trim(); l.Valor = l.Valor.Trim(); }
        d.Linhas.RemoveAll(l => l.Rotulo.Length == 0 && l.Valor.Length == 0);
        _s.WriteRow(Entidade, new KeyValuePair<string, object?>[] { new("id", d.Bu), new("dados", JsonSerializer.Serialize(d, Json)) });
    }

    /// <summary>Volta a BU aos dados iniciais.</summary>
    public void Restaurar(string bu) => _s.WriteRow(Entidade, new KeyValuePair<string, object?>[] { new("id", bu) }, deleted: true);

    private static DadosBancariosDaBu Ler(string bu, string? json)
    {
        DadosBancariosDaBu? d = null;
        if (!string.IsNullOrWhiteSpace(json))
            try { d = JsonSerializer.Deserialize<DadosBancariosDaBu>(json, Json); } catch (JsonException) { }
        d ??= FaturamentoDaBu.Padrao(bu);
        d.Bu = bu;
        d.Contas ??= new();
        d.Linhas ??= new();
        // toda moeda da BU tem a sua conta (vazia = a preencher)
        foreach (var m in FaturamentoDaBu.Bus.First(x => x.Bu == bu).Moedas)
            if (!d.Contas.ContainsKey(m.Codigo())) d.Contas[m.Codigo()] = FaturamentoDaBu.LinhasDeContaNova(bu, m);
        return d;
    }
}
