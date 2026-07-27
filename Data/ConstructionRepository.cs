using AfmHsa.Models;

namespace AfmHsa.Data;

public class ConstructionRepository
{
    private readonly ParquetStore _s;
    public ConstructionRepository(ParquetStore s) => _s = s;

    public List<ConstructionItem> All() => _s.ReadLatest("constructionData",
        "id, equipment_id, item_name, weight_kg, dimensions, material, equipment_part, item_model",
        r => new ConstructionItem
        {
            Id = Fmt.S(r, 0), EquipmentId = Fmt.S(r, 1), ItemName = Fmt.S(r, 2),
            WeightKg = Fmt.S(r, 3), Dimensions = Fmt.S(r, 4), Material = Fmt.S(r, 5),
            EquipmentPart = Fmt.S(r, 6), ItemModel = Fmt.S(r, 7),
        });

    public List<ConstructionItem> ForEquip(string equipmentId) =>
        All().Where(c => c.EquipmentId == equipmentId).ToList();

    public void Save(ConstructionItem c) => _s.WriteRow("constructionData", new KeyValuePair<string, object?>[]
    {
        new("id", c.Id), new("equipment_id", c.EquipmentId), new("item_name", c.ItemName),
        new("weight_kg", c.WeightKg), new("dimensions", c.Dimensions), new("material", c.Material),
        new("equipment_part", c.EquipmentPart), new("item_model", c.ItemModel),
    });
}
