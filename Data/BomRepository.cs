using AfmHsa.Models;

namespace AfmHsa.Data;

public class BomRepository
{
    private readonly ParquetStore _s;
    public BomRepository(ParquetStore s) => _s = s;

    private const string Cols =
        "id, equipment_id, tag, serial_no, part_number, quantidade, descricao, outros, observacao";

    public List<BomItem> All() => _s.ReadLatest("bom", Cols,
        r => new BomItem
        {
            Id = Fmt.S(r, 0), EquipmentId = Fmt.S(r, 1), Tag = Fmt.S(r, 2), SerialNo = Fmt.S(r, 3),
            PartNumber = Fmt.S(r, 4), Quantidade = Fmt.S(r, 5), Descricao = Fmt.S(r, 6),
            Outros = Fmt.S(r, 7), Observacao = Fmt.S(r, 8),
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
            new("tag", b.Tag), new("serial_no", b.SerialNo),
            new("part_number", b.PartNumber), new("quantidade", b.Quantidade),
            new("descricao", b.Descricao), new("outros", b.Outros),
            new("observacao", b.Observacao),
        };
}
