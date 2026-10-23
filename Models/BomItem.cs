namespace AfmHsa.Models;

// Item de BOM (Bill of Materials) de um equipamento.
// Usado na ficha técnica no lugar dos dados construtivos para todo tipo de
// equipamento que NÃO seja ventilador centrífugo — nesse caso a Howden trabalha
// com a lista de materiais (partnumber + quantidade), e não com peso/dimensões.
public class BomItem
{
    public string Id { get; set; } = "";
    public string EquipmentId { get; set; } = "";

    /// <summary>TAG e nº de série da unidade física a que este item pertence.
    /// O mesmo modelo pode ter várias unidades instaladas e cada uma com o seu
    /// BOM; em branco quando o item vale para o modelo inteiro.</summary>
    public string Tag { get; set; } = "";
    public string SerialNo { get; set; } = "";

    public string PartNumber { get; set; } = "";
    public string Quantidade { get; set; } = "";   // texto: há itens em "kg", "m", "1 jogo"…
    public string Descricao { get; set; } = "";
    public string Outros { get; set; } = "";
    public string Observacao { get; set; } = "";
}
