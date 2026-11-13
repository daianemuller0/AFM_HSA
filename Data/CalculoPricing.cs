namespace AfmHsa.Data;

/// <summary>
/// O cálculo do pricing do AFM, espelhando a aba PRICING da planilha
/// R_HSAXYZ0000-0.xlsm (revisão de 09/10/2025) com todas as opções dela.
/// As letras entre parênteses são as células de lá:
///
///   CÁLCULO DE CUSTO (E14:E33, com variação F14:F33) = custo total (E34)
///   + risco de variação (E36) + risco adicional (D37 × G34) = custo com riscos (E39)
///   × markup (E41) = venda pura (E42) → + seguro garantia / fiança (E43) = E44
///   ÷ (1 − comissões − negociação − PM+SACH − garantia − portal) = VENDA LÍQUIDA (E52)
///   → quadro PREÇO DE VENDA (K12:Q24): PIS/COFINS, ICMS, IPI, ISS (MO de
///     Reforma/Service), retenção, Back to Back.
///
/// O markup que a planilha acha com o Solver (margem D55 = margem pedida)
/// aqui é conta fechada. Fiança/garantias e portal com teto, que lá são
/// referência circular, também: a fiança é proporcional à venda líquida, e o
/// portal com teto cobra exatamente o valor do teto.
///
/// Onde a planilha usa "-" como alíquota numa conta (e o Excel devolve
/// #VALOR!), aqui o "-" vale zero — é o que ele quer dizer: não incide.
/// </summary>
public static class CalculoPricing
{
    // ---------- o que é fixo na planilha ----------

    /// <summary>PPR: 1% (BD_pricing B118 → L40).</summary>
    public const decimal Ppr = 0.01m;
    /// <summary>Sales Director / ELG: 0,4% (BD_pricing B111 → M40).</summary>
    public const decimal Elg = 0.004m;
    /// <summary>Sales Industrial (BD_pricing B112 → N40): zero; e no AFM sempre zero (D118).</summary>
    public const decimal SalesIndustrial = 0m;
    /// <summary>Provisão DSR: 64,11% do que vai para SALES (T40).</summary>
    public const decimal Dsr = 0.6411m;

    public const decimal MargemNegociacaoPadrao = 0.03m;  // D47
    /// <summary>PM + SACH: 1% (D48; os 0,4% de SACH saíram em 27/06/2024).</summary>
    public const decimal PmSach = 0.01m;
    public const decimal GarantiaDoProjeto = 0.02m;       // D49

    /// <summary>Risco/Contingência (D37): 3,70% (13/11/26 - LS).</summary>
    public const decimal RiscoPadrao = 0.037m;

    /// <summary>
    /// Protected Margin (F14:F33, a coluna "variação" de cada linha de custo):
    /// 2%, e 0% para VAX e JOY (os ventiladores axiais configurados).
    /// </summary>
    public const decimal ProtectedMarginPadrao = 0.02m;
    public static readonly string[] ProdutosVaxJoy = { "PAXIAL4 - Axials Configured Fans" };

    public const decimal Pis = 0.0165m;      // D61
    public const decimal Cofins = 0.076m;    // D62
    public const decimal Ipi = 0.0325m;      // D64 (decreto 11.055/2022)
    /// <summary>ISS (D66): Itatiba 2% (HSA-SP), Serra 5% (HSA-ES); ISS outro município 5% (D67).</summary>
    public const decimal IssItatiba = 0.02m, IssSerra = 0.05m, IssOutroMunicipio = 0.05m;
    /// <summary>PIS + COFINS do produto Back to Back (D65).</summary>
    public const decimal ImpostoBackToBack = 0.0925m;
    /// <summary>Fator do produto B2B: o preço dele é 1,25 × o custo (N84).</summary>
    public const decimal FatorBackToBack = 1.25m;

    /// <summary>Fiança: % da venda no 1º evento de pagamento (N49), os demais começam em zero.</summary>
    public const decimal FiancaPercentualPadrao = 0.30m;

    // ---------- tabela CÁLCULO DE CUSTO ----------

    /// <param name="MaoDeObra">Entra na parte MO de Reforma/Service (U79: E15, E17, E19, E21, E26, E27).</param>
    public sealed record LinhaDeCusto(int Linha, string Codigo, string Nome, bool MaoDeObra);

    /// <summary>As linhas B14:B33 (as três "-" vazias, 29 a 31, ficaram de fora).</summary>
    public static readonly LinhaDeCusto[] LinhasDeCusto =
    {
        new(14, "MP_ROTOR", "MP - ROTOR", false),
        new(15, "MO_ROTOR", "MO - ROTOR", true),
        new(16, "MP_EIXO", "MP - EIXO", false),
        new(17, "MO_EIXO", "MO - EIXO", true),
        new(18, "MP_CUBO", "MP - CUBO", false),
        new(19, "MO_CUBO", "MO - CUBO", true),
        new(20, "MP_ESTATICAS", "MP - PARTES ESTÁTICAS", false),
        new(21, "MO_ESTATICAS", "MO - PARTES ESTÁTICAS", true),
        new(22, "MPMO_ESTATICAS", "MP+MO - PARTES ESTÁTICAS", false),
        new(23, "COMPRADOS", "PROD. COMPRADOS", false),
        new(24, "IMPORTADOS", "PROD. IMPORTADOS", false),
        new(25, "BACK_TO_BACK", "PROD. BACK TO BACK", false),
        new(26, "ENGENHARIA", "ENGENHARIA/INSPEÇÕES", true),
        new(27, "ASSESSORIA", "ASSESSORIA TÉCNICA", true),
        new(28, "ENG_SERVICOS", "ENG DE SERVIÇOS", false),
        new(32, "IMPREVISTOS", "IMPREVISTOS", false),
        new(33, "PROTECTED", "PROTECTED MARGIN", false),
    };

    /// <summary>Um valor da tabela de custo: E (valor) e F (variação, em fração).</summary>
    public sealed record Custo(string Codigo, decimal Valor, decimal Variacao = 0m);

    /// <summary>Um evento de pagamento coberto pela fiança (M49:O58): % do pagamento e prazo (dias).</summary>
    public sealed record EventoDePagamento(decimal Percentual, int PrazoDias);

    /// <summary>Prazos padrão dos 10 eventos (O49 = 0, O50 = 30 + O49…).</summary>
    public static readonly int[] PrazosPadraoDosEventos = { 0, 30, 60, 90, 120, 150, 180, 210, 240, 270 };

    /// <summary>Os 10 eventos padrão da planilha: 30% no 1º, zero nos demais.</summary>
    public static List<EventoDePagamento> EventosPadrao() =>
        PrazosPadraoDosEventos.Select((p, i) => new EventoDePagamento(i == 0 ? FiancaPercentualPadrao : 0m, p)).ToList();

    /// <summary>Outras garantias (M64:R67): PB, PF, BD e OUTROS — % da garantia e dias de cobertura.</summary>
    public sealed record OutraGarantia(string Tipo, decimal Percentual, int DiasCobertura);
    public static readonly string[] TiposDeOutrasGarantias = { "PB", "PF", "BD", "OUTROS" };

    /// <summary>Igualdade como no Excel: sem diferença de maiúscula, com diferença de acento.</summary>
    private static bool Eq(string? a, string b) =>
        string.Equals((a ?? "").Trim(), b, StringComparison.OrdinalIgnoreCase);

    public static decimal RiscoPadraoDo(string segmento, string product) => RiscoPadrao;

    /// <summary>A Protected Margin padrão do produto: 0% para VAX e JOY, 2% para o resto.</summary>
    public static decimal ProtectedMarginDo(string? product) =>
        ProdutosVaxJoy.Any(p => Eq(product, p)) ? 0m : ProtectedMarginPadrao;

    /// <summary>O que a proposta manda para o cálculo.</summary>
    public sealed record Entrada(
        IReadOnlyList<Custo> Custos,                     // E14:F33
        string Segmento,                                  // J3
        string Product,                                   // P8
        decimal RiscoAdicional,                           // D37
        decimal MargemNegociacao,                         // D47
        string Bu,                                        // E8
        bool NcmReducaoIcms,                              // E10
        string VendaPara,                                 // J4
        string Destino,                                   // J5
        string Estado,                                    // J6
        bool Reforma,                                     // J7
        string ClienteCom,                                // J9
        int PrazoEntregaDias,                             // J8
        bool ComFianca,                                   // P3
        string TipoGarantia,                              // R46
        IReadOnlyList<EventoDePagamento> Eventos,         // N49:O58
        string TipoOutrasGarantias,                       // R61
        IReadOnlyList<OutraGarantia> OutrasGarantias,     // N64:R67
        string Portal,                                    // P4
        string Rep1,                                      // P5
        string Rep2,                                      // P6
        decimal? AliquotaIpi = null)                      // D64 pela NCM dos itens; null = a da planilha (3,25%)
    {
        public decimal ValorDe(string codigo) => Custos.Where(c => c.Codigo == codigo).Sum(c => c.Valor);
    }

    /// <summary>Uma linha do quadro PREÇO DE VENDA (K12:T24). Valores em R$ — a tela converte.</summary>
    /// <param name="Aliquota">Coluna P; null com <paramref name="AliquotaTexto"/> = texto ("-", "Estado").</param>
    /// <param name="Valor">Coluna Q em R$; null com <paramref name="Texto"/> = texto ("procurar Financeiro"…).</param>
    /// <param name="Fator">Coluna T (valor ÷ custo).</param>
    public sealed record LinhaDoQuadro(string Celula, string Rotulo, decimal? Aliquota, string? AliquotaTexto,
        decimal? Valor, string? Texto, decimal? Fator);

    /// <summary>O pricing calculado, na ordem da planilha. Valores em R$.</summary>
    public sealed record Resultado
    {
        public decimal CustoTotal { get; init; }          // E34
        public decimal CustoComVariacao { get; init; }    // G34
        public decimal RiscoDeVariacao { get; init; }     // E36
        public decimal RiscoAdicional { get; init; }      // E37
        public decimal Risco { get; init; }               // E38
        public decimal CustoComRiscos { get; init; }      // E39
        public decimal Markup { get; init; }              // E41
        public decimal VendaPura { get; init; }           // E42
        public decimal Fianca { get; init; }              // E43
        public decimal Comissoes { get; init; }           // D46
        public decimal Portal { get; init; }              // D50 (no teto: teto ÷ venda líquida)
        public decimal PercentuaisDeVenda { get; init; }  // D46 + D47 + D48 + D49 + D50
        public decimal VendaLiquida { get; init; }        // E52
        public decimal MargemPadrao { get; init; }        // D54
        public decimal Margem { get; init; }              // D55
        public decimal MargemProtegida { get; init; }     // D56
        public decimal MargemTotal { get; init; }         // D57
        public decimal MargemDeContribuicao { get; init; }// D58
        public decimal NetCostBasis { get; init; }        // Q28

        public bool SemImpostos { get; init; }            // exportação, export. com B2B, Chile ou Peru
        public bool ProcurarFinanceiro { get; init; }     // revenda ou não-contribuinte (S76)
        public bool ServicoOuReforma { get; init; }       // Reforma = Sim ou segmento Service
        public bool ExportacaoComBackToBack { get; init; }
        public bool BackToBack100 { get; init; }

        public decimal AliqPis { get; init; }
        public decimal AliqCofins { get; init; }
        public decimal? AliqIcms { get; init; }
        public decimal AliqIpi { get; init; }
        public decimal AliqIss { get; init; }
        public decimal AliqIssOutro { get; init; }

        public decimal? VendaMpLiquida { get; init; }     // S74
        public decimal? VendaComPisCofins { get; init; }  // S75
        public decimal? VendaComIcms { get; init; }       // S76
        public decimal? PrecoComImpostos { get; init; }   // S77
        public decimal? VendaMoLiquida { get; init; }     // S79
        public decimal? VendaMoSemIss { get; init; }      // S80
        public decimal? VendaMoComIss { get; init; }      // S81

        public decimal ValorPis { get; init; }            // E61
        public decimal ValorCofins { get; init; }         // E62
        public decimal? ValorIcms { get; init; }          // E63
        public decimal? ValorIpi { get; init; }           // E64
        public decimal? ValorBackToBack { get; init; }    // E65
        public decimal? ValorIss { get; init; }           // E66
        public decimal? ValorIssOutro { get; init; }      // E67
        public decimal? Retencao { get; init; }           // D68
        public decimal? ValorRetencao { get; init; }      // E68 (null quando a retenção é 100%)

        public decimal? ParteNacional { get; init; }      // Q13
        public decimal? ProdutoBackToBack { get; init; }  // Q22 (em R$)
        public decimal? ProdutoBackToBackFinanceiro { get; init; } // S84
        public decimal? TotalBackToBack { get; init; }    // S82 ou S83

        /// <summary>
        /// O valor sem impostos brasileiros destacados que vai para os itens da
        /// proposta: a venda líquida; na exportação com B2B o total S82 e no
        /// B2B 100% importado o S83 (os dois já têm o PIS/COFINS do B2B por dentro).
        /// </summary>
        public decimal ValorDaProposta { get; init; }

        /// <summary>O que o cliente paga com impostos (S77, + S81 na MO; S82/S83 no B2B; Q13 na exportação).</summary>
        public decimal? PrecoAoCliente { get; init; }

        public string TituloDoQuadro { get; init; } = "";          // K12
        public string ImpostosDoQuadro { get; init; } = "";        // P12
        public List<LinhaDoQuadro> Quadro { get; init; } = new();
        public List<string> Avisos { get; init; } = new();
    }

    /// <summary>Comissões (D46 = L40:T40): PPR + ELG + Sales Industrial + DSR + representantes.</summary>
    public static decimal Comissoes(string segmento, string rep1, string rep2)
    {
        var salesInd = Eq(segmento, "AFM") ? 0m : SalesIndustrial;
        return Ppr + Elg + salesInd + Dsr * (Elg + salesInd)
             + ListasDoPricing.ComissaoDoRepresentante(rep1, segmento)
             + ListasDoPricing.ComissaoDoRepresentante(rep2, segmento);
    }

    /// <summary>
    /// Fração da venda líquida que a fiança e as outras garantias custam
    /// (E43 ÷ E52): Σ % × dias de cobertura × taxa anual ÷ 365. Os eventos
    /// cobrem do prazo deles até o prazo de entrega (R49 = J8 − O49).
    /// </summary>
    public static decimal FatorFianca(Entrada e)
    {
        if (!e.ComFianca) return 0m;
        var eventos = e.Eventos.Sum(ev => ev.Percentual * Math.Max(e.PrazoEntregaDias - ev.PrazoDias, 0));
        var outras = e.OutrasGarantias.Sum(g => g.Percentual * g.DiasCobertura);
        return eventos * ListasDoPricing.TaxaDaGarantia(e.TipoGarantia) / 365m
             + outras * ListasDoPricing.TaxaDaGarantia(e.TipoOutrasGarantias) / 365m;
    }

    private sealed record Base(decimal E34, decimal G34, decimal E36, decimal E37, decimal E39,
        decimal Percentuais, decimal Teto, decimal Fianca, decimal Comissoes);

    private static Base Bases(Entrada e)
    {
        var e34 = e.Custos.Sum(c => c.Valor);
        var g34 = e.Custos.Sum(c => c.Valor + c.Valor * c.Variacao);
        var e36 = Math.Abs(g34 - e34);                                   // ((G34 − E34)²)^0,5
        var e37 = e.RiscoAdicional * g34;
        var comissoes = Comissoes(e.Segmento, e.Rep1, e.Rep2);
        var portal = ListasDoPricing.PortalDo(e.Portal);
        var percentuais = comissoes + e.MargemNegociacao + PmSach + GarantiaDoProjeto
                        + (portal.Teto is null ? portal.Percentual : 0m);
        return new Base(e34, g34, e36, e37, e34 + e36 + e37, percentuais, portal.Teto ?? 0m, FatorFianca(e), comissoes);
    }

    /// <summary>Calcula a partir da margem pedida (P26, "a partir da Margem").</summary>
    public static Resultado PelaMargem(Entrada e, decimal margemAlvo)
    {
        var b = Bases(e);
        var sobra = 1m - b.Percentuais - b.Fianca - margemAlvo;
        var vendaLiquida = sobra <= 0m ? 0m : (b.E39 + b.Teto) / sobra;
        return Montar(e, b, vendaLiquida);
    }

    /// <summary>Calcula a partir da venda líquida pedida (P27, "a partir do Preço de Venda").</summary>
    public static Resultado PeloPreco(Entrada e, decimal vendaLiquida) => Montar(e, Bases(e), Math.Max(vendaLiquida, 0m));

    private static decimal? Div(decimal a, decimal b) => b == 0m ? null : a / b;

    private static Resultado Montar(Entrada e, Base b, decimal vl)
    {
        var avisos = new List<string>();

        // ---------- venda e margens ----------
        var fianca = b.Fianca * vl;                                              // E43
        var portalValor = vl <= 0m ? 0m : b.Teto > 0m ? b.Teto : ListasDoPricing.PortalDo(e.Portal).Percentual * vl;
        var vendaPura = vl * (1m - b.Percentuais - b.Fianca) - (b.Teto > 0m && vl > 0m ? b.Teto : 0m);   // E42
        var markup = b.E39 > 0m ? vendaPura / b.E39 : 0m;
        var margem = vl <= 0m ? 0m : 1m - (b.E39 + (b.Teto > 0m ? b.Teto : 0m)) / vl - b.Fianca - b.Percentuais;
        var portalPct = vl > 0m ? portalValor / vl : ListasDoPricing.PortalDo(e.Portal).Percentual;
        var outrosPct = e.MargemNegociacao + PmSach + GarantiaDoProjeto;
        var protegido = e.ValorDe("PROTECTED");                                 // E33 = E56
        var margemProtegida = vl > 0m ? protegido / vl : 0m;                    // D56
        var margemContribuicao = vl > 0m
            ? (vl - (b.E39 + fianca + outrosPct * vl + portalValor - protegido)) / vl : 0m;   // D58

        // ---------- as portas da planilha ----------
        var exportacao = Eq(e.Destino, "Exportação");
        var expB2B = Eq(e.Destino, "Exportação com Back to Back");
        var b2b100 = Eq(e.Destino, "Back to Back 100% importado");
        var peru = Eq(e.Estado, "Peru");
        var chile = Eq(e.Estado, "Chile");
        var estadoExp = Eq(e.Estado, "EXP");    // a planilha testa "EXP" (a lista tem "EXPORT")
        var nb = Eq(e.Segmento, "NB");
        var zfm = Eq(e.ClienteCom, "Zona Franca de Manaus");
        var recap = Eq(e.ClienteCom, "Benefício RECAP / REIDI");
        var industrializacao = Eq(e.VendaPara, "Industrialização");
        var servRef = e.Reforma || Eq(e.Segmento, "Service");

        // ---------- alíquotas ("-" = zero) ----------
        var aliqPis = exportacao || (recap && nb) || zfm ? 0m : Pis;              // D61
        var aliqCofins = exportacao || (recap && nb) || zfm ? 0m : Cofins;        // D62
        var aliqIpi = exportacao || expB2B || nb || zfm ? 0m : e.AliquotaIpi ?? Ipi;   // D64
        var aliqIcms = AliquotaIcms(e, exportacao || expB2B || (zfm && industrializacao));   // D63
        decimal aliqIss = 0m, aliqIssOutro = 0m;
        if (servRef)
        {
            aliqIss = Eq(e.Bu, "HSA-SP") ? IssItatiba : Eq(e.Bu, "HSA-ES") ? IssSerra : 0m;   // D66
            aliqIssOutro = IssOutroMunicipio;                                                    // D67
            if (!Eq(e.Bu, "HSA-SP") && !Eq(e.Bu, "HSA-ES"))
                avisos.Add("Reforma/Service: a planilha só tem ISS do município para HSA-SP (2%) e HSA-ES (5%) — " +
                           "com outra BU ele fica \"-\" e a conta da MO dá erro lá. Aqui entrou zero.");
        }

        // ---------- MP e MO (Reforma / Service) ----------
        var mo = e.Custos.Where(c => LinhasDeCusto.Any(l => l.Codigo == c.Codigo && l.MaoDeObra)).Sum(c => c.Valor);
        var u79 = servRef && b.E34 > 0m ? mo / b.E34 : 0m;
        var u74 = servRef && b.E34 > 0m ? (b.E34 - mo) / b.E34 : 0m;

        var foraMp = exportacao || expB2B || estadoExp;                        // S74, S79, S80, S81
        var semImpostos = exportacao || expB2B || peru || chile;              // S75, S77
        decimal? s74 = !foraMp && servRef ? vl * u74 : null;
        decimal? s79 = !foraMp && servRef ? vl * u79 : null;

        decimal? s75 = null, s76 = null, s77 = null, s80 = null, s81 = null;
        var procurarFinanceiro = false;
        if (!semImpostos)
        {
            var baseMp = servRef ? (s74 ?? 0m) : vl;
            s75 = baseMp / (1m - aliqPis - aliqCofins);                         // S75
            if (!estadoExp)
            {
                if (Eq(e.VendaPara, "Revenda") || Eq(e.ClienteCom, "não-contribuinte ICMS"))
                {
                    procurarFinanceiro = true;
                    avisos.Add("Revenda ou cliente não-contribuinte de ICMS: a planilha manda procurar o " +
                               "Financeiro para fechar o preço com ICMS e IPI.");
                }
                else if (aliqIcms is null)
                {
                    if (!b2b100)
                        avisos.Add(string.IsNullOrWhiteSpace(e.Estado) || Eq(e.Estado, "preencher")
                            ? "Informe o Estado: sem ele não sai o ICMS nem o preço com impostos."
                            : $"Estado \"{e.Estado}\" não está na tabela de ICMS da planilha.");
                }
                else
                {
                    var fator = aliqIcms.Value + aliqIcms.Value * (industrializacao ? 0m : aliqIpi);
                    s76 = s75 / (1m - fator);                                     // S76
                    s77 = s76 * (1m + aliqIpi);                                   // S77
                }
            }
        }
        if (!foraMp && servRef && s79 is decimal moLiq)
        {
            s81 = moLiq / (1m - aliqPis - aliqCofins - aliqIss - aliqIssOutro);  // S81
            s80 = s81 * (1m - aliqIss - aliqIssOutro);                           // S80
        }

        // ---------- Back to Back ----------
        var e25 = e.ValorDe("BACK_TO_BACK");
        var g25 = e.Custos.Where(c => c.Codigo == "BACK_TO_BACK").Sum(c => c.Valor + c.Valor * c.Variacao);
        decimal? s84 = expB2B ? FatorBackToBack * (1m - aliqPis - aliqCofins) * e25 : null;   // S84
        decimal? q22 = expB2B ? s84 / (1m - aliqPis - aliqCofins) : null;                    // Q22 (R$)

        // Q13: na exportação com B2B, a parte nacional é a venda líquida menos o
        // produto B2B. (No Excel o teste ISERROR(SEARCH(…))=0 dessa célula nunca dá
        // verdadeiro e ela mostra a venda líquida inteira; no LibreOffice entra
        // como a fórmula pretende — é o que vale aqui.)
        var q13 = expB2B ? vl - (s84 ?? 0m) : exportacao ? vl : servRef ? (s74 ?? 0m) : vl;
        decimal? s82 = expB2B ? vl + ((q22 ?? 0m) - (s84 ?? 0m)) : null;                      // S82
        decimal? s83 = b2b100 ? q13 / (1m - aliqPis - aliqCofins) : null;                      // S83 (em R$)
        var t13 = Div(q13, b.E34 - (expB2B ? e25 : 0m));
        decimal? e65 = g25 > 0m && t13 is decimal fatorNac ? g25 * ImpostoBackToBack * fatorNac : null;  // E65

        // ---------- valores dos impostos ----------
        decimal pis, cofins;
        if (servRef)
        {
            pis = aliqPis * ((s75 ?? 0m) + (s81 ?? 0m));                         // E61
            cofins = aliqCofins * ((s75 ?? 0m) + (s81 ?? 0m));                   // E62
        }
        else if (expB2B)
        {
            pis = aliqPis * (q22 ?? 0m);
            cofins = aliqCofins * (q22 ?? 0m);
        }
        else
        {
            pis = (s75 ?? 0m) * aliqPis;
            cofins = (s75 ?? 0m) * aliqCofins;
        }
        decimal? icms = s77 is decimal p77 && aliqIcms is decimal ai ? p77 * ai : null;   // E63
        decimal? ipi = s76 is decimal p76 ? p76 * aliqIpi : null;                         // E64
        decimal? iss = s81 is decimal p81 ? p81 * aliqIss : null;                         // E66
        decimal? issOutro = s81 is decimal q81 ? q81 * aliqIssOutro : null;               // E67

        // ---------- retenção (D68/E68) ----------
        var retencao = Retencao(e, exportacao);
        decimal? valorRetencao = null;
        if (retencao is decimal r)
        {
            if (r >= 1m) avisos.Add("Retenção de 100% para este país e BU: consultar o Financeiro.");
            else valorRetencao = vl * r / (1m - r);
        }

        if (expB2B || b2b100)
            avisos.Add(expB2B
                ? "Exportação com Back to Back: o produto B2B (linha PROD. BACK TO BACK) sai a 1,25 × o custo, com " +
                  "PIS/COFINS por dentro; o resto é a parte nacional, sem impostos."
                : "Back to Back 100% importado: o valor ao cliente é a venda líquida com PIS/COFINS por dentro.");
        if (expB2B && e25 <= 0m)
            avisos.Add("Exportação com Back to Back sem custo na linha PROD. BACK TO BACK: preencha o custo do produto B2B.");

        // ---------- o que vai para a proposta ----------
        var valorDaProposta = expB2B ? s82 ?? vl : b2b100 ? s83 ?? vl : vl;
        decimal? precoAoCliente =
            expB2B ? s82 : b2b100 ? s83 : semImpostos ? q13
            : s77 is decimal comImp ? comImp + (servRef ? s81 ?? 0m : 0m) : null;

        var res = new Resultado
        {
            CustoTotal = b.E34, CustoComVariacao = b.G34, RiscoDeVariacao = b.E36, RiscoAdicional = b.E37,
            Risco = b.E36 + b.E37, CustoComRiscos = b.E39, Markup = markup, VendaPura = vendaPura, Fianca = fianca,
            Comissoes = b.Comissoes, Portal = portalPct, PercentuaisDeVenda = b.Comissoes + outrosPct + portalPct,
            VendaLiquida = vl, MargemPadrao = ListasDoPricing.MargemPadraoDo(e.Segmento), Margem = margem,
            MargemProtegida = margemProtegida, MargemTotal = margem + margemProtegida,
            MargemDeContribuicao = margemContribuicao, NetCostBasis = vl * (1m - margem),
            SemImpostos = semImpostos, ProcurarFinanceiro = procurarFinanceiro, ServicoOuReforma = servRef,
            ExportacaoComBackToBack = expB2B, BackToBack100 = b2b100,
            AliqPis = aliqPis, AliqCofins = aliqCofins, AliqIcms = aliqIcms, AliqIpi = aliqIpi,
            AliqIss = aliqIss, AliqIssOutro = aliqIssOutro,
            VendaMpLiquida = s74, VendaComPisCofins = s75, VendaComIcms = s76, PrecoComImpostos = s77,
            VendaMoLiquida = s79, VendaMoSemIss = s80, VendaMoComIss = s81,
            ValorPis = pis, ValorCofins = cofins, ValorIcms = icms, ValorIpi = ipi, ValorBackToBack = e65,
            ValorIss = iss, ValorIssOutro = issOutro, Retencao = retencao, ValorRetencao = valorRetencao,
            ParteNacional = q13, ProdutoBackToBack = q22, ProdutoBackToBackFinanceiro = s84,
            TotalBackToBack = s82 ?? s83, ValorDaProposta = valorDaProposta, PrecoAoCliente = precoAoCliente,
            Avisos = avisos,
        };
        return res with
        {
            TituloDoQuadro = e.Reforma ? "PREÇO DE VENDA - REFORMA"
                : exportacao || peru ? "PREÇO DE VENDA - EXPORTAÇÃO" : "PREÇO DE VENDA",
            ImpostosDoQuadro = exportacao || peru || chile ? "(sem impostos)" : "(impostos)",
            Quadro = Quadro(e, res, b.E34, e25, exportacao, expB2B, b2b100, peru, chile, estadoExp, servRef),
        };
    }

    /// <summary>
    /// O quadro PREÇO DE VENDA (K13:T23), linha a linha como a planilha monta:
    /// rótulo (K), alíquota (P), valor (Q) e fator sobre o custo (T). Linhas
    /// cujo rótulo a planilha deixa em "-" não entram.
    /// </summary>
    private static List<LinhaDoQuadro> Quadro(Entrada e, Resultado r, decimal e34, decimal e25,
        bool exportacao, bool expB2B, bool b2b100, bool peru, bool chile, bool estadoExp, bool servRef)
    {
        var q = new List<LinhaDoQuadro>();
        var semImpQuadro = exportacao || b2b100 || expB2B || peru || chile;   // P13:P16 e K14:K16
        var foraQ = exportacao || peru || chile;                               // Q14:Q20
        var foraMo = exportacao || expB2B || peru || chile;                    // O79
        var foraMo2 = foraMo || estadoExp;                                     // O80, O81
        decimal? F(decimal? v, decimal div) => v is decimal x && div != 0m ? x / div : null;

        // K13 / Q13
        var rotulo13 = exportacao || peru || Eq(e.Estado, "EXPORT")
            ? (expB2B ? "Parte nacional (sem impostos)" : "Venda Líquida (sem impostos)")
            : servRef && !exportacao && !expB2B ? "Venda MP Líquida (sem impostos)"
            : expB2B ? "Parte nacional (sem impostos)" : "Venda Líquida (sem impostos)";
        q.Add(new("K13", rotulo13, semImpQuadro ? null : 0m, semImpQuadro ? "-" : null,
            r.ParteNacional, null, F(r.ParteNacional, e34 - (expB2B ? e25 : 0m))));

        // K14 / Q14: fora do Brasil a planilha mostra "Valores em <moeda>" (a tela põe a moeda)
        var rotulo14 = semImpQuadro ? "Valores na moeda do cliente (sem impostos)"
            : servRef ? "Venda MP com PIS, COFINS e sem ICMS" : "Venda com PIS, COFINS e sem ICMS";
        var v14 = foraQ ? r.ParteNacional : r.VendaComPisCofins;
        q.Add(new("K14", rotulo14, semImpQuadro ? null : r.AliqPis + r.AliqCofins, semImpQuadro ? "-" : null,
            v14, v14 is null ? "-" : null, F(v14, e34)));

        if (!semImpQuadro)
        {
            var icmsTexto = r.AliqIcms is null ? "Estado" : null;
            var fin = r.ProcurarFinanceiro ? "procurar Financeiro" : "-";
            q.Add(new("K15", servRef ? "Venda MP com PIS, COFINS e com ICMS" : "Venda com PIS, COFINS e com ICMS",
                r.AliqIcms, icmsTexto, foraQ ? null : r.VendaComIcms, r.VendaComIcms is null ? fin : null, F(r.VendaComIcms, e34)));
            q.Add(new("K16", servRef ? "Venda MP com PIS, COFINS, ICMS e com IPI" : "Venda com PIS, COFINS, ICMS e com IPI",
                r.AliqIpi, null, foraQ ? null : r.PrecoComImpostos, r.PrecoComImpostos is null ? fin : null, F(r.PrecoComImpostos, e34)));
        }

        if (r.Retencao is decimal ret)
            q.Add(new("K17", "Retenção (a confirmar com o cliente)", ret >= 1m ? null : ret, ret >= 1m ? "-" : null,
                r.ValorRetencao, r.ValorRetencao is null ? "Consultar Financeiro" : null, F(r.ValorRetencao, e34)));

        if (servRef && !foraMo)
        {
            q.Add(new("K18", "Venda MO Líquida (sem impostos)", 0m, null, r.VendaMoLiquida, r.VendaMoLiquida is null ? "-" : null, F(r.VendaMoLiquida, e34)));
            if (!foraMo2)
            {
                q.Add(new("K19", "Venda MO com PIS, COFINS e sem ISS", r.AliqPis + r.AliqCofins, null, r.VendaMoSemIss, r.VendaMoSemIss is null ? "-" : null, F(r.VendaMoSemIss, e34)));
                q.Add(new("K20", "Venda MO com PIS, COFINS e ISS total", r.AliqIss + r.AliqIssOutro, null, r.VendaMoComIss, r.VendaMoComIss is null ? "-" : null, F(r.VendaMoComIss, e34)));
            }
        }

        if (expB2B)
        {
            q.Add(new("K22", "Prod. B2B (inf. s/ impostos na proposta)", null, null, r.ProdutoBackToBack, null, F(r.ProdutoBackToBack, e25)));
            q.Add(new("K23", "Exportação com Back to Back (sem impostos)", null, null, r.TotalBackToBack, null, null));
        }
        else if (b2b100)
        {
            q.Add(new("K23", "Export. Back to Back 100% import (s/ impostos)", null, null, r.TotalBackToBack, null, F(r.TotalBackToBack, e34)));
        }
        return q;
    }

    /// <summary>
    /// ICMS (D63): zero na exportação, export. com B2B e Zona Franca para
    /// industrialização; senão a tabela do estado, na coluna da HSA-SP ou da
    /// HSA-ES (qualquer outra BU cai na da HSA-ES, como na planilha), com base
    /// reduzida se "NCM c/redução base ICMS" = Sim. Null = estado não informado
    /// ou fora da tabela.
    /// </summary>
    public static decimal? AliquotaIcms(Entrada e, bool isento)
    {
        if (string.IsNullOrWhiteSpace(e.Estado) || Eq(e.Estado, "preencher")) return null;
        if (isento) return 0m;
        var linha = ListasDoPricing.Icms.FirstOrDefault(x => Eq(e.Estado, x.Key));
        if (linha.Key is null) return null;
        var sp = Eq(e.Bu, "HSA-SP");
        return e.NcmReducaoIcms
            ? (sp ? linha.Value.SpReduzida : linha.Value.EsReduzida)
            : (sp ? linha.Value.Sp : linha.Value.Es);
    }

    /// <summary>
    /// Retenção na venda (D68): serviço (segmento Service ou Ventsim) pela
    /// linha do país; material/produto exportado para a Colômbia pela linha
    /// "material / produto COL". Null = não há retenção.
    /// </summary>
    public static decimal? Retencao(Entrada e, bool exportacao)
    {
        var bu = (e.Bu ?? "").Trim();
        Dictionary<string, decimal>? linha = null;
        if (Eq(e.Segmento, "Service") || Eq(e.Product, "POTH1 - Ventsim Software"))
            linha = ListasDoPricing.Retencoes.FirstOrDefault(x => Eq(e.Estado, x.Key)).Value;
        else if (exportacao && Eq(e.Estado, "Colombia"))
            linha = ListasDoPricing.Retencoes["material / produto COL"];
        if (linha is null) return null;
        var col = linha.FirstOrDefault(x => Eq(bu, x.Key));
        return col.Key is null ? null : col.Value == 0m ? null : col.Value;
    }
}
