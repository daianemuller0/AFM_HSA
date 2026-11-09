using System.Security.Claims;
using AfmHsa.Components;
using AfmHsa.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AfmHsa;

/// <summary>
/// Fábrica do servidor do sistema (Kestrel + Blazor Server + endpoints), usada
/// em dois modos — como no axiaisvent:
///  - navegador (Program.cs): roda como site (dotnet run, servidor central);
///  - desktop (desktop/AfmHsa.Desktop): o MESMO servidor sobe dentro do
///    processo da janela e aparece num WebView2 — sem a janela preta, e
///    fechar a janela encerra o servidor.
/// </summary>
public static class BackendHost
{
    /// <summary>A porta preferida; ocupada, vale a próxima livre.</summary>
    public const int PortaPadrao = 5090;

    /// <param name="urls">endereço fixo (plano B do desktop: porta 0, o Windows escolhe)</param>
    /// <param name="loopback">modo desktop: só em 127.0.0.1 (ninguém da rede alcança a janela do usuário)</param>
    public static WebApplication CreateApp(string[] args, string[]? urls = null, bool loopback = false)
    {
        // A pasta do app (telas, modelos do Word/pricing): a atual quando ela tem o
        // wwwroot (dotnet run no projeto); senão a do executável — aberto da rede
        // ou pelo lançador, a pasta atual do Windows pode ser outra.
        var pastaDoApp = Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"))
            ? Directory.GetCurrentDirectory()
            : AppContext.BaseDirectory;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = pastaDoApp });

        // Fora do ambiente "Development" (o desktop) o dotnet run não carrega o
        // manifesto dos arquivos do wwwroot; no publicado o wwwroot é físico e
        // isto não faz nada.
        builder.WebHost.UseStaticWebAssets();

        // Endereço: o pedido (desktop) → --urls / ASPNETCORE_URLS (servidor
        // central: AfmHsa.exe --urls http://0.0.0.0:5090) → a primeira porta
        // livre a partir da 5090 (o sistema já aberto não impede de abrir).
        var explicito = builder.Configuration["urls"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        string enderecos;
        if (urls is { Length: > 0 })
        {
            enderecos = string.Join(";", urls);
            builder.WebHost.UseUrls(urls);
        }
        else if (!string.IsNullOrEmpty(explicito))
        {
            enderecos = explicito;
        }
        else
        {
            var preferida = builder.Configuration.GetValue("Porta", PortaPadrao);
            var porta = Enumerable.Range(preferida, 20).FirstOrDefault(PortaLivre);
            if (porta == 0) porta = preferida;
            if (porta != preferida)
                Console.WriteLine($"A porta {preferida} está em uso (o AFM HSA já pode estar aberto em outra janela). Abrindo na {porta}.");
            enderecos = $"http://{(loopback ? "127.0.0.1" : "localhost")}:{porta}";
            builder.WebHost.UseUrls(enderecos);
        }

        // --- Blazor Server (componentes interativos no servidor) ---
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // --- Autenticação por cookie (sessão fica no navegador do usuário) ---
        builder.Services.AddCascadingAuthenticationState();
        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
            });
        builder.Services.AddAuthorization();

        // --- Dados: DuckDB (motor) sobre Parquet numa pasta (mesmo padrão do Licencas_HSA) ---
        // "Data:Folder" aponta para o caminho de rede do projeto:
        //   \\BZVCPFIL003\proj_ramires$\DB\AFM_HSA
        var dataFolder = builder.Configuration["Data:Folder"] ?? "data";
        builder.Services.AddSingleton(new ParquetStore(dataFolder));

        // Repositórios por entidade (scoped) — camada de dados da base industrial.
        builder.Services.AddScoped<CompanyRepository>();
        builder.Services.AddScoped<ClientUnitRepository>();
        builder.Services.AddScoped<SalespersonRepository>();
        builder.Services.AddScoped<SegmentRepository>();
        builder.Services.AddScoped<EquipmentRepository>();
        builder.Services.AddScoped<InstalledBaseRepository>();
        builder.Services.AddScoped<PartRepository>();
        builder.Services.AddScoped<SalesRecordRepository>();
        builder.Services.AddScoped<AppUserRepository>();
        builder.Services.AddScoped<PerfisDeAcessoRepository>();
        builder.Services.AddScoped<DadosBancariosRepository>();
        builder.Services.AddScoped<VisitRepository>();
        builder.Services.AddScoped<OfferRepository>();
        builder.Services.AddScoped<OppOverrideRepository>();
        builder.Services.AddScoped<ConstructionRepository>();
        builder.Services.AddScoped<BomRepository>();
        builder.Services.AddScoped<NacionalizacaoRepository>();
        builder.Services.AddScoped<ConversaoRepository>();
        builder.Services.AddScoped<TempoRepository>();
        builder.Services.AddScoped<CondicoesComerciaisRepository>();
        builder.Services.AddScoped<ReferenciaDeCustoRepository>();
        builder.Services.AddScoped<PosicaoDaPlantaRepository>();
        builder.Services.AddScoped<CotacaoIntercompanyRepository>();
        builder.Services.AddScoped<ListasCompartilhadasRepository>();
        // o Outlook só abre direto quando o sistema roda no computador de quem usa (localhost)
        {
            var local = enderecos.Split(';').All(u => u.Contains("localhost", StringComparison.OrdinalIgnoreCase) || u.Contains("127.0.0.1"));
            builder.Services.AddSingleton(new OutlookLocal(local));
        }
        // Parâmetros da planilha de custos de importação (padrão: versão 02/03/2022);
        // a seção "Importacao" do appsettings substitui só os valores que trouxer.
        builder.Services.AddSingleton(sp =>
        {
            var p = new AfmHsa.Data.ParametrosImportacao();
            builder.Configuration.GetSection("Importacao").Bind(p);
            return p;
        });
        builder.Services.AddScoped<BrandingRepository>();
        builder.Services.AddScoped<ImportService>();

        // Motor de inteligência (oportunidades, alertas, lookups).
        builder.Services.AddScoped<IntelligenceService>();

        // Estado de idioma (PT/EN/ES) por sessão.
        builder.Services.AddScoped<LocState>();

        var app = builder.Build();

        // Popula o seed na primeira execução (hoje sem entidades — base nasce vazia).
        using (var scope = app.Services.CreateScope())
        {
            DbInitializer.Initialize(scope.ServiceProvider.GetRequiredService<ParquetStore>());
            // o que cada perfil vê, como o administrador configurou
            scope.ServiceProvider.GetRequiredService<PerfisDeAcessoRepository>().Carregar();
        }

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
        }

        app.UseStaticFiles();
        app.UseAntiforgery();
        app.UseAuthentication();
        app.UseAuthorization();

        // --- Endpoints de login/logout (precisam do HttpContext para gravar o cookie) ---
        // Login GERAL: uma credencial única para toda a equipe, definida no appsettings.json
        // (chaves Auth:Usuario e Auth:Senha).
        app.MapPost("/auth/login", async (HttpContext http, IConfiguration cfg, AfmHsa.Data.AppUserRepository users) =>
        {
            var form = await http.Request.ReadFormAsync();
            var email = form["usuario"].ToString().Trim();
            var senha = form["senha"].ToString();

            async Task SignIn(string id, string nome, string perfil, string salespersonId)
            {
                // o perfil (adm, gestao, aplicador) é o papel; o adm leva também "admin", que as telas antigas conferem
                perfil = Acessos.NormalizarPerfil(perfil);
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, id),
                    new(ClaimTypes.Name, nome),
                    new(ClaimTypes.Role, perfil),
                    new("salesperson_id", salespersonId),
                };
                if (perfil == Acessos.Adm) claims.Add(new(ClaimTypes.Role, "admin"));
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            }

            // 1) Usuário individual da base (tabela users) — e-mail + senha.
            // (a senha é gravada em hash; os cadastros antigos, em texto, também valem)
            var u = users.All().FirstOrDefault(x =>
                x.Email.Equals(email, StringComparison.OrdinalIgnoreCase) && Acessos.SenhaConfere(x.Password, senha));
            if (u != null)
            {
                if (!u.Ativo) return Results.Redirect("/login?error=2");
                await SignIn(u.Id, u.Nome, u.Perfil, u.SalespersonId);
                return Results.Redirect("/");
            }

            // 2) Fallback: credencial única da equipe (appsettings) — perfil admin.
            var cfgUsuario = cfg["Auth:Usuario"] ?? "howden";
            var cfgSenha = cfg["Auth:Senha"] ?? "howden2026";
            if (email.Equals(cfgUsuario, StringComparison.OrdinalIgnoreCase) && senha == cfgSenha)
            {
                await SignIn("equipe", "Equipe Howden", Acessos.Adm, "");
                return Results.Redirect("/");
            }

            return Results.Redirect("/login?error=1");
        }).DisableAntiforgery();

        app.MapPost("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).DisableAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }

    public static bool PortaLivre(int porta)
    {
        try
        {
            var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, porta);
            l.Start();
            l.Stop();
            return true;
        }
        catch (System.Net.Sockets.SocketException) { return false; }
    }
}
