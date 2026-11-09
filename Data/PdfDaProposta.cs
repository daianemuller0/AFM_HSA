using System.Diagnostics;
using System.Runtime.Versioning;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AfmHsa.Data;

/// <summary>
/// A proposta em PDF: o mesmo Word gerado, convertido. No Windows usa o
/// Microsoft Word do computador (automação, como o Outlook), que atualiza o
/// índice e exporta igual ao "Salvar como PDF"; sem o Word, o LibreOffice
/// (instalado no computador ou no servidor). Sem nenhum dos dois, avisa.
/// </summary>
public static class PdfDaProposta
{
    /// <summary>Converte o .docx em PDF. Lança <see cref="InvalidOperationException"/> com a explicação quando não dá.</summary>
    public static byte[] Converter(byte[] docx)
    {
        var pasta = Path.Combine(Path.GetTempPath(), "AFM_HSA", "pdf", Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(pasta);
        var entrada = Path.Combine(pasta, "proposta.docx");
        var saida = Path.Combine(pasta, "proposta.pdf");
        File.WriteAllBytes(entrada, SemAtualizarAoAbrir(docx));

        var motivos = new List<string>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (PeloWord(entrada, saida) is { } erroWord) motivos.Add("Word: " + erroWord);
                else if (File.Exists(saida)) return File.ReadAllBytes(saida);
            }
            if (PeloLibreOffice(entrada, pasta) is { } erroLo) motivos.Add("LibreOffice: " + erroLo);
            else if (File.Exists(saida)) return File.ReadAllBytes(saida);

            throw new InvalidOperationException(
                "para gerar o PDF é preciso o Microsoft Word ou o LibreOffice instalado neste computador" +
                (motivos.Count > 0 ? $" ({string.Join("; ", motivos)})" : "") + ". O Word continua disponível.");
        }
        finally
        {
            try { Directory.Delete(pasta, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// O Word gerado pede para atualizar os campos ao abrir (o índice); na
    /// conversão isso abriria uma pergunta que ninguém vê. A cópia que vira
    /// PDF vai sem o pedido (o Word atualiza o índice na própria conversão).
    /// </summary>
    private static byte[] SemAtualizarAoAbrir(byte[] docx)
    {
        using var ms = new MemoryStream();
        ms.Write(docx);
        using (var doc = WordprocessingDocument.Open(ms, true))
        {
            var settings = doc.MainDocumentPart?.DocumentSettingsPart?.Settings;
            if (settings is not null)
            {
                settings.RemoveAllChildren<UpdateFieldsOnOpen>();
                settings.Save();
            }
        }
        return ms.ToArray();
    }

    // ================= Microsoft Word (Windows) =================

    /// <returns>null = converteu; senão o motivo</returns>
    private static string? PeloWord(string entrada, string saida)
    {
        string? erro = null;
        var t = new Thread(() =>
        {
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("só no Windows");
                ConverterNoWord(entrada, saida);
            }
            catch (Exception ex) { erro = ex.Message; }
        });
        if (OperatingSystem.IsWindows()) t.SetApartmentState(ApartmentState.STA);
        t.Start();
        if (!t.Join(TimeSpan.FromSeconds(120))) erro = "o Word demorou a responder";
        return erro;
    }

    [SupportedOSPlatform("windows")]
    private static void ConverterNoWord(string entrada, string saida)
    {
        var tipo = Type.GetTypeFromProgID("Word.Application") ?? throw new InvalidOperationException("o Word não está instalado");
        dynamic word = Activator.CreateInstance(tipo) ?? throw new InvalidOperationException("o Word não abriu");
        dynamic? doc = null;
        try
        {
            word.Visible = false;
            word.DisplayAlerts = 0;   // wdAlertsNone
            doc = word.Documents.Open(entrada, false, true, false);   // FileName, ConfirmConversions, ReadOnly, AddToRecentFiles
            // o índice com as páginas certas, como no "atualizar campos" do Word
            int indices = doc.TablesOfContents.Count;
            for (var i = 1; i <= indices; i++) doc.TablesOfContents.Item(i).Update();
            doc.ExportAsFixedFormat(saida, 17);   // wdExportFormatPDF
        }
        finally
        {
            try { doc?.Close(false); } catch { /* já fechado */ }
            try { word.Quit(false); } catch { /* já saiu */ }
        }
    }

    // ================= LibreOffice =================

    /// <returns>null = converteu; senão o motivo</returns>
    private static string? PeloLibreOffice(string entrada, string pasta)
    {
        var soffice = AcharLibreOffice();
        if (soffice is null) return "não instalado";
        // um perfil só desta conversão: não briga com um LibreOffice aberto pelo usuário
        var perfil = new Uri(Path.Combine(pasta, "perfil-lo")).AbsoluteUri;
        var psi = new ProcessStartInfo(soffice)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in new[] { $"-env:UserInstallation={perfil}", "--headless", "--convert-to", "pdf", "--outdir", pasta, entrada })
            psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return "não abriu";
            if (!p.WaitForExit(120_000))
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return "demorou a responder";
            }
            return p.ExitCode == 0 ? null : $"saiu com erro {p.ExitCode}";
        }
        catch (System.ComponentModel.Win32Exception ex) { return ex.Message; }
    }

    private static string? AcharLibreOffice()
    {
        var candidatos = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                if (pf.Length > 0) candidatos.Add(Path.Combine(pf, "LibreOffice", "program", "soffice.exe"));
        }
        else
        {
            candidatos.AddRange(new[] { "/usr/bin/soffice", "/usr/bin/libreoffice", "/usr/local/bin/soffice", "/opt/libreoffice/program/soffice",
                                        "/Applications/LibreOffice.app/Contents/MacOS/soffice" });
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidatos.Add(Path.Combine(dir, OperatingSystem.IsWindows() ? "soffice.exe" : "soffice"));
        return candidatos.FirstOrDefault(File.Exists);
    }
}
