using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

namespace AfmHsa.Data;

/// <summary>Um item pedido na cotação Intercompany (e o que a unidade de fora respondeu).</summary>
public sealed class ItemDaCotacao
{
    /// <summary>O item da proposta de onde veio ("" = digitado na solicitação).</summary>
    public string ItemId { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string Modelo { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string Tag { get; set; } = "";
    public int Quantidade { get; set; } = 1;
    public string Observacao { get; set; } = "";
    // ---- a resposta ----
    public decimal PrecoCotado { get; set; }
    public int PrazoDias { get; set; }
}

/// <summary>
/// Uma solicitação de cotação Intercompany: os itens de uma proposta pedidos
/// a outra unidade Howden / Chart (de fora do Brasil), em português, inglês
/// ou espanhol. Fica registrada; a resposta (preço e prazo por item) volta
/// para a proposta como custo de importação, marcado como "cotação".
/// </summary>
public sealed class SolicitacaoIntercompany
{
    public string Id { get; set; } = "";
    /// <summary>"RFQ-IC-…": o código que vai no assunto e na planilha.</summary>
    public string Codigo { get; set; } = "";
    public string PropostaId { get; set; } = "";
    /// <summary>A referência da proposta (código-C revisão).</summary>
    public string Proposta { get; set; } = "";
    public string Cliente { get; set; } = "";
    /// <summary>A unidade de fora para quem vai o pedido (ex.: Howden UK – Renfrew).</summary>
    public string Unidade { get; set; } = "";
    public string Para { get; set; } = "";
    public string Cc { get; set; } = "";
    public string Idioma { get; set; } = IdiomasDaProposta.Ingles;
    /// <summary>yyyy-MM-dd: até quando precisamos da resposta.</summary>
    public string RespostaAte { get; set; } = "";
    /// <summary>Moeda em que pedimos o preço (a da importação da proposta).</summary>
    public string Moeda { get; set; } = "USD";
    public string Entrega { get; set; } = "";
    public string Observacoes { get; set; } = "";
    public List<ItemDaCotacao> Itens { get; set; } = new();
    /// <summary>"rascunho", "enviada" ou "respondida".</summary>
    public string Status { get; set; } = "rascunho";
    public string CriadaEm { get; set; } = "";
    public string EnviadaEm { get; set; } = "";
    public string Usuario { get; set; } = "";
}

/// <summary>As solicitações (entidade "cotacoes_intercompany").</summary>
public sealed class CotacaoIntercompanyRepository
{
    private const string Entidade = "cotacoes_intercompany";
    private readonly ParquetStore _store;
    public CotacaoIntercompanyRepository(ParquetStore store) => _store = store;

    public List<SolicitacaoIntercompany> Todas() => _store
        .ReadLatest(Entidade, "id, dados", r =>
        {
            try { return JsonSerializer.Deserialize<SolicitacaoIntercompany>(Fmt.S(r, 1)); }
            catch (JsonException) { return null; }
        })
        .Where(s => s is not null).Select(s => s!)
        .OrderByDescending(s => s.CriadaEm).ToList();

    public void Gravar(SolicitacaoIntercompany s)
    {
        if (s.Id.Length == 0) s.Id = Guid.NewGuid().ToString("N");
        if (s.CriadaEm.Length == 0) s.CriadaEm = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        _store.WriteRow(Entidade, new KeyValuePair<string, object?>[] { new("id", s.Id), new("dados", JsonSerializer.Serialize(s)) });
    }

    public void Apagar(string id) => _store.WriteRow(Entidade, new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);

    /// <summary>"RFQ-IC-HSA2026-0147-01": o código da proposta e o número da solicitação dela.</summary>
    public static string NovoCodigo(IEnumerable<SolicitacaoIntercompany> todas, string codigoDaProposta)
    {
        var baseCodigo = "RFQ-IC-" + (codigoDaProposta.Trim().Length > 0 ? codigoDaProposta.Trim() : DateTime.Now.ToString("yyyyMMdd"));
        var n = todas.Count(s => s.Codigo.StartsWith(baseCodigo, StringComparison.OrdinalIgnoreCase)) + 1;
        return $"{baseCodigo}-{n:D2}";
    }
}

/// <summary>
/// O pedido de cotação Intercompany nas três línguas: o e-mail e a planilha
/// que vai anexa (para a unidade preencher preço e prazo e devolver).
/// </summary>
public static class PedidoIntercompany
{
    /// <summary>As unidades de fora mais pedidas (a lista da tela; dá para digitar outra).</summary>
    public static readonly string[] Unidades =
    {
        "Howden UK – Renfrew", "Howden Compressors – Glasgow", "Howden Netherlands", "Howden Germany (Turbo)",
        "Howden Thomassen Compressors", "Howden China", "Howden India", "Howden USA", "Howden Chile (HCHL)",
        "Howden Peru (HPU)", "Howden Burton Corblin (France)", "Howden Italy",
    };

    private sealed record T(
        string Assunto, string Saudacao, string Pedido, string Informar, string Prazo, string Entrega, string Obs, string Despedida,
        string[] Colunas, string Planilha, string Rotulos);

    private static T De(string idioma) => IdiomasDaProposta.Normalizar(idioma) switch
    {
        IdiomasDaProposta.Ingles => new(
            "Intercompany quotation request {0} – {1}", "Dear colleagues,",
            "Could you please send us your quotation for the items below, for our proposal {0} (customer: {1})?",
            "Please inform the unit price ({0}), lead time and delivery terms (Incoterm) for each item. The attached spreadsheet can be filled in and returned.",
            "We would appreciate your reply by {0}.", "Delivery terms requested: {0}.", "Remarks: {0}", "Best regards,",
            new[] { "Item", "Part Number", "Description", "Compressor model", "Serial Number", "TAG", "Qty", "Unit price", "Currency", "Lead time (days)", "Remarks" },
            "QUOTATION REQUEST – INTERCOMPANY", "Request|Proposal|Customer|Requested by|Reply by|Delivery terms"),
        IdiomasDaProposta.Espanhol => new(
            "Solicitud de cotización Intercompany {0} – {1}", "Estimados,",
            "Solicitamos, por favor, su cotización para los ítems abajo, para nuestra oferta {0} (cliente: {1}).",
            "Por favor, informar el precio unitario ({0}), el plazo de entrega y la condición de entrega (Incoterm) de cada ítem. La planilla adjunta puede ser completada y devuelta.",
            "Agradecemos su respuesta hasta el {0}.", "Condición de entrega solicitada: {0}.", "Observaciones: {0}", "Saludos cordiales,",
            new[] { "Ítem", "Part Number", "Descripción", "Modelo del compresor", "Serial Number", "TAG", "Ctd.", "Precio unitario", "Moneda", "Plazo (días)", "Observaciones" },
            "SOLICITUD DE COTIZACIÓN – INTERCOMPANY", "Solicitud|Oferta|Cliente|Solicitado por|Respuesta hasta|Condición de entrega"),
        _ => new(
            "Solicitação de cotação Intercompany {0} – {1}", "Prezados,",
            "Solicitamos, por gentileza, a cotação dos itens abaixo para a nossa proposta {0} (cliente: {1}).",
            "Por favor, informar o preço unitário ({0}), o prazo de entrega e a condição de entrega (Incoterm) de cada item. A planilha anexa pode ser preenchida e devolvida.",
            "Agradecemos o retorno até {0}.", "Condição de entrega desejada: {0}.", "Observações: {0}", "Atenciosamente,",
            new[] { "Item", "Part Number", "Descrição", "Modelo do compressor", "Serial Number", "TAG", "Qtd.", "Preço unitário", "Moeda", "Prazo (dias)", "Observações" },
            "SOLICITAÇÃO DE COTAÇÃO – INTERCOMPANY", "Solicitação|Proposta|Cliente|Solicitado por|Resposta até|Condição de entrega"),
    };

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string Data(string iso) =>
        DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("dd/MM/yyyy") : "";

    public static EmailDaProposta Email(SolicitacaoIntercompany s, string remetente)
    {
        var t = De(s.Idioma);
        var assunto = string.Format(t.Assunto, s.Codigo, PropostaWord.Ou(s.Cliente, "—"));
        var pedido = string.Format(t.Pedido, PropostaWord.Ou(s.Proposta, s.Codigo), PropostaWord.Ou(s.Cliente, "—"));
        var informar = string.Format(t.Informar, s.Moeda);
        var linhasExtras = new List<string>();
        if (Data(s.RespostaAte) is { Length: > 0 } ate) linhasExtras.Add(string.Format(t.Prazo, ate));
        if (s.Entrega.Trim().Length > 0) linhasExtras.Add(string.Format(t.Entrega, s.Entrega.Trim()));
        if (s.Observacoes.Trim().Length > 0) linhasExtras.Add(string.Format(t.Obs, s.Observacoes.Trim()));
        var colunas = t.Colunas.Take(7).ToArray();

        var sb = new StringBuilder("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#1f2937;line-height:1.45\">");
        sb.Append($"<p>{H(t.Saudacao)}</p><p>{H(pedido)}</p>");
        sb.Append("<table cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;margin:10px 0;font-size:12.5px\"><tr>");
        foreach (var c in colunas)
            sb.Append($"<th style=\"background:#004785;color:#fff;padding:5px 10px;text-align:left;border:1px solid #004785\">{H(c)}</th>");
        sb.Append("</tr>");
        for (var i = 0; i < s.Itens.Count; i++)
        {
            var it = s.Itens[i];
            var fundo = i % 2 == 0 ? "#ffffff" : "#f3f7fb";
            var celulas = new[] { (i + 1).ToString("D2"), it.PartNumber, it.Descricao, it.Modelo, it.SerialNumber, it.Tag, it.Quantidade.ToString() };
            sb.Append("<tr>");
            foreach (var c in celulas)
                sb.Append($"<td style=\"padding:5px 10px;border:1px solid #d6dee8;background:{fundo};vertical-align:top\">{H(c)}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        sb.Append($"<p>{H(informar)}</p>");
        foreach (var l in linhasExtras) sb.Append($"<p>{H(l)}</p>");
        sb.Append($"<p style=\"margin-top:18px\">{H(t.Despedida)}<br>{H(remetente)}<br>Howden South America · Chart Industries</p></div>");

        var texto = new StringBuilder();
        texto.AppendLine(t.Saudacao).AppendLine().AppendLine(pedido).AppendLine();
        for (var i = 0; i < s.Itens.Count; i++)
        {
            var it = s.Itens[i];
            texto.AppendLine($"{i + 1:D2}. {it.PartNumber} – {it.Descricao}{(it.Modelo.Length > 0 ? $" – {it.Modelo}" : "")}{(it.SerialNumber.Length > 0 ? $" – SN {it.SerialNumber}" : "")} – {colunas[6]} {it.Quantidade}");
        }
        texto.AppendLine().AppendLine(informar);
        foreach (var l in linhasExtras) texto.AppendLine(l);
        texto.AppendLine().AppendLine(t.Despedida).AppendLine(remetente).AppendLine("Howden South America · Chart Industries");

        return new EmailDaProposta(s.Para, s.Cc, assunto, sb.ToString(), texto.ToString());
    }

    /// <summary>A planilha do pedido, na língua dele: os itens e as colunas de preço, moeda e prazo para a unidade preencher.</summary>
    public static byte[] Planilha(SolicitacaoIntercompany s, string remetente)
    {
        var t = De(s.Idioma);
        var azul = XLColor.FromHtml("#004785");
        var amarelo = XLColor.FromHtml("#FFF8D6");
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("RFQ");
        ws.Cell("A1").Value = t.Planilha;
        ws.Cell("A1").Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(azul);
        var rotulos = t.Rotulos.Split('|');
        var valores = new[] { s.Codigo, s.Proposta, s.Cliente, remetente, Data(s.RespostaAte), s.Entrega };
        for (var i = 0; i < rotulos.Length; i++)
        {
            ws.Cell(3 + i, 1).Value = rotulos[i];
            ws.Cell(3 + i, 1).Style.Font.SetBold();
            ws.Cell(3 + i, 2).Value = valores[i];
        }
        const int titulo = 10;
        for (var c = 0; c < t.Colunas.Length; c++)
        {
            var cel = ws.Cell(titulo, c + 1);
            cel.Value = t.Colunas[c];
            cel.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(azul).Alignment.SetWrapText();
        }
        var lin = titulo + 1;
        for (var i = 0; i < s.Itens.Count; i++, lin++)
        {
            var it = s.Itens[i];
            ws.Cell(lin, 1).Value = (i + 1).ToString("D2");
            ws.Cell(lin, 2).Value = it.PartNumber;
            ws.Cell(lin, 3).Value = it.Descricao;
            ws.Cell(lin, 4).Value = it.Modelo;
            ws.Cell(lin, 5).Value = it.SerialNumber;
            ws.Cell(lin, 6).Value = it.Tag;
            ws.Cell(lin, 7).Value = it.Quantidade;
            ws.Cell(lin, 9).Value = s.Moeda;
            ws.Cell(lin, 11).Value = it.Observacao;
            // o que a unidade preenche: preço, moeda e prazo
            foreach (var col in new[] { 8, 9, 10 }) ws.Cell(lin, col).Style.Fill.SetBackgroundColor(amarelo);
        }
        ws.Range(titulo + 1, 8, Math.Max(titulo + 1, lin - 1), 8).Style.NumberFormat.Format = "#,##0.00";
        ws.Range(titulo, 1, Math.Max(titulo, lin - 1), t.Colunas.Length).Style.Border.SetInsideBorder(XLBorderStyleValues.Hair)
            .Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.Column(1).Width = 18; ws.Column(2).Width = 18; ws.Column(3).Width = 40; ws.Column(4).Width = 24;
        ws.Column(5).Width = 16; ws.Column(6).Width = 12; ws.Column(7).Width = 8; ws.Column(8).Width = 14;
        ws.Column(9).Width = 10; ws.Column(10).Width = 12; ws.Column(11).Width = 30;
        ws.SheetView.FreezeRows(titulo);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        using var fluxo = new MemoryStream();
        wb.SaveAs(fluxo);
        return fluxo.ToArray();
    }

    public static string NomeDaPlanilha(SolicitacaoIntercompany s) => PricingExcel.Limpo($"{s.Codigo}.xlsx");
}
