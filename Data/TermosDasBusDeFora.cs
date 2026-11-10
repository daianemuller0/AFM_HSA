namespace AfmHsa.Data;

/// <summary>
/// Os "Términos y Condiciones Generales para la Venta de Bienes, Servicios y
/// Software" (com os Suplementares da VentSim), que a HCHL e a HPU põem no fim
/// da proposta, embaixo dos dados bancários — o texto da equipe, em espanhol,
/// em <c>wwwroot/modelos/termos-hchl-hpu-es.txt</c>.
///
/// Uma linha por parágrafo, "tipo|texto":
/// T = título; S = seção ("1. DEFINICIONES"); D = definição ("termo|texto");
/// P = cláusula ou parágrafo; I = item "(i) ..."; B = marcador.
/// </summary>
public static class TermosDasBusDeFora
{
    public const string Arquivo = "termos-hchl-hpu-es.txt";

    public sealed record Bloco(char Tipo, string Texto, string Termo = "");

    /// <summary>Lê o arquivo da pasta dos modelos (vazio = sem o arquivo: a proposta sai sem os termos).</summary>
    public static IReadOnlyList<Bloco> Ler(string pastaDosModelos)
    {
        var caminho = Path.Combine(pastaDosModelos, Arquivo);
        if (!File.Exists(caminho)) return Array.Empty<Bloco>();
        var blocos = new List<Bloco>();
        foreach (var linha in File.ReadAllLines(caminho))
        {
            if (linha.Length < 3 || linha[1] != '|') continue;
            var tipo = linha[0];
            var resto = linha[2..];
            if (tipo == 'D' && resto.IndexOf('|') is var i and > 0)
                blocos.Add(new Bloco('D', resto[(i + 1)..], resto[..i]));
            else blocos.Add(new Bloco(tipo, resto));
        }
        return blocos;
    }
}
