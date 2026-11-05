namespace AfmHsa.Data;

/// <summary>As línguas em que a proposta comercial sai.</summary>
public static class IdiomasDaProposta
{
    public const string Portugues = "pt";
    public const string Ingles = "en";
    public const string Espanhol = "es";

    public static readonly (string Codigo, string Nome)[] Opcoes =
    {
        (Portugues, "Português"), (Ingles, "Inglês"), (Espanhol, "Espanhol"),
    };

    public static string Nome(string? codigo) => Opcoes.FirstOrDefault(o => o.Codigo == codigo).Nome ?? "Português";

    public static string Normalizar(string? codigo) => codigo is Ingles or Espanhol ? codigo : Portugues;
}
