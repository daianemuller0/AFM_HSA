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
        ("E14:F33", "Cálculo de custo (valor e risco)"), ("D37", "Risco adicional"), ("E41", "Markup"),
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
            var doCodigo = e.Custos.Where(c => c.Codigo == l.Codigo).ToList();
            ws.Numero($"E{l.Linha}", Math.Round(doCodigo.Sum(c => c.Valor), 2));
            ws.Numero($"F{l.Linha}", doCodigo.Select(c => c.Variacao).FirstOrDefault());
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

        // ---------- moedas: I36 = R$ por USD; I37/I39 = EUR e CLP em USD (a planilha multiplica por I36) ----------
        var (usd, eur, clp) = RealPor(o, taxas);
        if (usd > 0m)
        {
            ws.Numero("I36", Math.Round(usd, 6));
            if (eur > 0m) ws.Numero("I37", Math.Round(eur / usd, 8));
            if (clp > 0m) ws.Numero("I39", Math.Round(clp / usd, 10));
        }

        PrecoAoCliente(pacote, ws, o, r);

        return pacote.Concluir();
    }

    /// <summary>
    /// O preço ao cliente na moeda e na taxa DA PROPOSTA — o mesmo da tela.
    ///
    /// O quadro PREÇO DE VENDA da planilha fica na moeda do custo (R$ no
    /// Brasil; a estrangeira nas BUs de fora). Em exportação / Chile / Peru a
    /// linha 14 converte: Q14 = Q13 ÷ BD_pricing!C2, com o rótulo
    /// "Valores em " &amp; BD_pricing!B2 (os botões de moeda da planilha, o
    /// primeiro marcado). O modelo traz ali o USD e a taxa I36 (R$ por USD, da
    /// aba Conversão): com custo em EUR o preço saía errado. Aqui a linha 1 dos
    /// botões passa a ser a moeda do preço com a taxa efetiva da proposta
    /// (moeda do custo por 1 da moeda do preço, já com a segurança).
    ///
    /// E a linha 21 do quadro (vazia no modelo) mostra sempre o preço ao
    /// cliente na moeda dele, no nível de impostos da proposta — também na
    /// venda nacional, que o quadro do modelo não converte.
    /// </summary>
    private static void PrecoAoCliente(PacoteXlsm pacote, PacoteXlsm.Aba ws, CommercialOffer o, CalculoPricing.Resultado r)
    {
        var custo = Cambio.MoedaDoCusto(o);
        var venda = Cambio.MoedaDoPreco(o);
        var converte = Cambio.Converte(o) && !Cambio.FaltaTaxa(o);
        var efetivo = converte ? Cambio.Efetivo(o) : 1m;
        var taxa = Math.Round(efetivo, 10).ToString(CultureInfo.InvariantCulture);

        // a linha 1 dos botões de moeda: a moeda do preço e a taxa da proposta
        var bd = pacote.AbaDe("BD_pricing");
        bd.Numero("A1", 1);
        bd.Texto("B2", (converte ? venda : custo).Codigo());
        bd.Numero("C2", efetivo);

        // o valor da proposta na moeda do custo, no nível de impostos que vai ao cliente
        var nivel = PricingDaProposta.NivelEfetivo(r, PricingDaProposta.NivelPedido(o));
        var valor = nivel.Codigo switch
        {
            "liquido" => r.ExportacaoComBackToBack ? "$S$82" : r.BackToBack100 ? "$S$83" : "$E$52",
            "pis_cofins" => "$S$75",
            "pis_cofins_icms" => "$S$76",
            _ => "$S$77",
        };
        if (nivel.Codigo != "liquido" && r.ServicoOuReforma) valor = $"({valor}+N($S$81))";

        var br = new CultureInfo("pt-BR");
        var rotulo = converte
            ? $"PREÇO AO CLIENTE EM {venda.Rotulo()} ({nivel.Rotulo.ToLowerInvariant()}) · 1 {venda.Rotulo()} = {efetivo.ToString("#,##0.######", br)} {custo.Rotulo()}" +
              (Cambio.Seguranca(o) > 0m ? $" (taxa {Cambio.Normalizada(o).ToString("#,##0.######", br)} − segurança {Cambio.Seguranca(o).ToString("0.##", br)}%)" : "") +
              $" · o quadro acima está em {custo.Rotulo()}"
            : Cambio.Converte(o)
                ? $"PREÇO AO CLIENTE: falta a taxa de câmbio {custo.Rotulo()} → {venda.Rotulo()} na proposta (valor abaixo em {custo.Rotulo()})"
                : $"PREÇO AO CLIENTE EM {custo.Rotulo()} ({nivel.Rotulo.ToLowerInvariant()})";
        ws.Texto("K21", rotulo);
        ws.Formula("Q21", $"ROUND({valor}/{taxa},2)");
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
