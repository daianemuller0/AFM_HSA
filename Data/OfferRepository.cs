using System.Globalization;
using AfmHsa.Models;

namespace AfmHsa.Data;

public class OfferRepository
{
    private readonly ParquetStore _s;
    public OfferRepository(ParquetStore s) => _s = s;

    private static string D(decimal v) => v.ToString(CultureInfo.InvariantCulture);

    // ---- ofertas ---------------------------------------------------------
    public List<CommercialOffer> All() => _s.ReadLatest("commercialOffers",
        "id, offer_code, client_unit_id, equipment_id, client_name, attention_to, city, our_reference, project, " +
        "offer_date, prepared_by, intro_text, tax_text, payment_terms, delivery_time, delivery_conditions, " +
        "validity_text, observations, sgi_text, terms_text, total_amount, status, created_at, " +
        "pv, revisao, bu, ncm_reducao_icms, segmento_margem, venda_para, destino, estado, reforma, " +
        "prazo_entrega, cliente_com, fianca_seguro, portal, rep1, rep2, categories, product, market_segments",
        r => new CommercialOffer
        {
            Id = Fmt.S(r, 0), OfferCode = Fmt.S(r, 1), ClientUnitId = Fmt.S(r, 2), EquipmentId = Fmt.S(r, 3),
            ClientName = Fmt.S(r, 4), AttentionTo = Fmt.S(r, 5), City = Fmt.S(r, 6), OurReference = Fmt.S(r, 7),
            Project = Fmt.S(r, 8), OfferDate = Fmt.S(r, 9), PreparedBy = Fmt.S(r, 10),
            IntroText = Fmt.S(r, 11), TaxText = Fmt.S(r, 12), PaymentTerms = Fmt.S(r, 13),
            DeliveryTime = Fmt.S(r, 14), DeliveryConditions = Fmt.S(r, 15), ValidityText = Fmt.S(r, 16),
            Observations = Fmt.S(r, 17), SgiText = Fmt.S(r, 18), TermsText = Fmt.S(r, 19),
            TotalAmount = Fmt.Dec(Fmt.S(r, 20)), Status = Fmt.S(r, 21), CreatedAt = Fmt.S(r, 22),
            Pv = Fmt.S(r, 23), Revisao = Fmt.S(r, 24), Bu = Fmt.S(r, 25), NcmReducaoIcms = Fmt.S(r, 26),
            SegmentoMargem = Fmt.S(r, 27), VendaPara = Fmt.S(r, 28), Destino = Fmt.S(r, 29), Estado = Fmt.S(r, 30),
            Reforma = Fmt.S(r, 31), PrazoEntrega = Fmt.S(r, 32), ClienteCom = Fmt.S(r, 33), FiancaSeguro = Fmt.S(r, 34),
            Portal = Fmt.S(r, 35), Rep1 = Fmt.S(r, 36), Rep2 = Fmt.S(r, 37), Categories = Fmt.S(r, 38),
            Product = Fmt.S(r, 39), MarketSegments = Fmt.S(r, 40),
        }, orderBy: "created_at DESC");

    public CommercialOffer? Get(string id) => All().FirstOrDefault(o => o.Id == id);

    public void Save(CommercialOffer o) => _s.WriteRow("commercialOffers", new KeyValuePair<string, object?>[]
    {
        new("id", o.Id), new("offer_code", o.OfferCode), new("client_unit_id", o.ClientUnitId),
        new("equipment_id", o.EquipmentId), new("client_name", o.ClientName), new("attention_to", o.AttentionTo),
        new("city", o.City), new("our_reference", o.OurReference), new("project", o.Project),
        new("offer_date", o.OfferDate), new("prepared_by", o.PreparedBy), new("intro_text", o.IntroText),
        new("tax_text", o.TaxText), new("payment_terms", o.PaymentTerms), new("delivery_time", o.DeliveryTime),
        new("delivery_conditions", o.DeliveryConditions), new("validity_text", o.ValidityText),
        new("observations", o.Observations), new("sgi_text", o.SgiText), new("terms_text", o.TermsText),
        new("total_amount", D(o.TotalAmount)), new("status", o.Status), new("created_at", o.CreatedAt),
        new("pv", o.Pv), new("revisao", o.Revisao), new("bu", o.Bu), new("ncm_reducao_icms", o.NcmReducaoIcms),
        new("segmento_margem", o.SegmentoMargem), new("venda_para", o.VendaPara), new("destino", o.Destino),
        new("estado", o.Estado), new("reforma", o.Reforma), new("prazo_entrega", o.PrazoEntrega),
        new("cliente_com", o.ClienteCom), new("fianca_seguro", o.FiancaSeguro), new("portal", o.Portal),
        new("rep1", o.Rep1), new("rep2", o.Rep2), new("categories", o.Categories), new("product", o.Product),
        new("market_segments", o.MarketSegments),
    });

    public void Delete(string id) => _s.WriteRow("commercialOffers",
        new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);

    // ---- itens -----------------------------------------------------------
    public List<CommercialOfferItem> AllItems() => _s.ReadLatest("commercialOfferItems",
        "id, offer_id, description, quantity, unit_price, total_price, " +
        "equipment_id, part_number, nacionalizado, custo_referencia, custo_data, ultima_venda, ultima_venda_data",
        r => new CommercialOfferItem
        {
            Id = Fmt.S(r, 0), OfferId = Fmt.S(r, 1), Description = Fmt.S(r, 2),
            Quantity = Fmt.Int(Fmt.S(r, 3)), UnitPrice = Fmt.Dec(Fmt.S(r, 4)), TotalPrice = Fmt.Dec(Fmt.S(r, 5)),
            EquipmentId = Fmt.S(r, 6), PartNumber = Fmt.S(r, 7), Nacionalizado = Fmt.Bool(Fmt.S(r, 8)),
            CustoReferencia = DecOuNull(Fmt.S(r, 9)), CustoData = Fmt.S(r, 10),
            UltimaVenda = DecOuNull(Fmt.S(r, 11)), UltimaVendaData = Fmt.S(r, 12),
        });

    /// <summary>Vazio continua vazio: "custo não disponível" não pode virar R$ 0,00.</summary>
    private static decimal? DecOuNull(string s) => string.IsNullOrWhiteSpace(s) ? null : Fmt.Dec(s);
    private static string DOuVazio(decimal? v) => v is decimal d ? D(d) : "";

    public List<CommercialOfferItem> ItemsFor(string offerId) =>
        AllItems().Where(i => i.OfferId == offerId).ToList();

    public void SaveItem(CommercialOfferItem it) => _s.WriteRow("commercialOfferItems",
        new KeyValuePair<string, object?>[]
        {
            new("id", it.Id), new("offer_id", it.OfferId), new("description", it.Description),
            new("quantity", it.Quantity.ToString()), new("unit_price", D(it.UnitPrice)),
            new("total_price", D(it.TotalPrice)),
            new("equipment_id", it.EquipmentId), new("part_number", it.PartNumber),
            new("nacionalizado", it.Nacionalizado ? "true" : "false"),
            new("custo_referencia", DOuVazio(it.CustoReferencia)), new("custo_data", it.CustoData),
            new("ultima_venda", DOuVazio(it.UltimaVenda)), new("ultima_venda_data", it.UltimaVendaData),
        });
}
