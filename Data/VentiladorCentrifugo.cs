using System.Globalization;
using System.Text.Json;
using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>
/// A ficha de custo de um ventilador centrífugo — uma aba "ITEM" da planilha
/// da equipe (C_HSAXYZ0000-0.xlsm): a seleção do equipamento e as linhas de
/// custo (matéria-prima, mão de obra, caldeiraria, comprados, engenharia,
/// ensaios, assessoria técnica, importados e back to back). Cada ficha é um
/// item da proposta; o custo total dela, dividido pela quantidade, vira o
/// custo unitário do item, e o pricing segue como nos compressores.
/// </summary>
public sealed class FichaVentilador
{
    // ---- seleção do equipamento ----
    public string TipoOferta { get; set; } = VentiladorCentrifugo.TiposDeOferta[0].Nome;
    public string Familia { get; set; } = "";
    public string Equipamento { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    /// <summary>De onde vem a referência do preço: "Último fornecimento" ou "Original" (vai para a base de referências).</summary>
    public string UltimoFornecimento { get; set; } = "";
    public int Quantidade { get; set; } = 1;
    public string Arranjo { get; set; } = "";
    public string Aplicacao { get; set; } = "";
    public string TipoFluido { get; set; } = "";
    public string Entrada { get; set; } = "";
    public string CaixaEntrada { get; set; } = "";
    public string TipoPas { get; set; } = "";
    public string Acionamento { get; set; } = "";
    // ---- condições ambientais / de projeto ----
    public string ReferenciaUsada { get; set; } = "";
    public string DiametroRotor { get; set; } = "";
    public string TempProjeto { get; set; } = "";
    public string PressaoProjeto { get; set; } = "";
    // ---- motor elétrico ----
    public string MotorPotencia { get; set; } = "";
    public string MotorRotacao { get; set; } = "";

    public List<LinhaVentilador> Linhas { get; set; } = new();
}

/// <summary>Uma linha de custo da ficha (uma linha da planilha).</summary>
public sealed class LinhaVentilador
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    /// <summary>O bloco da planilha (<see cref="VentiladorCentrifugo.Secoes"/>).</summary>
    public string Secao { get; set; } = "";
    /// <summary>Rotor, Cubo, Eixo, Partes Estáticas… (o resumo soma por ela).</summary>
    public string Categoria { get; set; } = "";
    public string Descricao { get; set; } = "";
    /// <summary>Peso [kg], área [m²], horas, dias ou quantidade, por peça (coluna E).</summary>
    public decimal Medida { get; set; }
    /// <summary>"0" não entra; "1" uma vez no pedido; "qtd" uma por ventilador (coluna G).</summary>
    public string Usar { get; set; } = "0";
    /// <summary>Material (MP, caldeiraria) ou recurso da mão de obra (Fábrica, Engenharia…).</summary>
    public string Material { get; set; } = "";
    /// <summary>R$ por kg, por hora, por m², por dia ou por unidade (coluna K).</summary>
    public decimal Valor { get; set; }
    /// <summary>MP, MO, MP+MO ou PC (coluna N).</summary>
    public string Tipo { get; set; } = "";
    /// <summary>Como a planilha calcula a linha (<see cref="VentiladorCentrifugo.Regras"/>).</summary>
    public string Regra { get; set; } = "";
    /// <summary>Linha acrescentada pela pessoa (pode apagar).</summary>
    public bool Extra { get; set; }
}

public static class VentiladorCentrifugo
{
    public const string Compressores = "compressores";
    public const string Ventiladores = "ventiladores";

    public static bool EhVentilador(CommercialOffer o) => o.TipoEquipamento == Ventiladores;

    /// <summary>"Tipo da oferta" da planilha (listas Y:AA): a NCM e o começo da descrição do item.</summary>
    public sealed record TipoDeOferta(string Nome, string Ncm, string Descricao);

    public static readonly TipoDeOferta[] TiposDeOferta =
    {
        new("Ventilador completo", "8414.59.90", "Ventilador Completo Howden Modelo"),
        new("Partes e Peças para Ventiladores", "8414.90.20", "Partes e Peças de Ventilador Howden Modelo"),
        new("Partes e Peças para compressores", "8414.90.39", "Partes e Peças para compressor Modelo"),
        new("Partes e Peças para Pré aquecedores", "8404.90.10", "Partes e Peças para Pré aquecedor Modelo"),
    };

    public static TipoDeOferta TipoDe(string? nome) =>
        TiposDeOferta.FirstOrDefault(t => Textos.Igual(t.Nome, nome)) ?? TiposDeOferta[0];

    /// <summary>Sugestões para os campos da seleção (dá para digitar outro valor).</summary>
    public static readonly Dictionary<string, string[]> Sugestoes = new()
    {
        ["Familia"] = new[] { "Centrífugo", "Centrífugo de alta pressão", "Centrífugo de dupla entrada" },
        ["Arranjo"] = new[] { "1", "3", "4", "7", "8", "9", "10" },
        ["Aplicacao"] = new[] { "Exaustor", "Ventilador de tiragem induzida (ID)", "Ventilador de tiragem forçada (FD)", "Ar primário (PA)", "Recirculação de gases", "Despoeiramento" },
        ["TipoFluido"] = new[] { "Ar", "Ar limpo", "Ar com pó", "Gases de combustão", "Gases quentes" },
        ["Entrada"] = new[] { "Simples", "Dupla" },
        ["CaixaEntrada"] = new[] { "Sim", "Não" },
        ["TipoPas"] = new[] { "Radial", "Curvadas para trás", "Curvadas para frente", "Aerofólio", "Inclinadas para trás" },
        ["Acionamento"] = new[] { "Direto", "Por acoplamento", "Por correias e polias" },
        ["UltimoFornecimento"] = new[] { "Último fornecimento", "Original" },
    };

    public static readonly string[] Categorias =
        { "Rotor", "Cubo", "Eixo", "Partes Estáticas", "Produtos Comprados", "Produtos Importados", "Produtos Back to Back", "Engenharia / Inspeções e Testes", "Assistencia", "Outros" };

    public static readonly string[] TiposDeCusto = { "MP", "MO", "MP+MO", "PC" };

    /// <summary>Os recursos da mão de obra (Lista_MO) e o valor da hora na planilha hoje (a tabela de custos vem depois).</summary>
    public static readonly (string Nome, decimal ValorHora)[] Recursos =
    {
        ("Fábrica", 300m), ("Engenharia", 350m), ("Serviços Especialistas (AT)", 320m),
    };

    /// <summary>Os blocos da planilha, na ordem dela.</summary>
    public sealed record Secao(string Codigo, string Nome, string Unidade, string Medida, string Material, string Explicacao);

    public static readonly Secao[] Secoes =
    {
        new("mp", "Matéria-prima (chapas e barras)", "kg", "Peso [kg]", "Material", "Peso por peça × R$/kg do material."),
        new("mo", "Mão de obra direta", "h", "Horas", "Recurso", "Horas por peça × valor da hora."),
        new("cald", "Acessórios (caldeiraria)", "kg", "Peso [kg]", "Material", "Peso por peça × R$/kg (matéria-prima + mão de obra)."),
        new("pc", "Produtos comprados", "un.", "Qtd / m²", "", "Quantidade (ou m² de pintura) × valor unitário."),
        new("eng", "Engenharia, inspeções e testes", "h", "Horas", "Recurso", "Horas × valor da hora."),
        new("ens", "Ensaios, inspeções e reuniões", "dia", "Dias", "", "Dias × valor por dia."),
        new("at", "Assessoria técnica", "dia", "Dias / qtd", "Recurso", "Diárias × 8 h × valor da hora; despesas pelos dias de viagem."),
        new("imp", "Produtos importados", "un.", "Qtd por vent.", "", "Quantidade por ventilador × valor unitário × quantidade de ventiladores."),
        new("b2b", "Produtos back to back", "un.", "Qtd", "", "Quantidade × valor unitário."),
    };

    /// <summary>As regras de cálculo de linhas especiais da planilha.</summary>
    public static class Regras
    {
        /// <summary>Diária: horas = dias × 8 (H168 = E168*8).</summary>
        public const string Diaria = "diaria";
        /// <summary>Despesa pelos dias de viagem (técnico + mobilização): custo = valor × dias × uso (M173 = L*G*E).</summary>
        public const string DiasDeViagem = "dias";
        /// <summary>Transporte casa-aeroporto: 2 trechos (H170 = 2).</summary>
        public const string DoisTrechos = "2x";
        /// <summary>Uma vez (H171 = 1).</summary>
        public const string UmaVez = "1x";
        /// <summary>Importado: quantidade por ventilador × quantidade de ventiladores (H185 = E/(G/qtd)).</summary>
        public const string PorVentilador = "porvent";
        /// <summary>"Outros parafusos/vedação": 1% da caldeiraria (K121 = SUM(M62:M94)*1%).</summary>
        public const string UmPorCentoCaldeiraria = "1pc";
    }

    private static LinhaVentilador L(string secao, string cat, string desc, string tipo, decimal valor = 0m, string material = "",
        decimal medida = 0m, string regra = "", string usar = "0") =>
        new() { Secao = secao, Categoria = cat, Descricao = desc, Tipo = tipo, Valor = valor, Material = material, Medida = medida, Regra = regra, Usar = usar };

    /// <summary>As linhas da aba ITEM da planilha, com os valores que ela tem hoje (a tabela de custos vem depois).</summary>
    public static List<LinhaVentilador> LinhasPadrao()
    {
        const string sar = "CHAPA SAR 80 8 a <12,7mm", barra = "ACO LAM. RED. SAE 1020/1045 até 4,5\" a 8\"";
        const string carc = "MP+MO (SAE 1010/1020) - Carcaça", reg = "MP+MO (SAE 1010/1020) - Registro", aten = "MP+MO (Carbono) - Atenuador",
            cavB = "MP+MO (ASTM A-36) - Cavalete (base)", cavT = "MP+MO (ASTM A-36) - Cavalete (transporte)", col = "MP+MO (Carbono) - Colarinho";
        var r = new List<LinhaVentilador>
        {
            // matéria-prima (linhas 25–40)
            L("mp", "Rotor", "Chapas rotor 1", "MP", 15.53m, sar), L("mp", "Rotor", "Chapas rotor 2", "MP", 15.53m, sar), L("mp", "Rotor", "Chapas rotor 3", "MP", 15.53m, sar),
            L("mp", "Partes Estáticas", "Chapas carcaça", "MP"), L("mp", "Partes Estáticas", "Chapas suporte", "MP"), L("mp", "Partes Estáticas", "Chapas base", "MP"),
            L("mp", "Partes Estáticas", "Chapas difusor", "MP"), L("mp", "Partes Estáticas", "Chapas registro", "MP"),
            L("mp", "Cubo", "Barra redonda 1", "MP", 9.73m, barra), L("mp", "Eixo", "Barra redonda 2", "MP", 9.73m, barra),
            // mão de obra direta (46–57)
            L("mo", "Rotor", "Horas corte das pás", "MO", 300m, "Fábrica"), L("mo", "Rotor", "Horas montagem", "MO", 300m, "Fábrica"),
            L("mo", "Rotor", "Horas balanceamento", "MO", 300m, "Fábrica"), L("mo", "Partes Estáticas", "Horas corte", "MO", 300m, "Fábrica"),
            L("mo", "Partes Estáticas", "Horas montagem", "MO", 300m, "Fábrica"), L("mo", "Partes Estáticas", "Horas balanceamento", "MO", 300m, "Fábrica"),
            // acessórios / caldeiraria (63–81)
            L("cald", "Partes Estáticas", "Chumbadores", "MP+MO", 27.83m, carc), L("cald", "Partes Estáticas", "Proteções", "MP+MO", 27.83m, carc),
            L("cald", "Partes Estáticas", "Registro - entrada", "MP+MO", 43.9m, reg), L("cald", "Partes Estáticas", "Registro - saída", "MP+MO", 43.9m, reg),
            L("cald", "Partes Estáticas", "Silenciador", "MP+MO", 43.9m, aten), L("cald", "Partes Estáticas", "Cavalete metálico", "MP+MO", 27.83m, cavB),
            L("cald", "Partes Estáticas", "Cavalete transporte", "MP+MO", 27.83m, cavT),
            L("cald", "Partes Estáticas", "Transição circ./circ.", "MP+MO", 27.83m, cavT), L("cald", "Partes Estáticas", "Transição circ./ret.", "MP+MO", 27.83m, cavT),
            L("cald", "Partes Estáticas", "Transição ret./ret.", "MP+MO", 27.83m, cavT),
            L("cald", "Partes Estáticas", "Estrutura flexível circular - entrada", "MP+MO", 43.9m, col), L("cald", "Partes Estáticas", "Estrutura flexível circular - saída", "MP+MO", 43.9m, col),
            L("cald", "Partes Estáticas", "Estrutura flexível ret. - entrada", "MP+MO", 43.9m, col), L("cald", "Partes Estáticas", "Estrutura flexível ret. - saída", "MP+MO", 43.9m, col),
            L("cald", "Partes Estáticas", "Interno flexível - entrada", "MP+MO", 43.9m, col), L("cald", "Partes Estáticas", "Interno flexível - saída", "MP+MO", 43.9m, col),
            L("cald", "Partes Estáticas", "Contra-flange - entrada", "MP+MO", 43.9m, col), L("cald", "Partes Estáticas", "Contra-flange - saída", "MP+MO", 43.9m, col),
        };
        // produtos comprados (97–121)
        foreach (var d in new[] { "Embalagem", "Acoplamento", "Pintura parte girante (m²)", "Pintura parte estática (m²)", "Selo de vedação", "Amortecedores de vibração",
                     "Silenciador cotado", "Placa de identificação", "Motor elétrico", "Sensor de vibração motor (1 por rolamento)", "Inversor de frequência", "Partidor",
                     "Sensor de velocidade", "Sensor de temperatura", "Sensor de vibração", "Freio de manutenção", "Unidade de lubrificação forçada", "Polias movida / motora",
                     "Correias", "Frete", "Custo JR se for exportação" })
            r.Add(L("pc", "Produtos Comprados", d, "PC"));
        r.Add(L("pc", "Produtos Comprados", "Outros parafusos/vedação/etc (1% da caldeiraria)", "PC", 0m, "", 1m, Regras.UmPorCentoCaldeiraria));
        // engenharia, inspeções e testes (137–150)
        foreach (var (d, rec) in new[] { ("Documentação", "Engenharia"), ("Engenharia [desenhos / ETCs]", "Engenharia"), ("Engenharia [outros]", "Engenharia"),
                     ("Engenharia elétrica", "Engenharia"), ("Contratos", "Engenharia"), ("Teste de performance: montagem tubulação (fábrica)", "Fábrica"),
                     ("Teste de performance: desmontagem tubulação (fábrica)", "Fábrica"), ("Teste de performance: horas de teste", "Fábrica"),
                     ("Teste de performance: montagem e desmontagem ventilador", "Fábrica"), ("Detalhamento projeto teste arr. 7 ou 8", "Engenharia"),
                     ("Detalhamento projeto teste arr. 1 ou 3", "Engenharia"), ("Medição e relatório (engenharia)", "Engenharia"), ("Medição ponto adicional (engenharia)", "Engenharia") })
            r.Add(L("eng", "Engenharia / Inspeções e Testes", d, "MO", ValorDaHora(rec), rec));
        // ensaios e inspeções (152–162): comprados, por dia
        foreach (var (d, v) in new[] { ("Kick-off meeting [no cliente]", 0m), ("Ensaio - líquido penetrante", 500m), ("Ensaio - radiografia", 200m), ("Ensaio - ultrassom", 1000m),
                     ("Ensaio - partículas magnéticas", 500m), ("Ensaio - outros", 0m), ("Ensaio ABENDI / ABRACO", 4000m), ("Inspeção - pintura", 1000m),
                     ("Inspeção - dimensional", 1000m), ("Inspeção - visual solda", 1000m), ("Inspeção - outros", 0m) })
            r.Add(L("ens", "Produtos Comprados", d, "PC", v));
        // assessoria técnica (168–174)
        r.Add(L("at", "Assistencia", "Diárias técnico", "MO", 320m, "Serviços Especialistas (AT)", 1m, Regras.Diaria));
        r.Add(L("at", "Assistencia", "Diárias mobilização / desmobilização", "MO", 320m, "Serviços Especialistas (AT)", 2m, Regras.Diaria));
        r.Add(L("at", "Assistencia", "Transporte casa-aeroporto (2 trechos)", "PC", 300m, "", 0m, Regras.DoisTrechos));
        r.Add(L("at", "Assistencia", "Transporte aéreo / carro", "PC", 3500m, "", 0m, Regras.UmaVez));
        r.Add(L("at", "Assistencia", "Transporte local", "PC", 190m, "", 0m, Regras.DiasDeViagem));
        r.Add(L("at", "Assistencia", "Alimentação", "PC", 140m, "", 0m, Regras.DiasDeViagem));
        r.Add(L("at", "Assistencia", "Hotel (estadia)", "PC", 400m, "", 0m, Regras.DiasDeViagem));
        // produtos importados (185–189)
        r.Add(L("imp", "Produtos Importados", "Motor elétrico", "PC", 2500m, "", 0m, Regras.PorVentilador));
        foreach (var d in new[] { "Atuador", "Mancal de casquilho", "Selo de vedação", "Silenciador" })
            r.Add(L("imp", "Produtos Importados", d, "PC", 0m, "", 0m, Regras.PorVentilador));
        // back to back (201–202)
        r.Add(L("b2b", "Produtos Back to Back", "Motor elétrico", "PC"));
        r.Add(L("b2b", "Produtos Back to Back", "Pás de ventiladores", "PC"));
        return r;
    }

    public static decimal ValorDaHora(string recurso) => Recursos.FirstOrDefault(x => Textos.Igual(x.Nome, recurso)).ValorHora;

    public static FichaVentilador Nova() => new() { Linhas = LinhasPadrao() };

    /// <summary>Uma linha nova (o "extra" da planilha), com o jeito do bloco.</summary>
    public static LinhaVentilador LinhaNova(string secao)
    {
        var (cat, tipo, valor, mat, regra) = secao switch
        {
            "mp" => ("Rotor", "MP", 0m, "", ""),
            "mo" => ("Rotor", "MO", 300m, "Fábrica", ""),
            "cald" => ("Partes Estáticas", "MP+MO", 27.83m, "MP+MO (SAE 1010/1020) - Carcaça", ""),
            "eng" => ("Engenharia / Inspeções e Testes", "MO", 350m, "Engenharia", ""),
            "ens" => ("Produtos Comprados", "PC", 0m, "", ""),
            "at" => ("Assistencia", "PC", 0m, "", ""),
            "imp" => ("Produtos Importados", "PC", 0m, "", Regras.PorVentilador),
            "b2b" => ("Produtos Back to Back", "PC", 0m, "", ""),
            _ => ("Produtos Comprados", "PC", 0m, "", ""),
        };
        var l = L(secao, cat, "", tipo, valor, mat, 0m, regra, "qtd");
        l.Extra = true;
        return l;
    }

    // ================= cálculo (o da planilha) =================

    /// <summary>O "G" da planilha: 0, 1 ou a quantidade de ventiladores.</summary>
    public static decimal Usos(FichaVentilador f, LinhaVentilador l) => l.Usar switch
    {
        "1" => 1m,
        "qtd" => Math.Max(f.Quantidade, 1),
        _ => 0m,
    };

    /// <summary>Dias de viagem do técnico: diárias + mobilização (E172 = E168 + E169).</summary>
    public static decimal DiasDeViagem(FichaVentilador f) =>
        f.Linhas.Where(x => x.Regra == Regras.Diaria && x.Usar != "0").Sum(x => x.Medida);

    /// <summary>Total da caldeiraria (a base do 1% de "outros parafusos").</summary>
    public static decimal TotalCaldeiraria(FichaVentilador f) =>
        f.Linhas.Where(x => x.Secao == "cald").Sum(x => Total(f, x));

    /// <summary>O valor unitário da linha (o 1% da caldeiraria é calculado).</summary>
    public static decimal ValorDe(FichaVentilador f, LinhaVentilador l) =>
        l.Regra == Regras.UmPorCentoCaldeiraria ? Math.Round(TotalCaldeiraria(f) * 0.01m, 2) : l.Valor;

    /// <summary>O "H" da planilha: peso, horas ou quantidade da linha (por peça).</summary>
    public static decimal MedidaEfetiva(FichaVentilador f, LinhaVentilador l) => l.Regra switch
    {
        Regras.Diaria => l.Medida * 8m,
        Regras.DoisTrechos => 2m,
        Regras.UmaVez => 1m,
        Regras.DiasDeViagem => DiasDeViagem(f),
        _ => l.Medida,
    };

    /// <summary>Custo por peça (coluna L).</summary>
    public static decimal PorPeca(FichaVentilador f, LinhaVentilador l) => ValorDe(f, l) * MedidaEfetiva(f, l);

    /// <summary>Custo total da linha no pedido (coluna M).</summary>
    public static decimal Total(FichaVentilador f, LinhaVentilador l)
    {
        var g = Usos(f, l);
        if (g == 0m) return 0m;
        return l.Regra == Regras.PorVentilador
            ? ValorDe(f, l) * l.Medida * Math.Max(f.Quantidade, 1)
            : PorPeca(f, l) * g;
    }

    public static decimal TotalDaSecao(FichaVentilador f, string secao) => f.Linhas.Where(l => l.Secao == secao).Sum(l => Total(f, l));

    public static decimal TotalGeral(FichaVentilador f) => f.Linhas.Sum(l => Total(f, l));

    /// <summary>O custo de um ventilador (o total ÷ a quantidade): o custo unitário do item na proposta.</summary>
    public static decimal PorVentiladorDe(FichaVentilador f) => Math.Round(TotalGeral(f) / Math.Max(f.Quantidade, 1), 2);

    /// <summary>O resumo da planilha (linhas 219–241): categoria × tipo de custo.</summary>
    public static List<(string Categoria, string Tipo, decimal Total)> Resumo(FichaVentilador f) =>
        f.Linhas.Select(l => (l, t: Total(f, l))).Where(x => x.t != 0m)
            .GroupBy(x => (x.l.Categoria, x.l.Tipo))
            .Select(g => (g.Key.Categoria, g.Key.Tipo, g.Sum(x => x.t)))
            .OrderBy(x => Array.IndexOf(Categorias, x.Categoria) is var i && i < 0 ? 99 : i).ThenBy(x => Array.IndexOf(TiposDeCusto, x.Tipo))
            .ToList();

    // ================= no item da proposta =================

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static FichaVentilador? Ler(CommercialOfferItem it)
    {
        if (string.IsNullOrWhiteSpace(it.FichaVentilador)) return null;
        try { return JsonSerializer.Deserialize<FichaVentilador>(it.FichaVentilador, Json); }
        catch (JsonException) { return null; }
    }

    public static FichaVentilador Copia(FichaVentilador f)
    {
        var c = JsonSerializer.Deserialize<FichaVentilador>(JsonSerializer.Serialize(f, Json), Json)!;
        foreach (var l in c.Linhas) l.Id = Guid.NewGuid().ToString("N")[..10];
        return c;
    }

    /// <summary>
    /// Passa a ficha para o item: quantidade, custo unitário (o total ÷ a
    /// quantidade), modelo, NCM e descrição pelo tipo da oferta; a ficha fica
    /// gravada no item (JSON).
    /// </summary>
    public static void Aplicar(FichaVentilador f, CommercialOfferItem it)
    {
        f.Quantidade = Math.Max(f.Quantidade, 1);
        var tipo = TipoDe(f.TipoOferta);
        it.FichaVentilador = JsonSerializer.Serialize(f, Json);
        it.Quantity = f.Quantidade;
        var custo = PorVentiladorDe(f);
        it.CustoManual = custo > 0m ? custo : null;
        it.Modelo = f.Equipamento.Trim();
        it.PartNumber = f.PartNumber.Trim();
        it.SerialNumber = f.SerialNumber.Trim();
        it.Ncm = tipo.Ncm;
        it.Description = $"{tipo.Descricao} {f.Equipamento}".Trim();
        it.TotalPrice = it.Quantity * it.UnitPrice;
    }

    public static string Dinheiro(decimal v) => v.ToString("#,##0.00", new CultureInfo("pt-BR"));
}
