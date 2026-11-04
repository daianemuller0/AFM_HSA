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

    public static CalculoPricing.Entrada EntradaDe(CommercialOffer o, decimal custoTotal) => new(
        CustoTotal: custoTotal,
        Segmento: (o.SegmentoMargem ?? "").Trim(),
        Product: o.Product ?? "",
        RiscoAdicional: o.RiscoAdicional,
        MargemNegociacao: o.MargemNegociacao,
        Bu: (o.Bu ?? "").Trim(),
        NcmReducaoIcms: Sim(o.NcmReducaoIcms),
        VendaPara: o.VendaPara ?? "",
        Destino: o.Destino ?? "",
        Estado: o.Estado ?? "",
        ClienteCom: o.ClienteCom ?? "",
        PrazoEntregaDias: PrazoEmDias(o.PrazoEntrega),
        ComFianca: Sim(o.FiancaSeguro),
        TipoGarantia: o.TipoGarantia ?? "",
        FiancaPercentual: o.FiancaPercentual,
        Portal: o.Portal ?? "",
        Rep1: Representante(o.Rep1),
        Rep2: Representante(o.Rep2));

    /// <summary>O pricing da proposta, pelo modo escolhido (margem pedida ou preço-meta).</summary>
    public static CalculoPricing.Resultado Da(CommercialOffer o, decimal custoTotal)
    {
        var e = EntradaDe(o, custoTotal);
        return o.PricingModo == "preco"
            ? CalculoPricing.PeloPreco(e, o.PrecoMeta)
            : CalculoPricing.PelaMargem(e, o.MargemAlvo);
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

    /// <summary>
    /// Preço unitário sugerido de um item: o custo dele com o mesmo fator do
    /// pricing (venda líquida ÷ custo total). É preço SEM impostos, como a
    /// proposta informa ("Impostos … não incluídos").
    /// </summary>
    public static decimal PrecoUnitario(decimal custoUnitario, CalculoPricing.Resultado r) =>
        r.CustoTotal <= 0 ? 0 : Math.Round(custoUnitario * r.VendaLiquida / r.CustoTotal, 2);
}
