using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using ExcelDataReader;

namespace AfmHsa.Data;

public class ImportResult
{
    public bool Ok { get; set; }
    public int Count { get; set; }
    public string Message { get; set; } = "";
}

// Importa planilhas (.xlsx, .xls e .csv) para as entidades da base (o "motor" que alimenta o
// sistema). Mapeia por cabeçalho (tolerante a acentos/maiúsculas) e resolve
// referências (cliente/unidade/equipamento/vendedor) por nome quando possível.
public class ImportService
{
    private readonly ParquetStore _store;
    private readonly ClientUnitRepository _units;
    private readonly EquipmentRepository _equip;
    private readonly SalespersonRepository _sp;
    private readonly CompanyRepository _companies;
    private readonly BomRepository _bom;
    private readonly SalesRecordRepository _vendas;
    private readonly InstalledBaseRepository _base;

    public ImportService(ParquetStore store, ClientUnitRepository units, EquipmentRepository equip,
        SalespersonRepository sp, CompanyRepository companies, BomRepository bom,
        SalesRecordRepository vendas, InstalledBaseRepository baseInstalada)
    {
        _store = store; _units = units; _equip = equip; _sp = sp; _companies = companies;
        _bom = bom; _vendas = vendas; _base = baseInstalada;
    }

    /// <summary>
    /// Importa um arquivo de planilha. Aceita .xlsx/.xlsm, .xls (Excel antigo) e .csv —
    /// o formato é detectado pelo conteúdo do arquivo (com a extensão como reforço).
    /// </summary>
    public ImportResult Import(string tipo, byte[] bytes, bool substituir, string fileName = "")
    {
        try
        {
            if (bytes is null || bytes.Length == 0)
                return new ImportResult { Ok = false, Message = "Arquivo vazio." };

            var (header, dataRows) = LerPlanilha(bytes, fileName);
            if (header.Length == 0)
                return new ImportResult { Ok = false, Message = "Planilha vazia ou sem linha de cabeçalho." };

            // mapa cabeçalho normalizado -> índice da coluna (base zero)
            var cols = new Dictionary<string, int>();
            for (int i = 0; i < header.Length; i++)
            {
                var chave = Norm(header[i]);
                if (chave.Length > 0 && !cols.ContainsKey(chave)) cols[chave] = i;
            }
            if (cols.Count == 0)
                return new ImportResult { Ok = false, Message = "Não foi possível ler o cabeçalho da planilha." };

            return tipo switch
            {
                "vendedores" => ImportVendedores(dataRows, cols, substituir),
                "construtivos" => ImportConstrutivos(dataRows, cols, substituir),
                "bom" => ImportBom(dataRows, cols, substituir),
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

    // ---- leitura do arquivo (xlsx / xls / csv) ----------------------------

    /// <summary>Devolve (cabeçalho, linhas de dados) já como texto, qualquer que seja o formato.</summary>
    private static (string[] Header, List<string[]> Rows) LerPlanilha(byte[] bytes, string fileName)
    {
        var ext = (Path.GetExtension(fileName ?? "") ?? "").ToLowerInvariant();
        return Formato(bytes, ext) switch
        {
            "xlsx" => Separar(LerXlsxComSeguranca(bytes)),
            "xls" => Separar(LerXls(bytes)),
            _ => Separar(LerCsv(bytes)),
        };
    }

    private static (string[], List<string[]>) Separar(List<string[]> todas)
    {
        // descarta linhas totalmente vazias (comuns no fim das planilhas)
        todas = todas.Where(l => l.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        if (todas.Count == 0) return (Array.Empty<string>(), new List<string[]>());
        return (todas[0], todas.Skip(1).ToList());
    }

    /// <summary>Detecta o formato pela assinatura do arquivo; usa a extensão como reforço.</summary>
    private static string Formato(byte[] b, string ext)
    {
        // .xlsx/.xlsm são ZIP  →  "PK\x03\x04"
        if (b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04) return "xlsx";
        // .xls antigo é OLE2  →  D0 CF 11 E0
        if (b.Length >= 4 && b[0] == 0xD0 && b[1] == 0xCF && b[2] == 0x11 && b[3] == 0xE0) return "xls";
        if (ext == ".xlsx" || ext == ".xlsm") return "xlsx";
        if (ext == ".xls") return "xls";
        return "csv";
    }

    /// <summary>
    /// Lê .xlsx pelo ClosedXML e, se ele falhar, pelo ExcelDataReader. O caso
    /// típico é fórmula com vínculo para outro arquivo ("='[Outra.xlsx]Aba'!B4"):
    /// o ClosedXML tenta recalcular e para com "References from other files are
    /// not yet implemented". O ExcelDataReader nunca recalcula — usa o valor que
    /// o Excel deixou salvo na célula, que é exatamente o que a pessoa vê.
    /// </summary>
    private static List<string[]> LerXlsxComSeguranca(byte[] bytes)
    {
        try
        {
            return LerXlsx(bytes);
        }
        catch (Exception)
        {
            return LerXls(bytes);   // ExcelReaderFactory também abre .xlsx
        }
    }

    private static List<string[]> LerXlsx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();
        var ultima = ws.LastColumnUsed();
        int largura = ultima?.ColumnNumber() ?? 0;
        var linhas = new List<string[]>();
        if (largura == 0) return linhas;
        foreach (var r in ws.RowsUsed())
        {
            var arr = new string[largura];
            for (int c = 1; c <= largura; c++) arr[c - 1] = CelulaTexto(r.Cell(c));
            linhas.Add(arr);
        }
        return linhas;
    }

    private static string CelulaTexto(IXLCell cell)
    {
        if (cell is null) return "";
        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var dt))
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return cell.GetString().Trim();
    }

    /// <summary>Lê .xls (e .xlsx, como plano B) com os valores salvos nas células.</summary>
    private static List<string[]> LerXls(byte[] bytes)
    {
        RegistrarCodePages();
        using var ms = new MemoryStream(bytes);
        using var reader = ExcelReaderFactory.CreateReader(ms);
        var linhas = new List<string[]>();
        // apenas a primeira planilha do arquivo
        while (reader.Read())
        {
            var arr = new string[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++) arr[i] = ValorTexto(reader.GetValue(i));
            linhas.Add(arr);
        }
        return linhas;
    }

    private static string ValorTexto(object? v) => v switch
    {
        null => "",
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        double n => n.ToString("0.############", CultureInfo.InvariantCulture),
        decimal n => n.ToString("0.############", CultureInfo.InvariantCulture),
        bool bo => bo ? "true" : "false",
        _ => (v.ToString() ?? "").Trim(),
    };

    private static List<string[]> LerCsv(byte[] bytes)
    {
        var texto = DecodificarTexto(bytes);
        var sep = SeparadorCsv(texto);
        var linhas = new List<string[]>();
        var campos = new List<string>();
        var sb = new StringBuilder();
        bool aspas = false;
        for (int i = 0; i < texto.Length; i++)
        {
            var ch = texto[i];
            if (aspas)
            {
                if (ch == '"')
                {
                    if (i + 1 < texto.Length && texto[i + 1] == '"') { sb.Append('"'); i++; }
                    else aspas = false;
                }
                else sb.Append(ch);
            }
            else if (ch == '"') aspas = true;
            else if (ch == sep) { campos.Add(sb.ToString().Trim()); sb.Clear(); }
            else if (ch == '\n')
            {
                campos.Add(sb.ToString().Trim()); sb.Clear();
                linhas.Add(campos.ToArray()); campos.Clear();
            }
            else if (ch != '\r') sb.Append(ch);
        }
        if (sb.Length > 0 || campos.Count > 0)
        {
            campos.Add(sb.ToString().Trim());
            linhas.Add(campos.ToArray());
        }
        return linhas;
    }

    /// <summary>UTF-8 (com ou sem BOM); se não for UTF-8 válido, assume Windows-1252 (Excel pt-BR).</summary>
    private static string DecodificarTexto(byte[] b)
    {
        RegistrarCodePages();
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        try { return new UTF8Encoding(false, true).GetString(b); }
        catch (DecoderFallbackException)
        {
            try { return Encoding.GetEncoding(1252).GetString(b); }
            catch { return Encoding.UTF8.GetString(b); }
        }
    }

    /// <summary>Descobre o separador do CSV olhando a primeira linha (; , ou tabulação).</summary>
    private static char SeparadorCsv(string texto)
    {
        var fim = texto.IndexOf('\n');
        var primeira = fim >= 0 ? texto[..fim] : texto;
        int pv = primeira.Count(c => c == ';');
        int vg = primeira.Count(c => c == ',');
        int tb = primeira.Count(c => c == '\t');
        if (tb > pv && tb > vg) return '\t';
        return vg > pv ? ',' : ';';
    }

    private static bool _codePagesOk;
    private static void RegistrarCodePages()
    {
        if (_codePagesOk) return;
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
        _codePagesOk = true;
    }

    // ---- por tipo --------------------------------------------------------
    private ImportResult ImportVendedores(List<string[]> rows, Dictionary<string, int> c, bool sub)
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
                ("segmento", Get(r, c, "segmento", "segmento_de_atuacao")),
                ("perfil", Def(Get(r, c, "perfil"), "vendedor"))));
        }
        return Write("salespeople", outRows, sub);
    }

    private ImportResult ImportConstrutivos(List<string[]> rows, Dictionary<string, int> c, bool sub)
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

    /// <summary>
    /// Lista de materiais (BOM) por equipamento — usada na ficha técnica de todo
    /// tipo que não seja ventilador centrífugo.
    /// </summary>
    private ImportResult ImportBom(List<string[]> rows, Dictionary<string, int> c, bool sub)
    {
        var eqByModelo = _equip.All().ToDictionary(e => Norm(e.Modelo), e => e.Id);
        var eqByNome = _equip.All().GroupBy(e => Norm(e.Nome)).ToDictionary(g => g.Key, g => g.First().Id);
        var outRows = new List<IReadOnlyList<KeyValuePair<string, object?>>>();
        foreach (var r in rows)
        {
            var linha = LinhaBom(r, c);
            if (linha is null) continue;

            var modelo = Get(r, c, "modelo_do_equipamento", "equipamento", "modelo");
            if (!eqByModelo.TryGetValue(Norm(modelo), out var eqId))
                eqByNome.TryGetValue(Norm(modelo), out eqId);

            outRows.Add(ComEquipamento(linha, eqId ?? modelo));
        }
        return Write("bom", outRows, sub);
    }

    /// <summary>
    /// Lê uma linha de BOM da planilha. Devolve null quando a linha não tem nada
    /// que a identifique (nem partnumber, nem item, nem descrição).
    /// </summary>
    private static IReadOnlyList<KeyValuePair<string, object?>>? LinhaBom(string[] r, Dictionary<string, int> c)
    {
        var item = Get(r, c, "item", "item_no", "pos", "posicao");
        var pn = Get(r, c, "partnumber", "part_number", "part_no", "codigo", "codigo_peca");
        var en = Get(r, c, "descricao_em_ingles", "descricao_ingles", "descricao_en", "description", "description_en");
        var pt = Get(r, c, "descricao_portugues", "descricao_em_portugues", "descricao_pt", "descricao", "descricao_do_item");

        if (string.IsNullOrWhiteSpace(pn) && string.IsNullOrWhiteSpace(item)
            && string.IsNullOrWhiteSpace(en) && string.IsNullOrWhiteSpace(pt)) return null;

        return Row(
            ("id", "bom-" + Guid.NewGuid().ToString("N")[..8]),
            ("equipment_id", ""),   // preenchido por ComEquipamento
            ("item", item),
            ("quantidade", Get(r, c, "quantidade", "qtd", "qty", "quantity")),
            ("part_number", pn),
            ("descricao_en", en),
            ("descricao_pt", pt),
            ("material", Get(r, c, "material", "materia_prima")));
    }

    private static IReadOnlyList<KeyValuePair<string, object?>> ComEquipamento(
        IReadOnlyList<KeyValuePair<string, object?>> linha, string equipmentId) =>
        linha.Select(kv => kv.Key == "equipment_id"
            ? new KeyValuePair<string, object?>(kv.Key, equipmentId) : kv).ToList();

    // ---- importação dentro da ficha de um equipamento ---------------------
    // Aqui a planilha não precisa dizer de que equipamento se trata: é sempre o
    // equipamento aberto na tela. "substituir" apaga só o que é desse
    // equipamento, nunca o dos outros.

    public ImportResult ImportBomDoEquipamento(string equipmentId, byte[] bytes, string fileName, bool substituir)
        => ImportarNaFicha(bytes, fileName, rows =>
        {
            var (c, dados) = rows;
            var outRows = new List<IReadOnlyList<KeyValuePair<string, object?>>>();
            foreach (var r in dados)
            {
                var linha = LinhaBom(r, c);
                if (linha is null) continue;
                outRows.Add(ComEquipamento(linha, equipmentId));
            }
            if (outRows.Count == 0) return new ImportResult { Ok = false, Message = "Nenhuma linha válida encontrada." };
            if (substituir) _bom.DeleteByEquip(equipmentId);
            _store.WriteBatch("bom", outRows);
            return new ImportResult { Ok = true, Count = outRows.Count, Message = $"{outRows.Count} item(ns) de BOM importado(s)." };
        });

    public ImportResult ImportHistoricoDoEquipamento(string equipmentId, byte[] bytes, string fileName, bool substituir)
        => ImportarNaFicha(bytes, fileName, rows =>
        {
            var (c, dados) = rows;
            var unitByName = _units.All().GroupBy(u => Norm(u.NomeUnidade)).ToDictionary(g => g.Key, g => g.First().Id);
            var unitIds = new HashSet<string>(_units.All().Select(u => u.Id), StringComparer.OrdinalIgnoreCase);
            var spByName = _sp.All().GroupBy(s => Norm(s.Nome)).ToDictionary(g => g.Key, g => g.First().Id);
            var spIds = new HashSet<string>(_sp.All().Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
            var descPorPn = DescricoesDoBom(equipmentId);
            var instalados = _base.All().Where(b => b.EquipmentId == equipmentId).ToList();

            var outRows = new List<IReadOnlyList<KeyValuePair<string, object?>>>();
            foreach (var r in dados)
            {
                var pn = Get(r, c, PnAliases);
                var data = GetDate(r, c, "data_venda", "data");
                if (string.IsNullOrWhiteSpace(pn) && string.IsNullOrWhiteSpace(data)) continue;

                var tag = Get(r, c, "tag");
                var serie = Get(r, c, SerieAliases);

                // a planilha pode trazer o nome da unidade ou o próprio id;
                // sem isso, a planta sai da unidade instalada com esse serial / TAG
                var unidade = SemTraco(Get(r, c, "unidade", "cliente_unidade", "cliente"));
                if (!unitByName.TryGetValue(Norm(unidade), out var unitId) && unitIds.Contains(unidade))
                    unitId = unidade;
                unitId ??= PlantaPorSerieOuTag(instalados, serie, tag);
                var vendedor = SemTraco(Get(r, c, "vendedor"));
                if (!spByName.TryGetValue(Norm(vendedor), out var spId) && spIds.Contains(vendedor))
                    spId = vendedor;

                var (qtd, unitario, totalVenda) = ValoresDaVenda(r, c);
                outRows.Add(Row(
                    ("id", "sl-" + Guid.NewGuid().ToString("N")[..8]),
                    ("data_venda", data),
                    ("pedido", Get(r, c, NumeroVendaAliases)),
                    ("client_unit_id", unitId ?? unidade),
                    ("equipment_id", equipmentId),
                    ("item_vendido", DescricaoDaVenda(descPorPn, pn, r, c)),
                    ("quantidade", qtd),
                    ("valor", totalVenda),
                    ("valor_unitario", unitario),
                    ("vendedor_id", spId ?? vendedor),
                    ("origem", Def(Get(r, c, "origem"), "Importação Excel")),
                    ("tag", tag),
                    ("serial_no", serie),
                    ("part_number", pn),
                    ("observacao", Get(r, c, "obs", "observacao", "observacoes", "remarks")),
                    ("tipo_importacao", TipoImportacao(Get(r, c, "importacao", "tipo_importacao", "importado_por"))),
                    ("prazo_entrega", Get(r, c, "prazo_de_entrega", "prazo_entrega", "prazo", "lead_time"))));
            }
            if (outRows.Count == 0) return new ImportResult { Ok = false, Message = "Nenhuma linha válida encontrada." };
            if (substituir) _vendas.DeleteByEquip(equipmentId);
            _store.WriteBatch("salesHistory", outRows);
            return new ImportResult { Ok = true, Count = outRows.Count, Message = $"{outRows.Count} registro(s) importado(s)." };
        });

    // ---- nacionalização ---------------------------------------------------

    /// <summary>
    /// Lista de peças nacionalizadas. Cabeçalhos do jeito que a equipe usa
    /// ("Data da nacionalização", "Cód. HSA", "Qnt.", "Já vendido?"…) ou em
    /// snake_case; os totais faltantes saem de quantidade × unitário.
    /// </summary>
    public ImportResult ImportNacionalizacao(byte[] bytes, string fileName, bool substituir)
        => ImportarNaFicha(bytes, fileName, rows =>
        {
            var (c, dados) = rows;
            var inv = CultureInfo.InvariantCulture;
            var outRows = new List<IReadOnlyList<KeyValuePair<string, object?>>>();
            foreach (var r in dados)
            {
                var pn = Get(r, c, PnAliases);
                var cod = Get(r, c, "cod__hsa", "cod_hsa", "codigo_hsa", "cod_hsa_");
                var desc = Get(r, c, "descricao_alterada", "descricao", "description");
                if (string.IsNullOrWhiteSpace(pn) && string.IsNullOrWhiteSpace(cod) && string.IsNullOrWhiteSpace(desc))
                    continue;

                var qtd = Fmt.Dec(NumStr(Get(r, c, "qnt", "qtd", "quantidade", "qty")));
                var (icUnit, icTotal) = UnitarioETotal(qtd,
                    Get(r, c, "valor_intercompany", "vl_intercompany", "intercompany"),
                    Get(r, c, "valor_total_intercompany", "total_intercompany"));
                var (brUnit, brTotal) = UnitarioETotal(qtd,
                    Get(r, c, "valor_br", "vl_br"),
                    Get(r, c, "total_br", "valor_total_br"));

                outRows.Add(Row(
                    ("id", "nac-" + Guid.NewGuid().ToString("N")[..8]),
                    ("data", GetDate(r, c, "data_da_nacionalizacao", "data_nacionalizacao", "data")),
                    ("projeto", Get(r, c, "projeto", "project")),
                    ("cod_hsa", cod),
                    ("part_number", pn),
                    ("descricao_alterada", desc),
                    ("quantidade", qtd.ToString(inv)),
                    ("valor_intercompany", icUnit.ToString(inv)),
                    ("valor_total_intercompany", icTotal.ToString(inv)),
                    ("valor_br", brUnit.ToString(inv)),
                    ("total_br", brTotal.ToString(inv)),
                    ("ja_vendido", SimNao(Get(r, c, "ja_vendido", "vendido", "ja_foi_vendido")) ? "true" : "false")));
            }
            return Write("nacionalizacao", outRows, substituir);
        });

    /// <summary>Unitário e total de uma linha: o que faltar sai do outro pela quantidade.</summary>
    private static (decimal Unit, decimal Total) UnitarioETotal(decimal qtd, string unitTexto, string totalTexto)
    {
        var unit = Fmt.Dec(NumStr(unitTexto));
        var total = Fmt.Dec(NumStr(totalTexto));
        if (total == 0m && unit != 0m && qtd > 0) total = unit * qtd;
        if (unit == 0m && total != 0m && qtd > 0) unit = total / qtd;
        return (unit, total);
    }

    /// <summary>"Sim", "S", "X", "true", "1", "yes" contam como sim.</summary>
    private static bool SimNao(string s)
    {
        var t = Norm(s);
        return t is "sim" or "s" or "x" or "true" or "1" or "yes" or "y" or "vendido";
    }

    /// <summary>Lê a planilha, monta o mapa de colunas e entrega ao importador.</summary>
    private static ImportResult ImportarNaFicha(byte[] bytes, string fileName,
        Func<(Dictionary<string, int> Cols, List<string[]> Rows), ImportResult> importar)
    {
        try
        {
            if (bytes is null || bytes.Length == 0)
                return new ImportResult { Ok = false, Message = "Arquivo vazio." };
            var (header, dataRows) = LerPlanilha(bytes, fileName);
            if (header.Length == 0)
                return new ImportResult { Ok = false, Message = "Não foi possível ler o cabeçalho da planilha." };

            var cols = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < header.Length; i++)
            {
                var chave = Norm(header[i]);
                if (chave.Length > 0 && !cols.ContainsKey(chave)) cols[chave] = i;
            }
            if (cols.Count == 0)
                return new ImportResult { Ok = false, Message = "Não foi possível ler o cabeçalho da planilha." };

            return importar((cols, dataRows));
        }
        catch (Exception ex)
        {
            return new ImportResult { Ok = false, Message = "Erro ao ler o arquivo: " + ex.Message };
        }
    }

    private ImportResult ImportHistorico(List<string[]> rows, Dictionary<string, int> c, bool sub)
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

            var (qtd, unitario, totalVenda) = ValoresDaVenda(r, c);
            var pnGeral = Get(r, c, PnAliases);
            outRows.Add(Row(
                ("id", "sl-" + Guid.NewGuid().ToString("N")[..8]),
                ("data_venda", data),
                ("pedido", Get(r, c, NumeroVendaAliases)),
                ("client_unit_id", unitId ?? unidade),
                ("equipment_id", eqId ?? equipamento),
                ("item_vendido", eqId is null ? Def(item, pnGeral)
                                              : DescricaoDaVenda(DescricoesDoBom(eqId), pnGeral, r, c)),
                ("quantidade", qtd),
                ("valor", totalVenda),
                ("valor_unitario", unitario),
                ("vendedor_id", spId ?? vendedor),
                ("origem", "Importação Excel"),
                ("tag", Get(r, c, "tag")),
                ("serial_no", Get(r, c, SerieAliases)),
                ("part_number", pnGeral),
                ("observacao", Get(r, c, "obs", "observacao", "observacoes", "remarks")),
                ("tipo_importacao", TipoImportacao(Get(r, c, "importacao", "tipo_importacao", "importado_por"))),
                ("prazo_entrega", Get(r, c, "prazo_de_entrega", "prazo_entrega", "prazo", "lead_time"))));
        }
        return Write("salesHistory", outRows, sub);
    }

    // Importa a planilha-mestre da Howden (HP Fan References, 27 colunas em
    // inglês). Guarda todas as colunas para exibição na aba Base Instalada e
    // deriva os campos que o motor de oportunidades precisa.
    private ImportResult ImportBase(List<string[]> rows, Dictionary<string, int> c, bool sub)
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
            var tag        = Get(r, c, "tag", "tag_do_equipamento", "etiqueta");

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
                ("agent", agent),
                ("tag", tag)));
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

    /// <summary>Lê a coluna pelo cabeçalho normalizado; devolve "" se não existir na linha.</summary>
    private static string Get(string[] r, Dictionary<string, int> cols, params string[] names)
    {
        foreach (var n in names)
            if (cols.TryGetValue(n, out var i) && i >= 0 && i < r.Length)
                return (r[i] ?? "").Trim();
        return "";
    }

    private static string GetDate(string[] r, Dictionary<string, int> cols, params string[] names)
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

    /// <summary>
    /// Quantidade, valor unitário e valor total de uma linha de venda. A planilha
    /// pode trazer os dois valores, só o unitário ou só o total — o que faltar é
    /// deduzido do outro pela quantidade.
    /// </summary>
    private static (string Qtd, string Unitario, string Total) ValoresDaVenda(string[] r, Dictionary<string, int> c)
    {
        var qtdTexto = Def(Get(r, c, "quantidade", "qtd", "qty"), "1");
        var qtd = Fmt.Int(qtdTexto);
        var unit = Fmt.Dec(NumStr(Get(r, c, "valor_unitario", "valor_unit", "vl_unitario", "preco_unitario", "unit_price")));
        var total = Fmt.Dec(NumStr(Get(r, c, "valor_total", "valor", "vl_total", "total")));

        if (total == 0m && unit != 0m && qtd > 0) total = unit * qtd;
        if (unit == 0m && total != 0m && qtd > 0) unit = total / qtd;

        var inv = CultureInfo.InvariantCulture;
        return (qtdTexto, unit.ToString(inv), total.ToString(inv));
    }

    /// <summary>
    /// Normaliza o tipo de importação para "total" ou "item" — a planilha pode vir
    /// com "Por total", "TOTAL", "por item", "Item"… Qualquer outra coisa fica vazia.
    /// </summary>
    private static string TipoImportacao(string s)
    {
        var t = Norm(s);
        if (t.Contains("total")) return "total";
        if (t.Contains("item")) return "item";
        return "";
    }

    // Cabeçalhos aceitos. "N° de venda" vira "n__de_venda" (o ° e o espaço viram
    // dois "_"), "Nº de venda" vira "nº_de_venda" — por isso as variações.
    private static readonly string[] NumeroVendaAliases =
        { "numero_de_venda", "n__de_venda", "nº_de_venda", "no_de_venda", "n_de_venda",
          "numero_venda", "n_venda", "venda", "pedido" };
    private static readonly string[] PnAliases =
        { "partnumber", "part_number", "part_no", "codigo_peca", "codigo" };
    private static readonly string[] SerieAliases =
        { "serial_number", "serial_no", "numero_de_serie", "n_de_serie", "n__de_serie", "nº_de_serie", "serie" };

    /// <summary>Partnumber → descrição (português, ou inglês na falta) do BOM do equipamento.</summary>
    private Dictionary<string, string> DescricoesDoBom(string equipmentId) =>
        _bom.ForEquip(equipmentId)
            .Where(b => !string.IsNullOrWhiteSpace(b.PartNumber))
            .GroupBy(b => IntelligenceService.NormPn(b.PartNumber))
            .ToDictionary(g => g.Key, g =>
            {
                var b = g.First();
                return !string.IsNullOrWhiteSpace(b.DescricaoPt) ? b.DescricaoPt : b.DescricaoEn;
            });

    /// <summary>
    /// A descrição do item vem do BOM pelo partnumber. Fora do BOM, usa a coluna
    /// de descrição da planilha, se houver, e por último o próprio partnumber.
    /// </summary>
    private static string DescricaoDaVenda(Dictionary<string, string> descPorPn, string pn,
        string[] r, Dictionary<string, int> c)
    {
        if (descPorPn.TryGetValue(IntelligenceService.NormPn(pn), out var d) && !string.IsNullOrWhiteSpace(d))
            return d;
        var daPlanilha = Get(r, c, "descricao", "item_vendido", "item", "peca");
        return Def(daPlanilha, pn);
    }

    /// <summary>Planta da unidade instalada com esse serial; sem serial, pela TAG (se única).</summary>
    private static string? PlantaPorSerieOuTag(List<AfmHsa.Models.InstalledBase> instalados, string serie, string tag)
    {
        if (!string.IsNullOrWhiteSpace(serie))
        {
            var b = instalados.FirstOrDefault(x => string.Equals(x.SerialNo, serie.Trim(), StringComparison.OrdinalIgnoreCase));
            if (b is not null) return b.ClientUnitId;
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var porTag = instalados.Where(x => string.Equals(x.Tag, tag.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (porTag.Count == 1) return porTag[0].ClientUnitId;
        }
        return null;
    }

    /// <summary>A exportação escreve "—" onde não há valor; na volta isso é vazio.</summary>
    private static string SemTraco(string s)
    {
        var t = (s ?? "").Trim();
        return t == "\u2014" || t == "-" ? "" : t;
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
