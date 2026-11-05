namespace AfmHsa.Data;

/// <summary>
/// As escolhas que o modelo de proposta comercial do AFM
/// (wwwroot/modelos/proposta-comercial.docx) traz para quem monta à mão: os
/// locais de entrega (Incoterms) e as NCMs da lista "Escolher um item." de cada
/// item. Mesma ideia das ListasDaProposta do axiaisvent.
/// </summary>
public static class ListasDaProposta
{
    /// <param name="Codigo">O que fica gravado na proposta.</param>
    /// <param name="Rotulo">Como aparece na tela.</param>
    /// <param name="Texto">A linha do modelo; {0} é o local digitado.</param>
    /// <param name="PedeLocal">A linha só fica completa com o local (destino, aeroporto).</param>
    public sealed record Incoterm(string Codigo, string Rotulo, string Texto, bool PedeLocal);

    /// <summary>As seis linhas de "Local de Entrega (Incoterms 2010)" do modelo.</summary>
    public static readonly Incoterm[] Incoterms =
    {
        new("EXW", "EX-Works / FCA – Fábrica Howden em Itatiba (SP)",
            "EX-Works / FCA – Fábrica Howden em Itatiba (SP) ou subfornecedor na região de São Paulo (SP), carregado sobre caminhão (amarração e travamento por conta do cliente).", false),
        new("FCA-SANTOS", "FCA – Terminal de carga (Porto de Santos – SP)",
            "FCA – Terminal de carga (Porto de Santos – SP).", false),
        new("FCA-AEROPORTO", "FCA – Terminal de carga (Aeroporto Internacional)",
            "FCA – Terminal de carga (Aeroporto Internacional {0}).", true),
        new("CIF", "CIF – Porto de destino",
            "CIF – Porto de {0}.", true),
        new("DAP", "DAP – Local de destino",
            "DAP – {0}, carregado sobre caminhão (descarregamento por conta do cliente).", true),
        new("DDP", "DDP – Local de destino",
            "DDP – {0}, carregado sobre caminhão (descarregamento por conta do cliente).", true),
    };

    /// <summary>A linha de entrega como sai na proposta; sem local, fica o "DESTINO" do modelo para a equipe ver.</summary>
    public static string LinhaDoIncoterm(string codigo, string local)
    {
        var i = Incoterms.FirstOrDefault(x => x.Codigo == codigo);
        if (i is null) return codigo.Trim();
        if (!i.PedeLocal) return i.Texto;
        var lugar = SoOLugar(local);
        return string.Format(i.Texto, lugar.Length > 0 ? lugar : "DESTINO (PAÍS DE DESTINO)");
    }

    /// <summary>
    /// O lugar sem o "Porto de" / "Local de" que a frase do modelo já traz —
    /// senão sairia "Porto de Porto de Callao" (o mesmo cuidado do axiaisvent).
    /// </summary>
    public static string SoOLugar(string? local)
    {
        var texto = (local ?? "").Trim();
        foreach (var repetido in new[] { "porto de ", "local de ", "aeroporto internacional ", "aeroporto de " })
            if (texto.StartsWith(repetido, StringComparison.OrdinalIgnoreCase))
                return texto[repetido.Length..].Trim();
        return texto;
    }

    /// <param name="Ipi">A alíquota de IPI que a própria opção do modelo informa.</param>
    public sealed record Ncm(string Codigo, string Texto, decimal Ipi);

    /// <summary>A lista "NCM: Escolher um item." de cada item da tabela de preço do modelo.</summary>
    public static readonly Ncm[] Ncms =
    {
        new("8414.90.20", "8414.90.20 = 3,25% IPI (Partes e peças de Ventiladores ou coifas aspirantes)", 0.0325m),
        new("8414.59.90", "8414.59.90 = 0% IPI (Ventilador Completo)", 0m),
        new("8414.90.31", "8414.90.31 = 5% IPI (Peça de compressor que contém Pistões ou êmbolos", 0.05m),
        new("8414.90.32", "8414.90.32 = 5% IPI (Peça de compressor que contém Anéis de segmento)", 0.05m),
        new("8414.90.33", "8414.90.33 = 5% IPI (Peça de compressor que contém Blocos de cilindros ,cabeçotes e cárteres)", 0.05m),
        new("8414.90.34", "8414.90.34 = 5% IPI (Peça de compressor que contém Válvulas)", 0.05m),
        new("8414.90.39", "8414.90.39 = 0% IPI (Outras peças de compressor)", 0m),
        new("8483.40.10", "8483.40.10 = 0% IPI (Redutores, multiplicadores, caixas de transmissão e variadores de velocidade, incluindo os conversores de torque)", 0m),
    };

    public static Ncm? NcmDo(string? codigo) => Ncms.FirstOrDefault(n => n.Codigo == (codigo ?? "").Trim());

    /// <summary>NCM sugerida para a peça de um equipamento: ventilador → partes e peças de ventilador; compressor e "Outro" ficam para escolher.</summary>
    public static string NcmPadrao(string? tipoDoEquipamento) =>
        Textos.Simples(tipoDoEquipamento) is var t && (t.Contains("compress") || t == "outro") ? "" : "8414.90.20";
}
