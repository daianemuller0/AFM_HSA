using System.Globalization;
using System.Text.RegularExpressions;
using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>
/// Motor de inteligência comercial — porta src/lib/engine.ts do app original.
/// Gera oportunidades de reposição a partir do histórico de vendas + ciclos
/// técnicos das peças, e os alertas preditivos derivados. Também centraliza os
/// "lookups" (nome de cliente/unidade/vendedor). Scoped: carrega as listas uma
/// vez por requisição.
/// </summary>
public class IntelligenceService
{
    // Data-base fixa (igual ao app original — mantém as janelas estáveis na demo).
    private static readonly DateTime Today = new(2026, 5, 28);

    private readonly SalesRecordRepository _salesRepo;
    private readonly PartRepository _partsRepo;
    private readonly InstalledBaseRepository _baseRepo;
    private readonly CompanyRepository _companiesRepo;
    private readonly ClientUnitRepository _unitsRepo;
    private readonly EquipmentRepository _equipRepo;
    private readonly SalespersonRepository _spRepo;
    private readonly OppOverrideRepository _ovrRepo;

    public IntelligenceService(
        SalesRecordRepository sales, PartRepository parts, InstalledBaseRepository baseInst,
        CompanyRepository companies, ClientUnitRepository units, EquipmentRepository equip,
        SalespersonRepository sp, OppOverrideRepository ovr)
    {
        _salesRepo = sales; _partsRepo = parts; _baseRepo = baseInst;
        _companiesRepo = companies; _unitsRepo = units; _equipRepo = equip; _spRepo = sp;
        _ovrRepo = ovr;
    }

    // Todos os status comerciais possíveis (para os seletores da UI).
    public static readonly string[] OppStatuses =
    {
        "Prevista", "Próxima", "Crítica", "Em contato", "Visita agendada",
        "Proposta enviada", "Negociação", "Ganha", "Perdida", "Sem interesse", "Reprogramada",
    };

    private List<SalesRecord>? _sales;
    private List<Part>? _parts;
    private List<InstalledBase>? _base;
    private Dictionary<string, string>? _companyName;
    private Dictionary<string, ClientUnit>? _unit;
    private Dictionary<string, Equipment>? _equip;
    private Dictionary<string, string>? _spName;

    public List<SalesRecord> Sales => _sales ??= _salesRepo.All();
    private List<Part> Parts => _parts ??= _partsRepo.All();
    private List<InstalledBase> Base => _base ??= _baseRepo.All();
    private Dictionary<string, string> CompanyNames =>
        _companyName ??= _companiesRepo.All().ToDictionary(c => c.Id, c => c.Nome);
    private Dictionary<string, ClientUnit> Units =>
        _unit ??= _unitsRepo.All().ToDictionary(u => u.Id, u => u);
    private Dictionary<string, Equipment> Equipments =>
        _equip ??= _equipRepo.All().ToDictionary(e => e.Id, e => e);
    private Dictionary<string, string> SpNames =>
        _spName ??= _spRepo.All().ToDictionary(s => s.Id, s => s.Nome);

    // ---- lookups ---------------------------------------------------------
    public string CompanyName(string id) => CompanyNames.TryGetValue(id, out var n) ? n : id;
    public string SalespersonName(string id) => SpNames.TryGetValue(id, out var n) ? n : "—";
    public string EquipmentModel(string id) => Equipments.TryGetValue(id, out var e) ? e.Modelo : "";
    public string EquipmentNome(string id) => Equipments.TryGetValue(id, out var e) ? e.Nome : id;
    public string UnitSegmento(string unitId) => Units.TryGetValue(unitId, out var u) ? u.Segmento : "—";
    public string EquipmentTipo(string id) => Equipments.TryGetValue(id, out var e) && !string.IsNullOrEmpty(e.Tipo) ? e.Tipo : "—";
    // Data-base de referência das janelas (fixa, igual ao app original).
    public static readonly DateOnly BaseDate = new(2026, 5, 28);
    public string UnitNome(string unitId) => Units.TryGetValue(unitId, out var u) ? u.NomeUnidade : unitId;
    public string ClienteDaUnidade(string unitId) => Units.TryGetValue(unitId, out var u) ? CompanyName(u.CompanyId) : unitId;

    public string UnitLabel(string unitId)
    {
        if (!Units.TryGetValue(unitId, out var u)) return unitId;
        return $"{CompanyName(u.CompanyId)} — {u.NomeUnidade}";
    }

    // ---- regras de ciclo de troca (meses) --------------------------------
    private record Rule(string[] Kw, int Meses, bool Critica);

    private static readonly Rule[] Rules =
    {
        new(new[] { "rotor", "palheta", "paleta", "eixo da paleta" }, 30, true),
        new(new[] { "eixo" }, 30, true),
        new(new[] { "cesto" }, 34, true),
        new(new[] { "mancal", "mancais", "mancai" }, 60, false),
        new(new[] { "carcaça", "carcaca" }, 60, true),
        new(new[] { "cone aspirante" }, 36, false),
        new(new[] { "difusor" }, 48, false),
        new(new[] { "acoplamento" }, 36, false),
        new(new[] { "cilindro" }, 48, false),
        new(new[] { "registro", "veneziana" }, 60, false),
        new(new[] { "silenciador" }, 72, false),
        new(new[] { "cardan" }, 48, false),
        new(new[] { "base met" }, 120, false),
        new(new[] { "completo" }, 120, true),
    };

    private static (int meses, bool critica) CycleFor(string item)
    {
        var s = item.ToLowerInvariant();
        foreach (var r in Rules)
            if (r.Kw.Any(k => s.Contains(k))) return (r.Meses, r.Critica);
        return (48, false);
    }

    private (int meses, bool critica) PartCycleFor(string equipmentId, string item)
    {
        var s = NormItem(item);
        var parts = Parts.Where(p => p.EquipmentId == equipmentId && p.Ativo).ToList();
        foreach (var p in parts)
        {
            var pn = NormItem(p.Nome);
            if (!string.IsNullOrEmpty(pn) && (s.Contains(pn) || pn.Contains(s)))
                return (p.CicloMeses, p.Critica);
        }
        foreach (var p in parts)
        {
            var tok = NormItem(p.Nome).Split(new[] { ' ', '+', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 2);
            if (tok.Any(t => s.Contains(t))) return (p.CicloMeses, p.Critica);
        }
        return CycleFor(item);
    }

    // ---- geração de oportunidades ----------------------------------------
    public List<Opportunity> Opportunities(bool includeDeleted = false)
    {
        var overrides = _ovrRepo.All();
        var groups = new Dictionary<string, List<SalesRecord>>();
        foreach (var s in Sales)
        {
            var key = $"{s.EquipmentId}::{NormItem(s.ItemVendido)}";
            if (!groups.TryGetValue(key, out var arr)) { arr = new(); groups[key] = arr; }
            arr.Add(s);
        }

        var opps = new List<Opportunity>();
        foreach (var (key, recs) in groups)
        {
            var id = "op-" + Regex.Replace(key, "[^a-z0-9]+", "-", RegexOptions.IgnoreCase).ToLowerInvariant();
            overrides.TryGetValue(id, out var ov);
            if (ov != null && ov.Deleted && !includeDeleted) continue;

            recs.Sort((a, b) => string.CompareOrdinal(a.DataVenda, b.DataVenda));
            var last = recs[^1];
            var item = last.ItemVendido;
            var (meses, partCritica) = PartCycleFor(last.EquipmentId, item);

            var reprog = ov != null && !string.IsNullOrEmpty(ov.Reprograma) ? ov.Reprograma : null;
            var prevista = reprog ?? AddMonths(last.DataVenda, meses);
            var dias = DaysBetween(prevista);

            var ib = Base.FirstOrDefault(b => b.EquipmentId == last.EquipmentId);
            var equipCritico = (ib != null && (ib.Criticidade == "crítica" || ib.Criticidade == "alta")) || partCritica;
            var sub = Regex.IsMatch(item, "carca|completo", RegexOptions.IgnoreCase) && dias <= 365 && dias > -365;

            var recent = recs.Skip(Math.Max(0, recs.Count - 2)).ToList();
            var valor = recent.Count > 0 ? recent.Sum(r => r.Valor) / recent.Count : last.Valor;

            var temporal = TemporalFor(dias);
            var urg = UrgencyFor(dias, equipCritico);
            var status = ov != null && !string.IsNullOrEmpty(ov.Status) ? ov.Status : DefaultStatus(dias, urg);

            opps.Add(new Opportunity
            {
                Id = id,
                ClientUnitId = last.ClientUnitId,
                EquipmentId = last.EquipmentId,
                Item = item,
                DataUltimaVenda = last.DataVenda,
                DataPrevista = prevista,
                CicloMeses = meses,
                DiasAteJanela = dias,
                DiasAtraso = dias < 0 ? -dias : 0,
                ValorEstimado = Math.Round(valor),
                Urgencia = urg,
                StatusTemporal = temporal,
                Status = status,
                VendedorId = last.VendedorId,
                Justificativa = $"Última reposição/venda de \"{item}\" em {Fmt.Date(last.DataVenda)}. " +
                                $"Ciclo técnico de troca: {meses} meses (~{(meses / 12.0).ToString("0.0", CultureInfo.InvariantCulture)} anos). " +
                                $"{(reprog != null ? "Janela técnica reprogramada para" : "Janela técnica prevista para")} {Fmt.Date(prevista)}.",
                ProximaAcao = RecommendedAction(dias, item, sub),
                CriticaEquip = equipCritico,
                SugereSubstituicao = sub,
                Reprogramada = reprog != null,
            });
        }

        return opps.OrderBy(o => o.DataPrevista, StringComparer.Ordinal).ToList();
    }

    private static string DefaultStatus(int dias, string urg)
    {
        if (urg == "Crítica") return "Crítica";
        if (dias <= 90) return "Próxima";
        return "Prevista";
    }

    private static string UrgencyFor(int dias, bool equipCritico)
    {
        if (dias < 0) return "Crítica";
        if (equipCritico && dias <= 180) return "Crítica";
        if (dias <= 90) return "Alta";
        if (dias <= 180) return "Média";
        return "Baixa";
    }

    private static string TemporalFor(int dias)
    {
        if (dias < -730) return "histórica";
        if (dias < -365) return "expirada";
        if (dias < 0) return "vencida";
        if (dias <= 90) return "próxima";
        return "futura";
    }

    private static string RecommendedAction(int dias, string item, bool sub)
    {
        if (sub) return "Avaliar substituição do equipamento completo em vez de peça avulsa.";
        if (dias < -730)
            return "Janela histórica — validar necessidade atual. Verificar se houve compra fora do histórico, troca do equipamento ou se a reposição ainda se aplica antes de qualquer abordagem.";
        if (dias < -365)
            return "A janela técnica prevista já foi ultrapassada. Recomenda-se validar se houve compra fora do histórico, substituição do equipamento ou necessidade de nova abordagem comercial.";
        if (dias < 0)
            return $"Janela técnica vencida — priorizar contato comercial imediato e confirmar a reposição de {item}.";
        if (dias <= 90) return $"Agendar ação em campo e enviar proposta técnica de {item}.";
        if (dias <= 180) return "Programar abordagem comercial para a próxima janela de reposição.";
        return "Monitorar — manter no radar de reposição.";
    }

    // ---- alertas ---------------------------------------------------------
    public List<Alert> Alerts(List<Opportunity> opps)
    {
        var done = new HashSet<string> { "Ganha", "Perdida", "Sem interesse" };
        var alerts = new List<Alert>();
        foreach (var o in opps)
        {
            if (done.Contains(o.Status)) continue;
            var cliente = UnitLabel(o.ClientUnitId);
            var modelo = EquipmentModel(o.EquipmentId);

            if (o.Urgencia == "Crítica")
            {
                alerts.Add(new Alert
                {
                    Id = "al-" + o.Id,
                    OpportunityId = o.Id,
                    VendedorId = o.VendedorId,
                    TipoAlerta = o.DiasAteJanela < 0 ? "Janela de reposição vencida" : "Equipamento crítico",
                    Mensagem = $"Alerta preditivo Howden Aftermarket Intelligence: {cliente} possui uma janela crítica " +
                               $"de reposição para {o.Item} no equipamento {modelo}. Última reposição/venda em " +
                               $"{Fmt.Date(o.DataUltimaVenda)}. Janela técnica prevista: {Fmt.Date(o.DataPrevista)}. " +
                               $"Valor potencial técnico: {Fmt.Brl0(o.ValorEstimado)}.",
                    Canal = "painel",
                    StatusEnvio = "pendente",
                    Nivel = "Crítica",
                });
            }
            if (o.SugereSubstituicao)
            {
                alerts.Add(new Alert
                {
                    Id = "al-sub-" + o.Id,
                    OpportunityId = o.Id,
                    VendedorId = o.VendedorId,
                    TipoAlerta = "Substituição de equipamento",
                    Mensagem = $"{cliente}: a janela da carcaça do {modelo} sugere avaliar a venda do equipamento " +
                               "completo em vez de peça avulsa.",
                    Canal = "painel",
                    StatusEnvio = "pendente",
                    Nivel = "Alta",
                });
            }
        }
        return alerts.OrderByDescending(a => Rank(a.Nivel)).ToList();
    }

    private static int Rank(string nivel) => nivel switch
    {
        "Crítica" => 4, "Alta" => 3, "Média" => 2, _ => 1,
    };

    // ---- Central de Inteligência (dashboard) -----------------------------
    // Status considerados "em aberto" (exclui Ganha/Perdida/Sem interesse).
    public static readonly string[] OpenStatuses =
    {
        "Prevista", "Próxima", "Crítica", "Em contato", "Visita agendada",
        "Proposta enviada", "Negociação", "Reprogramada",
    };

    public List<Opportunity> OpenOpportunities() =>
        Opportunities().Where(o => OpenStatuses.Contains(o.Status)).ToList();

    public DashboardVM Dashboard(List<Visit> visits)
    {
        var open = OpenOpportunities();
        var overdue = new HashSet<string> { "vencida", "expirada", "histórica" };

        // equipamentos sem venda há mais de 3 anos (relativo à data-base)
        var lastSale = new Dictionary<string, string>();
        foreach (var s in Sales)
            if (!lastSale.TryGetValue(s.EquipmentId, out var cur) || string.CompareOrdinal(s.DataVenda, cur) > 0)
                lastSale[s.EquipmentId] = s.DataVenda;
        var semVenda3a = lastSale.Values.Count(d => DaysBetween(d) < -365 * 3);

        var eqIds = Base.Select(b => b.EquipmentId).ToHashSet();
        var pecasCriticas = Parts.Count(p => p.Critica && p.Ativo && eqIds.Contains(p.EquipmentId));

        string ClienteDe(string u) => Units.TryGetValue(u, out var x) ? CompanyName(x.CompanyId) : "—";
        string SegDe(string u) => Units.TryGetValue(u, out var x) && !string.IsNullOrEmpty(x.Segmento) ? x.Segmento : "—";
        string TipoDe(string e) => Equipments.TryGetValue(e, out var x) && !string.IsNullOrEmpty(x.Tipo) ? x.Tipo : "—";

        static List<ChartItem> Count<T>(IEnumerable<T> items, Func<T, string> key) =>
            items.GroupBy(key).Select(g => new ChartItem(g.Key, g.Count())).OrderByDescending(c => c.Valor).ToList();

        List<ChartItem> ValueBy(Func<Opportunity, string> key, int top = 8) =>
            open.GroupBy(key).Select(g => new ChartItem(g.Key, Math.Round(g.Sum(o => o.ValorEstimado))))
                .OrderByDescending(c => c.Valor).Take(top).ToList();

        var porMes = open.Where(o => o.DataPrevista.Length >= 7)
            .GroupBy(o => o.DataPrevista[..7])
            .OrderBy(g => g.Key, StringComparer.Ordinal).Take(18)
            .Select(g => new ChartItem(MesLabel(g.Key), g.Count())).ToList();

        var salesByYear = Sales.Where(s => s.DataVenda.Length >= 4)
            .GroupBy(s => s.DataVenda[..4])
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ChartItem(g.Key, Math.Round(g.Sum(s => s.Valor)))).ToList();

        return new DashboardVM
        {
            OportunidadesAbertas = open.Count,
            ValorPotencial = open.Sum(o => o.ValorEstimado),
            CriticasVencidas = open.Count(o => o.Urgencia == "Crítica" || overdue.Contains(o.StatusTemporal)),
            Prox30 = open.Count(o => o.DiasAteJanela >= 0 && o.DiasAteJanela <= 30),
            Prox90 = open.Count(o => o.DiasAteJanela >= 0 && o.DiasAteJanela <= 90),
            SemVenda3a = semVenda3a,
            VisitasAgendadas = visits.Count(v => v.Status == "agendada"),
            PecasCriticas = pecasCriticas,
            PorMes = porMes,
            PorCliente = ValueBy(o => ClienteDe(o.ClientUnitId)),
            PorVendedor = ValueBy(o => SalespersonName(o.VendedorId)),
            PorSegmento = Count(open, o => SegDe(o.ClientUnitId)),
            PorUrgencia = Count(open, o => o.Urgencia),
            PorTipo = Count(Base, b => TipoDe(b.EquipmentId)),
            SalesByYear = salesByYear,
            CriticasTop = open.Where(o => o.Urgencia == "Crítica").Take(6).ToList(),
        };
    }

    private static string MesLabel(string ym)
    {
        var p = ym.Split('-');
        return p.Length == 2 ? $"{p[1]}/{p[0]}" : ym;
    }

    // ---- Previsão de Vendas (forecast) -----------------------------------
    // Confiança por faixa de prazo (mesmos patamares do app original).
    public static int ConfFor(int dias) => dias <= 90 ? 90 : dias <= 180 ? 82 : 64;

    public ForecastVM Forecast()
    {
        var open = OpenOpportunities();
        // Base = janelas previstas nos próximos 12 meses (futuras).
        var baseF = open.Where(o => o.DiasAteJanela >= 0 && o.DiasAteJanela <= 365).ToList();

        decimal Ponderado(IEnumerable<Opportunity> xs) => xs.Sum(o => o.ValorEstimado * ConfFor(o.DiasAteJanela) / 100m);

        var receita = baseF.Sum(o => o.ValorEstimado);
        var ponderadoTotal = Ponderado(baseF);

        var termos = new List<TermRow>();
        void Term(string nome, Func<Opportunity, bool> pred, int conf)
        {
            var xs = baseF.Where(pred).ToList();
            termos.Add(new TermRow(nome, xs.Count, xs.Sum(o => o.ValorEstimado), conf,
                Math.Round(xs.Sum(o => o.ValorEstimado) * conf / 100m)));
        }
        Term("Curto prazo (≤ 90 dias)", o => o.DiasAteJanela <= 90, 90);
        Term("Médio prazo (91–180 dias)", o => o.DiasAteJanela > 90 && o.DiasAteJanela <= 180, 82);
        Term("Longo prazo (> 180 dias)", o => o.DiasAteJanela > 180, 64);

        string ClienteDe(string u) => Units.TryGetValue(u, out var x) ? CompanyName(x.CompanyId) : "—";
        string SegDe(string u) => Units.TryGetValue(u, out var x) && !string.IsNullOrEmpty(x.Segmento) ? x.Segmento : "—";

        List<ChartItem> Val(Func<Opportunity, string> key, int top = 8) =>
            baseF.GroupBy(key).Select(g => new ChartItem(g.Key, Math.Round(g.Sum(o => o.ValorEstimado))))
                 .OrderByDescending(c => c.Valor).Take(top).ToList();

        var projecao = baseF.Where(o => o.DataPrevista.Length >= 7)
            .GroupBy(o => o.DataPrevista[..7]).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ChartItem(MesLabel(g.Key), Math.Round(g.Sum(o => o.ValorEstimado)))).ToList();

        var realizado = Sales.Where(s => s.DataVenda.Length >= 4)
            .GroupBy(s => s.DataVenda[..4]).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ChartItem(g.Key, Math.Round(g.Sum(s => s.Valor)))).ToList();

        var maiores = baseF.OrderByDescending(o => o.ValorEstimado).Take(10).ToList();

        return new ForecastVM
        {
            Receita12m = receita,
            Ponderado = Math.Round(ponderadoTotal),
            Previsto90d = baseF.Where(o => o.DiasAteJanela <= 90).Sum(o => o.ValorEstimado),
            Janelas12m = baseF.Count,
            TicketMedio = baseF.Count > 0 ? Math.Round(receita / baseF.Count) : 0,
            Termos = termos,
            ProjecaoMes = projecao,
            PorSegmento = Val(o => SegDe(o.ClientUnitId)),
            PorVendedor = Val(o => SalespersonName(o.VendedorId)),
            PorCliente = Val(o => ClienteDe(o.ClientUnitId)),
            RealizadoAno = realizado,
            Backtest = RunBacktest(),
            Maiores = maiores,
        };
    }

    // Backtest do modelo de ciclo: para cada série (equip+item) com histórico
    // suficiente, esconde a última venda e prevê a data a partir da anterior.
    private BacktestVM RunBacktest()
    {
        var groups = new Dictionary<string, List<SalesRecord>>();
        foreach (var s in Sales)
        {
            var key = $"{s.EquipmentId}::{NormItem(s.ItemVendido)}";
            if (!groups.TryGetValue(key, out var arr)) { arr = new(); groups[key] = arr; }
            arr.Add(s);
        }

        int series = 0, ok1 = 0, ok3 = 0;
        double somaErro = 0;
        foreach (var recs in groups.Values)
        {
            if (recs.Count < 2) continue;
            recs.Sort((a, b) => string.CompareOrdinal(a.DataVenda, b.DataVenda));
            var prev = recs[^2];
            var atual = recs[^1];
            var (meses, _) = PartCycleFor(prev.EquipmentId, prev.ItemVendido);
            if (!DateTime.TryParse(AddMonths(prev.DataVenda, meses), CultureInfo.InvariantCulture, DateTimeStyles.None, out var previsto)) continue;
            if (!DateTime.TryParse(atual.DataVenda, CultureInfo.InvariantCulture, DateTimeStyles.None, out var real)) continue;
            var erroDias = Math.Abs((previsto - real).TotalDays);
            series++;
            somaErro += erroDias;
            if (erroDias <= 31) ok1++;
            if (erroDias <= 92) ok3++;
        }

        return new BacktestVM
        {
            Series = series,
            Pct1m = series > 0 ? (int)Math.Round(100.0 * ok1 / series) : 0,
            Pct3m = series > 0 ? (int)Math.Round(100.0 * ok3 / series) : 0,
            ErroMedioMeses = series > 0 ? Math.Round(somaErro / series / 30.0, 1) : 0,
        };
    }

    // ---- helpers de data e texto -----------------------------------------
    private static string NormItem(string s) =>
        Regex.Replace(s.Trim().ToLowerInvariant(), @"\s+", " ");

    private static string AddMonths(string iso, int months)
    {
        if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return iso;
        return d.AddMonths(months).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static int DaysBetween(string iso)
    {
        if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return 0;
        return (int)Math.Round((d - Today).TotalDays);
    }

    // Classe CSS de badge por urgência (reaproveita o app.css).
    public static string UrgClass(string urg) => urg switch
    {
        "Crítica" => "st-vencida",
        "Alta" => "st-critica",
        "Média" => "st-urgente",
        _ => "st-ok",
    };
}

// Item genérico para gráficos (nome + valor).
public record ChartItem(string Nome, decimal Valor);

// Dados prontos da Central de Inteligência.
public class DashboardVM
{
    public int OportunidadesAbertas { get; set; }
    public decimal ValorPotencial { get; set; }
    public int CriticasVencidas { get; set; }
    public int Prox30 { get; set; }
    public int Prox90 { get; set; }
    public int SemVenda3a { get; set; }
    public int VisitasAgendadas { get; set; }
    public int PecasCriticas { get; set; }
    public List<ChartItem> PorMes { get; set; } = new();
    public List<ChartItem> PorCliente { get; set; } = new();
    public List<ChartItem> PorVendedor { get; set; } = new();
    public List<ChartItem> PorSegmento { get; set; } = new();
    public List<ChartItem> PorUrgencia { get; set; } = new();
    public List<ChartItem> PorTipo { get; set; } = new();
    public List<ChartItem> SalesByYear { get; set; } = new();
    public List<AfmHsa.Models.Opportunity> CriticasTop { get; set; } = new();
}

// Faixa de prazo da previsão (curto/médio/longo).
public record TermRow(string Faixa, int Janelas, decimal Valor, int ConfiancaPct, decimal Ponderado);

// Backtest do modelo de ciclo.
public class BacktestVM
{
    public int Series { get; set; }
    public int Pct1m { get; set; }
    public int Pct3m { get; set; }
    public double ErroMedioMeses { get; set; }
}

// Dados prontos da Previsão de Vendas.
public class ForecastVM
{
    public decimal Receita12m { get; set; }
    public decimal Ponderado { get; set; }
    public decimal Previsto90d { get; set; }
    public decimal TicketMedio { get; set; }
    public int Janelas12m { get; set; }
    public List<TermRow> Termos { get; set; } = new();
    public List<ChartItem> ProjecaoMes { get; set; } = new();
    public List<ChartItem> PorSegmento { get; set; } = new();
    public List<ChartItem> PorVendedor { get; set; } = new();
    public List<ChartItem> PorCliente { get; set; } = new();
    public List<ChartItem> RealizadoAno { get; set; } = new();
    public BacktestVM Backtest { get; set; } = new();
    public List<AfmHsa.Models.Opportunity> Maiores { get; set; } = new();
}
