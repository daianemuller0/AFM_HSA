using System.Globalization;
using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>As moedas em que a proposta pode sair para o cliente.</summary>
public enum Moeda { Brl, Usd, Eur, Clp, Pen }

/// <summary>
/// Moedas da proposta — a mesma ideia do axiaisvent (HowdenAxiais.Poc.Data.Moedas,
/// branch da "Proposta v2"): o custo do AFM está sempre em R$ (Valor BR da
/// Nacionalização e custo de importação), e o PREÇO ao cliente pode sair em
/// R$, USD, EUR, CLP ou PEN.
/// </summary>
public static class Moedas
{
    /// <summary>A moeda do custo no AFM (HSA-SP e HSA-ES; HPU e HCHL custam na moeda de fora, ver <see cref="Cambio.MoedaDoCusto"/>).</summary>
    public const Moeda DoCusto = Moeda.Brl;

    /// <summary>As moedas em que o custo pode ser feito quando a BU compra de fora (HPU, HCHL).</summary>
    public static readonly Moeda[] DeCustoDeFora = { Moeda.Usd, Moeda.Eur, Moeda.Clp, Moeda.Pen };

    /// <summary>As moedas do PREÇO ao cliente, na ordem do axiaisvent: CLP, USD, PEN, R$ e EUR.</summary>
    public static readonly Moeda[] DePreco = { Moeda.Clp, Moeda.Usd, Moeda.Pen, Moeda.Brl, Moeda.Eur };

    public static string Rotulo(this Moeda m) => m switch
    {
        Moeda.Usd => "USD",
        Moeda.Eur => "EUR",
        Moeda.Clp => "CLP",
        Moeda.Pen => "PEN",
        _ => "R$",
    };

    /// <summary>Como a moeda é gravada.</summary>
    public static string Codigo(this Moeda m) => m switch
    {
        Moeda.Usd => "USD",
        Moeda.Eur => "EUR",
        Moeda.Clp => "CLP",
        Moeda.Pen => "PEN",
        _ => "BRL",
    };

    /// <summary>Lê o código gravado; vazio ou desconhecido = real (as propostas antigas são em R$).</summary>
    public static Moeda Ler(string? texto) => (texto ?? "").Trim().ToUpperInvariant() switch
    {
        "USD" => Moeda.Usd,
        "EUR" => Moeda.Eur,
        "CLP" => Moeda.Clp,
        "PEN" => Moeda.Pen,
        _ => Moeda.Brl,
    };

    /// <summary>Peso chileno sem casas na tela; as outras moedas com duas (igual ao axiaisvent).</summary>
    public static string Formato(Moeda m) => m == Moeda.Clp ? "N0" : "N2";

    private static readonly CultureInfo Br = new("pt-BR");

    /// <summary>"R$ 1.234,56", "USD 1.234,56", "CLP 1.234.567".</summary>
    public static string Dinheiro(Moeda m, decimal valor) => $"{m.Rotulo()} {valor.ToString(Formato(m), Br)}";
}

/// <summary>
/// A conversão de moeda da proposta, com a mesma regra do axiaisvent
/// (HowdenAxiais.Poc.Data.Cambio, branch "Proposta v2"):
///
/// <list type="bullet">
/// <item>o custo e todo o pricing são calculados na moeda do custo — R$ nas
/// BUs do Brasil; nas BUs de fora (HPU, HCHL) a moeda estrangeira do preço
/// de fora, sem a tabela de importação —; só o
/// valor final, o que o cliente recebe, vai para a moeda do preço;</item>
/// <item>a taxa é digitada num de dois sentidos — "1 moeda do PREÇO em R$"
/// (<c>VendaEmCusto</c>, o padrão: 1 USD = 5,40) ou o contrário
/// (<c>CustoEmVenda</c>: 1 R$ = 0,185 USD) — e é guardada, internamente,
/// sempre no primeiro;</item>
/// <item>a segurança da moeda é um percentual tirado da taxa: com 1 USD = 5,40
/// e 5% de segurança a conversão usa 5,13 — o preço em USD sobe, e se o dólar
/// cair até 5% o valor em reais não perde;</item>
/// <item>valor no preço = valor em R$ ÷ taxa efetiva, com duas casas.</item>
/// </list>
///
/// Sem taxa a conversão não acontece (a tela avisa), e preço em R$ nunca converte.
/// </summary>
public static class Cambio
{
    public static Moeda MoedaDoPreco(CommercialOffer o) => Moedas.Ler(o.MoedaVenda);

    /// <summary>
    /// A BU compra de fora e revende (HPU, HCHL): o preço de fora + frete +
    /// margem, sem a tabela de importação, e o custo na moeda estrangeira.
    /// </summary>
    public static bool CompraDeFora(string? bu)
    {
        var b = (bu ?? "").Trim();
        return b.Equals("HPU", StringComparison.OrdinalIgnoreCase) || b.StartsWith("HCHL", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A moeda do custo que a planilha de pricing usa em cada BU de fora: HCHL em CLP; HPU e HCHL_Custo_USD em USD.</summary>
    public static Moeda CustoPadraoDaBu(string? bu) =>
        string.Equals((bu ?? "").Trim(), "HCHL", StringComparison.OrdinalIgnoreCase) ? Moeda.Clp : Moeda.Usd;

    /// <summary>A moeda em que o custo e o pricing são feitos: R$ no Brasil; nas BUs de fora, a escolhida (padrão da BU).</summary>
    public static Moeda MoedaDoCusto(CommercialOffer o)
    {
        if (!CompraDeFora(o.Bu)) return Moedas.DoCusto;
        var m = Moedas.Ler(o.MoedaCusto);
        return m != Moeda.Brl ? m : CustoPadraoDaBu(o.Bu);
    }

    /// <summary>Valor na moeda do custo, formatado ("R$ 1.234,56", "USD 1.234,56").</summary>
    public static string DinheiroDoCusto(CommercialOffer o, decimal valor) => Moedas.Dinheiro(MoedaDoCusto(o), valor);

    /// <summary>O preço sai numa moeda diferente da do custo?</summary>
    public static bool Converte(CommercialOffer o) => MoedaDoPreco(o) != MoedaDoCusto(o);

    /// <summary>A taxa na direção interna: 1 moeda do PREÇO = X na moeda do custo. Zero quando não há taxa.</summary>
    public static decimal Normalizada(CommercialOffer o)
    {
        if (o.CambioTaxa <= 0m) return 0m;
        return o.CambioDirecao == "CustoEmVenda" ? 1m / o.CambioTaxa : o.CambioTaxa;
    }

    /// <summary>A segurança da moeda, em percentual (0 a 99).</summary>
    public static decimal Seguranca(CommercialOffer o) => Math.Clamp(o.CambioSeguranca, 0m, 99m);

    /// <summary>Falta digitar a taxa: há conversão a fazer, mas não há com quê.</summary>
    public static bool FaltaTaxa(CommercialOffer o) => Converte(o) && Normalizada(o) <= 0m;

    /// <summary>
    /// A taxa que converte de verdade: a informada menos a segurança. 1 quando
    /// não há conversão — e também quando falta a taxa, para o valor sair como
    /// está em vez de dividido por zero.
    /// </summary>
    public static decimal Efetivo(CommercialOffer o)
    {
        if (!Converte(o)) return 1m;
        var normalizada = Normalizada(o);
        if (normalizada <= 0m) return 1m;
        return normalizada * Math.Max(1m - Seguranca(o) / 100m, 0.01m);
    }

    /// <summary>Leva um valor na moeda do custo para a moeda do preço.</summary>
    public static decimal ParaVenda(CommercialOffer o, decimal valorNoCusto)
    {
        var fx = Efetivo(o);
        return fx == 1m ? valorNoCusto : Math.Round(valorNoCusto / fx, 2);
    }

    /// <summary>O caminho de volta: um valor na moeda do preço, na moeda do custo (pela taxa efetiva).</summary>
    public static decimal ParaCusto(CommercialOffer o, decimal valorNaVenda) => valorNaVenda * Efetivo(o);

    /// <summary>
    /// Um valor na moeda do preço, em R$ — para somar a carteira. No Brasil é
    /// a volta pela taxa da proposta; nas BUs de fora, a moeda do custo vai a
    /// R$ pela aba Conversão (null quando ela não tem a taxa).
    /// </summary>
    public static decimal? EmReais(CommercialOffer o, decimal valorNaVenda, TaxasDeConversao taxas)
    {
        var noCusto = ParaCusto(o, valorNaVenda);
        var custo = MoedaDoCusto(o);
        if (custo == Moeda.Brl) return noCusto;
        return taxas.ReaisPor(custo) is decimal r ? noCusto * r : null;
    }

    /// <summary>Valor na moeda do preço, formatado ("USD 1.234,56").</summary>
    public static string Dinheiro(CommercialOffer o, decimal valorNaVenda) => Moedas.Dinheiro(MoedaDoPreco(o), valorNaVenda);
}
