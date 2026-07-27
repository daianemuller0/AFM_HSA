using System.Globalization;

namespace AfmHsa.Data;

/// <summary>Helpers de conversão (Parquet guarda tudo como texto) e formatação.</summary>
public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---- leitura do DataReader (Parquet devolve tudo como texto) ---------
    public static string S(System.Data.IDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);

    // ---- parse (string -> tipo) ------------------------------------------
    public static decimal Dec(string? s) =>
        decimal.TryParse(s, NumberStyles.Any, Inv, out var d) ? d : 0m;

    public static int Int(string? s) =>
        int.TryParse(s, NumberStyles.Any, Inv, out var i) ? i
        : (int)Math.Round(Dec(s));

    public static bool Bool(string? s) =>
        !string.IsNullOrWhiteSpace(s) && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1");

    // ---- format (tipo -> string amigável) --------------------------------
    // Moeda em reais no padrão pt-BR (R$ 1.234,56), sem depender da cultura do SO.
    public static string Brl(decimal v)
    {
        var nfi = new NumberFormatInfo { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };
        return "R$ " + v.ToString("#,##0.00", nfi);
    }

    public static string Brl(string? s) => Brl(Dec(s));

    // Número inteiro com separador de milhar.
    public static string N0(decimal v)
    {
        var nfi = new NumberFormatInfo { NumberGroupSeparator = "." };
        return v.ToString("#,##0", nfi);
    }
}
