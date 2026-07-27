using System.Text.Json;

namespace AfmHsa.Data;

/// <summary>
/// Semeia a base Parquet na primeira execução, a partir de Data/seed.json —
/// os MESMOS dados do app original (Leonardo). Cada registro vira um Parquet,
/// como qualquer edição. Entidades já semeadas (pasta não vazia) são puladas,
/// então rodar de novo não duplica nada.
/// </summary>
public static class DbInitializer
{
    // Arrays presentes no seed.json → subpastas Parquet com o mesmo nome.
    private static readonly string[] Entities =
    {
        "companies", "clientUnits", "salespeople", "equipment",
        "installedBase", "parts", "salesHistory", "users",
    };

    public static void Initialize(ParquetStore store)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "seed.json");
        if (!File.Exists(path)) return;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        foreach (var entity in Entities)
        {
            if (!root.TryGetProperty(entity, out var arr) || arr.ValueKind != JsonValueKind.Array)
                continue;
            if (!store.IsEmpty(entity)) continue; // já semeado

            foreach (var item in arr.EnumerateArray())
            {
                var row = new List<KeyValuePair<string, object?>>();
                foreach (var field in item.EnumerateObject())
                    row.Add(new KeyValuePair<string, object?>(field.Name, JsonToString(field.Value)));
                store.WriteRow(entity, row);
            }
        }
    }

    // Converte um valor JSON para texto (padrão do ParquetStore: tudo VARCHAR).
    private static string JsonToString(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Null => "",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => v.GetRawText(), // números e demais: texto cru (ex.: "350000.0")
    };
}
