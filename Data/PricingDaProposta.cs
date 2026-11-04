using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>
/// Liga a Gerar Proposta do AFM à fórmula de pricing copiada do axiaisvent
/// (<see cref="CalculoPricing"/>) — faz aqui o papel do EntradaDe/Da de lá.
///
/// O cabeçalho do AFM continua com as opções do AFM (texto livre com
/// sugestões); este arquivo traduz o que foi digitado para o que a fórmula
/// entende, sem mexer na conta. O custo total é a soma dos itens: custo de
/// importação quando o item é importado, senão a referência de custo
/// (Valor BR) da lista de Nacionalização.
/// </summary>
public static class PricingDaProposta
{
    /// <summary>Margem alvo padrão do AFM: 52% (no axiaisvent é 28%). Editável na proposta.</summary>
    public const decimal MargemAlvoPadrao = 0.52m;

    public static string PaisOuBrasil(string? pais) =>
        string.IsNullOrWhiteSpace(pais) ? "Brasil" : pais.Trim();

    /// <summary>
    /// Destino do AFM → destino do pricing. A fórmula só reconhece
    /// "Nacional", "Exportação…" e "…Back to Back…"; vazio ou qualquer outro
    /// texto no Brasil é venda nacional, e fora do Brasil é exportação.
    /// </summary>
    public static string DestinoDoPricing(string? destino, string? pais)
    {
        var d = Textos.Simples(destino);
        var exporta = d.Contains("export");
        if (d.Contains("back to back"))
            return exporta ? "Exportação com Back to Back" : "Back to Back 100% importado";
        if (exporta || !ListasDoPricing.TemImpostosBrasileiros(PaisOuBrasil(pais))) return "Exportação";
        return "Nacional";
    }

    private static readonly HashSet<string> Negativos =
        new() { "", "nao", "n", "-", "—", "nenhum", "nenhuma", "sem", "sem portal", "n/a", "na" };

    /// <summary>Portal (0,7%): qualquer valor, menos vazio, "Não" ou "Nenhum".</summary>
    public static bool TemPortal(string? portal) => !Negativos.Contains(Textos.Simples(portal));

    /// <summary>Fiança/seguro garantia: "Fiança", "Seguro Garantia" ou "Sim" contam; "Não" e vazio, não.</summary>
    public static bool TemFianca(string? fianca) => !Negativos.Contains(Textos.Simples(fianca));

    /// <summary>
    /// O representante com o nome exato da tabela de comissões (a fórmula
    /// compara letra por letra). Aceita o nome digitado sem acento ou com
    /// outra caixa; quem não está na tabela segue como está e fica sem comissão.
    /// </summary>
    public static string Representante(string? nome)
    {
        var n = (nome ?? "").Trim();
        return ListasDoPricing.Representantes.FirstOrDefault(r => r.Nome == n)?.Nome
            ?? ListasDoPricing.Representantes.FirstOrDefault(r => Textos.Igual(r.Nome, n))?.Nome
            ?? n;
    }

    public static decimal ComissaoDe(string? nome) =>
        ListasDoPricing.ComissaoDoRepresentante(Representante(nome));

    public static CalculoPricing.Entrada EntradaDe(CommercialOffer o, decimal custoTotal)
    {
        var pais = PaisOuBrasil(o.Pais);
        return new CalculoPricing.Entrada(
            CustoTotal: custoTotal,
            RiscoAdicional: o.RiscoAdicional,
            MargemNegociacao: o.MargemNegociacao,
            Destino: DestinoDoPricing(o.Destino, pais),
            Estado: ListasDoPricing.EstadoDoPricing(pais, o.Estado ?? ""),
            VendaPara: o.VendaPara ?? "",
            Beneficio: ListasDoPricing.TemImpostosBrasileiros(pais) && !string.IsNullOrWhiteSpace(o.Beneficio)
                ? o.Beneficio : "Sem benefício",
            Bu: ListasDoPricing.CodigoDaBu(o.Bu),
            ComPortal: TemPortal(o.Portal),
            Rep1: Representante(o.Rep1),
            Rep2: Representante(o.Rep2),
            ComFianca: TemFianca(o.FiancaSeguro),
            Pais: pais);
    }

    /// <summary>O pricing da proposta, pelo modo escolhido (margem alvo ou preço-meta).</summary>
    public static CalculoPricing.Resultado Da(CommercialOffer o, decimal custoTotal)
    {
        var e = EntradaDe(o, custoTotal);
        return o.PricingModo == "preco"
            ? CalculoPricing.PeloPreco(e, o.PrecoMeta)
            : CalculoPricing.PelaMargem(e, o.MargemAlvo);
    }

    /// <summary>
    /// Preço unitário sugerido de um item: o custo dele com o mesmo fator do
    /// pricing (venda líquida ÷ custo total). É preço SEM impostos, como a
    /// proposta informa ("Impostos … não incluídos").
    /// </summary>
    public static decimal PrecoUnitario(decimal custoUnitario, CalculoPricing.Resultado r) =>
        r.CustoTotal <= 0 ? 0 : Math.Round(custoUnitario * r.VendaLiquida / r.CustoTotal, 2);

    /// <summary>
    /// Os avisos da fórmula, na linguagem do AFM. Os que falam da planilha do
    /// axiaisvent (o "#VALOR!" do segmento NB, a fiança que "o Excel soma")
    /// viram o que vale aqui, já que o AFM não gera aquela planilha.
    /// </summary>
    public static List<string> Avisos(CalculoPricing.Resultado r, CommercialOffer o)
    {
        var lista = new List<string>();
        foreach (var a in r.Avisos)
        {
            if (a.StartsWith("Segmento NB")) continue;   // IPI zero: explicado fixo no painel
            if (a.StartsWith("Estado \"\""))
                lista.Add("Informe o Estado no cabeçalho: sem ele o preço com impostos sai sem ICMS.");
            else if (a.StartsWith("Fiança"))
                lista.Add($"Fiança/seguro garantia (\"{o.FiancaSeguro}\"): o custo dela não entra nesta conta — some à parte.");
            else
                lista.Add(a);
        }
        return lista;
    }
}
