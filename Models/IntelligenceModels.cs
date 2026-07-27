namespace AfmHsa.Models;

// Oportunidade de reposição gerada pelo motor (a partir do histórico de vendas
// + ciclos técnicos das peças). Espelha Opportunity de src/lib/types.ts.
public class Opportunity
{
    public string Id { get; set; } = "";
    public string ClientUnitId { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public string Item { get; set; } = "";
    public string DataUltimaVenda { get; set; } = "";
    public string DataPrevista { get; set; } = "";
    public int CicloMeses { get; set; }
    public int DiasAteJanela { get; set; }
    public int DiasAtraso { get; set; }
    public decimal ValorEstimado { get; set; }
    public string Urgencia { get; set; } = "";        // Crítica | Alta | Média | Baixa
    public string StatusTemporal { get; set; } = "";  // histórica | expirada | vencida | próxima | futura
    public string Status { get; set; } = "";
    public string VendedorId { get; set; } = "";
    public string Justificativa { get; set; } = "";
    public string ProximaAcao { get; set; } = "";
    public bool CriticaEquip { get; set; }
    public bool SugereSubstituicao { get; set; }
    public bool Reprogramada { get; set; }
}

// Alerta preditivo derivado das oportunidades.
public class Alert
{
    public string Id { get; set; } = "";
    public string OpportunityId { get; set; } = "";
    public string VendedorId { get; set; } = "";
    public string TipoAlerta { get; set; } = "";
    public string Mensagem { get; set; } = "";
    public string Canal { get; set; } = "";
    public string StatusEnvio { get; set; } = "";
    public string Nivel { get; set; } = "";
}

// Visita / ação em campo (Visit de src/lib/types.ts).
public class Visit
{
    public string Id { get; set; } = "";
    public string ClientUnitId { get; set; } = "";
    public string OpportunityId { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public string VendedorId { get; set; } = "";
    public string DataVisita { get; set; } = "";
    public string TipoVisita { get; set; } = "";
    public string Status { get; set; } = "";
    public string Notas { get; set; } = "";
    public string ProximoPasso { get; set; } = "";
    public string DataProximoContato { get; set; } = "";
    public string Resultado { get; set; } = "";
}
