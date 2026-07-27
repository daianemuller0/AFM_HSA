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
    builder.WebHost.UseUrls("http://localhost:5090");
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
builder.Services.AddScoped<EquipmentRepository>();
builder.Services.AddScoped<InstalledBaseRepository>();
builder.Services.AddScoped<PartRepository>();
builder.Services.AddScoped<SalesRecordRepository>();
builder.Services.AddScoped<AppUserRepository>();
builder.Services.AddScoped<VisitRepository>();
builder.Services.AddScoped<OfferRepository>();
builder.Services.AddScoped<OppOverrideRepository>();

// Motor de inteligência (oportunidades, alertas, lookups).
builder.Services.AddScoped<IntelligenceService>();

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
app.MapPost("/auth/login", async (HttpContext http, IConfiguration cfg) =>
{
    var form = await http.Request.ReadFormAsync();
    var usuario = form["usuario"].ToString().Trim();
    var senha = form["senha"].ToString();

    var cfgUsuario = cfg["Auth:Usuario"] ?? "howden";
    var cfgSenha = cfg["Auth:Senha"] ?? "howden2026";

    if (!usuario.Equals(cfgUsuario, StringComparison.OrdinalIgnoreCase) || senha != cfgSenha)
        return Results.Redirect("/login?error=1");

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, "equipe"),
        new(ClaimTypes.Name, "Equipe Howden"),
        new(ClaimTypes.Role, "admin"),
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/");
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
