using AfmHsa.Models;

namespace AfmHsa.Data;

public class VisitRepository
{
    private readonly ParquetStore _s;
    public VisitRepository(ParquetStore s) => _s = s;

    public List<Visit> All() => _s.ReadLatest("visits",
        "id, client_unit_id, opportunity_id, equipment_id, vendedor_id, data_visita, tipo_visita, status, notas, proximo_passo, data_proximo_contato, resultado",
        r => new Visit
        {
            Id = Fmt.S(r, 0), ClientUnitId = Fmt.S(r, 1), OpportunityId = Fmt.S(r, 2),
            EquipmentId = Fmt.S(r, 3), VendedorId = Fmt.S(r, 4), DataVisita = Fmt.S(r, 5),
            TipoVisita = Fmt.S(r, 6), Status = Fmt.S(r, 7), Notas = Fmt.S(r, 8),
            ProximoPasso = Fmt.S(r, 9), DataProximoContato = Fmt.S(r, 10), Resultado = Fmt.S(r, 11),
        }, orderBy: "data_visita DESC");

    public void Save(Visit v) => _s.WriteRow("visits", new KeyValuePair<string, object?>[]
    {
        new("id", v.Id),
        new("client_unit_id", v.ClientUnitId),
        new("opportunity_id", v.OpportunityId),
        new("equipment_id", v.EquipmentId),
        new("vendedor_id", v.VendedorId),
        new("data_visita", v.DataVisita),
        new("tipo_visita", v.TipoVisita),
        new("status", v.Status),
        new("notas", v.Notas),
        new("proximo_passo", v.ProximoPasso),
        new("data_proximo_contato", v.DataProximoContato),
        new("resultado", v.Resultado),
    });
}
