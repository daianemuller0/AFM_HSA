namespace AfmHsa.Data;

/// <summary>
/// Parâmetros da "Planilha de custos de importação" da Howden (versão de
/// 02/03/2022), no cenário em que a equipe a usa: embarque aéreo, peso líquido
/// de 20 kg, sem drawback, sem carga excedente, NCM 8414.90.20. Na planilha só
/// se preenche o valor da mercadoria, a moeda e a taxa cambial — o resto vem
/// destes números. Podem ser atualizados no appsettings.json (seção
/// "Importacao") sem mexer no código.
/// </summary>
public class ParametrosImportacao
{
    public string VersaoPlanilha { get; set; } = "02/03/2022";

    // Dados Importação
    public decimal FretePercentual { get; set; } = 0.20m;      // I2 = C2 × 20%
    public decimal PesoKg { get; set; } = 20m;                 // G7
    public decimal AliquotaII { get; set; } = 0.14m;           // F14

    // Impressão Importação (valores em R$, salvo indicação)
    public decimal Siscomex { get; set; } = 400m;              // I13
    public decimal Antidumping { get; set; } = 0m;             // I14
    public decimal Desconsolidacao { get; set; } = 990m;       // I22 (aéreo)
    public decimal CollectFeePercentual { get; set; } = 0.04m; // I23: 4% do frete (moeda)…
    public decimal CollectFeeMinimo { get; set; } = 80m;       //      …mínimo 80 (moeda)…
    public decimal CambioInterno { get; set; } = 3.30m;        //      …× R$ 3,30 (taxa fixa da planilha)
    public decimal Vistoria { get; set; } = 700m;              // I24
    public decimal ArmazenagemPercentual { get; set; } = 0.02m;          // AQ1: 2% do valor CIF em R$
    public decimal ArmazenagemPesoFator { get; set; } = 0.0349m;         // AQ2: se peso × 0,0349 ≤ 10…
    public decimal ArmazenagemPesoMinimo { get; set; } = 10m;            //      …10…
    public decimal ArmazenagemPesoFatorAlto { get; set; } = 0.0505m;     //      …senão peso × 0,0505
    public decimal ArmazenagemAdicional { get; set; } = 0.359m;          // AQ3: + 35,9%
    public decimal Sda { get; set; } = 1761m;                  // I28
    public decimal ComissariaPercentual { get; set; } = 0.0038m; // I29: 0,38% de (mercadoria + frete) na moeda
    public decimal ComissariaFaixaMinima { get; set; } = 200m;
    public decimal ComissariaFaixaMaxima { get; set; } = 750m;
    public decimal ComissariaAdicional { get; set; } = 125m;     //      + 125 só nas faixas mínima/máxima (como na planilha)
    public decimal ComissariaFixa { get; set; } = 40m;           //      + R$ 40
    public decimal TransporteCrt { get; set; } = 92m;            // AN3
    public decimal TransporteGru { get; set; } = 80m;            // AN4
    public decimal TransporteAdValorem { get; set; } = 0.0015m;  // AN6
    public decimal TransporteDivisor { get; set; } = 0.88m;      // ÷ 0,88
    public decimal CartaCreditoPercentual { get; set; } = 0m;    // F35
    // Seguro internacional (F34) é 0 na planilha e não é replicado: a base dele
    // inclui os créditos tributários, que aqui não são calculados.

    /// <summary>Transporte interno aéreo por faixa de peso (kg até → R$), coluna AI.</summary>
    public List<FaixaPeso> TransporteFaixas { get; set; } = new()
    {
        new(100, 611), new(300, 861), new(1000, 1111), new(3000, 1527),
        new(6000, 1735), new(12000, 2000), new(25000, 3000),
    };

    public record FaixaPeso(decimal AteKg, decimal Valor);
}

/// <summary>Uma linha da memória de cálculo (valor em R$).</summary>
public record LinhaImportacao(string Descricao, decimal Valor);

/// <summary>Resultado de uma importação: o custo total em R$ é o "Custo total da
/// operação já deduzidos os créditos tributários" (célula I45 da planilha).</summary>
public record CalculoImportacao(
    decimal ValorMoeda, decimal Taxa, IReadOnlyList<LinhaImportacao> Linhas,
    decimal CustoTotal, decimal CustoTotalMoeda, decimal PercentualSobreProduto);

/// <summary>
/// Réplica da planilha de custos de importação, aba "Impressão Importação".
/// Os créditos tributários (IPI, PIS, COFINS, ICMS) entram e saem na planilha —
/// por isso não aparecem aqui: o custo final é o mesmo de I45.
/// Conferido contra a própria planilha recalculada por um motor de Excel.
/// </summary>
public static class CustoImportacao
{
    public static CalculoImportacao Calcular(decimal valorMoeda, decimal taxa, ParametrosImportacao p)
    {
        if (valorMoeda <= 0 || taxa <= 0)
            return new CalculoImportacao(valorMoeda, taxa, Array.Empty<LinhaImportacao>(), 0, 0, 0);

        var frete = valorMoeda * p.FretePercentual;                 // moeda
        var cifMoeda = valorMoeda + frete;                          // mercadoria + frete (moeda)
        var cifReais = cifMoeda * taxa;                             // SUM(AF1:AG1)

        var ii = cifMoeda * p.AliquotaII * taxa;                    // I12

        var collect = Math.Max(p.CollectFeeMinimo, frete * p.CollectFeePercentual) * p.CambioInterno;   // I23
        // (na planilha o mínimo vale quando 4% do frete fica ABAIXO de 80; igual a 80 dá o mesmo valor)

        var vistoria = p.Desconsolidacao == 0 ? 0 : p.Vistoria;    // I24

        var armazPeso = p.PesoKg * p.ArmazenagemPesoFator <= p.ArmazenagemPesoMinimo
            ? p.ArmazenagemPesoMinimo : p.PesoKg * p.ArmazenagemPesoFatorAlto;
        var armazenagem = (cifReais * p.ArmazenagemPercentual + armazPeso) * (1 + p.ArmazenagemAdicional);   // I25 = AQ4

        var sda = valorMoeda <= 0.01m ? 0 : p.Sda;                  // I28

        var x = cifMoeda * p.ComissariaPercentual;                  // I29
        var faixa = x <= p.ComissariaFaixaMinima ? p.ComissariaFaixaMinima + p.ComissariaAdicional
                  : x >= p.ComissariaFaixaMaxima ? p.ComissariaFaixaMaxima + p.ComissariaAdicional
                  : x;
        var comissaria = faixa * p.CambioInterno + p.ComissariaFixa;

        var faixaPeso = p.TransporteFaixas.OrderBy(f => f.AteKg).FirstOrDefault(f => p.PesoKg <= f.AteKg)
                        ?? p.TransporteFaixas.OrderBy(f => f.AteKg).Last();
        var transporte = (faixaPeso.Valor + p.TransporteCrt + p.TransporteGru + cifReais * p.TransporteAdValorem)
                         / p.TransporteDivisor;                     // I30 (aéreo)

        var cartaCredito = cifReais * p.CartaCreditoPercentual;     // G35 × taxa

        var linhas = new List<LinhaImportacao>
        {
            new("Mercadoria", valorMoeda * taxa),
            new($"Frete internacional ({p.FretePercentual:P0} da mercadoria)", frete * taxa),
            new($"I.I. - Imposto de importação ({p.AliquotaII:P0})", ii),
            new("Siscomex", p.Siscomex),
            new("Desconsolidação / Liberação / Courier", p.Desconsolidacao),
            new("Collect Fee", collect),
            new("Vistoria aduaneira", vistoria),
            new("Armazenagem (15 dias)", armazenagem),
            new("SDA", sda),
            new("Comissária", comissaria),
            new("Transporte interno (aeroporto → Howden)", transporte),
        };
        if (p.Antidumping != 0) linhas.Add(new("Antidumping", p.Antidumping));
        if (cartaCredito != 0) linhas.Add(new("Carta de crédito", cartaCredito));

        var total = linhas.Sum(l => l.Valor);
        return new CalculoImportacao(valorMoeda, taxa, linhas,
            Math.Round(total, 2), Math.Round(total / taxa, 2), (total / taxa - valorMoeda) / valorMoeda);
    }
}
