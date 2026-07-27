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

            // Monta todas as linhas e grava em lote (1 arquivo por entidade).
            var rows = new List<IReadOnlyList<KeyValuePair<string, object?>>>();
            foreach (var item in arr.EnumerateArray())
            {
                var row = new List<KeyValuePair<string, object?>>();
                foreach (var field in item.EnumerateObject())
                    row.Add(new KeyValuePair<string, object?>(field.Name, JsonToString(field.Value)));
                rows.Add(row);
            }
            store.WriteBatch(entity, rows);
        }

        SeedVisits(store);
    }

    // Visitas de exemplo (seedVisits() de src/lib/store.ts do app original).
    private static void SeedVisits(ParquetStore store)
    {
        if (!store.IsEmpty("visits")) return;

        var visitas = new[]
        {
            new[] { "v-1", "votorantim-laranjeiras", "", "eq-1", "sp-3", "2026-04-20", "presencial", "realizada",
                "Cliente confirmou parada programada para o 2º semestre. Interesse em rotor + eixo.",
                "Enviar proposta de Rotor + Eixo", "2026-06-10", "Oportunidade confirmada" },
            new[] { "v-2", "vale-sao-luis", "", "eq-2", "sp-3", "2026-05-12", "online", "realizada",
                "Reunião técnica sobre janela de troca de paletas do VARIAX.",
                "Agendar visita presencial", "2026-06-02", "Em andamento" },
            new[] { "v-3", "vale-sao-luis", "", "eq-3", "sp-3", "2026-06-15", "presencial", "agendada",
                "Visita técnica para avaliar janela de troca do VARIAX COF.",
                "Levantar escopo de paletas", "2026-06-15", "" },
            new[] { "v-4", "gerdau-ouro-branco", "", "eq-7", "sp-1", "2026-06-05", "online", "agendada",
                "Apresentar proposta de rotor + eixo do ventilador centrífugo.",
                "Enviar proposta formal", "2026-06-05", "" },
        };

        var cols = new[] { "id", "client_unit_id", "opportunity_id", "equipment_id", "vendedor_id",
            "data_visita", "tipo_visita", "status", "notas", "proximo_passo", "data_proximo_contato", "resultado" };

        var rows = visitas.Select(v =>
            (IReadOnlyList<KeyValuePair<string, object?>>)cols
                .Select((c, i) => new KeyValuePair<string, object?>(c, v[i])).ToList()).ToList();

        store.WriteBatch("visits", rows);
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
