using ClosedXML.Excel;
using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>Uma linha da planilha de custo por item, já calculada pela tela.</summary>
public sealed record CustoDoItemNaPlanilha(
    int Item, string PartNumber, string Descricao, string Modelo, string SerialNumber, string Tag, string Origem,
    int PrazoDias, int Quantidade, decimal? CustoUnitario, decimal FreteUnitario, decimal PesoKg, decimal TaUnitario, decimal PrecoMeta,
    decimal PrecoUnitario, decimal PrecoTotal);

/// <summary>
/// O custo de cada item da proposta e o preço dele: custo unitário (R$), o
/// frete que coube a ele pelo peso, o TA
/// quando existe (marcado em amarelo — o custo acrescentado para o preço chegar
/// na meta), o custo total e o preço unitário e total na moeda da proposta.
/// Para conferir item a item e para o pedido de compras.
/// </summary>
public static class CustoPorItemExcel
{
    public const string TipoMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>R_&lt;código da proposta&gt;-C&lt;revisão&gt;.xlsx</summary>
    public static string NomeDoArquivo(CommercialOffer o) => PricingExcel.Limpo($"R_{PropostaWord.Referencia(o)}.xlsx");

    public static byte[] Gerar(CommercialOffer o, IReadOnlyList<CustoDoItemNaPlanilha> linhas, CalculoPricing.Resultado r,
        string nivelDeImposto)
    {
        var moeda = Cambio.MoedaDoPreco(o);
        var fmtReal = "\"R$\" #,##0.00";
        var fmtVenda = moeda == Moeda.Clp ? "\"CLP\" #,##0" : $"\"{moeda.Rotulo()}\" #,##0.00";
        var amarelo = XLColor.FromHtml("#FFF2B3");
        var azul = XLColor.FromHtml("#004785");

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Custo por item");

        // ---------- cabeçalho ----------
        ws.Cell("A1").Value = "CUSTO POR ITEM";
        ws.Cell("A1").Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(azul);
        var cab = new (string Rotulo, string Valor)[]
        {
            ("Proposta", PropostaWord.Referencia(o)),
            ("Cliente", o.ClientName),
            ("Data", DateTime.TryParse(o.OfferDate, out var d) ? d.ToString("dd/MM/yyyy") : o.OfferDate),
            ("Moeda do preço", moeda.Rotulo() + (Cambio.Converte(o) && Cambio.Efetivo(o) is var fx && fx != 1m ? $" (1 {moeda.Rotulo()} = R$ {fx:N4} com a segurança)" : "")),
            ("Preço ao cliente", nivelDeImposto),
            ("Margem do pricing", r.VendaLiquida > 0 ? r.Margem.ToString("P2", new System.Globalization.CultureInfo("pt-BR")) : "—"),
            ("Frete da proposta", o.FreteCusto > 0 ? o.FreteCusto.ToString("\"R$\" #,##0.00", new System.Globalization.CultureInfo("pt-BR")) : "—"),
        };
        for (var i = 0; i < cab.Length; i++)
        {
            ws.Cell(3 + i, 1).Value = cab[i].Rotulo;
            ws.Cell(3 + i, 1).Style.Font.SetBold();
            ws.Cell(3 + i, 2).Value = cab[i].Valor;
        }

        // ---------- tabela ----------
        var titulos = new[]
        {
            "Item", "Part Number", "Descrição", "Modelo", "Serial Number", "TAG", "Origem", "Prazo (dias)", "Qtd",
            "Peso unit. (kg)", "Custo unit. (R$)", "Frete unit. (R$)", "TA unit. (R$)", "Custo unit. c/ frete e TA (R$)",
            "Custo total (R$)", "Frete total (R$)", "TA total (R$)",
            $"Preço meta ({moeda.Rotulo()})", $"Preço unit. ({moeda.Rotulo()})", $"Preço total ({moeda.Rotulo()})",
        };
        const int linhaTitulo = 11;
        for (var c = 0; c < titulos.Length; c++)
        {
            var cel = ws.Cell(linhaTitulo, c + 1);
            cel.Value = titulos[c];
            cel.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(azul)
                .Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        }
        ws.Row(linhaTitulo).Height = 32;

        var lin = linhaTitulo + 1;
        foreach (var l in linhas)
        {
            ws.Cell(lin, 1).Value = l.Item.ToString("D2");
            ws.Cell(lin, 2).Value = l.PartNumber;
            ws.Cell(lin, 3).Value = l.Descricao;
            ws.Cell(lin, 4).Value = l.Modelo;
            ws.Cell(lin, 5).Value = l.SerialNumber;
            ws.Cell(lin, 6).Value = l.Tag;
            ws.Cell(lin, 7).Value = l.Origem;
            if (l.PrazoDias > 0) ws.Cell(lin, 8).Value = l.PrazoDias;
            ws.Cell(lin, 9).Value = l.Quantidade;
            if (l.PesoKg > 0) ws.Cell(lin, 10).Value = l.PesoKg;
            if (l.CustoUnitario is decimal cu)
            {
                ws.Cell(lin, 11).Value = cu;
                ws.Cell(lin, 12).Value = l.FreteUnitario;
                ws.Cell(lin, 13).Value = l.TaUnitario;
                ws.Cell(lin, 14).FormulaA1 = $"K{lin}+L{lin}+M{lin}";
                ws.Cell(lin, 15).FormulaA1 = $"N{lin}*I{lin}";
                ws.Cell(lin, 16).FormulaA1 = $"L{lin}*I{lin}";
                ws.Cell(lin, 17).FormulaA1 = $"M{lin}*I{lin}";
            }
            else ws.Cell(lin, 11).Value = "sem custo";
            if (l.PrecoMeta > 0) ws.Cell(lin, 18).Value = l.PrecoMeta;
            ws.Cell(lin, 19).Value = l.PrecoUnitario;
            ws.Cell(lin, 20).Value = l.PrecoTotal;

            // o item com TA fica marcado: o TA e o preço que ele formou
            if (l.TaUnitario > 0m)
            {
                foreach (var col in new[] { 13, 14, 17, 18, 19, 20 })
                    ws.Cell(lin, col).Style.Fill.SetBackgroundColor(amarelo);
                ws.Cell(lin, 13).Style.Font.SetBold();
                ws.Cell(lin, 19).Style.Font.SetBold();
            }
            lin++;
        }

        // ---------- totais ----------
        var ultima = lin - 1;
        ws.Cell(lin, 1).Value = "TOTAL";
        if (ultima > linhaTitulo)
        {
            foreach (var col in new[] { "I", "O", "P", "Q", "T" })
                ws.Cell($"{col}{lin}").FormulaA1 = $"SUM({col}{linhaTitulo + 1}:{col}{ultima})";
        }
        ws.Range(lin, 1, lin, titulos.Length).Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Medium);

        // ---------- formatos e legenda ----------
        ws.Range(linhaTitulo + 1, 10, lin, 10).Style.NumberFormat.Format = "#,##0.00";
        ws.Range(linhaTitulo + 1, 11, lin, 17).Style.NumberFormat.Format = fmtReal;
        ws.Range(linhaTitulo + 1, 18, lin, 20).Style.NumberFormat.Format = fmtVenda;
        ws.Range(linhaTitulo, 1, lin, titulos.Length).Style.Border.SetInsideBorder(XLBorderStyleValues.Hair)
            .Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.Cell(lin + 2, 1).Value = "Amarelo: item com TA — o custo acrescentado para o preço chegar no preço meta.";
        ws.Cell(lin + 2, 1).Style.Fill.SetBackgroundColor(amarelo);
        ws.Cell(lin + 3, 1).Value = "Frete: o frete da proposta dividido pelo peso (peso unit. × quantidade); sem peso em nenhum item, pelo valor.";
        ws.Cell(lin + 4, 1).Value = "O preço é o custo (com o frete e o TA) × o fator do pricing, no nível de impostos escolhido; uso interno, não vai ao cliente.";
        ws.Cell(lin + 4, 1).Style.Font.SetItalic();

        ws.Column(1).Width = 18; ws.Column(2).Width = 16; ws.Column(3).Width = 38; ws.Column(4).Width = 24;
        ws.Column(5).Width = 16; ws.Column(6).Width = 12; ws.Column(7).Width = 22; ws.Column(8).Width = 9;
        ws.Column(9).Width = 7; ws.Column(10).Width = 10;
        for (var c = 11; c <= titulos.Length; c++) ws.Column(c).Width = 15;
        ws.SheetView.FreezeRows(linhaTitulo);
        // impressa, a tabela cabe na largura de uma folha deitada
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.Margins.Left = 0.4; ws.PageSetup.Margins.Right = 0.4;

        using var fluxo = new MemoryStream();
        wb.SaveAs(fluxo);
        return fluxo.ToArray();
    }
}
