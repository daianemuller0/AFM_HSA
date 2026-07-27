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
}

public class ClientUnitRepository
{
    private readonly ParquetStore _s;
    public ClientUnitRepository(ParquetStore s) => _s = s;

    public List<ClientUnit> All() => _s.ReadLatest("clientUnits",
        "id, company_id, nome_unidade, cidade, estado, segmento, regiao, vendedor_id",
        r => new ClientUnit
        {
            Id = Fmt.S(r, 0), CompanyId = Fmt.S(r, 1), NomeUnidade = Fmt.S(r, 2),
            Cidade = Fmt.S(r, 3), Estado = Fmt.S(r, 4), Segmento = Fmt.S(r, 5),
            Regiao = Fmt.S(r, 6), VendedorId = Fmt.S(r, 7),
        }, orderBy: "nome_unidade");
}

public class SalespersonRepository
{
    private readonly ParquetStore _s;
    public SalespersonRepository(ParquetStore s) => _s = s;

    public List<Salesperson> All() => _s.ReadLatest("salespeople",
        "id, nome, email, telefone, cargo, regiao, gestor_id, ativo, perfil",
        r => new Salesperson
        {
            Id = Fmt.S(r, 0), Nome = Fmt.S(r, 1), Email = Fmt.S(r, 2),
            Telefone = Fmt.S(r, 3), Cargo = Fmt.S(r, 4), Regiao = Fmt.S(r, 5),
            GestorId = Fmt.S(r, 6),
            Ativo = r.IsDBNull(7) ? true : Fmt.Bool(Fmt.S(r, 7)),
            Perfil = Fmt.S(r, 8),
        }, orderBy: "nome");

    public void Save(Salesperson s) => _s.WriteRow("salespeople", new KeyValuePair<string, object?>[]
    {
        new("id", s.Id), new("nome", s.Nome), new("email", s.Email), new("telefone", s.Telefone),
        new("cargo", s.Cargo), new("regiao", s.Regiao), new("gestor_id", s.GestorId),
        new("ativo", s.Ativo ? "true" : "false"), new("perfil", s.Perfil),
    });
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
}

public class InstalledBaseRepository
{
    private readonly ParquetStore _s;
    public InstalledBaseRepository(ParquetStore s) => _s = s;

    public List<InstalledBase> All() => _s.ReadLatest("installedBase",
        "id, client_unit_id, equipment_id, quantidade, ano_instalacao, criticidade, status, vendedor_id",
        r => new InstalledBase
        {
            Id = Fmt.S(r, 0), ClientUnitId = Fmt.S(r, 1), EquipmentId = Fmt.S(r, 2),
            Quantidade = Fmt.Int(Fmt.S(r, 3)), AnoInstalacao = Fmt.Int(Fmt.S(r, 4)),
            Criticidade = Fmt.S(r, 5), Status = Fmt.S(r, 6), VendedorId = Fmt.S(r, 7),
        });
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
