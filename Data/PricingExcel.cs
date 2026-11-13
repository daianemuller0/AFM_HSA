using System.Globalization;
using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>
/// Preenche a planilha de pricing da equipe (R_HSAXYZ0000-0.xlsm, em
/// <c>wwwroot/modelos/pricing.xlsm</c>) com o que a proposta já sabe — o mesmo
/// caminho do axiaisvent (PricingExcel + PacoteXlsm).
///
/// O arquivo gerado é a <b>planilha deles</b>, com as abas, as fórmulas, as
/// listas e as macros intactas: só as células de entrada da aba PRICING são
/// escritas. Ao abrir, o Excel recalcula e chega nos mesmos números da tela
/// (a conta da tela é a da planilha, conferida célula a célula).
/// </summary>
public static class PricingExcel
{
    public const string CaminhoDoModelo = "wwwroot/modelos/pricing.xlsm";
    public const string Aba = "PRICING";
    public const string TipoMime = "application/vnd.ms-excel.sheet.macroEnabled.12";

    /// <summary>P_&lt;código da proposta&gt;-C&lt;revisão&gt;.xlsm</summary>
    public static string NomeDoArquivo(CommercialOffer o) => Limpo($"P_{PropostaWord.Referencia(o)}.xlsm");

    internal static string Limpo(string nome)
    {
        foreach (var c in Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }))
            nome = nome.Replace(c, '_');
        return nome;
    }

    /// <summary>
    /// O mapa da aba PRICING — o contrato entre o sistema e a planilha. Se ela
    /// mudar de linha, é aqui que muda.
    /// </summary>
    public static readonly (string Celula, string Campo)[] Mapa =
    {
        ("E3", "Nome do Cliente"), ("E4", "PV"), ("E5", "Ano"), ("E6", "Número da Proposta"), ("E7", "Revisão"),
        ("E8", "BU"), ("E9", "Elaborado por"), ("E10", "NCM c/redução base ICMS"), ("S1", "Data do Pricing"),
        ("J3", "Segmento (margem)"), ("J4", "Venda para"), ("J5", "Destino"), ("J6", "Estado"), ("J7", "Reforma"),
        ("J8", "Prazo de Entrega (dias)"), ("J9", "Cliente com"), ("J10", "Contém produto IC?"),
        ("P3", "Fiança/Seguro Garantia"), ("P4", "Portal"), ("P5", "Rep 1"), ("P6", "Rep 2"),
        ("P7", "Categories"), ("P8", "Product"), ("P9", "Market Segments"),
        ("E14:F33", "Cálculo de custo (valor e risco)"), ("D37", "Risco/Contingência"), ("E41", "Markup"),
        ("D47", "Margem de negociação"), ("D64", "IPI (pela NCM dos itens)"),
        ("R46, N49:O58", "Seguro garantia / carta fiança"), ("R61, N64:R67", "Outras garantias"),
        ("I36:I39", "Moedas (R$ por USD e cruzadas)"),
    };

    /// <summary>Gera o arquivo, em memória, pronto para download.</summary>
    public static byte[] Gerar(CommercialOffer o, CalculoPricing.Entrada e, CalculoPricing.Resultado r,
        TaxasDeConversao taxas, string caminhoDoModelo)
    {
        using var pacote = new PacoteXlsm(caminhoDoModelo);
        var ws = pacote.AbaDe(Aba);

        // ---------- cabeçalho (vazio fica o "preencher" do modelo) ----------
        ws.Texto("E3", o.ClientName);
        ws.Texto("E4", o.Pv);
        if (int.TryParse(o.Ano, out var ano)) ws.Numero("E5", ano); else ws.Texto("E5", o.Ano);
        ws.Texto("E6", o.OfferCode);
        if (int.TryParse(o.Revisao, out var rev)) ws.Numero("E7", rev); else ws.Texto("E7", o.Revisao);
        ws.Texto("E8", e.Bu);
        ws.Texto("E9", o.PreparedBy);
        ws.Texto("E10", o.NcmReducaoIcms);
        // a data do pricing é a da proposta (no lugar do HOJE() do modelo, que mudaria a cada abertura)
        if (DateTime.TryParseExact(o.OfferDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
            ws.Numero("S1", data.ToOADate());

        ws.Texto("J3", e.Segmento);
        ws.Texto("J4", e.VendaPara);
        ws.Texto("J5", e.Destino);
        ws.Texto("J6", e.Estado);
        ws.Texto("J7", o.Reforma);
        if (e.PrazoEntregaDias > 0) ws.Numero("J8", e.PrazoEntregaDias);
        ws.Texto("J9", e.ClienteCom);
        ws.Texto("J10", o.ContemProdutoIc);

        ws.Texto("P3", o.FiancaSeguro);
        ws.Texto("P4", e.Portal);
        ws.Texto("P5", PropostaWord.Ou(e.Rep1, "-"));
        ws.Texto("P6", PropostaWord.Ou(e.Rep2, "-"));
        ws.Texto("P7", o.Categories);
        ws.Texto("P8", e.Product);
        ws.Texto("P9", o.MarketSegments);

        // ---------- cálculo de custo (E14:F33): os itens entram em PROD. COMPRADOS / IMPORTADOS ----------
        foreach (var l in CalculoPricing.LinhasDeCusto)
        {
            // as linhas 29 a 31 ("-" no modelo) são embalagens, exportação e outros
            if (l.Linha is >= 29 and <= 31) ws.Texto($"B{l.Linha}", l.Nome);
            var doCodigo = e.Custos.Where(c => c.Codigo == l.Codigo).ToList();
            ws.Numero($"E{l.Linha}", Math.Round(doCodigo.Sum(c => c.Valor), 2));
            // a linha sem custo fica com a Protected Margin da proposta (o modelo traz 2% em todas)
            ws.Numero($"F{l.Linha}", doCodigo.Count > 0 ? doCodigo[0].Variacao : o.ProtectedMargin ?? 0m);
        }

        // ---------- risco, negociação e markup (a margem pedida chega pelo markup) ----------
        ws.Numero("D37", e.RiscoAdicional);
        ws.Numero("D47", e.MargemNegociacao);
        ws.Numero("E41", r.Markup);

        // ---------- IPI pela NCM dos itens (compressor 8414.90.39 = 0%) ----------
        if (e.AliquotaIpi is decimal ipi && ipi != CalculoPricing.Ipi)
        {
            var texto = (ipi * 100m).ToString("0.####", CultureInfo.InvariantCulture) + "%";
            var formula = ws.FormulaDe("D64");
            if (formula is not null && formula.Contains("3.25%", StringComparison.Ordinal))
                ws.Formula("D64", formula.Replace("3.25%", texto, StringComparison.Ordinal));
            else ws.Numero("D64", ipi);
        }

        // ---------- seguro garantia / carta fiança e outras garantias ----------
        ws.Texto("R46", e.TipoGarantia);
        for (var i = 0; i < Math.Min(10, e.Eventos.Count); i++)
        {
            ws.Numero($"N{49 + i}", e.Eventos[i].Percentual);
            ws.Numero($"O{49 + i}", e.Eventos[i].PrazoDias);
        }
        ws.Texto("R61", e.TipoOutrasGarantias);
        for (var i = 0; i < Math.Min(4, e.OutrasGarantias.Count); i++)
        {
            ws.Numero($"N{64 + i}", e.OutrasGarantias[i].Percentual);
            ws.Numero($"R{64 + i}", e.OutrasGarantias[i].DiasCobertura);
        }

        // ---------- moedas ----------
        if (Cambio.MoedaDoCusto(o) == Moeda.Brl)
        {
            // I36 = R$ por USD; I37/I39 = EUR e CLP em USD (a planilha multiplica por I36)
            var (usd, eur, clp) = RealPor(o, taxas);
            if (usd > 0m)
            {
                ws.Numero("I36", Math.Round(usd, 6));
                if (eur > 0m) ws.Numero("I37", Math.Round(eur / usd, 8));
                if (clp > 0m) ws.Numero("I39", Math.Round(clp / usd, 10));
            }
        }
        else if (CustoPorMoedaDaLinha36(o) is decimal i36)
        {
            // BU de fora: I36 = moeda do custo por 1 da moeda da linha 36 (PEN na HPU, senão USD) —
            // a aba PRR divide por ela; as cruzadas em R$ (I37/I39) não valem aqui
            ws.Numero("I36", Math.Round(i36, 10));
        }

        // ---------- os rótulos de moeda do custo: o modelo diz "(CLP)" sempre que a BU é HCHL ----------
        var moedaDoCusto = $"({Cambio.MoedaDoCusto(o).Rotulo()})";
        foreach (var celula in new[] { "E13", "G13", "P48", "T48", "P63", "T63" }) ws.Texto(celula, moedaDoCusto);

        // ---------- o botão "Calcular Preço" (RECALCULAR) tem de chegar no MESMO preço ----------
        // a partir da margem: re-resolve o markup até D55 = P26 (o modelo traz 52%);
        // a partir do preço: até a venda líquida = P27
        if (o.PricingModo == "preco")
        {
            ws.Texto("M26", "a partir do Preço de Venda");
            ws.Numero("P27", Math.Round(r.VendaLiquida, 2));
        }
        else ws.Texto("M26", "a partir da Margem");
        ws.Numero("P26", Math.Round(r.Margem, 6));

        // ---------- Back to Back na moeda do custo (como o sistema calcula): P22 "BRL" = sem conversão ----------
        ws.Texto("P22", "BRL");

        // ---------- portal: busca EXATA (o modelo usa a aproximada numa lista fora de ordem) ----------
        if (ws.FormulaDe("D50") is { } portal && portal.Contains("VLOOKUP(P4,listas!B3:C13,2)", StringComparison.Ordinal))
            ws.Formula("D50", portal.Replace("VLOOKUP(P4,listas!B3:C13,2)", "VLOOKUP(P4,listas!B3:C13,2,FALSE)", StringComparison.Ordinal));

        PrecoAoCliente(pacote, ws, o, r, taxas);

        return pacote.Concluir();
    }

    /// <summary>
    /// O preço ao cliente na moeda e na taxa DA PROPOSTA — o mesmo da tela.
    ///
    /// O quadro PREÇO DE VENDA da planilha fica na moeda do custo (R$ no
    /// Brasil; a estrangeira nas BUs de fora). Em exportação / Chile / Peru a
    /// linha 14 converte: Q14 = Q13 ÷ a taxa do botão de moeda marcado
    /// (BD_pricing!A1 escolhe a linha; BD_pricing!C2:C5 trazem a taxa).
    ///
    /// Como no modelo, a taxa vem do <b>quadro de conversão</b> da aba PRICING
    /// (I36:I39, e as linhas VENDA I41:I44 = moeda do custo por 1 de cada
    /// moeda): quem mexe no quadro muda o preço. O sistema só preenche o
    /// quadro com a taxa da proposta e marca o botão da moeda do preço. O
    /// modelo ligava os botões EUR/GBP/CLP direto às cruzadas em USD (I37:I39),
    /// o que dava preço errado: aqui eles ficam ligados às linhas VENDA.
    ///
    /// A linha 21 do quadro (vazia no modelo) mostra sempre o preço ao
    /// cliente na moeda dele, no nível de impostos da proposta — também na
    /// venda nacional, que o quadro do modelo não converte.
    /// </summary>
    private static void PrecoAoCliente(PacoteXlsm pacote, PacoteXlsm.Aba ws, CommercialOffer o, CalculoPricing.Resultado r, TaxasDeConversao taxas)
    {
        var custo = Cambio.MoedaDoCusto(o);
        var venda = Cambio.MoedaDoPreco(o);
        var converte = Cambio.Converte(o) && !Cambio.FaltaTaxa(o);
        var efetivo = converte ? Cambio.Efetivo(o) : 1m;
        var bd = pacote.AbaDe("BD_pricing");

        // a linha do quadro de conversão da moeda do preço: 1 = USD (PEN na HPU), 2 = EUR, 3 = GBP, 4 = CLP
        var linha36 = string.Equals((o.Bu ?? "").Trim(), "HPU", StringComparison.OrdinalIgnoreCase) ? Moeda.Pen : Moeda.Usd;
        int? botao = !converte ? null : venda == linha36 ? 1 : venda == Moeda.Eur ? 2 : venda == Moeda.Clp ? 4 : null;
        if (botao is int n)
        {
            bd.Numero("A1", n);
            for (var i = 0; i < 4; i++) bd.Formula($"C{2 + i}", $"PRICING!$I${41 + i}");
            if (n == 1) ws.Numero("I36", Math.Round(efetivo, 10));
            else
            {
                // a cruzada em USD (PEN) da moeda do preço; sem saber a moeda do custo por USD, a linha VENDA recebe a taxa direto
                var custoPorLinha36 = custo == Moeda.Brl ? RealPor(o, taxas).Usd : CustoPorMoedaDaLinha36(o) ?? 0m;
                if (custoPorLinha36 > 0m)
                {
                    ws.Numero("I36", Math.Round(custoPorLinha36, 10));
                    ws.Numero($"I{35 + n}", Math.Round(efetivo / custoPorLinha36, 10));
                }
                else ws.Numero($"I{40 + n}", Math.Round(efetivo, 10));
            }
        }
        else
        {
            // sem conversão (ou numa moeda que o quadro não tem): divide por 1, ou pela taxa da proposta
            bd.Numero("A1", 1);
            bd.Texto("B2", (converte ? venda : custo).Codigo());
            bd.Numero("C2", efetivo);
            foreach (var celula in new[] { "C3", "C4", "C5" }) bd.Formula(celula, "NA()");
        }
        const string taxaDoBotao = "VLOOKUP(BD_pricing!$A$1,BD_pricing!$A$2:$C$5,3,FALSE)";

        // o valor da proposta na moeda do custo, no nível de impostos que vai ao cliente
        var nivel = PricingDaProposta.NivelEfetivo(r, PricingDaProposta.NivelPedido(o));
        var valor = nivel.Codigo switch
        {
            // B2B 100% importado: S83 = Q13 ÷ (1 − PIS − COFINS), mas o Q13 do modelo dá resultados
            // diferentes no Excel e no LibreOffice nesse destino; a conta direta é a do sistema
            "liquido" => r.ExportacaoComBackToBack ? "$S$82"
                : r.BackToBack100 ? $"({(r.ServicoOuReforma ? "$S$74" : "$E$52")}/(1-SUM($D$61:$D$62)))"
                : "$E$52",
            "pis_cofins" => "$S$75",
            "pis_cofins_icms" => "$S$76",
            _ => "$S$77",
        };
        if (nivel.Codigo != "liquido" && r.ServicoOuReforma) valor = $"({valor}+N($S$81))";

        var br = new CultureInfo("pt-BR");
        var rotulo = converte
            ? $"PREÇO AO CLIENTE EM {venda.Rotulo()} ({nivel.Rotulo.ToLowerInvariant()}) · taxa da proposta: 1 {venda.Rotulo()} = {efetivo.ToString("#,##0.######", br)} {custo.Rotulo()}" +
              (Cambio.Seguranca(o) > 0m ? $" (taxa {Cambio.Normalizada(o).ToString("#,##0.######", br)} − segurança {Cambio.Seguranca(o).ToString("0.##", br)}%)" : "") +
              (botao is not null ? " · segue o quadro de conversão abaixo" : "") +
              $" · o quadro acima está em {custo.Rotulo()}"
            : Cambio.Converte(o)
                ? $"PREÇO AO CLIENTE: falta a taxa de câmbio {custo.Rotulo()} → {venda.Rotulo()} na proposta (valor abaixo em {custo.Rotulo()})"
                : $"PREÇO AO CLIENTE EM {custo.Rotulo()} ({nivel.Rotulo.ToLowerInvariant()})";
        ws.Texto("K21", rotulo);
        ws.Formula("Q21", $"ROUND({valor}/{taxaDoBotao},2)");

        // as moedas nos números do quadro PREÇO DE VENDA. O modelo traz a linha 15 fixa em EUR e a 16
        // em GBP (resto do quadro antigo, uma moeda por linha) e só põe R$ quando não há ICMS (M75):
        // numa venda nacional com ICMS saíam "EUR" e "GBP" em valores que são em R$. Aqui cada linha
        // leva a moeda do valor dela: 13, 15 e 16 são sempre na moeda do custo; a 14 converte (Q13 ÷
        // taxa) só em exportação, Chile e Peru — senão é a venda com PIS e COFINS, na moeda do custo.
        var naVenda = converte ? venda : custo;
        var linha14Converte = Textos.Igual(o.Destino, "Exportação") || Textos.Igual(o.Estado, "Peru") || Textos.Igual(o.Estado, "Chile");
        if (converte) QuadroNaMoedaDaVenda(pacote, ws, taxaDoBotao);
        var noQuadro = converte ? naVenda : custo;
        ws.FormatoDeMoeda(FormatoDe(noQuadro), "Q13", "Q15", "R15", "S15", "Q16", "R16", "S16");
        ws.FormatoDeMoeda(FormatoDe(converte || !linha14Converte ? noQuadro : custo), "Q14");
        ws.FormatoDeMoeda(FormatoDe(naVenda), "Q21");
        if (converte) pacote.AbaDe("EMAILS AUTOMÁTICOS").FormatoDeMoeda(FormatoDe(naVenda), "H43", "H44", "H45", "H46");
        // as taxas com as casas que precisam (0,941354 não vira 0,94)
        ws.FormatoDeMoeda(FormatoDe(custo, taxa: true), "I36", "I41", "I42", "I43", "I44");
        ws.FormatoDeMoeda(FormatoDe(linha36, taxa: true), "I37", "I38", "I39");
    }

    /// <summary>
    /// Preço em moeda diferente da do custo (HSA vendendo em USD ou EUR; HCHL
    /// com custo em EUR vendendo em USD): o quadro PREÇO DE VENDA inteiro sai
    /// na moeda da venda — sem misturar R$ e USD. As linhas 13 a 16 dividem
    /// pela taxa do quadro de conversão (a mesma do botão de moeda, I36…); o
    /// que usa o quadro para contas em R$ (o fator da coluna T e o Back to
    /// Back de S83) multiplica de volta. A macro trabalha em E52 / P27, fora
    /// do quadro. A aba EMAILS AUTOMÁTICOS copia o quadro: sai na moeda da venda.
    /// </summary>
    private static void QuadroNaMoedaDaVenda(PacoteXlsm pacote, PacoteXlsm.Aba ws, string taxa)
    {
        const string ConverteNaPlanilha = "OR(Destino=\"Exportação\",Estado=\"Peru\",Estado=\"Chile\")";
        if (ws.FormulaDe("Q13") is { Length: > 0 } q13) ws.Formula("Q13", $"({q13})/{taxa}");
        ws.Formula("Q14", $"IF({ConverteNaPlanilha},$Q$13,$S$75/{taxa})");
        ws.Formula("Q15", $"IF({ConverteNaPlanilha},\"-\",$S$76/{taxa})");
        ws.Formula("Q16", $"IF({ConverteNaPlanilha},\"-\",$S$77/{taxa})");
        ws.Formula("T13", $"Q13*{taxa}/($E$34-IF($J$5=\"Exportação com Back to Back\",$E$25,0))");
        for (var l = 14; l <= 16; l++) ws.Formula($"T{l}", $"Q{l}*{taxa}/$E$34");
        ws.Formula("S83", $"Q13*{taxa}/(1-SUM(D61:D62))/N83");
    }

    /// <summary>O formato de número do Excel com a moeda: "R$ 1.234,56", "EUR 1.234,56", "CLP 1.234".</summary>
    private static string FormatoDe(Moeda m, bool taxa = false)
    {
        var numero = taxa ? "#,##0.00####" : m == Moeda.Clp ? "#,##0" : "#,##0.00";
        return m == Moeda.Brl ? $"\"R$\"\\ {numero}" : $"[${m.Codigo()}]\\ {numero}";
    }

    /// <summary>
    /// Nas BUs de fora: a moeda do custo por 1 da moeda da linha 36 da planilha
    /// (PEN na HPU, senão USD) — quando dá para saber pela proposta. null = não dá.
    /// </summary>
    private static decimal? CustoPorMoedaDaLinha36(CommercialOffer o)
    {
        var linha36 = string.Equals((o.Bu ?? "").Trim(), "HPU", StringComparison.OrdinalIgnoreCase) ? Moeda.Pen : Moeda.Usd;
        if (Cambio.MoedaDoCusto(o) == linha36) return 1m;
        if (Cambio.MoedaDoPreco(o) == linha36 && Cambio.Converte(o) && !Cambio.FaltaTaxa(o)) return Cambio.Efetivo(o);
        return null;
    }

    /// <summary>
    /// Reais por USD, EUR e CLP: os da aba Conversão, com a taxa desta proposta
    /// no lugar da moeda em que o preço sai.
    /// </summary>
    private static (decimal Usd, decimal Eur, decimal Clp) RealPor(CommercialOffer o, TaxasDeConversao t)
    {
        var usd = t.UsdEmReais;
        var eur = t.EurEmReais;
        var clp = t.ClpPorReal > 0m ? 1m / t.ClpPorReal : 0m;
        // a taxa da proposta é em R$ só quando o custo é em R$ (nas BUs de fora ela é entre duas moedas
        // estrangeiras); a efetiva (com a segurança), a mesma que a tela usa
        var daProposta = Cambio.MoedaDoCusto(o) == Moeda.Brl && Cambio.Converte(o) && !Cambio.FaltaTaxa(o) ? Cambio.Efetivo(o) : 0m;
        if (daProposta > 0m)
        {
            switch (Cambio.MoedaDoPreco(o))
            {
                case Moeda.Usd: usd = daProposta; break;
                case Moeda.Eur: eur = daProposta; break;
                case Moeda.Clp: clp = daProposta; break;
            }
        }
        return (usd, eur, clp);
    }
}
