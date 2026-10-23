using AfmHsa.Models;

namespace AfmHsa.Data;

public class BomRepository
{
    private readonly ParquetStore _s;
    public BomRepository(ParquetStore s) => _s = s;

    public List<BomItem> All() => _s.ReadLatest("bom",
        "id, equipment_id, part_number, quantidade, descricao, outros, observacao",
        r => new BomItem
        {
            Id = Fmt.S(r, 0), EquipmentId = Fmt.S(r, 1), PartNumber = Fmt.S(r, 2),
            Quantidade = Fmt.S(r, 3), Descricao = Fmt.S(r, 4), Outros = Fmt.S(r, 5),
            Observacao = Fmt.S(r, 6),
        });

    public List<BomItem> ForEquip(string equipmentId) =>
        All().Where(b => b.EquipmentId == equipmentId).ToList();

    public void Save(BomItem b) => _s.WriteRow("bom", new KeyValuePair<string, object?>[]
    {
        new("id", b.Id), new("equipment_id", b.EquipmentId), new("part_number", b.PartNumber),
        new("quantidade", b.Quantidade), new("descricao", b.Descricao),
        new("outros", b.Outros), new("observacao", b.Observacao),
    });

    public void Delete(BomItem b) => _s.WriteRow("bom", new KeyValuePair<string, object?>[]
    {
        new("id", b.Id), new("equipment_id", b.EquipmentId), new("part_number", b.PartNumber),
        new("quantidade", b.Quantidade), new("descricao", b.Descricao),
        new("outros", b.Outros), new("observacao", b.Observacao),
    }, deleted: true);
}
