namespace AfmHsa.Data;

/// <summary>
/// CNPJ: normalização, formatação e validação dos dígitos verificadores.
/// O valor é guardado já formatado (00.000.000/0000-00) para ficar legível
/// nas telas e em qualquer exportação.
/// </summary>
public static class Cnpj
{
    /// <summary>Só os dígitos, descartando pontuação.</summary>
    public static string Digitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    /// <summary>Formata como 00.000.000/0000-00; devolve o texto original se não tiver 14 dígitos.</summary>
    public static string Formatar(string? s)
    {
        var d = Digitos(s);
        if (d.Length != 14) return (s ?? "").Trim();
        return $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}";
    }

    /// <summary>Valida os 14 dígitos e os dois dígitos verificadores.</summary>
    public static bool Valido(string? s)
    {
        var d = Digitos(s);
        if (d.Length != 14) return false;
        if (d.All(c => c == d[0])) return false; // 00000000000000, 11111111111111…

        int[] pesos1 = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        int[] pesos2 = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        return d[12] == DigitoVerificador(d, pesos1)
            && d[13] == DigitoVerificador(d, pesos2);
    }

    private static char DigitoVerificador(string d, int[] pesos)
    {
        var soma = 0;
        for (int i = 0; i < pesos.Length; i++) soma += (d[i] - '0') * pesos[i];
        var resto = soma % 11;
        return (char)('0' + (resto < 2 ? 0 : 11 - resto));
    }
}
