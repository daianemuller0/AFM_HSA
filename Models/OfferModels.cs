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
    // Cabeçalho da proposta (dados internos de controle comercial)
    public string Pv { get; set; } = "";
    public string Revisao { get; set; } = "0";
    public string Bu { get; set; } = "";
    public string NcmReducaoIcms { get; set; } = "";
    public string SegmentoMargem { get; set; } = "";
    public string VendaPara { get; set; } = "";
    public string Destino { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Reforma { get; set; } = "";
    public string PrazoEntrega { get; set; } = "";
    public string ClienteCom { get; set; } = "";
    public string FiancaSeguro { get; set; } = "";
    public string Portal { get; set; } = "";
    public string Rep1 { get; set; } = "";
    public string Rep2 { get; set; } = "";
    public string Categories { get; set; } = "";
    public string Product { get; set; } = "";
    public string MarketSegments { get; set; } = "";
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

    // --- Origem do item e apoio à precificação (uso interno) ----------------
    // Ficam gravados como foram vistos ao montar a proposta, para auditoria.
    // NÃO aparecem no documento do cliente e nunca alteram o preço sozinhos.
    public string EquipmentId { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public bool Nacionalizado { get; set; }
    /// <summary>Custo de referência (Valor BR da lista de Nacionalização); null = não disponível.</summary>
    public decimal? CustoReferencia { get; set; }
    public string CustoData { get; set; } = "";
    /// <summary>Preço unitário da última venda do partnumber; null = sem venda.</summary>
    public decimal? UltimaVenda { get; set; }
    public string UltimaVendaData { get; set; } = "";
}
