using System.Security.Claims;
using System.Security.Cryptography;
using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>
/// Quem vê o quê. Três perfis — Administrador, Gestão e Aplicador — e, por
/// usuário, a lista de páginas que ele enxerga (vazia = a do perfil). Só o
/// administrador cria logins e muda o que cada um vê (Dados e Importação →
/// Logins e acessos).
/// </summary>
public static class Acessos
{
    public const string Adm = "adm";
    public const string Gestao = "gestao";
    public const string Aplicador = "aplicador";

    public static readonly (string Codigo, string Nome, string Descricao)[] Perfis =
    {
        (Adm, "Administrador", "Vê tudo, cria logins e escolhe o que cada um vê."),
        (Gestao, "Gestão", "Vê tudo (painéis, carteira, propostas, dados), menos os logins."),
        (Aplicador, "Aplicador", "Monta propostas: Gerar Proposta, Propostas, Referências de preço e Clientes."),
    };

    /// <param name="Rotas">os caminhos da página (o primeiro é o do menu); vale também o que começa com eles</param>
    public sealed record Pagina(string Chave, string Nome, string Grupo, string[] Rotas);

    /// <summary>As páginas do sistema. Telas de detalhe vão junto com a página-mãe.</summary>
    public static readonly Pagina[] Paginas =
    {
        new("dashboard", "Central de Inteligência", "Gestão de Vendas", new[] { "/dashboard", "/oportunidades", "/previsao", "/planejamento", "/pecas" }),
        new("clientes", "Clientes e Unidades", "Gestão de Vendas", new[] { "/clientes", "/equipamentos", "/base-instalada" }),
        new("nacionalizacao", "Nacionalização", "Gestão de Vendas", new[] { "/nacionalizacao" }),
        new("carteira", "Plano de Ação Comercial", "Gestão de Vendas", new[] { "/carteira" }),
        new("diretoria", "Painel Executivo", "Gestão de Vendas", new[] { "/diretoria" }),
        new("historico", "Histórico Aftermarket", "Gestão de Vendas", new[] { "/historico" }),
        new("visitas", "Ações em Campo", "Gestão de Vendas", new[] { "/visitas" }),
        new("alertas", "Alertas Preditivos", "Gestão de Vendas", new[] { "/alertas" }),
        new("gerar-proposta", "Gerar Proposta", "Comercial", new[] { "/gerar-proposta", "/ofertas/nova" }),
        new("ofertas", "Propostas", "Comercial", new[] { "/ofertas" }),
        new("referencias", "Referências de preço", "Comercial", new[] { "/referencias-custo" }),
        new("tempos", "Tempo das propostas", "Comercial", new[] { "/tempos-proposta" }),
        new("dados", "Dados e Importação", "Controle", new[] { "/dados-marca", "/vendedores", "/marca", "/importar", "/conversao" }),
    };

    private static readonly string[] DoAplicador = { "gerar-proposta", "ofertas", "referencias", "clientes" };

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

    public static IReadOnlyList<string> PadraoDoPerfil(string? perfil) => NormalizarPerfil(perfil) switch
    {
        Aplicador => DoAplicador,
        _ => Paginas.Select(p => p.Chave).ToArray(),
    };

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
