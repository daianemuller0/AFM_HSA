using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>
/// As plantas e os equipamentos de uma proposta. A proposta não depende mais
/// da base instalada: a planta pode ser da base ou digitada, pode haver mais
/// de uma, e o equipamento pode ser da planta, de outra planta do cadastro ou
/// não cadastrado. Aqui ficam as regras comuns à tela e ao Word.
/// </summary>
public static class PlantasDaProposta
{
    public static readonly string[] TiposDeEquipamento = { "Ventilador", "Compressor", "Outro" };

    public static string NovoId(string prefixo) => $"{prefixo}-{Guid.NewGuid().ToString("N")[..8]}";

    /// <summary>A planta a partir da unidade cadastrada.</summary>
    public static PlantaDaProposta DaBase(ClientUnit u) => new()
    {
        Id = NovoId("p"), UnitId = u.Id, Nome = (u.NomeUnidade ?? "").Trim(),
        Cidade = (u.Cidade ?? "").Trim(), Estado = (u.Estado ?? "").Trim().ToUpperInvariant(),
    };

    /// <summary>"Usina Tubarão – Vitória/ES"; quando o nome é a própria cidade, "São Luís/MA".</summary>
    public static string Rotulo(PlantaDaProposta p)
    {
        var nome = (p.Nome ?? "").Trim();
        var cidade = (p.Cidade ?? "").Trim();
        var uf = (p.Estado ?? "").Trim();
        var lugar = cidade.Length > 0 && uf.Length > 0 ? $"{cidade}/{uf}" : cidade.Length > 0 ? cidade : uf;
        if (nome.Length == 0 || Textos.Igual(nome, cidade))
            return lugar.Length > 0 ? lugar : nome.Length > 0 ? nome : "planta sem nome";
        return lugar.Length > 0 ? $"{nome} – {lugar}" : nome;
    }

    /// <summary>A cidade da capa: as cidades das plantas, sem repetir ("São Luís / Itabira").</summary>
    public static string CidadesDaCapa(CommercialOffer o) => string.Join(" / ", o.Plantas
        .Select(p => (p.Cidade ?? "").Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));

    /// <summary>A primeira planta cadastrada: dela vêm o vendedor da unidade e o "este cliente" da última venda.</summary>
    public static string UnidadePrincipal(CommercialOffer o) =>
        o.Plantas.FirstOrDefault(p => p.Cadastrada)?.UnitId ?? "";

    public static HashSet<string> Unidades(CommercialOffer o) =>
        o.Plantas.Where(p => p.Cadastrada).Select(p => p.UnitId).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// O equipamento cadastrado entrando na proposta: modelo e tipo do
    /// cadastro; TAG e aplicação da base instalada — da planta escolhida
    /// quando ela é cadastrada, ou do único lugar onde ele está instalado.
    /// </summary>
    public static EquipamentoDaProposta DoCadastro(Equipment e, PlantaDaProposta? planta, IEnumerable<InstalledBase> baseInstalada)
    {
        var onde = baseInstalada.Where(b => b.EquipmentId == e.Id).ToList();
        var daPlanta = planta is { Cadastrada: true } ? onde.Where(b => b.ClientUnitId == planta.UnitId).ToList() : new();
        if (daPlanta.Count == 0 && onde.Select(b => b.ClientUnitId).Distinct().Count() == 1) daPlanta = onde;
        return new EquipamentoDaProposta
        {
            Id = e.Id,
            EquipmentId = e.Id,
            PlantaId = planta?.Id ?? "",
            Tipo = TipoDe(e.Tipo),
            Modelo = (e.Modelo ?? "").Trim().Length > 0 ? e.Modelo.Trim() : (e.Nome ?? "").Trim(),
            Tag = string.Join(", ", daPlanta.Select(b => (b.Tag ?? "").Trim()).Where(t => t.Length > 0).Distinct()),
            Aplicacao = Primeiro(e.Aplicacao, daPlanta.Select(b => b.Process).FirstOrDefault(x => (x ?? "").Trim().Length > 0)),
        };
    }

    /// <summary>O tipo do cadastro na lista da proposta ("VARIAX", "Ventilador centrífugo" → Ventilador).</summary>
    public static string TipoDe(string? tipo)
    {
        var t = Textos.Simples(tipo);
        if (t.Contains("compress")) return "Compressor";
        if (t.Length == 0 || t.Contains("ventil") || t.Contains("fan") || t.Contains("variax") || t.Contains("axial") || t.Contains("centrif"))
            return "Ventilador";
        return (tipo ?? "").Trim();
    }

    /// <summary>
    /// Proposta gravada antes das plantas: monta as duas listas da unidade
    /// (ClientUnitId) e dos equipamentos (EquipmentId e os dos itens). Não mexe
    /// numa proposta que já tem as listas.
    /// </summary>
    public static void Completar(CommercialOffer o, IEnumerable<CommercialOfferItem> itens, IReadOnlyList<ClientUnit> unidades,
        IReadOnlyList<Equipment> equipamentos, IReadOnlyList<InstalledBase> baseInstalada)
    {
        if (o.Plantas.Count == 0 && o.ClientUnitId.Length > 0 && unidades.FirstOrDefault(u => u.Id == o.ClientUnitId) is { } u)
            o.Plantas.Add(DaBase(u));
        if (o.EquipamentosDaProposta.Count > 0) return;

        var planta = o.Plantas.FirstOrDefault();
        var ids = o.EquipmentId.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(itens.Select(i => i.EquipmentId).Where(x => x.Length > 0))
            .Distinct();
        foreach (var id in ids)
            if (equipamentos.FirstOrDefault(e => e.Id == id) is { } e)
                o.EquipamentosDaProposta.Add(DoCadastro(e, planta, baseInstalada));
    }

    /// <summary>O equipamento da proposta a que o item pertence (null = item sem equipamento).</summary>
    public static EquipamentoDaProposta? DoItem(CommercialOffer o, CommercialOfferItem item) =>
        item.EquipmentId.Length == 0 ? null : o.EquipamentosDaProposta.FirstOrDefault(e => e.Id == item.EquipmentId);

    public static PlantaDaProposta? PlantaDe(CommercialOffer o, EquipamentoDaProposta? eq) =>
        eq is null || eq.PlantaId.Length == 0 ? null : o.Plantas.FirstOrDefault(p => p.Id == eq.PlantaId);

    private static string Primeiro(params string?[] valores) =>
        valores.Select(v => (v ?? "").Trim()).FirstOrDefault(v => v.Length > 0) ?? "";
}
