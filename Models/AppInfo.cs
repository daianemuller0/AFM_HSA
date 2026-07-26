namespace AfmHsa.Models;

/// <summary>
/// Estabelece o namespace de modelos (AfmHsa.Models) e guarda metadados simples
/// do app. À medida que cada módulo for implementado, as entidades persistidas
/// (Cliente, Unidade, Oportunidade, Oferta, Despesa, etc.) entram aqui — cada
/// uma com suas propriedades como texto, no mesmo padrão do Licencas_HSA
/// (ver Models/Renewal.cs, Models/Proposal.cs daquele projeto).
/// </summary>
public static class AppInfo
{
    public const string Nome = "AFM HSA";
    public const string Titulo = "Howden Aftermarket Intelligence";
}
