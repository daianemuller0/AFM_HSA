using ClosedXML.Excel;

namespace AfmHsa.Data;

/// <summary>
/// Gera arquivos .xlsx para download (exportação de BOM, histórico etc.).
/// Tudo sai como texto: a planilha serve para ser editada e devolvida ao sistema
/// pela importação, então o que importa é o conteúdo voltar igual ao que saiu.
/// </summary>
public static class Planilha
{
    public static byte[] Xlsx(string aba, IReadOnlyList<string> cabecalho,
        IEnumerable<IReadOnlyList<string>> linhas)
    {
        using var wb = new XLWorkbook();
        // nome de aba no Excel: máx. 31 caracteres e sem : \ / ? * [ ]
        var nome = new string((aba ?? "Dados").Where(c => !":\\/?*[]".Contains(c)).ToArray());
        if (nome.Length == 0) nome = "Dados";
        if (nome.Length > 31) nome = nome[..31];
        var ws = wb.Worksheets.Add(nome);

        for (var i = 0; i < cabecalho.Count; i++)
        {
            var cel = ws.Cell(1, i + 1);
            cel.SetValue(cabecalho[i]);
            cel.Style.Font.Bold = true;
            cel.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF4FA");
        }

        var linha = 2;
        foreach (var l in linhas)
        {
            for (var i = 0; i < l.Count && i < cabecalho.Count; i++)
                ws.Cell(linha, i + 1).SetValue(l[i] ?? "");
            linha++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, 200, 8.0, 60.0);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
