using System.Globalization;
using AfmHsa.Models;

namespace AfmHsa.Data;

// Persiste os ajustes do usuário sobre as oportunidades (status, reprogramação,
// exclusão lógica) e as notas comerciais. Tudo em Parquet, no mesmo padrão.
public class OppOverrideRepository
{
    private readonly ParquetStore _s;
    public OppOverrideRepository(ParquetStore s) => _s = s;

    // ---- overrides (1 por oportunidade) ----------------------------------
    public Dictionary<string, OppOverride> All() => _s.ReadLatest("oppOverrides",
        "id, status, reprograma, deleted, delete_reason, delete_by, delete_at",
        r => new OppOverride
        {
            Id = Fmt.S(r, 0), Status = Fmt.S(r, 1), Reprograma = Fmt.S(r, 2),
            Deleted = Fmt.Bool(Fmt.S(r, 3)), DeleteReason = Fmt.S(r, 4),
            DeleteBy = Fmt.S(r, 5), DeleteAt = Fmt.S(r, 6),
        }).ToDictionary(o => o.Id);

    public OppOverride Get(string id) => All().TryGetValue(id, out var o) ? o : new OppOverride { Id = id };

    private void Write(OppOverride o) => _s.WriteRow("oppOverrides", new KeyValuePair<string, object?>[]
    {
        new("id", o.Id),
        new("status", o.Status),
        new("reprograma", o.Reprograma),
        new("deleted", o.Deleted ? "true" : "false"),
        new("delete_reason", o.DeleteReason),
        new("delete_by", o.DeleteBy),
        new("delete_at", o.DeleteAt),
    });

    public void SetStatus(string id, string status)
    {
        var o = Get(id);
        o.Status = status;
        Write(o);
    }

    public void Reprogram(string id, string dateIso)
    {
        var o = Get(id);
        o.Reprograma = dateIso;
        o.Status = "Reprogramada";
        Write(o);
    }

    public void Delete(string id, string by, string reason)
    {
        var o = Get(id);
        o.Deleted = true;
        o.DeleteBy = by;
        o.DeleteReason = string.IsNullOrWhiteSpace(reason) ? "Sem motivo informado" : reason;
        o.DeleteAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Write(o);
    }

    public void Restore(string id)
    {
        var o = Get(id);
        o.Deleted = false;
        Write(o);
    }

    // ---- notas (N por oportunidade) --------------------------------------
    public List<OppNote> AllNotes() => _s.ReadLatest("oppNotes",
        "id, opp_id, texto, tipo, usuario, data",
        r => new OppNote
        {
            Id = Fmt.S(r, 0), OppId = Fmt.S(r, 1), Texto = Fmt.S(r, 2),
            Tipo = Fmt.S(r, 3), Usuario = Fmt.S(r, 4), Data = Fmt.S(r, 5),
        }, orderBy: "data DESC");

    public List<OppNote> Notes(string oppId) => AllNotes().Where(n => n.OppId == oppId).ToList();

    public void AddNote(string oppId, string texto, string tipo, string usuario) =>
        _s.WriteRow("oppNotes", new KeyValuePair<string, object?>[]
        {
            new("id", Guid.NewGuid().ToString("N")),
            new("opp_id", oppId),
            new("texto", texto),
            new("tipo", string.IsNullOrWhiteSpace(tipo) ? "Observação interna" : tipo),
            new("usuario", usuario),
            new("data", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
        });
}
