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

    private ImportResult ImportBase(List<IXLRow> rows, Dictionary<string, int> c, bool sub)
    {
        var unitByName = _units.All().GroupBy(u => Norm(u.NomeUnidade)).ToDictionary(g => g.Key, g => g.First().Id);
        var eqByModelo = _equip.All().ToDictionary(e => Norm(e.Modelo), e => e.Id);

        var novoEquip = new List<IReadOnlyList<KeyValuePair<string, object?>>>();
        var outRows = new List<IReadOnlyList<KeyValuePair<string, object?>>>();
        int novoDoc = _equip.All().Count;

        foreach (var r in rows)
        {
            var modelo = Get(r, c, "modelo", "equipamento");
            if (string.IsNullOrWhiteSpace(modelo)) continue;

            if (!eqByModelo.TryGetValue(Norm(modelo), out var eqId))
            {
                eqId = "eq-" + Guid.NewGuid().ToString("N")[..8];
                eqByModelo[Norm(modelo)] = eqId;
                novoDoc++;
                novoEquip.Add(Row(
                    ("id", eqId),
                    ("doc_num", novoDoc.ToString("D2")),
                    ("tipo", Get(r, c, "tipo", "tipo_equipamento")),
                    ("modelo", modelo),
                    ("nome", (Get(r, c, "tipo") + " " + modelo).Trim()),
                    ("aplicacao", Get(r, c, "aplicacao")),
                    ("segmento", Def(Get(r, c, "segmento"), "Indústria")),
                    ("fabricante", "Howden Aftermarket Intelligence"),
                    ("valor_completo", NumStr(Get(r, c, "valor_completo", "valor")))));
            }

            var unidade = Get(r, c, "unidade", "cliente");
            unitByName.TryGetValue(Norm(unidade), out var unitId);
            outRows.Add(Row(
                ("id", "ib-" + Guid.NewGuid().ToString("N")[..8]),
                ("client_unit_id", unitId ?? unidade),
                ("equipment_id", eqId),
                ("quantidade", Def(Get(r, c, "quantidade", "qtd"), "1")),
                ("ano_instalacao", Get(r, c, "ano_instalacao", "ano")),
                ("criticidade", Def(Get(r, c, "criticidade"), "média")),
                ("status", Def(Get(r, c, "status", "ativo_ou_nao"), "ativo")),
                ("vendedor_id", "")));
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
