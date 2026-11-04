namespace AfmHsa.Data;

/// <summary>
/// As listas do pricing do AFM — cópia das validações e tabelas da planilha
/// R_HSAXYZ0000-0.xlsm (abas PRICING, listas e BD_pricing, revisão de
/// 09/10/2025). Os textos têm de ficar iguais aos da planilha: as fórmulas de
/// lá comparam letra por letra ("Exportação", "Industrialização", "HSA-SP"…).
/// Os contatos dos representantes não foram copiados.
/// </summary>
public static class ListasDoPricing
{
    /// <summary>BU (E8, lista tabela_AREAS). Define a coluna do ICMS e a moeda da planilha.</summary>
    public static readonly (string Codigo, string Rotulo)[] Bus =
    {
        ("HSA-SP", "HSA-SP — Itatiba"),
        ("HSA-ES", "HSA-ES — Serra"),
        ("HCHL", "HCHL — Chile"),
        ("HCHL_Custo_USD", "HCHL_Custo_USD — Chile, custo em USD"),
        ("HPU", "HPU — Peru"),
    };

    /// <summary>Segmento (margem) (J3, lista_segmento) e a margem padrão de cada um (BD_pricing A21:B24).</summary>
    public static readonly (string Nome, decimal MargemPadrao)[] Segmentos =
    {
        ("AFM", 0.52m), ("NB", 0.32m), ("Intercompany", 0.28m), ("Service", 0.50m),
    };

    /// <summary>Margem padrão quando o segmento ainda não foi escolhido ("preencher", BD_pricing B25).</summary>
    public const decimal MargemSemSegmento = 0.80m;

    public static decimal MargemPadraoDo(string segmento) =>
        Segmentos.FirstOrDefault(s => Textos.Igual(s.Nome, segmento)).MargemPadrao is var m && m > 0
            ? m : MargemSemSegmento;

    /// <summary>Venda para (J4, Vendas_Para).</summary>
    public static readonly string[] VendaPara = { "Cliente Final", "Industrialização", "Revenda" };

    /// <summary>Destino (J5, Destino_Venda).</summary>
    public static readonly string[] Destinos =
    {
        "Nacional", "Exportação", "Exportação com Back to Back", "Back to Back 100% importado",
    };

    /// <summary>Estado quando o destino é Nacional (Nacional_: BD_pricing A31:A59).</summary>
    public static readonly string[] EstadosNacional =
    {
        "AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT", "PA", "PB",
        "PE", "PI", "PR", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO", "Chile", "Peru",
    };

    /// <summary>Estado (país) quando o destino é Exportação (Exportação_: BD_pricing A123:A129).</summary>
    public static readonly string[] PaisesExportacao =
        { "Brasil", "Chile", "Colombia", "Panama", "Peru", "Rep Dominicana", "Outro País" };

    /// <summary>As opções de Estado para o destino escolhido (J6 = INDIRECT(J5&amp;"_")).</summary>
    public static string[] EstadosDo(string destino) =>
        Textos.Igual(destino, "Exportação") ? PaisesExportacao
        : Textos.Simples(destino).Contains("back to back") ? new[] { "EXPORT" }
        : EstadosNacional;

    /// <summary>Cliente com (J9, Regime_Cliente).</summary>
    public static readonly string[] RegimeCliente =
    {
        "Sem benefício", "não-contribuinte ICMS", "Benefício RECAP / REIDI", "Zona Franca de Manaus",
    };

    public static readonly string[] SimNao = { "Sim", "Não" };

    /// <summary>
    /// Portal (P4, lista_Portal → listas B3:D13): percentual sobre a venda, ou
    /// "teto" — um valor fixo na moeda da planilha (D50 entra em referência
    /// circular e acaba cobrando exatamente esse valor).
    /// </summary>
    public sealed record OpcaoPortal(string Nome, decimal Percentual, decimal? Teto = null);

    public static readonly OpcaoPortal[] Portais =
    {
        new("não", 0m),
        new("Petronect < BRL 22,5 M", 0.002m),
        new("Petronect > BRL 22,5 M", 0m, 45000m),
        new("Quadrem_Ariba HSA_HCHL > USD 50k", 0.00155m),
        new("Quadrem_Ariba HSA_HCHL > USD 50k(SV-RF)", 0.0035m),
        new("Quadrem_Ariba HPU < USD 25k", 0.004m),
        new("Quadrem_Ariba HPU > USD 25k", 0m, 100m),
        new("Unilink < USD 74,074", 0.0027m),
        new("Unilink > USD 74,074", 0m, 200m),
        new("NIMBI", 0.0045m),
    };

    /// <summary>Portal ainda não escolhido ("preencher" na planilha): 0,45%.</summary>
    public static readonly OpcaoPortal PortalSemEscolha = new("preencher", 0.0045m);

    public static OpcaoPortal PortalDo(string nome) =>
        Portais.FirstOrDefault(p => Textos.Igual(p.Nome, nome)) ?? PortalSemEscolha;

    /// <summary>Tipo de seguro garantia / carta fiança (R46, Custo_Garantia: listas J3:K7) — custo ao ano.</summary>
    public static readonly (string Nome, decimal TaxaAnual)[] TiposGarantia =
    {
        ("HSA C. Fiança (Banco)", 0.05m),
        ("HSA S. Garantia (Corretora)", 0.01m),
        ("HCHL/HPU C. Fiança (Banco)", 0.0375m),
        ("HCHL/HPU S. Garantia (Corretora)", 0.0175m),
    };

    /// <summary>Tipo de garantia não escolhido ("preencher"): 5% ao ano.</summary>
    public const decimal TaxaGarantiaSemEscolha = 0.05m;

    public static decimal TaxaDaGarantia(string tipo) =>
        TiposGarantia.FirstOrDefault(t => Textos.Igual(t.Nome, tipo)).TaxaAnual is var t && t > 0
            ? t : TaxaGarantiaSemEscolha;

    /// <summary>Categories (P7, Categories_) e os Products de cada uma (P8 = INDIRECT(P7&amp;"_")).</summary>
    public static readonly (string Categoria, string[] Produtos)[] Categorias =
    {
        ("Axials", new[] { "PAXIAL4 - Axials Configured Fans", "PAXIAL5 - Axials Engineered" }),
        ("CentrifugalFans", new[]
        {
            "PCFAN1 - Turbo Fans", "PCFAN2 - Engineered Fans - Technopal", "PCFAN3 - Centrifugal Engineered Fans",
        }),
        ("Compressors", new[]
        {
            "PCOMP1 - Centrifugal Blower", "PCOMP7 - Screw Packages", "PCOMP7 - Screw Bareshaft",
            "PCOMP12 - Steam Turbines", "PCOMP13 - Centrifugal Blower - (IC-STE)",
            "PCOMP14 - Centrifugal Blower - (EC-STE)", "P000057 - Spares- Centrifugal - Other - Compressors ",
        }),
        ("Heaters", new[] { "PHEAT2 - Air Preheater" }),
        ("Other", new[] { "POTH1 - Ventsim Software", "POTH5 - Other_Other" }),
    };

    public static string[] ProdutosDa(string categoria) =>
        Categorias.FirstOrDefault(c => Textos.Igual(c.Categoria, categoria)).Produtos ?? Array.Empty<string>();

    /// <summary>Market Segments (P9, MarketSegments_: listas G3:G26).</summary>
    public static readonly string[] MarketSegments =
    {
        "Coal", "Energy & Renewable Power - Energy Recovery", "Energy & Renewable Power - Midstream",
        "Energy & Renewable Power - Other", "Energy & Renewable Power - Other Hydrogen",
        "Energy & Renewable Power - Refinery", "Energy & Renewable Power - Renewable Hydrogen",
        "Energy & Renewable Power - Upstream", "Industrial - Cement", "Industrial - Chemicals",
        "Industrial - Food & Beverages", "Industrial - Metals Processing", "Industrial - Petrochemical",
        "Industrial - Other Industries - Ethanol", "Industrial - Other Industries - Fertilizer",
        "Industrial - Other Industries - Glass", "Industrial - Other Industries - Pulp and Paper",
        "Industrial - Other Industries", "Infrastructure & Mobility - Marine",
        "Infrastructure & Mobility - Traction", "Infrastructure & Mobility - Transportation",
        "Infrastructure & Mobility - Tunnel", "Infrastructure & Mobility - Water (Municipal)", "Mining",
    };

    /// <param name="ComissaoNb">Coluna "Comissao NB" (BD_pricing C), usada quando o segmento é NB.</param>
    /// <param name="ComissaoAfm">Coluna "Comissao AFM" (BD_pricing D), usada nos demais segmentos.</param>
    public sealed record Representante(string Grupo, string Nome, decimal ComissaoNb, decimal ComissaoAfm);

    /// <summary>
    /// Representantes (P5/P6, lista_representantes: BD_pricing A67:D99). "-" é
    /// "sem representante". O pricing acha a comissão com MATCH(…, 0) — o nome
    /// tem de bater letra por letra.
    /// </summary>
    public static readonly Representante[] Representantes =
    {
        new("", "-", 0m, 0m),
        new("Brasil", "Douglas (Mezza & Baga)", 0.03m, 0.03m),
        new("Brasil", "Alexandre (Artman)", 0.03m, 0.03m),
        new("Brasil", "Gerson (Lizan)", 0.03m, 0.03m),
        new("Brasil", "Ivars (Dzelme & Leite Ltda)", 0.03m, 0.03m),
        new("Brasil", "Júlio (Doulus)", 0.03m, 0.03m),
        new("Brasil", "Maurício (Livimat)", 0.03m, 0.03m),
        new("Brasil", "Ricardo (Sesbras)", 0.05m, 0.05m),
        new("Brasil", "Sander (Provent)", 0.05m, 0.05m),
        new("Brasil", "Thais (InTec)", 0.03m, 0.03m),
        new("Brasil / Exterior", "Adolpho (Atric)", 0.05m, 0.05m),
        new("Brasil", "Polonio (Giovanni e Polonio)", 0.03m, 0.03m),
        new("Exterior", "ASESORIA Y EQUIPO < =USD 500K", 0.1m, 0.1m),
        new("Exterior", "ASESORIA Y EQUIPO < USD 1MM", 0.075m, 0.075m),
        new("Exterior", "ASESORIA Y EQUIPO > USD 1MM", 0.05m, 0.05m),
        new("Exterior", "FERRUNION", 0.05m, 0.05m),
        new("Exterior", "H&T", 0.05m, 0.05m),
        new("Exterior", "HCA", 0.06m, 0.06m),
        new("Exterior", "HRI S.A.", 0.05m, 0.05m),
        new("Exterior", "IPT Colômbia < =EUR 1,25MM", 0.035m, 0.035m),
        new("Exterior", "IPT Colômbia < =EUR 1MM", 0.05m, 0.05m),
        new("Exterior", "IPT Colômbia < =EUR 500K", 0.08m, 0.08m),
        // na planilha está como TEXTO "6.5%": o SUM das comissões (D46) ignora
        // texto e a comissão sai zero. Aqui vale 6,5%, que é a intenção.
        new("Exterior", "IPT Colômbia < =EUR 750K", 0.065m, 0.065m),
        new("Exterior", "IPT Colômbia >EUR 1,25MM", 0.03m, 0.03m),
        new("Exterior", "SIMINCO", 0.05m, 0.05m),
        new("Exterior", "SISTAGUA", 0.05m, 0.05m),
        new("Exterior", "TEJADA", 0.05m, 0.05m),
        new("Exterior", "Turbomaquinarias <= EUR 100 k", 0.04m, 0.04m),
        new("Exterior", "Turbomaquinarias <= EUR 2,5 M", 0.025m, 0.025m),
        new("Exterior", "Turbomaquinarias <= EUR 500 k", 0.03m, 0.03m),
        new("Exterior", "Turbomaquinarias <= EUR 7,0 M", 0.02m, 0.02m),
        new("Exterior", "Turbomaquinarias > EUR 7,0 M", 0.01m, 0.01m),
    };

    /// <summary>Comissão do representante (BD_pricing C63/D63): coluna NB se o segmento é NB, senão AFM.</summary>
    public static decimal ComissaoDoRepresentante(string nome, string segmento)
    {
        var r = Representantes.FirstOrDefault(x => x.Nome == (nome ?? "").Trim());
        if (r is null) return 0m;
        return segmento == "NB" ? r.ComissaoNb : r.ComissaoAfm;
    }

    /// <summary>
    /// ICMS por estado (BD_pricing A33:E59): alíquota normal e com base
    /// reduzida ("NCM c/redução base ICMS" = Sim), na coluna da HSA-SP e na da HSA-ES.
    /// </summary>
    public static readonly Dictionary<string, (decimal Sp, decimal Es, decimal SpReduzida, decimal EsReduzida)> Icms = new()
    {
        ["AC"] = (0.07m, 0.12m, 0.0514m, 0.088m), ["AL"] = (0.07m, 0.12m, 0.0514m, 0.088m),
        ["AM"] = (0.07m, 0.12m, 0.0514m, 0.088m), ["AP"] = (0.07m, 0.12m, 0.0514m, 0.088m),
        ["BA"] = (0.07m, 0.12m, 0.0514m, 0.088m), ["CE"] = (0.07m, 0.12m, 0.0514m, 0.088m),
        ["DF"] = (0.07m, 0.12m, 0.0514m, 0.088m), ["ES"] = (0.07m, 0.17m, 0.0514m, 0.088m),
        ["GO"] = (0.07m, 0.12m, 0.0514m, 0.088m), ["MA"] = (0.07m, 0.12m, 0.0514m, 0.088m),
        ["MG"] = (0.12m, 0.12m, 0.088m, 0.088m),  ["MS"] = (0.07m, 0.12m, 0.0514m, 0.088m),
        ["MT"] = (0.07m, 0.12m, 0.0514m, 0.088m), ["PA"] = (0.07m, 0.12m, 0.0514m, 0.088m),
        ["PB"] = (0.07m, 0.12m, 0.0514m, 0.088m), ["PE"] = (0.07m, 0.12m, 0.0514m, 0.088m),
        ["PI"] = (0.07m, 0.12m, 0.0514m, 0.088m), ["PR"] = (0.12m, 0.12m, 0.088m, 0.088m),
        ["RJ"] = (0.12m, 0.12m, 0.088m, 0.088m),  ["RN"] = (0.07m, 0.12m, 0.0514m, 0.088m),
        ["RO"] = (0.07m, 0.12m, 0.0514m, 0.088m), ["RR"] = (0.07m, 0.12m, 0.0514m, 0.088m),
        ["RS"] = (0.12m, 0.12m, 0.088m, 0.088m),  ["SC"] = (0.12m, 0.12m, 0.088m, 0.088m),
        ["SE"] = (0.07m, 0.12m, 0.0514m, 0.088m), ["SP"] = (0.18m, 0.12m, 0.088m, 0.088m),
        ["TO"] = (0.07m, 0.12m, 0.0514m, 0.088m),
        // Chile e Peru estão na lista com as células vazias: ICMS zero
        ["Chile"] = (0m, 0m, 0m, 0m), ["Peru"] = (0m, 0m, 0m, 0m),
    };

    /// <summary>
    /// Retenções na venda (Tabela_Retencoes_Venda: BD_pricing A121:E129) por
    /// linha (país, ou "material / produto COL") e BU. 1 = 100%: consultar o Financeiro.
    /// </summary>
    public static readonly Dictionary<string, Dictionary<string, decimal>> Retencoes = new()
    {
        ["material / produto COL"] = new() { ["HCHL"] = 0.03m, ["HPU"] = 0.1m, ["HSA-ES"] = 0.03m, ["HSA-SP"] = 0.03m },
        ["Brasil"] = new() { ["HCHL"] = 0.42m, ["HPU"] = 0.42m, ["HSA-ES"] = 0m, ["HSA-SP"] = 0m },
        ["Chile"] = new() { ["HCHL"] = 0m, ["HPU"] = 1m, ["HSA-ES"] = 1m, ["HSA-SP"] = 1m },
        ["Colombia"] = new() { ["HCHL"] = 0.2m, ["HPU"] = 0.2m, ["HSA-ES"] = 0.2m, ["HSA-SP"] = 0.2m },
        ["Panama"] = new() { ["HCHL"] = 0.125m, ["HPU"] = 1m, ["HSA-ES"] = 1m, ["HSA-SP"] = 1m },
        ["Peru"] = new() { ["HCHL"] = 0.15m, ["HPU"] = 0m, ["HSA-ES"] = 0.15m, ["HSA-SP"] = 0.15m },
        ["Rep Dominicana"] = new() { ["HCHL"] = 0.27m, ["HPU"] = 1m, ["HSA-ES"] = 0.27m, ["HSA-SP"] = 0.27m },
        ["Outro País"] = new() { ["HCHL"] = 1m, ["HPU"] = 1m, ["HSA-ES"] = 1m, ["HSA-SP"] = 1m },
    };
}
