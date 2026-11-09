using Microsoft.AspNetCore.Builder;

namespace AfmHsa.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Ambiente "Desktop": o servidor lê o appsettings.json principal (pasta
        // de dados, login) + o appsettings.Desktop.json (sem abrir navegador).
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Desktop");
        // a pasta do programa é a do .exe (wwwroot e appsettings ficam ao lado)
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new AppContexto(args));
    }
}

/// <summary>
/// A abertura: tela com a % → servidor (Kestrel) no próprio processo → janela
/// com o WebView2 → a tela de abertura some quando o sistema carrega.
/// Fechar a janela para o servidor e encerra o processo inteiro.
/// </summary>
internal sealed class AppContexto : ApplicationContext
{
    private readonly SplashForm _splash = new();
    private WebApplication? _backend;
    private MainForm? _janela;
    private bool _encerrando;

    public AppContexto(string[] args)
    {
        MainForm = _splash;
        _splash.Shown += async (_, _) => await IniciarAsync(args);
    }

    private async Task IniciarAsync(string[] args)
    {
        try
        {
            _splash.Reportar(5, "Iniciando…");
            _splash.Reportar(15, "Subindo o servidor interno…");
            _backend = await Task.Run(() => SubirBackendAsync(args));
            var url = (_backend.Urls.FirstOrDefault() ?? $"http://127.0.0.1:{BackendHost.PortaPadrao}")
                .Replace("localhost", "127.0.0.1");

            _splash.Reportar(55, "Servidor pronto — abrindo a janela…");
            _janela = new MainForm(url, _splash.Reportar, JanelaPronta);
            _janela.FormClosed += async (_, _) => await EncerrarAsync();
            _janela.Show();          // fica atrás da tela de abertura até o "pronto"
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Não foi possível iniciar o AFM HSA.\n\n" + ex.Message +
                "\n\nSe o problema for a pasta de dados na rede, confira o caminho em appsettings.json (Data:Folder).",
                "AFM HSA", MessageBoxButtons.OK, MessageBoxIcon.Error);
            await EncerrarAsync();
        }
    }

    /// <summary>
    /// Sobe o servidor em 127.0.0.1 na primeira porta livre a partir da 5090
    /// (a mesma porta mantém o login entre aberturas). Plano B com a porta 0
    /// (o Windows escolhe): alguém pode ter pegado a porta entre verificar e usar.
    /// </summary>
    private static async Task<WebApplication> SubirBackendAsync(string[] args)
    {
        var app = BackendHost.CreateApp(args, loopback: true);
        try
        {
            await app.StartAsync();
            return app;
        }
        catch (IOException)
        {
            await app.DisposeAsync();
            app = BackendHost.CreateApp(args, new[] { "http://127.0.0.1:0" });
            await app.StartAsync();
            return app;
        }
    }

    /// <summary>A primeira tela carregou: some a tela de abertura, aparece o sistema.</summary>
    private void JanelaPronta()
    {
        if (_splash.IsDisposed) return;
        _splash.BeginInvoke(() =>
        {
            MainForm = null;      // fechar a tela de abertura não pode encerrar o app
            _splash.Close();
            _janela?.Activate();
        });
    }

    private async Task EncerrarAsync()
    {
        if (_encerrando) return;
        _encerrando = true;
        try
        {
            if (_backend is not null)
            {
                await _backend.StopAsync(TimeSpan.FromSeconds(3));
                await _backend.DisposeAsync();
            }
        }
        catch { /* encerrando de qualquer forma */ }
        ExitThread();
    }
}
