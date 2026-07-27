using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace AfmHsa.Data;

public class ImportResult
{
    public bool Ok { get; set; }
    public int Count { get; set; }
    public string Message { get; set; } = "";
}

// Importa planilhas .xlsx para as entidades da base (o "motor" que alimenta o
// sistema). Mapeia por cabeçalho (tolerante a acentos/maiúsculas) e resolve
// referências (cliente/unidade/equipamento/vendedor) por nome quando possível.
public class ImportService
{
    private readonly ParquetStore _store;
    private readonly ClientUnitRepository _units;
    private readonly EquipmentRepository _equip;
    private readonly SalespersonRepository _sp;
    private readonly CompanyRepository _companies;

    public ImportService(ParquetStore store, ClientUnitRepository units, EquipmentRepository equip,
        SalespersonRepository sp, CompanyRepository companies)
    {
        _store = store; _units = units; _equip = equip; _sp = sp; _companies = companies;
    }

    public ImportResult Import(string tipo, byte[] bytes, bool substituir)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            var ws = wb.Worksheets.First();
            var header = ws.FirstRowUsed();
            if (header == null) return new ImportResult { Ok = false, Message = "Planilha vazia." };

            // mapa cabeçalho normalizado -> número da coluna
            var cols = new Dictionary<string, int>();
            foreach (var cell in header.CellsUsed())
                cols[Norm(cell.GetString())] = cell.Address.ColumnNumber;

            var dataRows = ws.RowsUsed().Skip(1).ToList();

            return tipo switch
            {
                "vendedores" => ImportVendedores(dataRows, cols, substituir),
                "construtivos" => ImportConstrutivos(dataRows, cols, substituir),
                "historico" => ImportHistorico(dataRows, cols, substituir),
                "base" => ImportBase(dataRows, cols, substituir),
                _ => new ImportResult { Ok = false, Message = "Tipo desconhecido." },
            };
        }
        catch (Exception ex)
        {
            return new ImportResult { Ok = false, Message = "Erro ao ler o arquivo: " + ex.Message };
        }
    }

    // ---- por tipo --------------------------------------------------------
    private ImportResult ImportVendedores(List<IXLRow> rows, Dictionary<string, int> c, bool sub)
    {
        var outRows = new List<IReadOnlyList<KeyValuePair<string, object?>>>();
        foreach (var r in rows)
        {
            var nome = Get(r, c, "nome", "vendedor");
            if (string.IsNullOrWhiteSpace(nome)) continue;
            outRows.Add(Row(
                ("id", "sp-" + Guid.NewGuid().ToString("N")[..8]),
                ("nome", nome),
                ("email", Get(r, c, "email", "e_mail")),
                ("telefone", Get(r, c, "telefone", "fone")),
                ("cargo", Def(Get(r, c, "cargo"), "Vendedor")),
                ("regiao", Get(r, c, "regiao", "regiao_geografica")),
                ("gestor_id", ""),
                ("ativo", "true"),
                ("perfil", Def(Get(r, c, "perfil"), "vendedor"))));
        }
        return Write("salespeople", outRows, sub);
    }

    private ImportResult ImportConstrutivos(List<IXLRow> rows, Dictionary<string, int> c, bool sub)
    {
        var eqByModelo = _equip.All().ToDictionary(e => Norm(e.Modelo), e => e.Id);
        var outRows = new List<IReadOnlyList<KeyValuePair<string, object?>>>();
        foreach (var r in rows)
        {
            var item = Get(r, c, "item", "item_name", "peca");
            if (string.IsNullOrWhiteSpace(item)) continue;
            var modelo = Get(r, c, "modelo_do_equipamento", "equipamento", "modelo");
            eqByModelo.TryGetValue(Norm(modelo), out var eqId);
            outRows.Add(Row(
                ("id", "cd-" + Guid.NewGuid().ToString("N")[..8]),
                ("equipment_id", eqId ?? modelo),
                ("item_name", item),
                ("weight_kg", Get(r, c, "peso_kg", "peso")),
                ("dimensions", Get(r, c, "dimensoes", "dimensao")),
                ("material", Get(r, c, "material")),
                ("equipment_part", Get(r, c, "parte_do_equipamento", "parte")),
                ("item_model", Get(r, c, "modelo_do_item", "modelo_item"))));
        }
        return Write("constructionData", outRows, sub);
    }

    private ImportResult ImportHistorico(List<IXLRow> rows, Dictionary<string, int> c, bool sub)
    {
        var unitById = _units.All();
        var unitByName = unitById.GroupBy(u => Norm(u.NomeUnidade)).ToDictionary(g => g.Key, g => g.First().Id);
        var eqByModelo = _equip.All().ToDictionary(e => Norm(e.Modelo), e => e.Id);
        var eqByDoc = _equip.All().GroupBy(e => Norm(e.DocNum)).ToDictionary(g => g.Key, g => g.First().Id);
        var spByName = _sp.All().GroupBy(s => Norm(s.Nome)).ToDictionary(g => g.Key, g => g.First().Id);

        var outRows = new List<IReadOnlyList<KeyValuePair<string, object?>>>();
        foreach (var r in rows)
        {
            var item = Get(r, c, "item_vendido", "item", "peca");
            var data = GetDate(r, c, "data_venda", "data");
            if (string.IsNullOrWhiteSpace(item) && string.IsNullOrWhiteSpace(data)) continue;

            var unidade = Get(r, c, "unidade", "cliente");
            unitByName.TryGetValue(Norm(unidade), out var unitId);
            var equipamento = Get(r, c, "equipamento", "tag_equipamento", "modelo");
            if (!eqByModelo.TryGetValue(Norm(equipamento), out var eqId))
                eqByDoc.TryGetValue(Norm(equipamento), out eqId);
            var vendedor = Get(r, c, "vendedor");
            spByName.TryGetValue(Norm(vendedor), out var spId);

            outRows.Add(Row(
                ("id", "sl-" + Guid.NewGuid().ToString("N")[..8]),
                ("data_venda", data),
                ("pedido", Get(r, c, "pedido")),
                ("client_unit_id", unitId ?? unidade),
                ("equipment_id", eqId ?? equipamento),
                ("item_vendido", item),
                ("quantidade", Def(Get(r, c, "quantidade", "qtd"), "1")),
                ("valor", NumStr(Get(r, c, "valor"))),
                ("vendedor_id", spId ?? vendedor),
                ("origem", "Importação Excel")));
        }
        return Write("salesHistory", outRows, sub);
    }

    // Importa a planilha-mestre da Howden (HP Fan References, 27 colunas em
    // inglês). Guarda todas as colunas para exibição na aba Base Instalada e
    // deriva os campos que o motor de oportunidades precisa.
    private ImportResult ImportBase(List<IXLRow> rows, Dictionary<string, int> c, bool sub)
    {
        var unitByName = _units.All().GroupBy(u => Norm(u.NomeUnidade)).ToDictionary(g => g.Key, g => g.First().Id);
        var eqByModelo = _equip.All().ToDictionary(e => Norm(e.Modelo), e => e.Id);
        var spByName = _sp.All().GroupBy(s => Norm(s.Nome)).ToDictionary(g => g.Key, g => g.First().Id);

        var novoEquip = new List<IReadOnlyList<KeyValuePair<string, object?>>>();
        var outRows = new List<IReadOnlyList<KeyValuePair<string, object?>>>();
        int novoDoc = _equip.All().Count;

        foreach (var r in rows)
        {
            // Colunas da planilha (aceita variações de normalização do cabeçalho).
            var hpId       = Get(r, c, "hpfanreferencesid", "hp_fan_reference_id", "hpfanreferenceid");
            var plantName  = Get(r, c, "plant_name", "plantname");
            var city       = Get(r, c, "city");
            var state      = Get(r, c, "state");
            var country    = Get(r, c, "country");
            var industry   = Get(r, c, "industry");
            var process    = Get(r, c, "process");
            var siteUnit   = Get(r, c, "site_loc__unit", "site_loc_unit", "site_unit", "unidade");
            var prodType   = Get(r, c, "product_type", "producttype");
            var brand      = Get(r, c, "brand");
            var modelFam   = Get(r, c, "model__family", "model_family", "modelfamily", "modelo", "model");
            var designation= Get(r, c, "designation");
            var contractNo = Get(r, c, "contract_no", "contractno");
            var serialNo   = Get(r, c, "serial_no", "serialno");
            var clientRef  = Get(r, c, "clientrefno", "client_ref_no");
            var gaDrawing  = Get(r, c, "ga_drawing_no", "gadrawingno");
            var appType    = Get(r, c, "applicationtype", "application_type");
            var fansBoiler = Get(r, c, "fansperboiler", "fans_per_boiler");
            var prodCo     = Get(r, c, "product_company", "productcompany");
            var installYr  = Get(r, c, "install_year", "installyear", "ano");
            var opStatus   = Get(r, c, "operating_status", "operatingstatus", "status");
            var endCust    = Get(r, c, "endcustomer", "end_customer");
            var client     = Get(r, c, "client");
            var clientCtry = Get(r, c, "client_country", "clientcountry");
            var projName   = Get(r, c, "projectname", "project_name");
            var refNo      = Get(r, c, "refno", "ref_no");
            var agent      = Get(r, c, "agent");

            // Linha considerada válida se tiver ao menos modelo, planta ou serial.
            if (string.IsNullOrWhiteSpace(modelFam) && string.IsNullOrWhiteSpace(plantName)
                && string.IsNullOrWhiteSpace(serialNo)) continue;

            // Equipamento: casa por modelo (família) ou cria um novo.
            var chaveModelo = Norm(string.IsNullOrWhiteSpace(modelFam) ? designation : modelFam);
            if (!eqByModelo.TryGetValue(chaveModelo, out var eqId))
            {
                eqId = "eq-" + Guid.NewGuid().ToString("N")[..8];
                eqByModelo[chaveModelo] = eqId;
                novoDoc++;
                novoEquip.Add(Row(
                    ("id", eqId),
                    ("doc_num", novoDoc.ToString("D2")),
                    ("tipo", prodType),
                    ("modelo", string.IsNullOrWhiteSpace(modelFam) ? designation : modelFam),
                    ("nome", Def($"{prodType} {modelFam}".Trim(), designation)),
                    ("aplicacao", appType),
                    ("segmento", Def(industry, "Indústria")),
                    ("fabricante", Def(brand, "Howden")),
                    ("valor_completo", "0")));
            }

            // Unidade/cliente: resolve por nome (planta) quando existir na base.
            var unidadeNome = Def(plantName, Def(siteUnit, client));
            unitByName.TryGetValue(Norm(unidadeNome), out var unitId);

            // Vendedor: tenta casar pelo agente.
            spByName.TryGetValue(Norm(agent), out var spId);

            outRows.Add(Row(
                ("id", "ib-" + Guid.NewGuid().ToString("N")[..8]),
                // campos do motor (derivados)
                ("client_unit_id", unitId ?? unidadeNome),
                ("equipment_id", eqId),
                ("quantidade", Def(fansBoiler, "1")),
                ("ano_instalacao", installYr),
                ("criticidade", "média"),
                ("status", Def(opStatus, "ativo")),
                ("vendedor_id", spId ?? ""),
                // colunas da planilha (exibição)
                ("hp_fan_reference_id", hpId),
                ("plant_name", plantName),
                ("city", city),
                ("state", state),
                ("country", country),
                ("industry", industry),
                ("process", process),
                ("site_unit", siteUnit),
                ("product_type", prodType),
                ("brand", brand),
                ("model_family", modelFam),
                ("designation", designation),
                ("contract_no", contractNo),
                ("serial_no", serialNo),
                ("client_ref_no", clientRef),
                ("ga_drawing_no", gaDrawing),
                ("application_type", appType),
                ("fans_per_boiler", fansBoiler),
                ("product_company", prodCo),
                ("install_year", installYr),
                ("operating_status", opStatus),
                ("end_customer", endCust),
                ("client", client),
                ("client_country", clientCtry),
                ("project_name", projName),
                ("ref_no", refNo),
                ("agent", agent)));
        }

        if (novoEquip.Count > 0) _store.WriteBatch("equipment", novoEquip);
        return Write("installedBase", outRows, sub);
    }

    // ---- infra -----------------------------------------------------------
    private ImportResult Write(string entity, List<IReadOnlyList<KeyValuePair<string, object?>>> rows, bool sub)
    {
        if (rows.Count == 0) return new ImportResult { Ok = false, Message = "Nenhuma linha válida encontrada." };
        if (sub) _store.Clear(entity);
        _store.WriteBatch(entity, rows);
        return new ImportResult { Ok = true, Count = rows.Count, Message = $"{rows.Count} registro(s) importado(s)." };
    }

    private static IReadOnlyList<KeyValuePair<string, object?>> Row(params (string k, object? v)[] pairs) =>
        pairs.Select(p => new KeyValuePair<string, object?>(p.k, p.v)).ToList();

    private static string Get(IXLRow r, Dictionary<string, int> cols, params string[] names)
    {
        foreach (var n in names)
            if (cols.TryGetValue(n, out var col))
            {
                var cell = r.Cell(col);
                if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var dt))
                    return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return cell.GetString().Trim();
            }
        return "";
    }

    private static string GetDate(IXLRow r, Dictionary<string, int> cols, params string[] names)
    {
        var s = Get(r, cols, names);
        if (DateTime.TryParse(s, new CultureInfo("pt-BR"), DateTimeStyles.None, out var d) ||
            DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return s;
    }

    private static string NumStr(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "0";
        s = s.Replace("R$", "").Trim();
        // "1.234,56" -> "1234.56"
        if (s.Contains(',') && s.Contains('.')) s = s.Replace(".", "").Replace(",", ".");
        else if (s.Contains(',')) s = s.Replace(",", ".");
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d.ToString(CultureInfo.InvariantCulture) : "0";
    }

    private static string Def(string s, string fallback) => string.IsNullOrWhiteSpace(s) ? fallback : s;

    private static string Norm(string s)
    {
        s = (s ?? "").Trim().ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        var t = sb.ToString();
        var outSb = new StringBuilder();
        foreach (var ch in t)
            outSb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return outSb.ToString().Trim('_');
    }
}
