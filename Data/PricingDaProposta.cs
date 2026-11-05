using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>
/// Liga a Gerar Proposta ao pricing do AFM (<see cref="CalculoPricing"/>).
/// O cabeçalho da proposta tem os mesmos campos e as mesmas listas do
/// cabeçalho da planilha, então aqui só se converte (Sim/Não, prazo em dias,
/// nome do representante) e se aponta o que falta preencher.
///
/// O custo total é a soma dos itens: custo de importação quando o item é
/// importado, senão a referência de custo (Valor BR) da lista de Nacionalização.
/// </summary>
public static class PricingDaProposta
{
    /// <summary>Margem pedida padrão do segmento (BD_pricing: AFM 52%, NB 32%, Intercompany 28%, Service 50%).</summary>
    public static decimal MargemPadrao(string segmento) => ListasDoPricing.MargemPadraoDo(segmento);

    /// <summary>"90", "90 dias", "90d" → 90. Vazio ou sem número → 0.</summary>
    public static int PrazoEmDias(string? prazo)
    {
        var digitos = new string((prazo ?? "").Trim().TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digitos, out var d) ? d : 0;
    }

    private static bool Sim(string? valor) => Textos.Igual(valor, "Sim");

    /// <summary>
    /// O nome exato da tabela de representantes (a planilha compara letra por
    /// letra). Aceita o nome sem acento ou com outra caixa; quem não está na
    /// tabela segue como está e fica sem comissão.
    /// </summary>
    public static string Representante(string? nome)
    {
        var n = (nome ?? "").Trim();
        return ListasDoPricing.Representantes.FirstOrDefault(r => r.Nome == n)?.Nome
            ?? ListasDoPricing.Representantes.FirstOrDefault(r => Textos.Igual(r.Nome, n))?.Nome
            ?? n;
    }

    public static decimal ComissaoDe(string? nome, string? segmento) =>
        ListasDoPricing.ComissaoDoRepresentante(Representante(nome), (segmento ?? "").Trim());

    /// <summary>
    /// A tabela CÁLCULO DE CUSTO da proposta: o que foi digitado em cada linha
    /// e, por cima, o custo dos itens — nacionalizados em PROD. COMPRADOS e
    /// importados em PROD. IMPORTADOS.
    /// </summary>
    public static List<CalculoPricing.Custo> CustosDa(CommercialOffer o, decimal itensComprados, decimal itensImportados)
    {
        var lista = new List<CalculoPricing.Custo>();
        foreach (var l in CalculoPricing.LinhasDeCusto)
        {
            var manual = o.CustosPricing.FirstOrDefault(c => c.Codigo == l.Codigo);
            var valor = (manual?.Valor ?? 0m)
                      + (l.Codigo == "COMPRADOS" ? itensComprados : l.Codigo == "IMPORTADOS" ? itensImportados : 0m);
            var variacao = manual?.Variacao ?? 0m;
            if (valor != 0m || variacao != 0m) lista.Add(new CalculoPricing.Custo(l.Codigo, valor, variacao));
        }
        return lista;
    }

    /// <summary>Os 10 eventos, completando com os padrões se a lista gravada vier curta.</summary>
    private static List<CalculoPricing.EventoDePagamento> Eventos(CommercialOffer o)
    {
        var padrao = CalculoPricing.EventosPadrao();
        return padrao.Select((p, i) => i < o.EventosFianca.Count ? o.EventosFianca[i] : p with { Percentual = 0m }).ToList();
    }

    public static CalculoPricing.Entrada EntradaDe(CommercialOffer o, decimal itensComprados, decimal itensImportados,
        decimal? aliquotaIpi = null) => new(
        Custos: CustosDa(o, itensComprados, itensImportados),
        Segmento: (o.SegmentoMargem ?? "").Trim(),
        Product: o.Product ?? "",
        RiscoAdicional: o.RiscoAdicional,
        MargemNegociacao: o.MargemNegociacao,
        Bu: (o.Bu ?? "").Trim(),
        NcmReducaoIcms: Sim(o.NcmReducaoIcms),
        VendaPara: o.VendaPara ?? "",
        Destino: o.Destino ?? "",
        Estado: o.Estado ?? "",
        Reforma: Sim(o.Reforma),
        ClienteCom: o.ClienteCom ?? "",
        PrazoEntregaDias: PrazoEmDias(o.PrazoEntrega),
        ComFianca: Sim(o.FiancaSeguro),
        TipoGarantia: o.TipoGarantia ?? "",
        Eventos: Eventos(o),
        TipoOutrasGarantias: o.TipoOutrasGarantias ?? "",
        OutrasGarantias: o.OutrasGarantias,
        Portal: o.Portal ?? "",
        Rep1: Representante(o.Rep1),
        Rep2: Representante(o.Rep2),
        AliquotaIpi: aliquotaIpi);

    /// <summary>O pricing da proposta, pelo modo escolhido (margem pedida ou preço de venda).</summary>
    public static CalculoPricing.Resultado Da(CommercialOffer o, decimal itensComprados, decimal itensImportados,
        decimal? aliquotaIpi = null)
    {
        var e = EntradaDe(o, itensComprados, itensImportados, aliquotaIpi);
        var r = o.PricingModo == "preco"
            ? CalculoPricing.PeloPreco(e, o.PrecoMeta)
            : CalculoPricing.PelaMargem(e, o.MargemAlvo);
        if (e.Rep1 == "IPT Colômbia >EUR 1,25MM" || e.Rep2 == "IPT Colômbia >EUR 1,25MM")
            r.Avisos.Add("IPT Colômbia >EUR 1,25MM: a comissão tem teto de EUR 250K (K24 da planilha) — confira se passou.");
        return r;
    }

    /// <summary>
    /// Campos do cabeçalho que o pricing usa e ainda estão vazios — na
    /// planilha eles aparecem como "preencher". A conta sai mesmo assim, com
    /// o que a planilha faz nesse caso (portal 0,45%, coluna de ICMS da HSA-ES…).
    /// </summary>
    public static List<string> Pendencias(CommercialOffer o)
    {
        var p = new List<string>();
        void Se(bool vazio, string campo) { if (vazio) p.Add(campo); }
        bool V(string? s) => string.IsNullOrWhiteSpace(s);
        Se(V(o.SegmentoMargem), "Segmento (margem)");
        Se(V(o.Bu), "BU");
        Se(V(o.NcmReducaoIcms), "NCM c/redução base ICMS");
        Se(V(o.VendaPara), "Venda para");
        Se(V(o.Destino), "Destino");
        Se(V(o.Estado), "Estado");
        Se(V(o.ClienteCom), "Cliente com");
        Se(V(o.FiancaSeguro), "Fiança/Seguro Garantia");
        Se(V(o.Portal), "Portal");
        if (Sim(o.FiancaSeguro))
        {
            Se(V(o.TipoGarantia), "tipo de garantia");
            Se(PrazoEmDias(o.PrazoEntrega) <= 0, "Prazo de Entrega (dias)");
        }
        Se(Cambio.FaltaTaxa(o), $"taxa de câmbio do {Cambio.MoedaDoPreco(o).Rotulo()}");
        return p;
    }

    /// <summary>
    /// Segmento do cliente na base (texto livre: "Mineração", "Cimento"…) → o
    /// Market Segment da planilha, quando dá para reconhecer. Vazio quando não dá.
    /// A ordem importa: "petroquímica" antes de "química".
    /// </summary>
    public static string MarketSegmentDo(string? segmentoDaBase)
    {
        var t = Textos.Simples(segmentoDaBase);
        if (t.Length == 0) return "";
        (string[] Chaves, string Mercado)[] mapa =
        {
            (new[] { "petroquim", "petrochem" }, "Industrial - Petrochemical"),
            (new[] { "quimic", "chemical" }, "Industrial - Chemicals"),
            (new[] { "minera", "mining" }, "Mining"),
            (new[] { "carvao", "coal" }, "Coal"),
            (new[] { "cimento", "cement" }, "Industrial - Cement"),
            (new[] { "siderur", "metalur", "aco", "metal", "steel" }, "Industrial - Metals Processing"),
            (new[] { "aliment", "bebida", "food" }, "Industrial - Food & Beverages"),
            (new[] { "etanol", "ethanol", "sucro", "acucar", "usina" }, "Industrial - Other Industries - Ethanol"),
            (new[] { "fertiliz" }, "Industrial - Other Industries - Fertilizer"),
            (new[] { "vidro", "glass" }, "Industrial - Other Industries - Glass"),
            (new[] { "papel", "celulose", "pulp" }, "Industrial - Other Industries - Pulp and Paper"),
            (new[] { "refinar", "refinery" }, "Energy & Renewable Power - Refinery"),
            (new[] { "hidrogen", "hydrogen" }, "Energy & Renewable Power - Other Hydrogen"),
            (new[] { "tunel", "tunnel", "metro" }, "Infrastructure & Mobility - Tunnel"),
            (new[] { "saneamento", "agua", "water" }, "Infrastructure & Mobility - Water (Municipal)"),
            (new[] { "naval", "marin", "porto" }, "Infrastructure & Mobility - Marine"),
            (new[] { "ferrovi", "transporte" }, "Infrastructure & Mobility - Transportation"),
            (new[] { "energia", "termel", "power", "eletric" }, "Energy & Renewable Power - Other"),
        };
        foreach (var (chaves, mercado) in mapa)
            if (chaves.Any(c => t.Contains(c))) return mercado;
        return "";
    }

    // ---------------------------------------------------------------------
    // custo do item, IPI pela NCM e os níveis de imposto do preço
    // ---------------------------------------------------------------------

    /// <summary>
    /// O custo unitário (R$) de um item gravado: importação, digitado ou a
    /// referência da Nacionalização. null = sem custo.
    /// </summary>
    public static decimal? CustoGravado(CommercialOfferItem it) =>
        it.CustoUnitario is > 0 ? it.CustoUnitario
        : it.Importado ? (it.CustoImportacao is > 0 ? it.CustoImportacao : null)
        : it.CustoManual is > 0 ? it.CustoManual
        : it.Nacionalizado && it.CustoReferencia is > 0 ? it.CustoReferencia : null;

    /// <summary>
    /// O IPI do pricing pela NCM dos itens, ponderado pelo custo: com todas as
    /// peças em 8414.90.39 (compressor) dá 0%. Sem NCM em nenhum item, null
    /// (a alíquota da planilha).
    /// </summary>
    public static decimal? IpiDosItens(IEnumerable<(CommercialOfferItem It, decimal Custo)> itens)
    {
        decimal peso = 0m, soma = 0m;
        foreach (var (it, custo) in itens)
        {
            if (ListasDaProposta.NcmDo(it.Ncm) is not { } ncm || custo <= 0m) continue;
            var c = custo * Math.Max(it.Quantity, 1);
            peso += c;
            soma += c * ncm.Ipi;
        }
        return peso > 0m ? Math.Round(soma / peso, 6) : null;
    }

    /// <param name="Rotulo">como a tela mostra</param>
    /// <param name="NoWord">a opção da lista do total da tabela de preço do modelo</param>
    public sealed record NivelDeImposto(string Codigo, string Rotulo, string NoWord, bool PisCofins, bool Icms, bool Ipi);

    /// <summary>Com que impostos o preço vai ao cliente — as quatro opções do total da tabela de preço do modelo.</summary>
    public static readonly NivelDeImposto[] Niveis =
    {
        new("liquido", "Sem impostos (valor líquido)", "VALOR LÍQUIDO", false, false, false),
        new("pis_cofins", "Com PIS e COFINS", "VALOR COM PIS E COFINS", true, false, false),
        new("pis_cofins_icms", "Com PIS, COFINS e ICMS", "VALOR COM PIS, COFINS E ICMS", true, true, false),
        new("todos", "Com PIS, COFINS, ICMS e IPI", "VALOR COM PIS, COFINS, ICMS E IPI", true, true, true),
    };

    public static NivelDeImposto NivelDe(string? codigo) => Niveis.FirstOrDefault(n => n.Codigo == codigo) ?? Niveis[0];

    /// <summary>
    /// O valor da proposta (R$) num nível de imposto: o quadro PREÇO DE VENDA
    /// (K13 a K16), com a MO de Reforma/Service (S81) somada quando há imposto.
    /// null = o nível não existe nesta proposta (exportação, Back to Back, sem
    /// Estado, procurar Financeiro).
    /// </summary>
    public static decimal? ValorNoNivel(CalculoPricing.Resultado r, string? codigo)
    {
        var nivel = NivelDe(codigo);
        if (nivel.Codigo == "liquido") return r.ValorDaProposta;
        if (r.SemImpostos || r.ExportacaoComBackToBack || r.BackToBack100) return null;
        decimal? mp = nivel.Ipi ? r.PrecoComImpostos : nivel.Icms ? r.VendaComIcms : r.VendaComPisCofins;
        if (mp is null) return null;
        return mp + (r.ServicoOuReforma ? r.VendaMoComIss ?? 0m : 0m);
    }

    /// <summary>O nível que vale de fato: o escolhido, ou o líquido quando ele não existe nesta proposta.</summary>
    public static NivelDeImposto NivelEfetivo(CalculoPricing.Resultado r, string? codigo) =>
        ValorNoNivel(r, codigo) is not null ? NivelDe(codigo) : Niveis[0];

    /// <summary>O fator custo → preço no nível de imposto escolhido.</summary>
    public static decimal FatorNoNivel(CalculoPricing.Resultado r, string? codigo) =>
        r.CustoTotal <= 0m ? 0m : (ValorNoNivel(r, codigo) ?? r.ValorDaProposta) / r.CustoTotal;

    /// <summary>
    /// O fator que leva o custo ao preço (como o "Fator" do axiaisvent): o
    /// valor da proposta (venda líquida; no Back to Back, S82/S83) ÷ o custo total.
    /// </summary>
    public static decimal Fator(CalculoPricing.Resultado r) => r.CustoTotal <= 0m ? 0m : r.ValorDaProposta / r.CustoTotal;

    /// <summary>
    /// Preço unitário sugerido de um item, já na moeda do preço: o custo dele
    /// vezes o fator, convertido (o fator leva o custo ao preço, e a conversão
    /// leva o preço à moeda do cliente). É preço SEM impostos, como a proposta
    /// informa ("Impostos … não incluídos").
    /// </summary>
    public static decimal PrecoUnitario(CommercialOffer o, decimal custoUnitario, CalculoPricing.Resultado r) =>
        Math.Round(Cambio.ParaVenda(o, custoUnitario * Fator(r)), 2);
}
