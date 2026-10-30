namespace AfmHsa.Models;

// Peça nacionalizada: item importado (intercompany) que passou a ter custo e
// preço no Brasil. A ficha do equipamento consulta esta lista pelo partnumber
// para dizer se a peça já é nacionalizada.
public class Nacionalizacao
{
    public string Id { get; set; } = "";
    public string Data { get; set; } = "";          // data da nacionalização, ISO yyyy-MM-dd
    public string Projeto { get; set; } = "";
    public string CodHsa { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string DescricaoAlterada { get; set; } = "";
    public decimal Quantidade { get; set; }

    /// <summary>Valor unitário intercompany e o total da linha (quantidade × unitário).</summary>
    public decimal ValorIntercompany { get; set; }
    public decimal ValorTotalIntercompany { get; set; }

    /// <summary>Valor unitário no Brasil e o total da linha.</summary>
    public decimal ValorBr { get; set; }
    public decimal TotalBr { get; set; }

    public bool JaVendido { get; set; }
}
