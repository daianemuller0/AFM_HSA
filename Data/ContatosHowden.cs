namespace AfmHsa.Data;

/// <param name="Area">o mercado ou a região que ele atende (vazio quando não há)</param>
public sealed record ContatoHowden(string Nome, string Cargo, string Area, string[] Telefones, string Email);

/// <summary>
/// A equipe comercial que assina a proposta: o vendedor escolhido na etapa
/// "Cliente e condições" é o contato que sai na capa do Word, ao lado do
/// diretor (nome, cargo, área, telefones e e-mail). Lista passada pela equipe,
/// mais os incluídos pelo "+" na proposta (gravados na base, para todos —
/// <see cref="ContatosHowdenRepository"/>).
/// </summary>
public static class ContatosHowden
{
    /// <summary>A equipe fixa e, depois, os incluídos na base (lidos de novo a cada 20 s: outro computador pode ter incluído).</summary>
    public static ContatoHowden[] Todos
    {
        get
        {
            var incluidos = Incluidos();
            return incluidos.Count == 0 ? DaEquipe
                : DaEquipe.Concat(incluidos.Where(i => !DaEquipe.Any(d => Textos.Igual(d.Nome, i.Nome)))).ToArray();
        }
    }

    private static ContatosHowdenRepository? _repo;
    private static List<ContatoHowden> _cache = new();
    private static DateTime _lidoEm = DateTime.MinValue;
    private static readonly object _trava = new();

    /// <summary>Liga a lista à base (no início do programa).</summary>
    public static void UsarBase(ContatosHowdenRepository repo) { lock (_trava) { _repo = repo; _lidoEm = DateTime.MinValue; } }

    private static List<ContatoHowden> Incluidos()
    {
        lock (_trava)
        {
            if (_repo is null) return _cache;
            if (DateTime.UtcNow - _lidoEm > TimeSpan.FromSeconds(20))
            {
                try { _cache = _repo.Todos(); } catch (Exception) { /* sem a base, fica a equipe fixa */ }
                _lidoEm = DateTime.UtcNow;
            }
            return _cache;
        }
    }

    /// <summary>Inclui um contato na base (ou atualiza o de mesmo nome).</summary>
    public static void Incluir(ContatoHowden c)
    {
        lock (_trava)
        {
            _repo?.Gravar(c);
            _lidoEm = DateTime.MinValue;
        }
    }

    private static readonly ContatoHowden[] DaEquipe =
    {
        new("Rafael Ribeiro de Toledo", "Sales Manager", "Industrial",
            new[] { "+56 9 8285 9721 (Chile)", "+55 11 99450 7548 (Brazil)" }, "rafael.toledo@chartindustries.com"),
        new("André Carvalho", "Key Account Manager", "Pulp & Paper / Metal Process",
            new[] { "+55 11 99452 5152" }, "andre.carvalho@chartindustries.com"),
        new("José Ovídio Moura", "Key Account Manager", "Fertilizer / Glass / Cement / Chemical",
            new[] { "+55 11 96858 2183" }, "jose.moura@chartindustries.com"),
        new("Paulo Agostinho", "Key Account Manager", "Biofuel / Food",
            new[] { "+55 11 98145 1988" }, "paulo.agostinho@chartindustries.com"),
        new("Stephanie Cipriani", "Sales Manager", "Tunnel / Metro",
            new[] { "+55 11 99451 4986" }, "stephanie.cipriani@chartindustries.com"),
        new("José Carlos L. Pereira", "Key Account", "Tunnel / Metro",
            new[] { "+55 11 99443 0060" }, "Jose.pereira@chartindustries.com"),
        new("Rodrigo Ugas", "Key Account Manager", "UG Mining",
            new[] { "+56 93947 3380" }, "rodrigo.ugas@chartindustries.com"),
        new("Manuel Gajardo", "Ventilation Engineer", "",
            new[] { "+56 9 2644 2972" }, "manuel.gajardo@chartindustries.com"),
        new("Wagner Ortíz", "Ventilation Engineer", "",
            new[] { "+51 972 459 337" }, "wagner.ortiz@chartindustries.com"),
        new("Daniel Nuñez", "Ventilation Engineer", "",
            new[] { "+51 953 887 645" }, "daniel.nunez@chartindustries.com"),
        new("Elmer Calle Chumacero", "Sales Engineer", "",
            new[] { "+51 989 012 650" }, "elmer.calle@chartindustries.com"),
        new("Manuel Gutierrez", "Sales Engineer", "",
            new[] { "+51 951 175 126" }, "manuel.gutierrez@chartindustries.com"),
        new("Douglas M. Matavelli", "Key Account Manager", "Water",
            new[] { "+55 11 97144 3085" }, "douglas.matavelli@chartindustries.com"),
        new("Bruno Patricio de Castro", "Sales Manager", "PCOG / CCUS",
            new[] { "+55 11 98787 1188" }, "bruno.castro@chartindustries.com"),
        new("Marcos Pinto", "Business Development Manager", "",
            new[] { "+55 11 9 9618 0413 (Brazil)", "+56 9 3395 2405 (Chile)" }, "Marcos.Pinto@chartindustries.com"),
        new("Rogerio Silva", "Field Service Manager", "",
            new[] { "+55 11 99448 1088" }, "rogerio.silva@chartindustries.com"),
        new("Thiago César Veiga", "Application Engineer Manager", "",
            new[] { "+55 11 97144 3083" }, "thiago.veiga@chartindustries.com"),
    };

    /// <summary>O contato pelo nome (sem acento e sem caixa); null = não está na lista.</summary>
    public static ContatoHowden? Do(string? nome)
    {
        var n = Textos.Simples(nome);
        if (n.Length == 0) return null;
        return Todos.FirstOrDefault(c => Textos.Simples(c.Nome) == n)
            ?? Todos.FirstOrDefault(c => n.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: >= 2 } partes
                                         && partes.All(Textos.Simples(c.Nome).Contains));
    }

    /// <summary>As linhas do cartão no Word: nome, cargo, área, telefones e e-mail.</summary>
    public static List<string> Cartao(ContatoHowden c) =>
        new[] { c.Nome, c.Cargo, c.Area }.Concat(c.Telefones).Append(c.Email)
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
}

/// <summary>Os contatos incluídos pela equipe na proposta (entidade "contatos_howden").</summary>
public sealed class ContatosHowdenRepository
{
    private const string Entidade = "contatos_howden";
    private readonly ParquetStore _s;
    public ContatosHowdenRepository(ParquetStore s) => _s = s;

    public List<ContatoHowden> Todos() => _s.ReadLatest(Entidade, "id, nome, cargo, area, telefones, email",
            r => new ContatoHowden(Fmt.S(r, 1), Fmt.S(r, 2), Fmt.S(r, 3),
                Fmt.S(r, 4).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), Fmt.S(r, 5)))
        .Where(c => c.Nome.Trim().Length > 0).OrderBy(c => c.Nome).ToList();

    public void Gravar(ContatoHowden c) => _s.WriteRow(Entidade, new KeyValuePair<string, object?>[]
    {
        new("id", Textos.Simples(c.Nome)), new("nome", c.Nome.Trim()), new("cargo", c.Cargo.Trim()), new("area", c.Area.Trim()),
        new("telefones", string.Join(" | ", c.Telefones.Select(t => t.Trim()).Where(t => t.Length > 0))), new("email", c.Email.Trim()),
    });
}
