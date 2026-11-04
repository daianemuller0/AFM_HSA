namespace AfmHsa.Data;

/// <summary>
/// As listas de que o pricing precisa — cópia do que o cálculo usa de
/// HowdenAxiais.Poc.Data.ListasDaProposta (projeto axiaisvent): BUs, estados,
/// as opções de venda/destino/benefício/portal e a tabela de representantes
/// com as comissões. Ficou de fora só o que é do documento de lá (contatos,
/// endereços, incoterms).
/// </summary>
public static class ListasDoPricing
{
    /// <summary>
    /// A BU emissora (E8 do pricing). O <b>valor</b> é o código que a planilha
    /// espera — as fórmulas de ICMS e de moeda comparam com "HSA-SP", "HSA-ES"
    /// e "HCHL" —, e o rótulo é para a equipe saber qual é qual.
    /// </summary>
    public static readonly (string Codigo, string Rotulo)[] Bus =
    {
        ("HSA-SP", "HSA-SP — Itatiba (Brasil)"),
        ("HSA-ES", "HSA-ES — Serra (Brasil)"),
        ("HCHL", "HCHL — Chile"),
        ("HPU", "HPU — Peru"),
    };

    public static readonly string[] Paises =
        { "Brasil", "Chile", "Peru", "Colômbia", "México", "Argentina", "Estados Unidos", "Outro" };

    /// <summary>
    /// Os estados que o pricing aceita (J6, lista_Estados): as UFs mais EXPORT,
    /// Chile e Peru, que é como a planilha marca venda fora do Brasil.
    /// </summary>
    public static readonly string[] Estados =
    {
        "EXPORT", "Chile", "Peru",
        "AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT",
        "PA", "PB", "PE", "PI", "PR", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO",
    };

    /// <summary>Venda para (J4 do pricing).</summary>
    public static readonly string[] VendaPara = { "Industrialização", "Cliente Final", "Revenda" };

    /// <summary>Destino (J5 do pricing).</summary>
    public static readonly string[] Destinos =
    {
        "Nacional", "Exportação", "Exportação com Back to Back", "Back to Back 100% importado",
    };

    /// <summary>Regime do cliente (J9 do pricing).</summary>
    public static readonly string[] Beneficios =
    {
        "Sem benefício", "não-contribuinte ICMS", "Benefício RECAP / REIDI", "Zona Franca de Manaus",
    };

    public static readonly string[] Portais = { "Nenhum", "Ariba", "Coupa", "SAP", "Outro" };

    /// <summary>
    /// O que vai na célula J6 do pricing — a chave dos impostos brasileiros.
    /// Só o Brasil tem UF: no Chile e no Peru a planilha espera o nome do país,
    /// e em qualquer outro lugar espera "EXPORT".
    /// </summary>
    public static string EstadoDoPricing(string pais, string estado)
    {
        if (Textos.Igual(pais, "Brasil")) return estado;

        if (Textos.Igual(pais, "Chile")) return "Chile";
        if (Textos.Igual(pais, "Peru") || Textos.Igual(pais, "Perú")) return "Peru";

        return "EXPORT";
    }

    /// <summary>Venda com impostos brasileiros? Só no Brasil.</summary>
    public static bool TemImpostosBrasileiros(string pais) => Textos.Igual(pais, "Brasil");

    /// <summary>
    /// Traz uma BU gravada de outro jeito para o código da planilha, para não
    /// gerar pricing com a BU errada.
    /// </summary>
    public static string CodigoDaBu(string valor)
    {
        var t = Textos.Simples(valor);
        if (t.Length == 0) return "HSA-SP";

        foreach (var (codigo, _) in Bus)
            if (Textos.Igual(codigo, valor)) return codigo;

        if (t.Contains("itatiba") || t.Contains("sao paulo") || t.Contains("sp")) return "HSA-SP";
        if (t.Contains("serra") || t.Contains("es")) return "HSA-ES";
        if (t.Contains("chile") || t.Contains("santiago")) return "HCHL";
        if (t.Contains("peru") || t.Contains("lima")) return "HPU";

        return "HSA-SP";
    }

    /// <summary>
    /// A PORTA "tem portal?" do pricing (P4): Sim/Não, 0,7% sobre a venda.
    /// "Nenhum" (ou vazio) é o único que quer dizer não.
    /// </summary>
    public static bool TemPortal(string portal) =>
        (portal ?? "").Trim().Length > 0 && !Textos.Igual(portal, "Nenhum");

    /// <param name="Comissao">A comissão dele, da tabela BD_pricing do pricing.</param>
    public sealed record Representante(string Grupo, string Nome, decimal Comissao);

    /// <summary>
    /// CÓPIA FIEL de <c>BD_pricing!A68:E101</c> (via axiaisvent) — nomes e
    /// comissões. Os nomes têm de bater letra por letra: o pricing acha a
    /// comissão com <c>MATCH(P5, lista_representantes, 0)</c>, e um acento ou um
    /// espaço a mais cai no <c>IFERROR</c>, que devolve comissão zero. Por isso
    /// "Maurício" tem acento e "Wander (Wanseve )" tem o espaço antes do parêntese.
    /// </summary>
    public static readonly Representante[] Representantes =
    {
        new("Brasil", "Douglas (Mezza & Baga)", 0.03m),
        new("Brasil", "Alexandre (Artman)", 0.03m),
        new("Brasil", "Gerson (Lizan)", 0.03m),
        new("Brasil", "Ivars (Dzelme & Leite Ltda)", 0.03m),
        new("Brasil", "Júlio (Doulus)", 0.03m),
        new("Brasil", "Maurício (Livimat)", 0.03m),
        new("Brasil", "Ricardo (Sesbras)", 0.05m),
        new("Brasil", "Sander (Provent)", 0.05m),
        new("Brasil", "Thais (InTec)", 0.03m),
        new("Brasil", "Wander (Wanseve )", 0.03m),
        new("Brasil", "Adolpho (Atric)", 0.05m),
        new("Exterior", "ASESORIA Y EQUIPO < =USD 500K", 0.1m),
        new("Exterior", "ASESORIA Y EQUIPO < USD 1MM", 0.075m),
        new("Exterior", "ASESORIA Y EQUIPO > USD 1MM", 0.05m),
        new("Exterior", "FERRUNION", 0.05m),
        new("Exterior", "H&T", 0.05m),
        new("Exterior", "HCA", 0.06m),
        new("Exterior", "HRI S.A.", 0.05m),
        new("Exterior", "IPT Colômbia < =EUR 1,25MM", 0.035m),
        new("Exterior", "IPT Colômbia < =EUR 1MM", 0.05m),
        new("Exterior", "IPT Colômbia < =EUR 500K", 0.08m),
        new("Exterior", "IPT Colômbia < =EUR 750K", 0.065m),
        new("Exterior", "IPT Colômbia >EUR 1,25MM", 0.03m),
        new("Exterior", "SIMINCO", 0.05m),
        new("Exterior", "TEJADA", 0.05m),
        new("Exterior", "Turbomaquinarias <= EUR 100 k", 0.04m),
        new("Exterior", "Turbomaquinarias <= EUR 2,5 M", 0.025m),
        new("Exterior", "Turbomaquinarias <= EUR 500 k", 0.03m),
        new("Exterior", "Turbomaquinarias <= EUR 7,0 M", 0.02m),
        new("Exterior", "Turbomaquinarias > EUR 7,0 M", 0.01m),
    };

    /// <summary>A comissão de um representante. Sem representante, zero.</summary>
    public static decimal ComissaoDoRepresentante(string nome) => Representantes
        .FirstOrDefault(r => r.Nome == nome)?.Comissao ?? 0m;
}
