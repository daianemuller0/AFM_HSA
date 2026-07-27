namespace AfmHsa.Models;

// Dado construtivo de um equipamento (item físico: peça/parte com peso,
// dimensões, material, modelo). Espelha EquipmentConstructionData do original.
public class ConstructionItem
{
    public string Id { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string WeightKg { get; set; } = "";     // texto ("" quando n/a)
    public string Dimensions { get; set; } = "";
    public string Material { get; set; } = "";
    public string EquipmentPart { get; set; } = "";
    public string ItemModel { get; set; } = "";
}
