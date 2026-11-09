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
    /// <summary>O sistema é de compressores: o equipamento da proposta é um compressor.</summary>
    public const string Compressor = "Compressor";

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
    /// A ficha do equipamento cadastrado para a proposta: serial number, TAG e
    /// aplicação. Serial e TAG vêm da base instalada — da instalação na planta
    /// do equipamento; senão de uma das plantas cadastradas da proposta; senão
    /// da planta com o mesmo nome ou cidade; senão do único lugar onde ele
    /// está instalado. A aplicação vem do cadastro do equipamento (ou do
    /// processo / tipo de aplicação da instalação).
    /// </summary>
    public static (string Serial, string Tag, string Aplicacao) Ficha(Equipment? e, string equipmentId, PlantaDaProposta? planta,
        IEnumerable<PlantaDaProposta> plantas, IEnumerable<InstalledBase> baseInstalada)
    {
        var onde = baseInstalada.Where(b => b.EquipmentId == equipmentId).ToList();
        var sel = planta is { Cadastrada: true } ? onde.Where(b => b.ClientUnitId == planta.UnitId).ToList() : new();
        if (sel.Count == 0)
        {
            var unidades = plantas.Where(p => p.Cadastrada).Select(p => p.UnitId).ToHashSet();
            sel = onde.Where(b => unidades.Contains(b.ClientUnitId)).ToList();
        }
        if (sel.Count == 0 && planta is not null)
        {
            var nome = Textos.Simples(planta.Nome);
            var cidade = Textos.Simples(planta.Cidade);
            sel = onde.Where(b => (nome.Length > 2 && Textos.Simples(b.PlantName).Contains(nome))
                                  || (cidade.Length > 2 && Textos.Simples(b.City) == cidade)).ToList();
        }
        if (sel.Count == 0 && onde.Select(b => b.ClientUnitId).Distinct().Count() == 1) sel = onde;

        static string Juntar(IEnumerable<string?> v) =>
            string.Join(", ", v.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
        var aplicacao = Primeiro(e?.Aplicacao,
            sel.Select(b => b.Process).FirstOrDefault(x => (x ?? "").Trim().Length > 0),
            sel.Select(b => b.ApplicationType).FirstOrDefault(x => (x ?? "").Trim().Length > 0),
            onde.Select(b => b.Process).Where(x => (x ?? "").Trim().Length > 0).Distinct().Count() == 1 ? onde.First(b => (b.Process ?? "").Trim().Length > 0).Process : "");
        return (Juntar(sel.Select(b => b.SerialNo)), Juntar(sel.Select(b => b.Tag)), aplicacao);
    }

    /// <summary>
    /// Preenche serial, TAG e aplicação do equipamento da proposta pela ficha.
    /// Sem <paramref name="sobrescrever"/>, só os campos vazios (o digitado fica).
    /// </summary>
    public static bool PreencherDaFicha(EquipamentoDaProposta eq, Equipment? e, IEnumerable<PlantaDaProposta> plantas,
        IEnumerable<InstalledBase> baseInstalada, bool sobrescrever)
    {
        if (!eq.Cadastrado || eq.EquipmentId.Length == 0) return false;
        var planta = plantas.FirstOrDefault(p => p.Id == eq.PlantaId);
        var (serial, tag, aplicacao) = Ficha(e, eq.EquipmentId, planta, plantas, baseInstalada);
        var mudou = false;
        void Por(Func<string> atual, Action<string> por, string novo)
        {
            if (novo.Length == 0 || (!sobrescrever && atual().Trim().Length > 0) || atual() == novo) return;
            por(novo);
            mudou = true;
        }
        Por(() => eq.SerialNumber, v => eq.SerialNumber = v, serial);
        Por(() => eq.Tag, v => eq.Tag = v, tag);
        Por(() => eq.Aplicacao, v => eq.Aplicacao = v, aplicacao);
        return mudou;
    }

    /// <summary>O equipamento cadastrado entrando na proposta: modelo e tipo do cadastro; serial, TAG e aplicação da ficha.</summary>
    public static EquipamentoDaProposta DoCadastro(Equipment e, PlantaDaProposta? planta, IEnumerable<InstalledBase> baseInstalada,
        IEnumerable<PlantaDaProposta>? plantas = null)
    {
        var (serial, tag, aplicacao) = Ficha(e, e.Id, planta, plantas ?? (planta is null ? Array.Empty<PlantaDaProposta>() : new[] { planta }), baseInstalada);
        return new EquipamentoDaProposta
        {
            Id = e.Id,
            EquipmentId = e.Id,
            PlantaId = planta?.Id ?? "",
            Tipo = Compressor,
            Modelo = (e.Modelo ?? "").Trim() is { Length: > 0 } modelo ? modelo : (e.Nome ?? "").Trim(),
            Tag = tag,
            SerialNumber = serial,
            Aplicacao = aplicacao,
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
                o.EquipamentosDaProposta.Add(DoCadastro(e, planta, baseInstalada, o.Plantas));
    }

    /// <summary>O equipamento da proposta a que o item pertence (null = item sem equipamento).</summary>
    public static EquipamentoDaProposta? DoItem(CommercialOffer o, CommercialOfferItem item) =>
        item.EquipmentId.Length == 0 ? null : o.EquipamentosDaProposta.FirstOrDefault(e => e.Id == item.EquipmentId);

    public static PlantaDaProposta? PlantaDe(CommercialOffer o, EquipamentoDaProposta? eq) =>
        eq is null || eq.PlantaId.Length == 0 ? null : o.Plantas.FirstOrDefault(p => p.Id == eq.PlantaId);

    private static string Primeiro(params string?[] valores) =>
        valores.Select(v => (v ?? "").Trim()).FirstOrDefault(v => v.Length > 0) ?? "";
}
