using System.Diagnostics;
using System.Runtime.Versioning;

namespace AfmHsa.Data;

/// <summary>
/// Abre o e-mail direto no Outlook do computador, como as macros da planilha
/// de pricing (CreateObject("Outlook.Application") → CreateItem(0) → Display):
/// a mensagem nova aparece com destinatários, assunto, corpo, anexos e a
/// assinatura de quem usa (o corpo entra antes dela).
///
/// Só vale quando o sistema roda no próprio computador de quem clica (o modo
/// por usuário, em localhost) e no Windows. Se o Outlook não responder, abre
/// o .eml no programa de e-mail padrão; se nem isso der, a tela baixa o .eml.
/// </summary>
public sealed class OutlookLocal
{
    private readonly bool _local;

    /// <param name="local">o sistema está em localhost: o Outlook que abre é o de quem está usando</param>
    public OutlookLocal(bool local) => _local = local;

    public bool Disponivel => _local && OperatingSystem.IsWindows();

    /// <summary>Abre a mensagem; null = abriu, senão o motivo de não ter aberto (a tela então baixa o .eml).</summary>
    public string? Abrir(EmailDaProposta email, IReadOnlyList<AnexoDoEmail> anexos)
    {
        if (!Disponivel) return "O sistema não está rodando neste computador (Windows) para abrir o Outlook.";

        var pasta = Path.Combine(Path.GetTempPath(), "AFM_HSA", "emails", Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(pasta);
        var caminhos = new List<string>();
        foreach (var a in anexos)
        {
            var caminho = Path.Combine(pasta, PricingExcel.Limpo(a.Nome));
            File.WriteAllBytes(caminho, a.Conteudo);
            caminhos.Add(caminho);
        }

        // 1) o Outlook pela automação (igual à macro), numa thread STA
        string? erro = null;
        var t = new Thread(() =>
        {
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("o Outlook só abre no Windows");
                AbrirPeloOutlook(email, caminhos);
            }
            catch (Exception ex) { erro = ex.Message; }
        });
        if (OperatingSystem.IsWindows()) t.SetApartmentState(ApartmentState.STA);
        t.Start();
        if (!t.Join(TimeSpan.FromSeconds(60))) erro = "o Outlook demorou a responder";
        if (erro is null) return null;

        // 2) o .eml no programa de e-mail padrão (abre como mensagem nova)
        try
        {
            var eml = Path.Combine(pasta, PricingExcel.Limpo(email.Assunto.TrimEnd('.')) + ".eml");
            File.WriteAllBytes(eml, EmailsDaProposta.Eml(email, anexos));
            Process.Start(new ProcessStartInfo(eml) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            return $"Não deu para abrir o Outlook ({erro}) nem o programa de e-mail ({ex.Message}).";
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AbrirPeloOutlook(EmailDaProposta email, IReadOnlyList<string> anexos)
    {
        var tipo = Type.GetTypeFromProgID("Outlook.Application") ?? throw new InvalidOperationException("o Outlook não está instalado");
        dynamic outlook = Activator.CreateInstance(tipo) ?? throw new InvalidOperationException("o Outlook não abriu");
        dynamic msg = outlook.CreateItem(0);   // 0 = olMailItem
        msg.To = email.Para ?? "";
        msg.CC = email.Cc ?? "";
        msg.Subject = email.Assunto ?? "";
        foreach (var a in anexos) msg.Attachments.Add(a);
        // Display antes do corpo: assim o Outlook já pôs a assinatura, e o texto entra antes dela (como a macro)
        msg.Display(false);
        string assinatura = msg.HTMLBody ?? "";
        var corpo = assinatura.IndexOf("<body", StringComparison.OrdinalIgnoreCase) is var b and >= 0
                    && assinatura.IndexOf('>', b) is var fim and > 0
            ? assinatura[..(fim + 1)] + email.Html + "<br>" + assinatura[(fim + 1)..]
            : email.Html + "<br>" + assinatura;
        msg.HTMLBody = corpo;
    }
}
