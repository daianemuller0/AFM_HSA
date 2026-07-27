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
        "validity_text, observations, sgi_text, terms_text, total_amount, status, created_at",
        r => new CommercialOffer
        {
            Id = Fmt.S(r, 0), OfferCode = Fmt.S(r, 1), ClientUnitId = Fmt.S(r, 2), EquipmentId = Fmt.S(r, 3),
            ClientName = Fmt.S(r, 4), AttentionTo = Fmt.S(r, 5), City = Fmt.S(r, 6), OurReference = Fmt.S(r, 7),
            Project = Fmt.S(r, 8), OfferDate = Fmt.S(r, 9), PreparedBy = Fmt.S(r, 10),
            IntroText = Fmt.S(r, 11), TaxText = Fmt.S(r, 12), PaymentTerms = Fmt.S(r, 13),
            DeliveryTime = Fmt.S(r, 14), DeliveryConditions = Fmt.S(r, 15), ValidityText = Fmt.S(r, 16),
            Observations = Fmt.S(r, 17), SgiText = Fmt.S(r, 18), TermsText = Fmt.S(r, 19),
            TotalAmount = Fmt.Dec(Fmt.S(r, 20)), Status = Fmt.S(r, 21), CreatedAt = Fmt.S(r, 22),
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
    });

    public void Delete(string id) => _s.WriteRow("commercialOffers",
        new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);

    // ---- itens -----------------------------------------------------------
    public List<CommercialOfferItem> AllItems() => _s.ReadLatest("commercialOfferItems",
        "id, offer_id, description, quantity, unit_price, total_price",
        r => new CommercialOfferItem
        {
            Id = Fmt.S(r, 0), OfferId = Fmt.S(r, 1), Description = Fmt.S(r, 2),
            Quantity = Fmt.Int(Fmt.S(r, 3)), UnitPrice = Fmt.Dec(Fmt.S(r, 4)), TotalPrice = Fmt.Dec(Fmt.S(r, 5)),
        });

    public List<CommercialOfferItem> ItemsFor(string offerId) =>
        AllItems().Where(i => i.OfferId == offerId).ToList();

    public void SaveItem(CommercialOfferItem it) => _s.WriteRow("commercialOfferItems",
        new KeyValuePair<string, object?>[]
        {
            new("id", it.Id), new("offer_id", it.OfferId), new("description", it.Description),
            new("quantity", it.Quantity.ToString()), new("unit_price", D(it.UnitPrice)),
            new("total_price", D(it.TotalPrice)),
        });
}
