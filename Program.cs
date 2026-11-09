using System.Security.Claims;
using AfmHsa.Components;
using AfmHsa.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Porta padrão (modo por-usuário). Para "servidor central", rode com:
//   AfmHsa.exe --urls http://0.0.0.0:5090
if (string.IsNullOrEmpty(builder.Configuration["urls"]) &&
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    // se a 5090 já estiver ocupada (o sistema já aberto em outra janela, ou outro programa),
    // sobe na próxima porta livre em vez de parar com "address already in use"
    var porta = Enumerable.Range(5090, 20).FirstOrDefault(PortaLivre);
    if (porta == 0) porta = 5090;
    if (porta != 5090)
        Console.WriteLine($"A porta 5090 está em uso (o AFM HSA já pode estar aberto em outra janela). Abrindo em http://localhost:{porta}");
    builder.WebHost.UseUrls($"http://localhost:{porta}");
}

static bool PortaLivre(int porta)
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
    var urls = builder.Configuration["urls"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost";
    var local = urls.Split(';').All(u => u.Contains("localhost", StringComparison.OrdinalIgnoreCase) || u.Contains("127.0.0.1"));
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

// Modo por-usuário: abre o navegador sozinho ao iniciar.
// Desligue com "OpenBrowser": false no appsettings quando rodar como servidor central.
if (builder.Configuration.GetValue("OpenBrowser", true))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            var url = (app.Urls.FirstOrDefault() ?? "http://localhost:5090")
                .Replace("0.0.0.0", "localhost").Replace("[::]", "localhost");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* sem navegador disponível: apenas ignora */ }
    });
}

app.Run();
