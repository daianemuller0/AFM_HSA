namespace AfmHsa.Data;

/// <summary>
/// O cálculo do pricing do AFM, espelhando a aba PRICING da planilha
/// R_HSAXYZ0000-0.xlsm (revisão de 09/10/2025). As letras entre parênteses
/// são as células de lá, para quem for conferir um número:
///
///   custo total (E34) + risco adicional (D37 × custo) = custo com riscos (E39)
///   × markup (E41) = venda pura (E42) → + fiança (E43) = E44
///   ÷ (1 − comissões − negociação − PM+SACH − garantia − portal) = VENDA LÍQUIDA (E52)
///   → PIS/COFINS (S75) → ICMS (S76) → IPI (S77) = preço com impostos
///
/// O markup que a planilha acha com o Solver (margem D55 = margem pedida)
/// aqui é conta fechada. Fiança e portal com teto, que lá são referência
/// circular, também viram conta fechada: a fiança é proporcional à venda
/// líquida, e o portal com teto acaba cobrando exatamente o valor do teto.
///
/// Os custos dos itens da proposta entram como material (sem mão de obra),
/// então Reforma/Service não mudam os impostos — como na planilha quando não
/// há linhas de MO. Substitui a cópia do axiaisvent (commit 5e9d735).
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

    /// <summary>Risco adicional (D37): 5%, ou 2% para NB com estes produtos.</summary>
    public const decimal RiscoPadrao = 0.05m;
    public const decimal RiscoReduzido = 0.02m;
    public static readonly string[] ProdutosComRiscoReduzido =
        { "PAXIAL4 - Axials Configured Fans", "PCOMP7 - Screw Bareshaft", "POTH1 - Ventsim Software" };

    public const decimal Pis = 0.0165m;      // D61
    public const decimal Cofins = 0.076m;    // D62
    public const decimal Ipi = 0.0325m;      // D64 (decreto 11.055/2022)

    /// <summary>Fiança: % da venda coberto no 1º evento de pagamento (N49), contado do dia 0 (O49).</summary>
    public const decimal FiancaPercentualPadrao = 0.30m;

    /// <summary>Igualdade como no Excel: sem diferença de maiúscula, com diferença de acento.</summary>
    private static bool Eq(string? a, string b) =>
        string.Equals((a ?? "").Trim(), b, StringComparison.OrdinalIgnoreCase);

    public static decimal RiscoPadraoDo(string segmento, string product) =>
        Eq(segmento, "NB") && ProdutosComRiscoReduzido.Any(p => Eq(product, p)) ? RiscoReduzido : RiscoPadrao;

    /// <summary>O que a proposta manda para o cálculo.</summary>
    public sealed record Entrada(
        decimal CustoTotal,          // E34
        string Segmento,             // J3
        string Product,              // P8
        decimal RiscoAdicional,      // D37
        decimal MargemNegociacao,    // D47
        string Bu,                   // E8
        bool NcmReducaoIcms,         // E10
        string VendaPara,            // J4
        string Destino,              // J5
        string Estado,               // J6
        string ClienteCom,           // J9
        int PrazoEntregaDias,        // J8
        bool ComFianca,              // P3
        string TipoGarantia,         // R46
        decimal FiancaPercentual,    // N49
        string Portal,               // P4
        string Rep1,                 // P5
        string Rep2);                // P6

    /// <summary>O pricing calculado, na ordem da planilha.</summary>
    public sealed record Resultado(
        decimal CustoTotal,          // E34
        decimal Risco,               // E38
        decimal CustoComRiscos,      // E39
        decimal Markup,              // E41
        decimal VendaPura,           // E42
        decimal Fianca,              // E43
        decimal Comissoes,           // D46
        decimal Portal,              // D50 (efetivo: no teto, teto ÷ venda líquida)
        decimal PercentuaisDeVenda,  // D46 + D47 + D48 + D49 + D50
        decimal VendaLiquida,        // E52
        decimal Margem,              // D55
        decimal MargemPadrao,        // D54
        decimal NetCostBasis,        // Q28
        bool SemImpostos,            // exportação, export. com B2B, Chile, Peru
        bool ProcurarFinanceiro,     // revenda ou não-contribuinte de ICMS (S76)
        decimal AliqPis, decimal AliqCofins, decimal? AliqIcms, decimal AliqIpi,
        decimal VendaComPisCofins,   // S75
        decimal? VendaComIcms,       // S76
        decimal? PrecoComImpostos,   // S77
        decimal ValorPis,            // E61
        decimal ValorCofins,         // E62
        decimal ValorIcms,           // E63
        decimal ValorIpi,            // E64
        decimal? Retencao,           // D68
        decimal? ValorRetencao,      // E68 (null quando a retenção é 100%)
        List<string> Avisos);

    /// <summary>Comissões (D46 = L40:T40): PPR + ELG + Sales Industrial + DSR + representantes.</summary>
    public static decimal Comissoes(string segmento, string rep1, string rep2)
    {
        var salesInd = Eq(segmento, "AFM") ? 0m : SalesIndustrial;
        return Ppr + Elg + salesInd + Dsr * (Elg + salesInd)
             + ListasDoPricing.ComissaoDoRepresentante(rep1, segmento)
             + ListasDoPricing.ComissaoDoRepresentante(rep2, segmento);
    }

    /// <summary>Fração da venda líquida que a fiança custa (E43 ÷ E52).</summary>
    public static decimal FatorFianca(Entrada e) => !e.ComFianca ? 0m
        : e.FiancaPercentual * Math.Max(e.PrazoEntregaDias, 0) * ListasDoPricing.TaxaDaGarantia(e.TipoGarantia) / 365m;

    private sealed record Base(decimal Comissoes, decimal Percentuais, decimal Teto, decimal Fianca,
        decimal Risco, decimal CustoComRiscos);

    private static Base Bases(Entrada e)
    {
        var comissoes = Comissoes(e.Segmento, e.Rep1, e.Rep2);
        var portal = ListasDoPricing.PortalDo(e.Portal);
        var percentuais = comissoes + e.MargemNegociacao + PmSach + GarantiaDoProjeto
                        + (portal.Teto is null ? portal.Percentual : 0m);
        var risco = e.RiscoAdicional * e.CustoTotal;              // E37 (= E38: sem variação por rubrica)
        return new Base(comissoes, percentuais, portal.Teto ?? 0m, FatorFianca(e), risco, e.CustoTotal + risco);
    }

    /// <summary>Calcula a partir da margem pedida (P26, "a partir da Margem").</summary>
    public static Resultado PelaMargem(Entrada e, decimal margemAlvo)
    {
        var b = Bases(e);
        var sobra = 1m - b.Percentuais - b.Fianca - margemAlvo;
        var vendaLiquida = sobra <= 0m ? 0m : (b.CustoComRiscos + b.Teto) / sobra;
        return Montar(e, b, vendaLiquida);
    }

    /// <summary>Calcula a partir da venda líquida pedida (P27, "a partir do Preço de Venda").</summary>
    public static Resultado PeloPreco(Entrada e, decimal vendaLiquida) => Montar(e, Bases(e), Math.Max(vendaLiquida, 0m));

    private static Resultado Montar(Entrada e, Base b, decimal vl)
    {
        var avisos = new List<string>();

        var fianca = b.Fianca * vl;                                      // E43
        var portalValor = vl <= 0m ? 0m : b.Teto > 0m ? b.Teto : 0m;    // E50 no teto
        var vendaPura = vl * (1m - b.Percentuais - b.Fianca) - portalValor;   // E42 = E52 × (1 − D46…D50) − E43
        var markup = b.CustoComRiscos > 0m ? vendaPura / b.CustoComRiscos : 0m;
        var margem = vl <= 0m ? 0m : 1m - (b.CustoComRiscos + b.Teto) / vl - b.Fianca - b.Percentuais;
        var portalPct = b.Teto > 0m ? (vl > 0m ? b.Teto / vl : 0m) : ListasDoPricing.PortalDo(e.Portal).Percentual;
        var percentuais = b.Percentuais + (b.Teto > 0m ? portalPct : 0m);

        // ---------- impostos ----------
        var exportacao = Eq(e.Destino, "Exportação");
        var expB2B = Eq(e.Destino, "Exportação com Back to Back");
        var b2b100 = Eq(e.Destino, "Back to Back 100% importado");
        var chilePeru = Eq(e.Estado, "Chile") || Eq(e.Estado, "Peru");
        var nb = Eq(e.Segmento, "NB");
        var zfm = Eq(e.ClienteCom, "Zona Franca de Manaus");
        var recap = Eq(e.ClienteCom, "Benefício RECAP / REIDI");
        var industrializacao = Eq(e.VendaPara, "Industrialização");

        // "-" na planilha vira zero aqui (é o que a planilha quer dizer: não incide)
        var aliqPis = exportacao || (recap && nb) || zfm ? 0m : Pis;          // D61
        var aliqCofins = exportacao || (recap && nb) || zfm ? 0m : Cofins;    // D62
        var aliqIpi = exportacao || expB2B || nb || zfm ? 0m : Ipi;          // D64
        var aliqIcms = AliquotaIcms(e, exportacao || expB2B || (zfm && industrializacao));   // D63

        var semImpostos = exportacao || expB2B || chilePeru;
        var procurarFinanceiro = !semImpostos &&
            (Eq(e.VendaPara, "Revenda") || Eq(e.ClienteCom, "não-contribuinte ICMS"));

        decimal s75 = vl, pis = 0m, cofins = 0m, icms = 0m, ipi = 0m;
        decimal? s76 = null, s77 = null;
        if (!semImpostos)
        {
            s75 = vl / (1m - aliqPis - aliqCofins);                           // S75
            pis = s75 * aliqPis;                                              // E61
            cofins = s75 * aliqCofins;                                        // E62
            if (procurarFinanceiro)
                avisos.Add("Revenda ou cliente não-contribuinte de ICMS: a planilha manda procurar o " +
                           "Financeiro para fechar o preço com ICMS e IPI.");
            else if (aliqIcms is null)
                avisos.Add(string.IsNullOrWhiteSpace(e.Estado) || Eq(e.Estado, "preencher")
                    ? "Informe o Estado: sem ele não sai o ICMS nem o preço com impostos."
                    : $"Estado \"{e.Estado}\" não está na tabela de ICMS da planilha.");
            else
            {
                var fator = aliqIcms.Value + aliqIcms.Value * (industrializacao ? 0m : aliqIpi);
                s76 = s75 / (1m - fator);                                     // S76
                s77 = s76 * (1m + aliqIpi);                                   // S77
                icms = s77.Value * aliqIcms.Value;                            // E63
                ipi = s76.Value * aliqIpi;                                    // E64
            }
        }

        if (expB2B || b2b100)
            avisos.Add("Back to Back: a planilha monta o preço em moeda estrangeira e com PIS/COFINS " +
                       "do produto B2B (Q22, S82 a S84) — confira o preço final na planilha do pricing.");
        if (!Eq(e.Bu, "HSA-SP") && !Eq(e.Bu, "HSA-ES") && !string.IsNullOrWhiteSpace(e.Bu))
            avisos.Add($"BU {e.Bu}: na planilha os valores ficam em {(Eq(e.Bu, "HCHL") ? "CLP" : "USD")}; " +
                       "aqui os custos e preços estão em R$.");

        // ---------- retenção (D68/E68) ----------
        var retencao = Retencao(e, exportacao);
        decimal? valorRetencao = null;
        if (retencao is decimal r)
        {
            if (r >= 1m) avisos.Add("Retenção de 100% para este país e BU: consultar o Financeiro.");
            else valorRetencao = vl * r / (1m - r);
        }

        var margemPadrao = ListasDoPricing.MargemPadraoDo(e.Segmento);
        return new Resultado(e.CustoTotal, b.Risco, b.CustoComRiscos, markup, vendaPura, fianca,
            b.Comissoes, portalPct, percentuais, vl, margem, margemPadrao, vl * (1m - margem),
            semImpostos, procurarFinanceiro, aliqPis, aliqCofins, aliqIcms, aliqIpi,
            s75, s76, s77, pis, cofins, icms, ipi, retencao, valorRetencao, avisos);
    }

    /// <summary>
    /// ICMS (D63): zero na exportação, export. com B2B e Zona Franca para
    /// industrialização; senão a tabela do estado, na coluna da HSA-SP ou da
    /// HSA-ES (qualquer outra BU cai na da HSA-ES, como na planilha), com base
    /// reduzida se "NCM c/redução base ICMS" = Sim. Null = estado não informado.
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
