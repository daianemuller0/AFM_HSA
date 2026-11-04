namespace AfmHsa.Data;

/// <summary>
/// Comparação de texto do jeito que a equipe digita: sem acento, sem caixa e
/// sem espaço sobrando. "Não" e "NAO" são a mesma coisa para o sistema.
/// Cópia de HowdenAxiais.Poc.Data.Textos (projeto axiaisvent), usada pelo pricing.
/// </summary>
public static class Textos
{
    public static string Simples(string? texto)
    {
        var normal = (texto ?? "").Trim().ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD);

        var limpo = new string(normal
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());

        return limpo.Normalize(System.Text.NormalizationForm.FormC);
    }

    public static bool Igual(string? a, string? b) => Simples(a) == Simples(b);
}
