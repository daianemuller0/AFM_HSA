namespace AfmHsa.Models;

// Entidades centrais da base industrial — mesmos campos do app original
// (src/lib/types.ts do projeto Leonardo). Persistidas em Parquet com os mesmos
// nomes de coluna (snake_case), preservando o formato do sistema de origem.

// companies
public class Company
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Segmento { get; set; } = "";
    public string Cidade { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Regiao { get; set; } = "";
}

// clientUnits
public class ClientUnit
{
    public string Id { get; set; } = "";
    public string CompanyId { get; set; } = "";
    public string NomeUnidade { get; set; } = "";
    public string Cidade { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Segmento { get; set; } = "";
    public string Regiao { get; set; } = "";
    public string VendedorId { get; set; } = "";
}

// salespeople
public class Salesperson
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Email { get; set; } = "";
    public string Telefone { get; set; } = "";
    public string Cargo { get; set; } = "";
    public string Regiao { get; set; } = "";
    public string GestorId { get; set; } = "";
    public bool Ativo { get; set; } = true;
    public string Perfil { get; set; } = "";
}

// equipment
public class Equipment
{
    public string Id { get; set; } = "";
    public string DocNum { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Modelo { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Aplicacao { get; set; } = "";
    public string Segmento { get; set; } = "";
    public string Fabricante { get; set; } = "";
    public decimal ValorCompleto { get; set; }
}

// installedBase
public class InstalledBase
{
    public string Id { get; set; } = "";

    // --- Campos usados pelo motor (oportunidades, dashboards, etc.) --------
    // Preenchidos a partir da planilha na importação; mantidos para não
    // quebrar o restante do sistema.
    public string ClientUnitId { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public int Quantidade { get; set; }
    public int AnoInstalacao { get; set; }
    public string Criticidade { get; set; } = "";
    public string Status { get; set; } = "";
    public string VendedorId { get; set; } = "";

    // --- Colunas da planilha-mestre da Howden (HP Fan References) ----------
    // Exibidas na aba Base Instalada, na mesma ordem da planilha.
    public string HpFanReferenceId { get; set; } = "";
    public string PlantName { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Country { get; set; } = "";
    public string Industry { get; set; } = "";
    public string Process { get; set; } = "";
    public string SiteUnit { get; set; } = "";
    public string ProductType { get; set; } = "";
    public string Brand { get; set; } = "";
    public string ModelFamily { get; set; } = "";
    public string Designation { get; set; } = "";
    public string ContractNo { get; set; } = "";
    public string SerialNo { get; set; } = "";
    public string ClientRefNo { get; set; } = "";
    public string GaDrawingNo { get; set; } = "";
    public string ApplicationType { get; set; } = "";
    public string FansPerBoiler { get; set; } = "";
    public string ProductCompany { get; set; } = "";
    public string InstallYear { get; set; } = "";
    public string OperatingStatus { get; set; } = "";
    public string EndCustomer { get; set; } = "";
    public string Client { get; set; } = "";
    public string ClientCountry { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string RefNo { get; set; } = "";
    public string Agent { get; set; } = "";
}

// parts
public class Part
{
    public string Id { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public string Nome { get; set; } = "";
    public int CicloMeses { get; set; }
    public bool Critica { get; set; }
    public string Descritivo { get; set; } = "";
    public string Codigo { get; set; } = "";
    public decimal ValorEstimado { get; set; }
    public bool VenderJunto { get; set; }
    public string PecaComplementar { get; set; } = "";
    public string Observacao { get; set; } = "";
    public bool Ativo { get; set; } = true;
}

// salesHistory
public class SalesRecord
{
    public string Id { get; set; } = "";
    public string DataVenda { get; set; } = "";
    public string Pedido { get; set; } = "";
    public string ClientUnitId { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public string ItemVendido { get; set; } = "";
    public int Quantidade { get; set; }
    public decimal Valor { get; set; }
    public string VendedorId { get; set; } = "";
    public string Origem { get; set; } = "";
}

// users
public class AppUser
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string Perfil { get; set; } = "";
    public string Telefone { get; set; } = "";
    public string SalespersonId { get; set; } = "";
}
