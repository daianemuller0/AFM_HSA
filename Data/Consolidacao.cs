using System.Globalization;
using System.Text.Json;
using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>
/// Consolidação: a proposta foi vendida e é consolidada internamente para
/// passar ao próximo setor. Ela recebe o número do projeto (PV), o número do
/// pedido (a nova "Sua referência"), o valor do pedido conferido, a data de
/// entrega e o pagamento confirmados, e a revisão passa a aparecer como
/// "2 - Consolidado". O jeito como ela estava antes fica guardado para desfazer.
/// </summary>
public static class Consolidacao
{
    public const string Selo = "Consolidado";

    public static bool Consolidada(CommercialOffer o) => !string.IsNullOrWhiteSpace(o.ConsolidadaEm);

    /// <summary>A revisão como aparece nas telas e no Word: "2" ou "2 - Consolidado".</summary>
    public static string Revisao(CommercialOffer o) =>
        PropostaWord.Ou(o.Revisao, "0") + (Consolidada(o) ? $" - {Selo}" : "");

    /// <summary>A revisão com dois dígitos ("02" ou "02 - Consolidado"), como no topo da proposta.</summary>
    public static string Revisao2(CommercialOffer o) =>
        PropostaWord.Revisao2(o.Revisao) + (Consolidada(o) ? $" - {Selo}" : "");

    public static DateOnly? Data(string? texto) =>
        DateOnly.TryParseExact((texto ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static string Texto(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    /// <summary>A data de entrega confirmada do item: a dele ou a da proposta; null = não consolidada (vale o prazo em dias).</summary>
    public static DateOnly? DataDeEntrega(CommercialOffer o, CommercialOfferItem it) =>
        Consolidada(o) ? Data(it.DataEntrega) ?? Data(o.DataEntrega) : null;

    /// <summary>A sugestão de data de entrega: hoje + o prazo do item (ou o da proposta).</summary>
    public static DateOnly? DataPeloPrazo(CommercialOffer o, CommercialOfferItem it, DateOnly hoje)
    {
        var dias = it.PrazoEntregaDias > 0 ? it.PrazoEntregaDias : PricingDaProposta.PrazoEmDias(o.PrazoEntrega);
        return dias > 0 ? hoje.AddDays(dias) : null;
    }

    // ---- moeda da BU (HCHL em CLP, HPU em PEN) ----

    /// <summary>A moeda em que a BU de fora precisa ver o valor consolidado: HCHL → CLP, HPU → PEN; null = BU do Brasil.</summary>
    public static Moeda? MoedaDaBu(CommercialOffer o)
    {
        var bu = (o.Bu ?? "").Trim();
        if (bu.StartsWith("HCHL", StringComparison.OrdinalIgnoreCase)) return Moeda.Clp;
        if (bu.Equals("HPU", StringComparison.OrdinalIgnoreCase)) return Moeda.Pen;
        return null;
    }

    /// <summary>A consolidação pede a conversão: BU HCHL/HPU com a proposta em outra moeda (já em CLP/PEN não precisa).</summary>
    public static bool PedeConversao(CommercialOffer o) => MoedaDaBu(o) is Moeda m && Cambio.MoedaDoPreco(o) != m;

    /// <summary>"1 USD = 950,2500 CLP"; null = sem conversão.</summary>
    public static string? TextoDaTaxa(CommercialOffer o) =>
        PedeConversao(o) && o.TaxaMoedaBu > 0m
            ? $"1 {Cambio.MoedaDoPreco(o).Codigo()} = {o.TaxaMoedaBu.ToString("#,##0.####", new System.Globalization.CultureInfo("pt-BR"))} {MoedaDaBu(o)!.Value.Codigo()}"
            : null;

    /// <summary>Um valor na moeda da proposta, na moeda da BU ("CLP 123.456.789"); null = sem conversão.</summary>
    public static string? NaMoedaDaBu(CommercialOffer o, decimal valor) =>
        PedeConversao(o) && o.TaxaMoedaBu > 0m ? Moedas.Dinheiro(MoedaDaBu(o)!.Value, Math.Round(valor * o.TaxaMoedaBu, 2)) : null;

    // ---- desfazer ----

    public sealed record Retrato(CommercialOffer Oferta, List<CommercialOfferItem> Itens);

    /// <summary>Guarda a proposta e os itens como estavam (sem o retrato anterior dentro).</summary>
    public static string Guardar(CommercialOffer o, IEnumerable<CommercialOfferItem> itens)
    {
        var copia = JsonSerializer.Deserialize<CommercialOffer>(JsonSerializer.Serialize(o))!;
        copia.ConsolidacaoAntes = "";
        return JsonSerializer.Serialize(new Retrato(copia, itens.ToList()));
    }

    public static Retrato? Ler(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Retrato>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Copia as marcas da consolidação de uma proposta gravada para a aberta.</summary>
    public static void CopiarMarcas(CommercialOffer de, CommercialOffer para)
    {
        (para.ConsolidadaEm, para.ConsolidadaPor, para.NumeroPedido, para.ValorPedido, para.MoedaPedido, para.DataEntrega, para.ConsolidacaoAntes) =
            (de.ConsolidadaEm, de.ConsolidadaPor, de.NumeroPedido, de.ValorPedido, de.MoedaPedido, de.DataEntrega, de.ConsolidacaoAntes);
        para.TaxaMoedaBu = de.TaxaMoedaBu;
    }

    /// <summary>Tira as marcas da consolidação (revisão nova feita a partir de uma consolidada, cópia…).</summary>
    public static void Limpar(CommercialOffer o, IEnumerable<CommercialOfferItem> itens)
    {
        o.ConsolidadaEm = o.ConsolidadaPor = o.NumeroPedido = o.MoedaPedido = o.DataEntrega = o.ConsolidacaoAntes = "";
        o.ValorPedido = 0m;
        o.TaxaMoedaBu = 0m;
        foreach (var it in itens) it.DataEntrega = "";
    }
}
