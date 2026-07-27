namespace AfmHsa.Models;

// Ajustes do usuário sobre uma oportunidade gerada pelo motor (o motor recalcula
// sempre; estes overrides sobrevivem). Espelha db.oppStatus / oppReprogram /
// oppDeleted do app original. Chave = id da oportunidade.
public class OppOverride
{
    public string Id { get; set; } = "";       // id da oportunidade
    public string Status { get; set; } = "";    // status comercial escolhido
    public string Reprograma { get; set; } = ""; // nova data prevista (ISO) ou ""
    public bool Deleted { get; set; }
    public string DeleteReason { get; set; } = "";
    public string DeleteBy { get; set; } = "";
    public string DeleteAt { get; set; } = "";
}

// Nota/registro comercial de uma oportunidade (db.oppNotes do app original).
public class OppNote
{
    public string Id { get; set; } = "";
    public string OppId { get; set; } = "";
    public string Texto { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Usuario { get; set; } = "";
    public string Data { get; set; } = "";
}
