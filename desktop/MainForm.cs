using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AfmHsa.Desktop;

/// <summary>
/// A janela do sistema: um WebView2 (o Edge embutido) ocupando tudo, apontando
/// para o servidor que roda dentro deste mesmo processo.
/// </summary>
public sealed class MainForm : Form
{
    private readonly string _homeUrl;
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly Action<int, string> _progresso;
    private readonly Action _pronto;
    private bool _avisouPronto;

    public MainForm(string url, Action<int, string> progresso, Action pronto)
    {
        _homeUrl = url;
        _progresso = progresso;
        _pronto = pronto;

        Text = "AFM HSA · Aftermarket Intelligence — Howden";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1440, 900);
        MinimumSize = new Size(1000, 640);
        WindowState = FormWindowState.Maximized;

        var menu = new MenuStrip();
        var inicio = new ToolStripMenuItem("Início");
        inicio.Click += (_, _) => _webView.CoreWebView2?.Navigate(_homeUrl);
        var voltar = new ToolStripMenuItem("← Voltar") { ShortcutKeys = Keys.Alt | Keys.Left, ShowShortcutKeys = false };
        voltar.Click += (_, _) => { if (_webView.CoreWebView2?.CanGoBack == true) _webView.CoreWebView2.GoBack(); };
        var recarregar = new ToolStripMenuItem("Recarregar") { ShortcutKeys = Keys.F5, ShowShortcutKeys = true };
        recarregar.Click += (_, _) => _webView.CoreWebView2?.Reload();
        var downloads = new ToolStripMenuItem("Arquivos baixados");
        downloads.Click += (_, _) => AbrirDownloads();
        menu.Items.Add(inicio);
        menu.Items.Add(voltar);
        menu.Items.Add(recarregar);
        menu.Items.Add(downloads);
        MainMenuStrip = menu;

        Controls.Add(_webView);   // Fill primeiro
        Controls.Add(menu);       // Top depois

        Load += MainForm_Load;
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        try
        {
            _progresso(70, "Preparando o navegador embutido…");

            // Fixed Version (pasta WebView2Runtime ao lado do .exe) quando existir;
            // senão o Evergreen do Windows/Edge
            var pastaFixa = Path.Combine(AppContext.BaseDirectory, "WebView2Runtime");
            var pastaDados = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HowdenAFM", "WebView2UserData");
            var ambiente = await CoreWebView2Environment.CreateAsync(
                Directory.Exists(pastaFixa) ? pastaFixa : null, pastaDados);

            await _webView.EnsureCoreWebView2Async(ambiente);

            // Word, PDF, pricing e e-mails podem baixar vários arquivos seguidos:
            // sem isto o navegador embutido pararia para perguntar a cada um
            _webView.CoreWebView2.PermissionRequested += (_, pedido) =>
            {
                if (pedido.PermissionKind == CoreWebView2PermissionKind.MultipleAutomaticDownloads)
                    pedido.State = CoreWebView2PermissionState.Allow;
            };

            // links para fora do sistema (sites, mapas) abrem no navegador padrão
            _webView.CoreWebView2.NewWindowRequested += (_, pedido) =>
            {
                if (!pedido.Uri.StartsWith(_homeUrl, StringComparison.OrdinalIgnoreCase))
                {
                    pedido.Handled = true;
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pedido.Uri) { UseShellExecute = true }); }
                    catch { /* sem navegador: ignora */ }
                }
            };

            _progresso(85, "Carregando o sistema…");
            _webView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                if (_avisouPronto) return;
                _avisouPronto = true;
                _progresso(100, "Pronto!");
                _pronto();
            };
            _webView.CoreWebView2.Navigate(_homeUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "Este computador não tem o WebView2 Runtime (vem com o Windows 10/11 atualizado ou o Edge).\n\n" +
                "Peça ao TI para instalar o \"Microsoft Edge WebView2 Runtime (Evergreen)\" — download em:\n" +
                "https://developer.microsoft.com/microsoft-edge/webview2/",
                "AFM HSA — componente faltando", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não consegui abrir a janela do sistema:\n\n" + ex.Message,
                "AFM HSA", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    /// <summary>A pasta de Downloads do usuário (onde o Word, o PDF e as planilhas caem).</summary>
    private static void AbrirDownloads()
    {
        try
        {
            var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pasta) { UseShellExecute = true });
        }
        catch { /* sem pasta: ignora */ }
    }
}
