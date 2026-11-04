namespace AfmHsa.Data;

/// <summary>
/// O cálculo do pricing, espelhando a aba PRICING da planilha da equipe.
///
/// A cadeia é a mesma, e as letras entre parênteses são as células de lá, para
/// quem for conferir um número não precisar adivinhar:
///
///   custo total (E33) → + riscos (E37) = custo com riscos (E38)
///   × markup (E40) = venda pura (E41) → + fiança (E42) = E43
///   ÷ (1 − comissões − margem negoc. − PM/SACH − garantia − portal) = VENDA LÍQUIDA (E51)
///   → gross-up de PIS/COFINS, ICMS e IPI = preço com impostos (S76)
///
/// O que a planilha faz com Solver — achar o markup que dá a margem pedida —
/// aqui é uma conta fechada, porque a margem é linear no markup.
///
/// Cópia fiel de HowdenAxiais.Poc.Data.CalculoPricing (projeto axiaisvent,
/// commit 94f39cd). Só ficou de fora EntradaDe/Da, que montam a entrada a
/// partir do modelo de proposta de lá — no AFM a entrada vem da proposta daqui.
/// Ao mudar a regra, mude nos dois projetos.
/// </summary>
public static class CalculoPricing
{
    // ---------- o que é fixo na planilha ----------

    /// <summary>Comissão de PPR: 1% (BD_pricing B114).</summary>
    public const decimal Ppr = 0.01m;
    /// <summary>Sales Director / ELG: 0,4% (BD_pricing B107).</summary>
    public const decimal SalesDirector = 0.004m;
    /// <summary>Sales Industrial: 0% fora de AFM (BD_pricing B108).</summary>
    public const decimal SalesIndustrial = 0m;
    /// <summary>Provisão DSR: 64,11% do que vai para SALES (PRICING T39).</summary>
    public const decimal Dsr = 0.6411m;

    public const decimal MargemNegociacaoPadrao = 0.03m;   // D46
    public const decimal RiscoAdicionalPadrao = 0.02m;     // D36, para axiais NB
    /// <summary>
    /// A margem que a proposta persegue quando ninguém mexe. A planilha nasceu
    /// com 25% (P25); a equipe subiu para 28%.
    /// </summary>
    public const decimal MargemAlvoPadrao = 0.28m;
    public const decimal GarantiaDoProjeto = 0.02m;        // D48
    public const decimal Portal = 0.007m;                  // D49
    public const decimal PmSachNacional = 0.014m;          // D47
    public const decimal PmSachExterior = 0.01m;

    public const decimal Pis = 0.0165m;                    // D60
    public const decimal Cofins = 0.076m;                  // D61
    public const decimal Ipi = 0.0325m;                    // D64 — "-" quando o segmento é NB

    /// <summary>ICMS por estado (BD_pricing A30:C60): coluna de HSA-SP e de HSA-ES.</summary>
    public static readonly Dictionary<string, (decimal Sp, decimal Es)> IcmsPorEstado = new()
    {
        ["AC"] = (0.07m, 0.12m), ["AL"] = (0.07m, 0.12m), ["AM"] = (0.07m, 0.12m),
        ["AP"] = (0.07m, 0.12m), ["BA"] = (0.07m, 0.12m), ["CE"] = (0.07m, 0.12m),
        ["DF"] = (0.07m, 0.12m), ["ES"] = (0.07m, 0.17m), ["GO"] = (0.07m, 0.12m),
        ["MA"] = (0.07m, 0.12m), ["MG"] = (0.12m, 0.12m), ["MS"] = (0.07m, 0.12m),
        ["MT"] = (0.07m, 0.12m), ["PA"] = (0.07m, 0.12m), ["PB"] = (0.07m, 0.12m),
        ["PE"] = (0.07m, 0.12m), ["PI"] = (0.07m, 0.12m), ["PR"] = (0.12m, 0.12m),
        ["RJ"] = (0.12m, 0.12m), ["RN"] = (0.07m, 0.12m), ["RO"] = (0.07m, 0.12m),
        ["RR"] = (0.07m, 0.12m), ["RS"] = (0.12m, 0.12m), ["SC"] = (0.12m, 0.12m),
        ["SE"] = (0.07m, 0.12m), ["SP"] = (0.18m, 0.12m), ["TO"] = (0.07m, 0.12m),
    };

    /// <summary>
    /// Venda sem os impostos brasileiros. São três portas, e basta uma:
    /// o país não é o Brasil (Chile, Peru, qualquer outro), o destino é
    /// exportação ou back to back, ou o estado já está marcado como fora
    /// (EXPORT, Chile, Peru) — que é exatamente o que a planilha testa.
    /// </summary>
    public static bool SemImpostos(string destino, string estado, string pais = "Brasil")
    {
        if (pais.Trim().Length > 0 && !ListasDoPricing.TemImpostosBrasileiros(pais)) return true;

        var d = Textos.Simples(destino);
        var e = Textos.Simples(estado);

        return d.StartsWith("exporta")
            || d.Contains("back to back")
            || e is "exp" or "export" or "peru" or "chile";
    }

    /// <summary>
    /// As comissões que entram no preço: as fixas da casa mais a do
    /// representante 1 e a do representante 2 (PRICING L39:T39).
    /// </summary>
    public static decimal Comissoes(string rep1, string rep2)
    {
        var fixas = Ppr + SalesDirector + SalesIndustrial + Dsr * (SalesDirector + SalesIndustrial);
        return fixas
             + ListasDoPricing.ComissaoDoRepresentante(rep1)
             + ListasDoPricing.ComissaoDoRepresentante(rep2);
    }

    /// <summary>O que a proposta manda para o cálculo.</summary>
    public sealed record Entrada(
        decimal CustoTotal,
        decimal RiscoAdicional,
        decimal MargemNegociacao,
        string Destino,
        string Estado,
        string VendaPara,
        string Beneficio,
        string Bu,
        bool ComPortal,
        string Rep1,
        string Rep2,
        bool ComFianca = false,
        string Pais = "Brasil");

    /// <summary>O pricing calculado, na ordem em que a tela mostra.</summary>
    public sealed record Resultado(
        decimal CustoTotal,
        decimal Risco,
        decimal CustoComRiscos,
        decimal Markup,
        decimal VendaPura,
        decimal VendaLiquida,
        decimal Margem,
        decimal Comissoes,
        decimal PercentuaisDeVenda,
        decimal ValorPis,
        decimal ValorCofins,
        decimal ValorIcms,
        decimal ValorIpi,
        decimal PrecoComImpostos,
        List<string> Avisos);

    /// <summary>A soma dos percentuais que saem da venda (D45 a D49).</summary>
    public static decimal PercentuaisDeVenda(Entrada e) =>
        Comissoes(e.Rep1, e.Rep2)
        + e.MargemNegociacao
        + (Textos.Igual(e.Destino, "Nacional") ? PmSachNacional : PmSachExterior)
        + GarantiaDoProjeto
        + (e.ComPortal ? Portal : 0m);

    /// <summary>Custo com riscos (E38). Sem variação por rubrica, E35 é zero.</summary>
    public static decimal CustoComRiscos(Entrada e) =>
        e.CustoTotal + e.RiscoAdicional * e.CustoTotal;

    /// <summary>
    /// O markup que produz a margem pedida. Sai de inverter a fórmula da
    /// planilha: margem = 1 − custo/venda − percentuais, com
    /// venda = markup × custo ÷ (1 − percentuais).
    /// </summary>
    public static decimal MarkupParaMargem(decimal percentuais, decimal margemAlvo)
    {
        var sobra = 1m - percentuais - margemAlvo;
        return sobra <= 0m ? 0m : (1m - percentuais) / sobra;
    }

    /// <summary>Monta o resultado a partir de um markup já escolhido.</summary>
    public static Resultado ComMarkup(Entrada e, decimal markup)
    {
        var avisos = new List<string>();

        var risco = e.RiscoAdicional * e.CustoTotal;
        var custoComRiscos = e.CustoTotal + risco;
        var percentuais = PercentuaisDeVenda(e);

        var vendaPura = markup * custoComRiscos;
        var vendaLiquida = percentuais >= 1m ? 0m : vendaPura / (1m - percentuais);

        var margem = vendaLiquida == 0m
            ? 0m
            : 1m - custoComRiscos / vendaLiquida - percentuais;

        // ---------- impostos por cima da venda líquida ----------
        decimal pis = 0m, cofins = 0m, icms = 0m, ipi = 0m, preco = vendaLiquida;

        if (!SemImpostos(e.Destino, e.Estado, e.Pais))
        {
            var semPisCofins = Textos.Simples(e.Beneficio) is "zona franca de manaus"
                or "beneficio recap / reidi";

            var aliqPis = semPisCofins ? 0m : Pis;
            var aliqCofins = semPisCofins ? 0m : Cofins;

            // o IPI só existe fora do segmento NB, e axiais são NB: a planilha
            // devolve "-" em D64. E aí, fora de "Industrialização", a conta do
            // ICMS multiplica esse "-" e o Excel devolve #VALOR! — a venda
            // líquida continua boa, mas o preço COM impostos não sai de lá
            var aliqIpi = 0m;

            if (!Textos.Igual(e.VendaPara, "Industrialização"))
            {
                avisos.Add("Segmento NB com venda para \"" + e.VendaPara + "\": na planilha o " +
                           "IPI fica \"-\" e a célula do preço com impostos (S75/S76) devolve " +
                           "#VALOR!. A venda líquida (E51) bate; o preço bruto, confira no Excel.");
            }

            var aliqIcms = IcmsDoEstado(e.Estado, e.Bu);
            if (aliqIcms is null)
            {
                avisos.Add($"Estado \"{e.Estado}\" não está na tabela de ICMS — o preço saiu sem ICMS.");
                aliqIcms = 0m;
            }

            if (Textos.Igual(e.VendaPara, "Revenda") ||
                Textos.Simples(e.Beneficio) == "nao-contribuinte icms")
            {
                avisos.Add("Revenda ou cliente não-contribuinte de ICMS: a planilha manda " +
                           "procurar o Financeiro para fechar o preço bruto.");
            }

            var base1 = vendaLiquida / (1m - aliqPis - aliqCofins);
            var fatorIcms = aliqIcms.Value
                + aliqIcms.Value * (Textos.Igual(e.VendaPara, "Industrialização") ? 0m : aliqIpi);

            var base2 = fatorIcms >= 1m ? base1 : base1 / (1m - fatorIcms);
            preco = base2 * (1m + aliqIpi);

            pis = base1 * aliqPis;
            cofins = base1 * aliqCofins;
            ipi = base2 * aliqIpi;
            icms = preco * aliqIcms.Value;
        }

        if (e.ComFianca)
        {
            avisos.Add("Fiança/seguro garantia marcado como \"Sim\": o valor dela (E42) sai da " +
                       "tabela de eventos de pagamento da planilha, que o sistema ainda não " +
                       "preenche — a prévia não a inclui, e o Excel vai somá-la.");
        }

        return new Resultado(e.CustoTotal, risco, custoComRiscos, markup, vendaPura,
            vendaLiquida, margem, Comissoes(e.Rep1, e.Rep2), percentuais,
            pis, cofins, icms, ipi, preco, avisos);
    }

    /// <summary>Calcula a partir da margem pedida — o caminho normal.</summary>
    public static Resultado PelaMargem(Entrada e, decimal margemAlvo) =>
        ComMarkup(e, MarkupParaMargem(PercentuaisDeVenda(e), margemAlvo));

    /// <summary>
    /// Calcula a partir de um preço-meta (a venda líquida que o cliente aceita):
    /// o markup sai do preço, e a margem é consequência.
    /// </summary>
    public static Resultado PeloPreco(Entrada e, decimal precoMeta)
    {
        var custoComRiscos = CustoComRiscos(e);
        if (custoComRiscos <= 0m) return ComMarkup(e, 0m);

        var markup = precoMeta * (1m - PercentuaisDeVenda(e)) / custoComRiscos;
        return ComMarkup(e, markup);
    }

    /// <summary>A alíquota de ICMS do estado, pela BU que emite.</summary>
    public static decimal? IcmsDoEstado(string estado, string bu)
    {
        var uf = Textos.Simples(estado).ToUpperInvariant();
        var achado = IcmsPorEstado.FirstOrDefault(p => Textos.Simples(p.Key) == Textos.Simples(uf));
        if (achado.Key is null) return null;

        // a planilha escolhe a coluna pela empresa emissora (E8)
        // a planilha escolhe a coluna comparando E8 com "HSA-SP"
        return Textos.Igual(bu, "HSA-SP") ? achado.Value.Sp : achado.Value.Es;
    }
}
