namespace AfmHsa.Data;

/// <summary>
/// As BUs de fora (HCHL, HPU) no documento da proposta: o preço sai líquido
/// com o imposto local somado à parte ("+ IVA" no Chile, "+ IGV" no Peru) e
/// os "Datos para facturación" são os da empresa da BU, com a conta bancária.
/// Os dados são os que a equipe enviou; corrigir aqui se mudarem.
/// </summary>
public static class FaturamentoDaBu
{
    /// <param name="Negrito">o valor sai em negrito (como no modelo da equipe)</param>
    public sealed record Linha(string Rotulo, string Valor, bool Negrito = true);

    /// <param name="Nome">a razão social (primeira linha, em negrito)</param>
    /// <param name="Linhas">endereço, registro fiscal e bancos</param>
    /// <param name="MoedaDaConta">o título da tabela da conta ("Euro - EUR")</param>
    /// <param name="Conta">a tabela da conta: beneficiário, banco, conta, swift…</param>
    public sealed record Empresa(string Nome, Linha[] Linhas, string MoedaDaConta, Linha[] Conta);

    /// <summary>O imposto que vai somado ao preço líquido: IVA (Chile), IGV (Peru); null nas BUs do Brasil.</summary>
    public static string? ImpostoLocal(string? bu)
    {
        var b = (bu ?? "").Trim();
        if (b.StartsWith("HCHL", StringComparison.OrdinalIgnoreCase)) return "IVA";
        if (b.Equals("HPU", StringComparison.OrdinalIgnoreCase)) return "IGV";
        return null;
    }

    private static readonly Empresa Chile = new(
        "HOWDEN CHILE SpA",
        new Linha[]
        {
            new("Dirección:", "Calle Cordillera 575, Pudahuel - Santiago"),
            new("RUT:", "76.274.455-4"),
            new("Banco:", "HSBC Bank Chile"),
        },
        "Euro - EUR",
        new Linha[]
        {
            new("Beneficiario:", "Howden Chile SpA"),
            new("Banco Beneficiario:", "HSBC Bank Chile"),
            new("Cuenta", "000-059261-061"),
            new("Swift Banco Beneficiario:", "BLICCLRM"),
            new("Banco intermediario:", "HSBC Pls"),
            new("Cuenta/IBAN:", "GB92MIDL40051557202815"),
            new("Swift Banco Intermediario:", "MIDLGB22"),
        });

    private static readonly Empresa Peru = new(
        "HPU - Howden Perú S.R.L.",
        new Linha[]
        {
            new("Dirección:", "Calle Guillermo Marconi 386, oficina 101, San Isidro, Lima, Peru - CP: 150131"),
            new("RUC:", "20606553618"),
            new("Banco:", "Scotiabank (EUR)", Negrito: false),
            new("Banco:", "Santander (Dólar Americano – USD)", Negrito: false),
        },
        "Dólar Americano - USD",
        new Linha[]
        {
            new("Beneficiario:", "Howden Perú S.R.L."),
            new("Banco Beneficiario:", "Banco Santander"),
            new("Cuenta", "056-001-000008103356-15"),
            new("Swift Banco Beneficiario:", "-"),
            new("Banco intermediario:", "-"),
            new("Cuenta:", "-"),
            new("Swift Banco Intermediario:", "-"),
            new("ABA:", "-"),
        });

    /// <summary>A empresa que fatura pela BU; null = a do Brasil (a do modelo).</summary>
    public static Empresa? DaBu(string? bu) => ImpostoLocal(bu) switch
    {
        "IVA" => Chile,
        "IGV" => Peru,
        _ => null,
    };
}
