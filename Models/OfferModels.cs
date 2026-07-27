namespace AfmHsa.Models;

// Oferta comercial no template Howden/Chart (versão funcional da migração).
// Persistida em Parquet como as demais entidades.
public class CommercialOffer
{
    public string Id { get; set; } = "";
    public string OfferCode { get; set; } = "";
    public string ClientUnitId { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    // Cabeçalho
    public string ClientName { get; set; } = "";
    public string AttentionTo { get; set; } = "";
    public string City { get; set; } = "";
    public string OurReference { get; set; } = "";
    public string Project { get; set; } = "";
    public string OfferDate { get; set; } = "";
    public string PreparedBy { get; set; } = "";
    // Condições comerciais (editáveis)
    public string IntroText { get; set; } = "";
    public string TaxText { get; set; } = "";
    public string PaymentTerms { get; set; } = "";
    public string DeliveryTime { get; set; } = "";
    public string DeliveryConditions { get; set; } = "";
    public string ValidityText { get; set; } = "";
    public string Observations { get; set; } = "";
    public string SgiText { get; set; } = "";
    public string TermsText { get; set; } = "";
    // Totais / controle
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Rascunho";
    public string CreatedAt { get; set; } = "";
}

// Item de uma oferta comercial.
public class CommercialOfferItem
{
    public string Id { get; set; } = "";
    public string OfferId { get; set; } = "";
    public string Description { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
}
