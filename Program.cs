using AfmHsa;

// Modo navegador: o mesmo servidor do modo desktop (BackendHost), rodando como
// site — dotnet run, servidor central, testes. Para o dia a dia dos usuários,
// o programa é a janela (desktop/), aberta pelo lançador da rede (launcher/).
var app = BackendHost.CreateApp(args);

// Abre o navegador sozinho ao iniciar (desligue com "OpenBrowser": false no
// appsettings quando rodar como servidor central).
if (app.Configuration.GetValue("OpenBrowser", true))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            var url = (app.Urls.FirstOrDefault() ?? $"http://localhost:{BackendHost.PortaPadrao}")
                .Replace("0.0.0.0", "localhost").Replace("[::]", "localhost");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* sem navegador disponível: apenas ignora */ }
    });
}

app.Run();
