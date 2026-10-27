using AfmHsa.Models;

namespace AfmHsa.Data;

public class BomRepository
{
    private readonly ParquetStore _s;
    public BomRepository(ParquetStore s) => _s = s;

    // "descricao" é da versão anterior da BOM: continua sendo lida para que os
    // itens já cadastrados não apareçam sem descrição. Ao salvar, o valor migra
    // para descricao_pt e a coluna antiga deixa de ser usada.
    private const string Cols =
        "id, equipment_id, item, quantidade, part_number, descricao_en, descricao_pt, material, descricao";

    public List<BomItem> All() => _s.ReadLatest("bom", Cols,
        r =>
        {
            var pt = Fmt.S(r, 6);
            if (string.IsNullOrWhiteSpace(pt)) pt = Fmt.S(r, 8);
            return new BomItem
            {
                Id = Fmt.S(r, 0), EquipmentId = Fmt.S(r, 1), Item = Fmt.S(r, 2),
                Quantidade = Fmt.S(r, 3), PartNumber = Fmt.S(r, 4),
                DescricaoEn = Fmt.S(r, 5), DescricaoPt = pt, Material = Fmt.S(r, 7),
            };
        });

    public List<BomItem> ForEquip(string equipmentId) =>
        All().Where(b => b.EquipmentId == equipmentId).ToList();

    public void Save(BomItem b) => _s.WriteRow("bom", Linha(b));
    public void Delete(BomItem b) => _s.WriteRow("bom", Linha(b), deleted: true);

    /// <summary>Apaga (soft delete) todo o BOM de um equipamento — usado quando a
    /// importação da planilha é feita em modo "substituir" dentro da ficha.</summary>
    public int DeleteByEquip(string equipmentId)
    {
        var itens = ForEquip(equipmentId);
        if (itens.Count == 0) return 0;
        _s.WriteBatch("bom", itens.Select(Linha).ToList(), deleted: true);
        return itens.Count;
    }

    private static IReadOnlyList<KeyValuePair<string, object?>> Linha(BomItem b) =>
        new KeyValuePair<string, object?>[]
        {
            new("id", b.Id), new("equipment_id", b.EquipmentId),
            new("item", b.Item), new("quantidade", b.Quantidade),
            new("part_number", b.PartNumber),
            new("descricao_en", b.DescricaoEn), new("descricao_pt", b.DescricaoPt),
            new("material", b.Material),
        };
}
