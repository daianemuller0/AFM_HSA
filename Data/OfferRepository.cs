using AfmHsa.Models;

namespace AfmHsa.Data;

public class OfferRepository
{
    private readonly ParquetStore _s;
    public OfferRepository(ParquetStore s) => _s = s;

    public List<CommercialOffer> All() => _s.ReadLatest("commercialOffers",
        "id, offer_code, client_name, attention_to, title, total_amount, status, offer_date, created_at",
        r => new CommercialOffer
        {
            Id = Fmt.S(r, 0), OfferCode = Fmt.S(r, 1), ClientName = Fmt.S(r, 2),
            AttentionTo = Fmt.S(r, 3), Title = Fmt.S(r, 4), TotalAmount = Fmt.Dec(Fmt.S(r, 5)),
            Status = Fmt.S(r, 6), OfferDate = Fmt.S(r, 7), CreatedAt = Fmt.S(r, 8),
        }, orderBy: "created_at DESC");

    public void Save(CommercialOffer o) => _s.WriteRow("commercialOffers", new KeyValuePair<string, object?>[]
    {
        new("id", o.Id),
        new("offer_code", o.OfferCode),
        new("client_name", o.ClientName),
        new("attention_to", o.AttentionTo),
        new("title", o.Title),
        new("total_amount", o.TotalAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new("status", o.Status),
        new("offer_date", o.OfferDate),
        new("created_at", o.CreatedAt),
    });

    public void Delete(string id) => _s.WriteRow("commercialOffers",
        new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);
}
