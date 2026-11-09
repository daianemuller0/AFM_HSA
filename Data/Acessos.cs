using System.Security.Claims;
using System.Security.Cryptography;
using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>
/// Quem vê o quê. Três perfis — Administrador, Gestão e Aplicador. O que cada
/// perfil vê é configurado pelo administrador (Dados e Importação → Logins e
/// acessos → "O que cada perfil vê"); cada login segue o do perfil ou tem a
/// sua lista própria. Só o administrador cria logins e muda os acessos.
/// </summary>
public static class Acessos
{
    public const string Adm = "adm";
    public const string Gestao = "gestao";
    public const string Aplicador = "aplicador";

    public static readonly (string Codigo, string Nome, string Descricao)[] Perfis =
    {
        (Adm, "Administrador", "Vê tudo, cria logins e escolhe o que cada um vê."),
        (Gestao, "Gestão", "Vê o que o administrador liberar para a gestão (padrão: tudo, menos os logins)."),
        (Aplicador, "Aplicador", "Vê o que o administrador liberar para os aplicadores (padrão: Comercial e Controle, menos Dados e Importação)."),
    };

    /// <summary>Os grupos do menu, na ordem dele.</summary>
    public static readonly string[] Grupos = { "Gestão de Vendas", "Comercial", "Controle" };

    /// <param name="Rotas">os caminhos da página (o primeiro é o do menu); vale também o que começa com eles</param>
    public sealed record Pagina(string Chave, string Nome, string Grupo, string[] Rotas);

    /// <summary>
    /// As páginas do sistema, nos grupos do menu. Telas de detalhe vão junto
    /// com a página-mãe. Clientes e Unidades e Nacionalização ficam em
    /// Controle (o menu também as mostra em Gestão de Vendas para quem vê esse grupo).
    /// </summary>
    public static readonly Pagina[] Paginas =
    {
        new("dashboard", "Central de Inteligência", "Gestão de Vendas", new[] { "/dashboard", "/oportunidades", "/previsao", "/planejamento", "/pecas" }),
        new("carteira", "Plano de Ação Comercial", "Gestão de Vendas", new[] { "/carteira" }),
        new("diretoria", "Painel Executivo", "Gestão de Vendas", new[] { "/diretoria" }),
        new("historico", "Histórico Aftermarket", "Gestão de Vendas", new[] { "/historico" }),
        new("visitas", "Ações em Campo", "Gestão de Vendas", new[] { "/visitas" }),
        new("alertas", "Alertas Preditivos", "Gestão de Vendas", new[] { "/alertas" }),
        new("gerar-proposta", "Gerar Proposta", "Comercial", new[] { "/gerar-proposta", "/ofertas/nova" }),
        new("ofertas", "Propostas", "Comercial", new[] { "/ofertas" }),
        new("referencias", "Referências de preço", "Comercial", new[] { "/referencias-custo" }),
        new("intercompany", "Cotação Intercompany", "Comercial", new[] { "/cotacao-intercompany" }),
        new("tempos", "Tempo das propostas", "Comercial", new[] { "/tempos-proposta" }),
        new("clientes", "Clientes e Unidades", "Controle", new[] { "/clientes", "/equipamentos", "/base-instalada" }),
        new("nacionalizacao", "Nacionalização", "Controle", new[] { "/nacionalizacao" }),
        new("dados", "Dados e Importação", "Controle", new[] { "/dados-marca", "/vendedores", "/marca", "/importar", "/conversao" }),
    };

    /// <summary>O padrão do aplicador: o grupo Comercial e o Controle, menos Dados e Importação.</summary>
    private static readonly string[] DoAplicador =
        Paginas.Where(p => p.Grupo == "Comercial" || (p.Grupo == "Controle" && p.Chave != "dados")).Select(p => p.Chave).ToArray();

    /// <summary>O que o administrador configurou para cada perfil (lido do disco ao iniciar; vale para todos na hora).</summary>
    private static Dictionary<string, string[]> _configurados = new();

    /// <summary>Troca a configuração dos perfis (ver <see cref="PerfisDeAcessoRepository"/>).</summary>
    public static void Configurar(IReadOnlyDictionary<string, string[]> porPerfil) =>
        _configurados = porPerfil.ToDictionary(x => NormalizarPerfil(x.Key), x => x.Value.Where(k => Paginas.Any(p => p.Chave == k)).ToArray());

    /// <summary>O padrão de fábrica do perfil (antes de qualquer configuração).</summary>
    public static IReadOnlyList<string> PadraoDeFabrica(string? perfil) => NormalizarPerfil(perfil) switch
    {
        Aplicador => DoAplicador,
        _ => Paginas.Select(p => p.Chave).ToArray(),
    };

    /// <summary>O perfil foi configurado pelo administrador (diferente do padrão de fábrica)?</summary>
    public static bool Configurado(string? perfil) => _configurados.ContainsKey(NormalizarPerfil(perfil));

    public static string NormalizarPerfil(string? perfil) => (perfil ?? "").Trim().ToLowerInvariant() switch
    {
        "adm" or "admin" or "administrador" => Adm,
        // os perfis antigos do cadastro: diretor e gestor viram Gestão; vendedor, Aplicador
        "gestao" or "gestão" or "gestor" or "gerente" or "diretor" => Gestao,
        _ => Aplicador,
    };

    public static string NomeDoPerfil(string? perfil)
    {
        var p = NormalizarPerfil(perfil);
        return Perfis.First(x => x.Codigo == p).Nome;
    }

    /// <summary>O que o perfil vê: o configurado pelo administrador, senão o padrão de fábrica. O administrador vê tudo.</summary>
    public static IReadOnlyList<string> PadraoDoPerfil(string? perfil)
    {
        var p = NormalizarPerfil(perfil);
        if (p == Adm) return Paginas.Select(x => x.Chave).ToArray();
        return _configurados.TryGetValue(p, out var lista) && lista.Length > 0 ? lista : PadraoDeFabrica(p);
    }

    /// <summary>As páginas que o usuário vê: as escolhidas pelo administrador, ou as do perfil. O administrador vê tudo.</summary>
    public static HashSet<string> PaginasDo(string? perfil, string? escolhidas)
    {
        if (NormalizarPerfil(perfil) == Adm) return Paginas.Select(p => p.Chave).ToHashSet();
        var lista = (escolhidas ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => Paginas.Any(p => p.Chave == k)).ToList();
        return (lista.Count > 0 ? lista : PadraoDoPerfil(perfil)).ToHashSet();
    }

    /// <summary>A página de um caminho ("/gerar-proposta/abc" → gerar-proposta); null = não é página controlada.</summary>
    public static Pagina? PaginaDoCaminho(string caminho)
    {
        var c = "/" + (caminho ?? "").Split('?', '#')[0].Trim('/').ToLowerInvariant();
        return Paginas
            .SelectMany(p => p.Rotas.Select(r => (p, r)))
            .Where(x => c == x.r || c.StartsWith(x.r + "/", StringComparison.Ordinal))
            .OrderByDescending(x => x.r.Length)          // "/ofertas/nova" ganha de "/ofertas"
            .Select(x => x.p).FirstOrDefault();
    }

    /// <summary>Pode abrir o caminho? Caminho fora do catálogo (login, erro, início) é livre.</summary>
    public static bool PodeAbrir(IReadOnlySet<string> paginas, string caminho) =>
        PaginaDoCaminho(caminho) is not { } p || paginas.Contains(p.Chave);

    /// <summary>A primeira página que o usuário vê (para onde o início leva).</summary>
    public static string Inicio(IReadOnlySet<string> paginas) =>
        Paginas.FirstOrDefault(p => paginas.Contains(p.Chave))?.Rotas[0] ?? "/login";

    // ================= quem está logado =================

    public const string ClaimPaginas = "paginas";

    /// <summary>As páginas de quem está logado, lidas do cadastro (a mudança do administrador vale na próxima tela).</summary>
    public static HashSet<string> PaginasDe(ClaimsPrincipal user, AppUserRepository usuarios)
    {
        if (user.Identity?.IsAuthenticated != true) return new();
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var perfil = user.FindFirst(ClaimTypes.Role)?.Value;
        if (id.Length > 0 && id != "equipe" && usuarios.All().FirstOrDefault(u => u.Id == id) is { } u)
            return u.Ativo ? PaginasDo(u.Perfil, u.Paginas) : new();
        return PaginasDo(perfil, user.FindFirst(ClaimPaginas)?.Value);
    }

    public static bool EhAdm(ClaimsPrincipal user) => NormalizarPerfil(user.FindFirst(ClaimTypes.Role)?.Value) == Adm;

    // ================= senha =================

    private const int Voltas = 120_000;

    /// <summary>"pbkdf2$voltas$sal$hash" (a senha nunca é gravada como foi digitada).</summary>
    public static string HashDaSenha(string senha)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(senha, sal, Voltas, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Voltas}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Confere a senha; aceita também a gravada em texto pelos cadastros antigos.</summary>
    public static bool SenhaConfere(string gravada, string digitada)
    {
        if (string.IsNullOrEmpty(gravada) || string.IsNullOrEmpty(digitada)) return false;
        var partes = gravada.Split('$');
        if (partes.Length != 4 || partes[0] != "pbkdf2") return gravada == digitada;
        if (!int.TryParse(partes[1], out var voltas)) return false;
        try
        {
            var sal = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var hash = Rfc2898DeriveBytes.Pbkdf2(digitada, sal, voltas, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(hash, esperado);
        }
        catch (FormatException) { return false; }
    }
}

/// <summary>
/// Grava o que cada perfil vê (entidade "perfis_acesso": perfil → páginas).
/// Ao gravar, a configuração vale na hora para todos os logins que seguem o
/// perfil (os com lista própria continuam com a deles).
/// </summary>
public class PerfisDeAcessoRepository
{
    private const string Entidade = "perfis_acesso";
    private readonly ParquetStore _s;
    public PerfisDeAcessoRepository(ParquetStore s) => _s = s;

    /// <summary>Lê do disco e aplica (na inicialização).</summary>
    public void Carregar() => Acessos.Configurar(Ler());

    public Dictionary<string, string[]> Ler() => _s.ReadLatest(Entidade, "id, paginas",
            r => (Perfil: Fmt.S(r, 0), Paginas: Fmt.S(r, 1)))
        .Where(x => x.Perfil.Length > 0)
        .ToDictionary(x => x.Perfil, x => x.Paginas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>Grava as páginas do perfil; vazio = volta ao padrão de fábrica.</summary>
    public void Gravar(string perfil, IEnumerable<string> paginas)
    {
        var p = Acessos.NormalizarPerfil(perfil);
        var lista = paginas.ToList();
        var fabrica = Acessos.PadraoDeFabrica(p).ToHashSet();
        if (lista.Count == 0 || lista.ToHashSet().SetEquals(fabrica))
            _s.WriteRow(Entidade, new KeyValuePair<string, object?>[] { new("id", p) }, deleted: true);
        else
            _s.WriteRow(Entidade, new KeyValuePair<string, object?>[] { new("id", p), new("paginas", string.Join(",", lista)) });
        Carregar();
    }
}
