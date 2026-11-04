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
    /// <summary>A moeda do custo no AFM.</summary>
    public const Moeda DoCusto = Moeda.Brl;

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
/// <item>o custo e todo o pricing são calculados na moeda do custo (R$); só o
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

    /// <summary>O preço sai numa moeda diferente da do custo?</summary>
    public static bool Converte(CommercialOffer o) => MoedaDoPreco(o) != Moedas.DoCusto;

    /// <summary>A taxa na direção interna: 1 moeda do PREÇO = X reais. Zero quando não há taxa.</summary>
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

    /// <summary>Leva um valor em R$ para a moeda do preço.</summary>
    public static decimal ParaVenda(CommercialOffer o, decimal valorEmReais)
    {
        var fx = Efetivo(o);
        return fx == 1m ? valorEmReais : Math.Round(valorEmReais / fx, 2);
    }

    /// <summary>O caminho de volta: um valor na moeda do preço, em R$ (pela taxa efetiva).</summary>
    public static decimal ParaReais(CommercialOffer o, decimal valorNaVenda) => valorNaVenda * Efetivo(o);

    /// <summary>Valor na moeda do preço, formatado ("USD 1.234,56").</summary>
    public static string Dinheiro(CommercialOffer o, decimal valorNaVenda) => Moedas.Dinheiro(MoedaDoPreco(o), valorNaVenda);
}
