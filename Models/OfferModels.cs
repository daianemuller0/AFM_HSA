namespace AfmHsa.Models;

// Oferta comercial (versão inicial simplificada do CommercialOffer do app
// original — os campos completos do template Howden/Chart entram numa próxima
// fase). Persistida em Parquet como as demais entidades.
public class CommercialOffer
{
    public string Id { get; set; } = "";
    public string OfferCode { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string AttentionTo { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Rascunho";
    public string OfferDate { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}
