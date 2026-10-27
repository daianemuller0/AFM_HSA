namespace AfmHsa.Models;

// Item de BOM (Bill of Materials) de um equipamento.
// Usado na ficha técnica no lugar dos dados construtivos para todo tipo de
// equipamento que NÃO seja ventilador centrífugo — nesse caso a Howden trabalha
// com a lista de materiais, e não com peso/dimensões.
public class BomItem
{
    public string Id { get; set; } = "";
    public string EquipmentId { get; set; } = "";

    /// <summary>Número/posição do item dentro da lista de materiais.</summary>
    public string Item { get; set; } = "";

    public string Quantidade { get; set; } = "";   // texto: há itens em "kg", "m", "1 jogo"…
    public string PartNumber { get; set; } = "";
    public string DescricaoEn { get; set; } = "";
    public string DescricaoPt { get; set; } = "";
    public string Material { get; set; } = "";
}
