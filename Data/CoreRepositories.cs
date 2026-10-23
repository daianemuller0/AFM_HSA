using AfmHsa.Models;

namespace AfmHsa.Data;

// Repositórios finos por entidade sobre o ParquetStore. Leem a versão
// consolidada (mais recente por id, sem apagados). Colunas em snake_case,
// iguais ao seed/app original.

public class CompanyRepository
{
    private readonly ParquetStore _s;
    public CompanyRepository(ParquetStore s) => _s = s;

    public List<Company> All() => _s.ReadLatest("companies",
        "id, nome, segmento, cidade, estado, regiao",
        r => new Company
        {
            Id = Fmt.S(r, 0), Nome = Fmt.S(r, 1), Segmento = Fmt.S(r, 2),
            Cidade = Fmt.S(r, 3), Estado = Fmt.S(r, 4), Regiao = Fmt.S(r, 5),
        }, orderBy: "nome");

    public void Save(Company c) => _s.WriteRow("companies", new KeyValuePair<string, object?>[]
    {
        new("id", c.Id), new("nome", c.Nome), new("segmento", c.Segmento),
        new("cidade", c.Cidade), new("estado", c.Estado), new("regiao", c.Regiao),
    });
}

public class ClientUnitRepository
{
    private readonly ParquetStore _s;
    public ClientUnitRepository(ParquetStore s) => _s = s;

    public List<ClientUnit> All() => _s.ReadLatest("clientUnits",
        "id, company_id, nome_unidade, cidade, estado, segmento, regiao, vendedor_id, cnpj",
        r => new ClientUnit
        {
            Id = Fmt.S(r, 0), CompanyId = Fmt.S(r, 1), NomeUnidade = Fmt.S(r, 2),
            Cidade = Fmt.S(r, 3), Estado = Fmt.S(r, 4), Segmento = Fmt.S(r, 5),
            Regiao = Fmt.S(r, 6), VendedorId = Fmt.S(r, 7), Cnpj = Fmt.S(r, 8),
        }, orderBy: "nome_unidade");

    public void Save(ClientUnit u) => _s.WriteRow("clientUnits", new KeyValuePair<string, object?>[]
    {
        new("id", u.Id), new("company_id", u.CompanyId), new("nome_unidade", u.NomeUnidade),
        new("cidade", u.Cidade), new("estado", u.Estado), new("segmento", u.Segmento),
        new("regiao", u.Regiao), new("vendedor_id", u.VendedorId), new("cnpj", u.Cnpj),
    });

    public void Delete(string id) => _s.WriteRow("clientUnits",
        new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);
}

/// <summary>
/// Conversão entre a lista de segmentos de um vendedor e a coluna de texto onde ela
/// é guardada (separada por ";"). Um valor único antigo continua válido — vira uma
/// lista de um item só.
/// </summary>
public static class SegmentoTexto
{
    private static readonly char[] Separadores = { ';', '|' };

    public static List<string> Split(string? s) => (s ?? "")
        .Split(Separadores, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>Formato de armazenamento.</summary>
    public static string Join(IEnumerable<string>? segs) => string.Join("; ", Limpar(segs));

    /// <summary>Formato de exibição na tela.</summary>
    public static string Display(IEnumerable<string>? segs)
    {
        var lista = Limpar(segs);
        return lista.Count == 0 ? "—" : string.Join(", ", lista);
    }

    public static bool Contem(IEnumerable<string>? segs, string nome) =>
        segs is not null && segs.Any(s => string.Equals(s?.Trim(), nome?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static List<string> Limpar(IEnumerable<string>? segs) => (segs ?? Enumerable.Empty<string>())
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Select(s => s.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}

public class SalespersonRepository
{
    private readonly ParquetStore _s;
    public SalespersonRepository(ParquetStore s) => _s = s;

    public List<Salesperson> All() => _s.ReadLatest("salespeople",
        "id, nome, email, telefone, cargo, regiao, gestor_id, ativo, perfil, segmento",
        r => new Salesperson
        {
            Id = Fmt.S(r, 0), Nome = Fmt.S(r, 1), Email = Fmt.S(r, 2),
            Telefone = Fmt.S(r, 3), Cargo = Fmt.S(r, 4), Regiao = Fmt.S(r, 5),
            GestorId = Fmt.S(r, 6),
            Ativo = r.IsDBNull(7) ? true : Fmt.Bool(Fmt.S(r, 7)),
            Perfil = Fmt.S(r, 8), Segmentos = SegmentoTexto.Split(Fmt.S(r, 9)),
        }, orderBy: "nome");

    public void Save(Salesperson s) => _s.WriteRow("salespeople", new KeyValuePair<string, object?>[]
    {
        new("id", s.Id), new("nome", s.Nome), new("email", s.Email), new("telefone", s.Telefone),
        new("cargo", s.Cargo), new("regiao", s.Regiao), new("gestor_id", s.GestorId),
        new("ativo", s.Ativo ? "true" : "false"), new("perfil", s.Perfil),
        new("segmento", SegmentoTexto.Join(s.Segmentos)),
    });

    public void Delete(string id) => _s.WriteRow("salespeople",
        new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);
}

/// <summary>Lista de segmentos de mercado — o eixo da divisão comercial.</summary>
public class SegmentRepository
{
    private readonly ParquetStore _s;
    public SegmentRepository(ParquetStore s) => _s = s;

    public List<Segment> All() => _s.ReadLatest("segments",
        "id, nome",
        r => new Segment { Id = Fmt.S(r, 0), Nome = Fmt.S(r, 1) }, orderBy: "nome");

    public void Save(Segment g) => _s.WriteRow("segments", new KeyValuePair<string, object?>[]
    {
        new("id", g.Id), new("nome", g.Nome),
    });

    public void Delete(string id) => _s.WriteRow("segments",
        new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);
}

public class EquipmentRepository
{
    private readonly ParquetStore _s;
    public EquipmentRepository(ParquetStore s) => _s = s;

    public List<Equipment> All() => _s.ReadLatest("equipment",
        "id, doc_num, tipo, modelo, nome, aplicacao, segmento, fabricante, valor_completo",
        r => new Equipment
        {
            Id = Fmt.S(r, 0), DocNum = Fmt.S(r, 1), Tipo = Fmt.S(r, 2),
            Modelo = Fmt.S(r, 3), Nome = Fmt.S(r, 4), Aplicacao = Fmt.S(r, 5),
            Segmento = Fmt.S(r, 6), Fabricante = Fmt.S(r, 7),
            ValorCompleto = Fmt.Dec(Fmt.S(r, 8)),
        }, orderBy: "nome");

    public void Save(Equipment e) => _s.WriteRow("equipment", new KeyValuePair<string, object?>[]
    {
        new("id", e.Id), new("doc_num", e.DocNum), new("tipo", e.Tipo), new("modelo", e.Modelo),
        new("nome", e.Nome), new("aplicacao", e.Aplicacao), new("segmento", e.Segmento),
        new("fabricante", e.Fabricante),
        new("valor_completo", e.ValorCompleto.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    });

    public void Delete(string id) => _s.WriteRow("equipment",
        new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);
}

public class InstalledBaseRepository
{
    private readonly ParquetStore _s;
    public InstalledBaseRepository(ParquetStore s) => _s = s;

    public List<InstalledBase> All() => _s.ReadLatest("installedBase",
        "id, client_unit_id, equipment_id, quantidade, ano_instalacao, criticidade, status, vendedor_id, " +
        "hp_fan_reference_id, plant_name, city, state, country, industry, process, site_unit, product_type, " +
        "brand, model_family, designation, contract_no, serial_no, client_ref_no, ga_drawing_no, application_type, " +
        "fans_per_boiler, product_company, install_year, operating_status, end_customer, client, client_country, " +
        "project_name, ref_no, agent",
        r => new InstalledBase
        {
            Id = Fmt.S(r, 0), ClientUnitId = Fmt.S(r, 1), EquipmentId = Fmt.S(r, 2),
            Quantidade = Fmt.Int(Fmt.S(r, 3)), AnoInstalacao = Fmt.Int(Fmt.S(r, 4)),
            Criticidade = Fmt.S(r, 5), Status = Fmt.S(r, 6), VendedorId = Fmt.S(r, 7),
            HpFanReferenceId = Fmt.S(r, 8), PlantName = Fmt.S(r, 9), City = Fmt.S(r, 10),
            State = Fmt.S(r, 11), Country = Fmt.S(r, 12), Industry = Fmt.S(r, 13),
            Process = Fmt.S(r, 14), SiteUnit = Fmt.S(r, 15), ProductType = Fmt.S(r, 16),
            Brand = Fmt.S(r, 17), ModelFamily = Fmt.S(r, 18), Designation = Fmt.S(r, 19),
            ContractNo = Fmt.S(r, 20), SerialNo = Fmt.S(r, 21), ClientRefNo = Fmt.S(r, 22),
            GaDrawingNo = Fmt.S(r, 23), ApplicationType = Fmt.S(r, 24), FansPerBoiler = Fmt.S(r, 25),
            ProductCompany = Fmt.S(r, 26), InstallYear = Fmt.S(r, 27), OperatingStatus = Fmt.S(r, 28),
            EndCustomer = Fmt.S(r, 29), Client = Fmt.S(r, 30), ClientCountry = Fmt.S(r, 31),
            ProjectName = Fmt.S(r, 32), RefNo = Fmt.S(r, 33), Agent = Fmt.S(r, 34),
        });

    /// <summary>Exclui uma referência da base (marcação lógica, igual aos demais cadastros).</summary>
    public void Delete(string id) => _s.WriteRow("installedBase",
        new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);

    /// <summary>
    /// Cadastra/atualiza um equipamento instalado numa planta. Além dos campos usados pelo
    /// motor de oportunidades, preenche as colunas da planilha-mestre que dá para derivar
    /// da planta e do equipamento, para o registro aparecer completo na Base Instalada.
    /// </summary>
    public void Save(InstalledBase b) => _s.WriteRow("installedBase", new KeyValuePair<string, object?>[]
    {
        new("id", b.Id),
        new("client_unit_id", b.ClientUnitId), new("equipment_id", b.EquipmentId),
        new("quantidade", b.Quantidade.ToString()), new("ano_instalacao", b.AnoInstalacao.ToString()),
        new("criticidade", b.Criticidade), new("status", b.Status), new("vendedor_id", b.VendedorId),
        new("plant_name", b.PlantName), new("city", b.City), new("state", b.State),
        new("client", b.Client), new("industry", b.Industry), new("site_unit", b.SiteUnit),
        new("product_type", b.ProductType), new("model_family", b.ModelFamily),
        new("brand", b.Brand), new("application_type", b.ApplicationType),
        new("fans_per_boiler", b.FansPerBoiler), new("install_year", b.InstallYear),
        new("operating_status", b.OperatingStatus),
    });

    /// <summary>Exclui de uma vez todas as referências de uma planta (um único arquivo).</summary>
    public void DeleteByUnit(string clientUnitId)
    {
        var ids = All().Where(b => b.ClientUnitId == clientUnitId).Select(b => b.Id).ToList();
        if (ids.Count == 0) return;
        var linhas = ids
            .Select(id => (IReadOnlyList<KeyValuePair<string, object?>>)
                new KeyValuePair<string, object?>[] { new("id", id) })
            .ToList();
        _s.WriteBatch("installedBase", linhas, deleted: true);
    }

    /// <summary>
    /// Apaga TODA a base instalada. Depois de limpar, grava um marcador apagado para que
    /// a entidade não fique "vazia" — senão o seed de demonstração repovoaria a base na
    /// próxima inicialização do app. O marcador não aparece em nenhuma consulta.
    /// </summary>
    public void ClearAll()
    {
        _s.Clear("installedBase");
        _s.WriteRow("installedBase",
            new KeyValuePair<string, object?>[] { new("id", "__base_limpa__") }, deleted: true);
    }
}

public class PartRepository
{
    private readonly ParquetStore _s;
    public PartRepository(ParquetStore s) => _s = s;

    public List<Part> All() => _s.ReadLatest("parts",
        "id, equipment_id, nome, ciclo_meses, critica, descritivo, codigo, valor_estimado, vender_junto, peca_complementar, observacao, ativo",
        r => new Part
        {
            Id = Fmt.S(r, 0), EquipmentId = Fmt.S(r, 1), Nome = Fmt.S(r, 2),
            CicloMeses = Fmt.Int(Fmt.S(r, 3)), Critica = Fmt.Bool(Fmt.S(r, 4)),
            Descritivo = Fmt.S(r, 5), Codigo = Fmt.S(r, 6),
            ValorEstimado = Fmt.Dec(Fmt.S(r, 7)), VenderJunto = Fmt.Bool(Fmt.S(r, 8)),
            PecaComplementar = Fmt.S(r, 9), Observacao = Fmt.S(r, 10),
            Ativo = r.IsDBNull(11) ? true : Fmt.Bool(Fmt.S(r, 11)),
        }, orderBy: "nome");
}

public class SalesRecordRepository
{
    private readonly ParquetStore _s;
    public SalesRecordRepository(ParquetStore s) => _s = s;

    public List<SalesRecord> All() => _s.ReadLatest("salesHistory",
        "id, data_venda, pedido, client_unit_id, equipment_id, item_vendido, quantidade, valor, vendedor_id, origem",
        r => new SalesRecord
        {
            Id = Fmt.S(r, 0), DataVenda = Fmt.S(r, 1), Pedido = Fmt.S(r, 2),
            ClientUnitId = Fmt.S(r, 3), EquipmentId = Fmt.S(r, 4), ItemVendido = Fmt.S(r, 5),
            Quantidade = Fmt.Int(Fmt.S(r, 6)), Valor = Fmt.Dec(Fmt.S(r, 7)),
            VendedorId = Fmt.S(r, 8), Origem = Fmt.S(r, 9),
        }, orderBy: "data_venda DESC");
}

public class AppUserRepository
{
    private readonly ParquetStore _s;
    public AppUserRepository(ParquetStore s) => _s = s;

    public List<AppUser> All() => _s.ReadLatest("users",
        "id, nome, email, password, perfil, telefone, salesperson_id",
        r => new AppUser
        {
            Id = Fmt.S(r, 0), Nome = Fmt.S(r, 1), Email = Fmt.S(r, 2),
            Password = Fmt.S(r, 3), Perfil = Fmt.S(r, 4), Telefone = Fmt.S(r, 5),
            SalespersonId = Fmt.S(r, 6),
        }, orderBy: "nome");
}
