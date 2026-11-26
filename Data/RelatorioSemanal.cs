using System.Globalization;
using System.Net;
using System.Text;
using AfmHsa.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AfmHsa.Data;

/// <summary>Para quem e quando sai o relatório semanal.</summary>
public sealed class ConfigDoRelatorioSemanal
{
    public bool Ativo { get; set; } = true;
    /// <summary>Destinatários, separados por ";".</summary>
    public string Para { get; set; } = "";
    public string Cc { get; set; } = "";
    /// <summary>A partir de que hora da sexta-feira ele sai (0–23).</summary>
    public int Hora { get; set; } = 16;
}

/// <summary>Um envio do relatório (ou a tentativa que falhou).</summary>
public sealed record EnvioDoRelatorio(string Semana, string EnviadoEm, string Por, string Para, string Resultado)
{
    public bool Ok => Resultado == "enviado";
}

/// <summary>A configuração e o registro dos envios (entidades "relatorio_semanal_config" e "relatorio_semanal_envios").</summary>
public sealed class RelatorioSemanalRepository
{
    private const string EntidadeConfig = "relatorio_semanal_config";
    private const string EntidadeEnvios = "relatorio_semanal_envios";
    private readonly ParquetStore _s;
    public RelatorioSemanalRepository(ParquetStore s) => _s = s;

    /// <summary>O destinatário que o sistema já conhece (Rafael, nos contatos da Howden); o da Thais é informado na tela.</summary>
    public static string ParaPadrao() =>
        ContatosHowden.Todos.FirstOrDefault(c => c.Nome.StartsWith("Rafael", StringComparison.OrdinalIgnoreCase))?.Email ?? "";

    public ConfigDoRelatorioSemanal Config()
    {
        var c = _s.ReadLatest(EntidadeConfig, "id, ativo, para, cc, hora", r => new ConfigDoRelatorioSemanal
        {
            Ativo = Fmt.S(r, 1) != "0",
            Para = Fmt.S(r, 2),
            Cc = Fmt.S(r, 3),
            Hora = int.TryParse(Fmt.S(r, 4), out var h) && h is >= 0 and <= 23 ? h : 16,
        }).FirstOrDefault();
        return c ?? new ConfigDoRelatorioSemanal { Para = ParaPadrao() };
    }

    public void GravarConfig(ConfigDoRelatorioSemanal c) => _s.WriteRow(EntidadeConfig, new KeyValuePair<string, object?>[]
    {
        new("id", "padrao"), new("ativo", c.Ativo ? "1" : "0"), new("para", c.Para.Trim()), new("cc", c.Cc.Trim()),
        new("hora", c.Hora.ToString(CultureInfo.InvariantCulture)),
    });

    public List<EnvioDoRelatorio> Envios() =>
        _s.ReadLatest(EntidadeEnvios, "id, enviado_em, por, para, resultado",
                r => new EnvioDoRelatorio(Fmt.S(r, 0), Fmt.S(r, 1), Fmt.S(r, 2), Fmt.S(r, 3), Fmt.S(r, 4)))
            .OrderByDescending(e => e.EnviadoEm, StringComparer.Ordinal).ToList();

    public void RegistrarEnvio(EnvioDoRelatorio e) => _s.WriteRow(EntidadeEnvios, new KeyValuePair<string, object?>[]
    {
        new("id", e.Semana), new("enviado_em", e.EnviadoEm), new("por", e.Por), new("para", e.Para), new("resultado", e.Resultado),
    });
}

/// <summary>
/// O relatório semanal das propostas, no padrão Howden: o PDF sai do modelo
/// Word da proposta comercial (logo da Howden no cabeçalho, rodapé "Howden |
/// Chart Industries" com a paginação, fonte Arial e o azul Howden), e o
/// e-mail traz o resumo. Cobre a semana (segunda até o momento do relatório):
/// o tempo de cada proposta montada, a meta, onde ela demora, quem montou,
/// a evolução das últimas semanas e o movimento comercial (enviadas e
/// consolidadas).
/// </summary>
public static class RelatorioSemanal
{
    private static readonly CultureInfo Br = new("pt-BR");
    public const string AzulHowden = "004785";
    private const string Zebra = "EEF3F9";
    private const string Borda = "C9D3DF";

    private static readonly (int Numero, string Nome)[] Etapas =
        { (1, "Começar"), (2, "Cliente e condições"), (3, "Itens"), (4, "Preço"), (5, "Documentos") };

    // ================= dados =================

    public sealed record Medicao(TempoProposta Tempo, string Cliente);
    public sealed record Pessoa(string Nome, int Semana, double MedianaSemana, int Trinta, double MedianaTrinta);
    public sealed record SemanaPassada(DateTime Inicio, int Propostas, double MedianaTodas, double MedianaNova, int Novas);
    public sealed record PropostaDaSemana(CommercialOffer Oferta, DateTime Data);

    public sealed record Dados(DateTime Inicio, DateTime Fim, DateTime GeradoEm,
        List<Medicao> DaSemana, double MedianaSemana, double MedianaNovaSemana, int NovasSemana,
        double MedianaNova30, int Novas30, int ReferenciaMinutos,
        List<(string Etapa, double Mediana, double Parte)> PorEtapa,
        List<Pessoa> PorPessoa, List<SemanaPassada> Evolucao,
        List<PropostaDaSemana> Enviadas, List<PropostaDaSemana> Consolidadas)
    {
        public double MetaSegundos => ReferenciaMinutos * 60.0 * (double)TempoRepository.Meta;
        public string Periodo => $"{Inicio:dd/MM/yyyy} a {Fim:dd/MM/yyyy}";
        public decimal ValorEnviado => Enviadas.Sum(p => p.Oferta.TotalReais);
        public decimal ValorConsolidado => Consolidadas.Sum(p => p.Oferta.TotalReais);
    }

    /// <summary>A segunda-feira (00:00) da semana do dia.</summary>
    public static DateTime InicioDaSemana(DateTime dia) => dia.Date.AddDays(-(((int)dia.DayOfWeek + 6) % 7));

    /// <summary>A sexta-feira em que o relatório da semana do dia sai (a chave do envio).</summary>
    public static DateTime SextaDaSemana(DateTime dia) => InicioDaSemana(dia).AddDays(4);

    public static Dados Montar(IReadOnlyList<TempoProposta> tempos, int referenciaMinutos, IReadOnlyList<CommercialOffer> ofertas,
        IReadOnlyDictionary<string, FollowUpDaProposta> followUps, DateTime ate)
    {
        var inicio = InicioDaSemana(ate);
        var primeiras = TempoRepository.PrimeiraDeCada(tempos);   // só as que já geraram documento
        bool Na(TempoProposta t, DateTime de, DateTime a) => t.FimComoData is { } d && d >= de && d <= a;
        var clientes = ofertas.GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First().ClientName);
        var semana = primeiras.Where(t => Na(t, inicio, ate)).OrderBy(t => t.Fim, StringComparer.Ordinal)
            .Select(t => new Medicao(t, clientes.GetValueOrDefault(t.PropostaId, ""))).ToList();
        var trinta = primeiras.Where(t => Na(t, ate.AddDays(-30), ate)).ToList();
        var novasSemana = semana.Where(m => m.Tempo.Origem == "nova").ToList();
        var novas30 = trinta.Where(t => t.Origem == "nova").ToList();
        double Med(IEnumerable<TempoProposta> l) => TempoRepository.Mediana(l.Select(t => (double)t.SegundosAtivos));

        // onde demora: as novas dos últimos 30 dias (ou todas, se ainda não há novas)
        var baseEtapas = novas30.Count > 0 ? novas30 : trinta;
        var medEtapas = Etapas.Select(e => (e.Nome, Med: TempoRepository.Mediana(baseEtapas.Select(t =>
            (double)t.PorEtapaComoMapa.GetValueOrDefault(e.Numero.ToString(CultureInfo.InvariantCulture)))))).ToList();
        var soma = medEtapas.Sum(x => x.Med);
        var porEtapa = medEtapas.Select(x => ($"{x.Nome}", x.Med, soma > 0 ? x.Med / soma : 0)).ToList();

        var pessoas = semana.Select(m => m.Tempo.Usuario).Concat(trinta.Select(t => t.Usuario))
            .Select(u => string.IsNullOrWhiteSpace(u) ? "—" : u.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(u =>
            {
                bool Eh(TempoProposta t) => string.Equals(string.IsNullOrWhiteSpace(t.Usuario) ? "—" : t.Usuario.Trim(), u, StringComparison.OrdinalIgnoreCase);
                var s = semana.Select(m => m.Tempo).Where(Eh).ToList();
                var t30 = trinta.Where(Eh).ToList();
                return new Pessoa(u, s.Count, Med(s), t30.Count, Med(t30));
            })
            .OrderByDescending(p => p.Semana).ThenByDescending(p => p.Trinta).ToList();

        var evolucao = Enumerable.Range(0, 8).Select(k => inicio.AddDays(-7 * k)).Reverse().Select(seg =>
        {
            var fimSem = seg.AddDays(7).AddTicks(-1);
            var daSem = primeiras.Where(t => Na(t, seg, fimSem)).ToList();
            var nov = daSem.Where(t => t.Origem == "nova").ToList();
            return new SemanaPassada(seg, daSem.Count, Med(daSem), Med(nov), nov.Count);
        }).ToList();

        // movimento comercial da semana (última revisão de cada número)
        var enviadas = ofertas.Where(FollowUps.Enviada)
            .Select(o => new PropostaDaSemana(o, FollowUps.DataDoEnvio(o, followUps.GetValueOrDefault(o.Id))))
            .Where(p => p.Data >= inicio && p.Data <= ate)
            .OrderBy(p => p.Data).ToList();
        var consolidadas = ofertas.Where(Consolidacao.Consolidada)
            .Select(o => (o, Data: DateTime.TryParseExact(o.ConsolidadaEm, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null))
            .Where(x => x.Data is { } d && d >= inicio && d <= ate)
            .Select(x => new PropostaDaSemana(x.o, x.Data!.Value)).OrderBy(p => p.Data).ToList();

        return new Dados(inicio, ate, DateTime.Now, semana, Med(semana.Select(m => m.Tempo)), Med(novasSemana.Select(m => m.Tempo)), novasSemana.Count,
            Med(novas30), novas30.Count, referenciaMinutos, porEtapa, pessoas, evolucao, enviadas, consolidadas);
    }

    // ================= formatos =================

    public static string Minutos(double segundos)
    {
        if (segundos <= 0) return "—";
        var m = (int)Math.Round(segundos / 60);
        return m < 1 ? "< 1 min" : m >= 60 ? $"{m / 60} h {m % 60:D2} min" : $"{m} min";
    }

    private static string Reais(decimal v) => "R$ " + v.ToString("#,##0", Br);

    private static string ResultadoDaMeta(Dados d, double mediana, int quantas)
    {
        if (d.ReferenciaMinutos <= 0) return "sem o tempo de antes informado";
        if (quantas == 0) return "sem proposta nova no período";
        var reducao = 1 - mediana / (d.ReferenciaMinutos * 60.0);
        return mediana <= d.MetaSegundos
            ? $"atingida ({reducao.ToString("P0", Br)} menos que antes)"
            : $"faltam {Minutos(mediana - d.MetaSegundos)} ({reducao.ToString("P0", Br)} menos que antes)";
    }

    private static string Assunto(Dados d) => $"Relatório semanal · Propostas AFM HSA · {d.Inicio:dd/MM} a {d.Fim:dd/MM/yyyy}";

    public static string NomeDoArquivo(Dados d, string extensao) => $"Relatorio_Semanal_Propostas_{d.Inicio:yyyy-MM-dd}.{extensao}";

    // ================= Word / PDF no modelo Howden =================

    /// <summary>O relatório em Word, sobre o modelo da proposta comercial (cabeçalho com o logo, rodapé e estilos da Howden).</summary>
    public static byte[] Docx(Dados d, string caminhoDoModelo)
    {
        var fluxo = new MemoryStream();
        using (var modelo = File.OpenRead(caminhoDoModelo)) modelo.CopyTo(fluxo);
        fluxo.Position = 0;
        using (var doc = WordprocessingDocument.Open(fluxo, true))
        {
            var main = doc.MainDocumentPart!;
            var corpo = main.Document.Body!;
            var secao = corpo.Elements<SectionProperties>().LastOrDefault()?.CloneNode(true) as SectionProperties ?? new SectionProperties();
            corpo.RemoveAllChildren();
            secao.RemoveAllChildren<TitlePage>();   // o cabeçalho (logo) também na 1ª página
            foreach (var img in main.ImageParts.ToList()) main.DeletePart(img);

            // cabeçalho: fica o logo; sai a caixa com "Cliente / Ref. / Sua Ref." da proposta
            foreach (var h in main.HeaderParts)
            {
                foreach (var r in h.Header.Descendants<Run>().Where(r => r.Descendants<Text>().Any(t => t.Text.Contains("Cliente"))).ToList()) r.Remove();
                h.Header.Save();
            }
            doc.PackageProperties.Title = Assunto(d);
            doc.PackageProperties.Creator = "AFM HSA";

            Escrever(corpo, d);
            corpo.Append(secao);
            main.Document.Save();
        }
        return fluxo.ToArray();
    }

    /// <summary>O PDF (pelo Word ou LibreOffice do computador); sem conversor, null — o Word vai no lugar.</summary>
    public static byte[]? Pdf(byte[] docx)
    {
        try { return PdfDaProposta.Converter(docx); }
        catch (Exception) { return null; }
    }

    private static void Escrever(Body b, Dados d)
    {
        b.Append(Texto("RELATÓRIO SEMANAL", 9, true, "007A9E", depois: 40));
        b.Append(Texto("Propostas comerciais · AFM HSA", 22, true, AzulHowden, depois: 60));
        b.Append(Texto($"Semana de {d.Periodo} · gerado em {d.GeradoEm:dd/MM/yyyy HH:mm}", 10, false, "4B5563", depois: 240));

        // resumo
        b.Append(Faixa("Resumo da semana"));
        var kpis = new List<(string, string, string)>
        {
            ("Propostas montadas", d.DaSemana.Count.ToString(Br), "primeiro Word de cada número e revisão"),
            ("Tempo mediano", Minutos(d.MedianaSemana), "todas as propostas da semana"),
            ("Proposta nova", Minutos(d.MedianaNovaSemana), $"{d.NovasSemana} nova(s) · meta {(d.ReferenciaMinutos > 0 ? Minutos(d.MetaSegundos) : "—")}"),
            ("Meta (50% de antes)", ResultadoDaMeta(d, d.MedianaNovaSemana, d.NovasSemana), "na semana, propostas novas"),
            ("Enviadas ao cliente", d.Enviadas.Count.ToString(Br), Reais(d.ValorEnviado)),
            ("Consolidadas (vendidas)", d.Consolidadas.Count.ToString(Br), Reais(d.ValorConsolidado)),
        };
        b.Append(Indicadores(kpis));
        b.Append(Texto($"Últimos 30 dias: proposta nova em {Minutos(d.MedianaNova30)} (mediana de {d.Novas30}) · meta {ResultadoDaMeta(d, d.MedianaNova30, d.Novas30)}.", 9, false, "4B5563", antes: 80, depois: 200));

        // tempo de cada proposta
        b.Append(Faixa("Tempo de cada proposta montada na semana"));
        if (d.DaSemana.Count == 0) b.Append(Texto("Nenhuma proposta montada na semana.", 10, false, "4B5563", depois: 200));
        else
            b.Append(Tabela(new[] { "Data", "Proposta", "Cliente", "Quem", "Origem", "Itens", "Tempo", "Etapa mais longa" },
                d.DaSemana.Select(m =>
                {
                    var t = m.Tempo;
                    var maior = t.PorEtapaComoMapa.OrderByDescending(p => p.Value).FirstOrDefault();
                    var nomeMaior = int.TryParse(maior.Key, out var n) && Etapas.Any(e => e.Numero == n) ? Etapas.First(e => e.Numero == n).Nome : "—";
                    return new[] { t.FimComoData?.ToString("dd/MM HH:mm") ?? t.Fim, $"{t.Numero} rev {t.Revisao}", m.Cliente, t.Usuario, t.Origem,
                        t.Itens.ToString(Br), Minutos(t.SegundosAtivos), maior.Value > 0 ? $"{nomeMaior} ({Minutos(maior.Value)})" : "—" };
                }).ToList(), new[] { 5, 6 }, new[] { 9, 21, 17, 10, 8, 7, 9, 19 }));

        // onde demora
        b.Append(Faixa("Onde a proposta demora (mediana por etapa, últimos 30 dias)"));
        b.Append(Tabela(new[] { "Etapa", "Mediana", "Parte do tempo" },
            d.PorEtapa.Select(e => new[] { e.Etapa, Minutos(e.Mediana), e.Parte.ToString("P0", Br) }).ToList(), new[] { 1, 2 }, new[] { 50, 25, 25 }));

        // por pessoa
        b.Append(Faixa("Por pessoa"));
        if (d.PorPessoa.Count == 0) b.Append(Texto("Sem medições nos últimos 30 dias.", 10, false, "4B5563", depois: 200));
        else
            b.Append(Tabela(new[] { "Quem", "Propostas na semana", "Mediana na semana", "Propostas em 30 dias", "Mediana em 30 dias" },
                d.PorPessoa.Select(p => new[] { p.Nome, p.Semana.ToString(Br), Minutos(p.MedianaSemana), p.Trinta.ToString(Br), Minutos(p.MedianaTrinta) }).ToList(),
                new[] { 1, 2, 3, 4 }, new[] { 32, 17, 17, 17, 17 }));

        // evolução
        b.Append(Faixa("Evolução das últimas 8 semanas"));
        b.Append(Tabela(new[] { "Semana", "Propostas", "Mediana (todas)", "Mediana (novas)", "Situação da meta" },
            d.Evolucao.Select(s => new[]
            {
                $"{s.Inicio:dd/MM} a {s.Inicio.AddDays(6):dd/MM}", s.Propostas.ToString(Br), Minutos(s.MedianaTodas), Minutos(s.MedianaNova),
                d.ReferenciaMinutos <= 0 || s.Novas == 0 ? "—" : s.MedianaNova <= d.MetaSegundos ? "dentro da meta" : "acima da meta",
            }).ToList(), new[] { 1, 2, 3 }, new[] { 22, 14, 20, 20, 24 }));

        // movimento comercial
        b.Append(Faixa("Propostas enviadas ao cliente na semana"));
        if (d.Enviadas.Count == 0) b.Append(Texto("Nenhuma proposta marcada como enviada na semana.", 10, false, "4B5563", depois: 200));
        else
            b.Append(Tabela(new[] { "Data", "Proposta", "Cliente", "Vendedor", "Valor (R$)" },
                d.Enviadas.Select(p => new[] { p.Data.ToString("dd/MM"), $"{p.Oferta.OfferCode} rev {PropostaWord.Ou(p.Oferta.Revisao, "0")}", p.Oferta.ClientName,
                    PropostaWord.Ou(p.Oferta.Vendedor, p.Oferta.PreparedBy), Reais(p.Oferta.TotalReais) })
                    .Append(new[] { "", "Total", "", "", Reais(d.ValorEnviado) }).ToList(), new[] { 4 }, new[] { 10, 24, 32, 16, 18 }, totalNaUltima: true));

        b.Append(Faixa("Propostas consolidadas (vendidas) na semana"));
        if (d.Consolidadas.Count == 0) b.Append(Texto("Nenhuma proposta consolidada na semana.", 10, false, "4B5563", depois: 200));
        else
            b.Append(Tabela(new[] { "Data", "Proposta", "Cliente", "Projeto (PV)", "Pedido", "Valor (R$)" },
                d.Consolidadas.Select(p => new[] { p.Data.ToString("dd/MM"), $"{p.Oferta.OfferCode} rev {Consolidacao.Revisao(p.Oferta)}", p.Oferta.ClientName,
                    p.Oferta.Pv, p.Oferta.NumeroPedido, Reais(p.Oferta.TotalReais) })
                    .Append(new[] { "", "Total", "", "", "", Reais(d.ValorConsolidado) }).ToList(), new[] { 5 }, new[] { 9, 24, 25, 14, 13, 15 }, totalNaUltima: true));

        b.Append(Texto("Como é medido: o cronômetro da Gerar Proposta soma o tempo de trabalho até o primeiro Word de cada número e revisão; " +
                       "pausas de mais de 3 minutos não contam. A meta vale para a proposta nova (cópia e revisão são mais rápidas). " +
                       "Valores em R$ pela taxa da proposta.", 8, false, "6B7280", antes: 240));
    }

    private static RunProperties Fonte(double pontos, bool negrito, string cor)
    {
        var rp = new RunProperties(new RunFonts { Ascii = "Arial", HighAnsi = "Arial", ComplexScript = "Arial" });
        if (negrito) rp.Append(new Bold());
        rp.Append(new Color { Val = cor });
        rp.Append(new FontSize { Val = ((int)(pontos * 2)).ToString(CultureInfo.InvariantCulture) });
        return rp;
    }

    private static Paragraph Texto(string texto, double pontos, bool negrito, string cor, int antes = 0, int depois = 0, JustificationValues? alinhar = null)
    {
        var pp = new ParagraphProperties(new SpacingBetweenLines { Before = antes.ToString(CultureInfo.InvariantCulture), After = depois.ToString(CultureInfo.InvariantCulture) });
        if (alinhar is { } a) pp.Append(new Justification { Val = a });
        return new Paragraph(pp, new Run(Fonte(pontos, negrito, cor), new Text(texto) { Space = SpaceProcessingModeValues.Preserve }));
    }

    /// <summary>O título de seção: faixa azul Howden com o texto em branco.</summary>
    private static Paragraph Faixa(string titulo) => new(
        new ParagraphProperties(
            new KeepNext(),
            new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = AzulHowden },
            new SpacingBetweenLines { Before = "120", After = "100" },
            new Indentation { Left = "0" }),
        new Run(Fonte(11, true, "FFFFFF"), new Text(" " + titulo) { Space = SpaceProcessingModeValues.Preserve }));

    private static Table Indicadores(List<(string Rotulo, string Valor, string Sub)> kpis)
    {
        var t = TabelaVazia(new[] { 34, 33, 33 }, comBordas: false);
        for (var i = 0; i < kpis.Count; i += 3)
        {
            var linha = new TableRow();
            foreach (var (rotulo, valor, sub) in kpis.Skip(i).Take(3))
            {
                var cel = new TableCell(new TableCellProperties(
                        new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "F3F6FA" },
                        new TableCellMargin(new TopMargin { Width = "100", Type = TableWidthUnitValues.Dxa }, new BottomMargin { Width = "100", Type = TableWidthUnitValues.Dxa },
                                            new LeftMargin { Width = "140", Type = TableWidthUnitValues.Dxa })),
                    Texto(rotulo.ToUpperInvariant(), 7.5, true, "6B7280", depois: 20),
                    Texto(valor, valor.Length > 22 ? 10.5 : 16, true, AzulHowden, depois: 20),
                    Texto(sub, 8, false, "4B5563"));
                linha.Append(cel);
            }
            while (linha.Elements<TableCell>().Count() < 3) linha.Append(new TableCell(new Paragraph()));
            t.Append(linha);
        }
        return t;
    }

    private static Table TabelaVazia(int[] larguras, bool comBordas = true)
    {
        var bordas = comBordas
            ? new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = Borda }, new BottomBorder { Val = BorderValues.Single, Size = 4, Color = Borda },
                new LeftBorder { Val = BorderValues.Nil }, new RightBorder { Val = BorderValues.Nil },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = Borda }, new InsideVerticalBorder { Val = BorderValues.Nil })
            : new TableBorders(
                new TopBorder { Val = BorderValues.Nil }, new BottomBorder { Val = BorderValues.Nil }, new LeftBorder { Val = BorderValues.Nil },
                new RightBorder { Val = BorderValues.Nil },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 18, Color = "FFFFFF" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 18, Color = "FFFFFF" });
        var t = new Table(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableLayout { Type = TableLayoutValues.Fixed },
            bordas,
            new TableCellMarginDefault(new TopMargin { Width = "50", Type = TableWidthUnitValues.Dxa }, new BottomMargin { Width = "50", Type = TableWidthUnitValues.Dxa },
                new TableCellLeftMargin { Width = 90, Type = TableWidthValues.Dxa }, new TableCellRightMargin { Width = 90, Type = TableWidthValues.Dxa })));
        var grade = new TableGrid();
        const int total = 10204;   // largura útil da página (A4, margens do modelo)
        foreach (var l in larguras) grade.Append(new GridColumn { Width = (total * l / 100).ToString(CultureInfo.InvariantCulture) });
        t.Append(grade);
        return t;
    }

    private static Table Tabela(string[] cabecalho, List<string[]> linhas, int[] direita, int[] larguras, bool totalNaUltima = false)
    {
        var t = TabelaVazia(larguras);
        TableCell Cel(string texto, bool cab, bool dir, string? fundo, bool negrito, int largura) => new(
            new TableCellProperties(
                new TableCellWidth { Width = (5000 * largura / 100).ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Pct },
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fundo ?? "auto" }),
            Texto(texto, cab ? 8 : 8.5, cab || negrito, cab ? "FFFFFF" : "1F2937", alinhar: dir ? JustificationValues.Right : JustificationValues.Left));

        var cab = new TableRow(new TableRowProperties(new TableHeader()));
        for (var i = 0; i < cabecalho.Length; i++) cab.Append(Cel(cabecalho[i].ToUpperInvariant(), true, direita.Contains(i), AzulHowden, true, larguras[i]));
        t.Append(cab);
        for (var r = 0; r < linhas.Count; r++)
        {
            var total = totalNaUltima && r == linhas.Count - 1;
            var tr = new TableRow(new TableRowProperties(new CantSplit()));
            for (var i = 0; i < cabecalho.Length; i++)
                tr.Append(Cel(i < linhas[r].Length ? linhas[r][i] : "", false, direita.Contains(i), total ? "DCE6F2" : r % 2 == 1 ? Zebra : null, total, larguras[i]));
            t.Append(tr);
        }
        return t;
    }

    // ================= e-mail =================

    public static EmailDaProposta Email(Dados d, ConfigDoRelatorioSemanal c, bool temPdf)
    {
        string H(string s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#1f2937\">");
        sb.Append("<p>Olá,</p>");
        sb.Append($"<p>Segue o relatório semanal das propostas comerciais do AFM HSA, semana de <b>{H(d.Periodo)}</b>. O relatório completo está no {(temPdf ? "PDF" : "Word")} anexo.</p>");
        sb.Append($"<table cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;margin:12px 0\"><tr>");
        void Kpi(string rotulo, string valor, string sub) =>
            sb.Append($"<td style=\"background:#f3f6fa;border-left:4px solid #{AzulHowden};padding:10px 14px;vertical-align:top\">" +
                      $"<div style=\"font-size:10px;font-weight:bold;color:#6b7280;text-transform:uppercase;letter-spacing:.04em\">{H(rotulo)}</div>" +
                      $"<div style=\"font-size:20px;font-weight:bold;color:#{AzulHowden};margin:2px 0\">{H(valor)}</div>" +
                      $"<div style=\"font-size:11px;color:#4b5563\">{H(sub)}</div></td><td style=\"width:8px\"></td>");
        Kpi("Propostas montadas", d.DaSemana.Count.ToString(Br), $"tempo mediano {Minutos(d.MedianaSemana)}");
        Kpi("Proposta nova", Minutos(d.MedianaNovaSemana), $"meta {(d.ReferenciaMinutos > 0 ? Minutos(d.MetaSegundos) : "—")}");
        Kpi("Enviadas", d.Enviadas.Count.ToString(Br), Reais(d.ValorEnviado));
        Kpi("Consolidadas", d.Consolidadas.Count.ToString(Br), Reais(d.ValorConsolidado));
        sb.Append("</tr></table>");
        sb.Append($"<p><b>Meta (50% do tempo de antes):</b> {H(ResultadoDaMeta(d, d.MedianaNovaSemana, d.NovasSemana))}. Últimos 30 dias: {H(Minutos(d.MedianaNova30))} por proposta nova.</p>");

        if (d.DaSemana.Count > 0)
        {
            sb.Append($"<p style=\"margin:14px 0 4px;font-weight:bold;color:#{AzulHowden}\">Propostas montadas na semana</p>");
            sb.Append("<table cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:12px\">");
            sb.Append($"<tr>{string.Concat(new[] { "Data", "Proposta", "Cliente", "Quem", "Tempo" }.Select(c2 => $"<th style=\"background:#{AzulHowden};color:#fff;text-align:left;padding:5px 10px\">{c2}</th>"))}</tr>");
            var i = 0;
            foreach (var m in d.DaSemana)
            {
                var fundo = i++ % 2 == 1 ? $"#{Zebra}" : "#ffffff";
                sb.Append($"<tr style=\"background:{fundo}\"><td style=\"padding:4px 10px\">{H(m.Tempo.FimComoData?.ToString("dd/MM") ?? "")}</td>" +
                          $"<td style=\"padding:4px 10px\">{H($"{m.Tempo.Numero} rev {m.Tempo.Revisao}")}</td><td style=\"padding:4px 10px\">{H(m.Cliente)}</td>" +
                          $"<td style=\"padding:4px 10px\">{H(m.Tempo.Usuario)}</td><td style=\"padding:4px 10px;text-align:right\"><b>{H(Minutos(m.Tempo.SegundosAtivos))}</b></td></tr>");
            }
            sb.Append("</table>");
        }
        sb.Append("<p style=\"margin-top:14px\">Este e-mail é enviado automaticamente toda sexta-feira pelo sistema AFM HSA.</p></div>");

        var texto = $"Relatório semanal das propostas AFM HSA, semana de {d.Periodo}: {d.DaSemana.Count} proposta(s) montada(s), " +
                    $"tempo mediano {Minutos(d.MedianaSemana)}; {d.Enviadas.Count} enviada(s) ({Reais(d.ValorEnviado)}); " +
                    $"{d.Consolidadas.Count} consolidada(s) ({Reais(d.ValorConsolidado)}). Relatório completo em anexo.";
        return new EmailDaProposta(c.Para, c.Cc, Assunto(d), sb.ToString(), texto);
    }

    /// <summary>Monta tudo (dados, Word/PDF e e-mail) com o que está gravado agora.</summary>
    public static (Dados Dados, EmailDaProposta Email, AnexoDoEmail Anexo) Gerar(ParquetStore store, string caminhoDoModelo, DateTime ate)
    {
        var tempos = new TempoRepository(store);
        var ofertas = new OfferRepository(store).All();
        var d = Montar(tempos.Todos(), tempos.MinutosDeReferencia(), ofertas, new FollowUpRepository(store).Todos(), ate);
        var docx = Docx(d, caminhoDoModelo);
        var pdf = Pdf(docx);
        var anexo = pdf is not null
            ? new AnexoDoEmail(NomeDoArquivo(d, "pdf"), "application/pdf", pdf)
            : new AnexoDoEmail(NomeDoArquivo(d, "docx"), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", docx);
        return (d, Email(d, new RelatorioSemanalRepository(store).Config(), pdf is not null), anexo);
    }
}

/// <summary>
/// Envia o relatório toda sexta-feira, a partir da hora configurada, pelo
/// Outlook do computador em que o sistema está aberto. O envio fica
/// registrado na pasta de dados (compartilhada): se outro computador já
/// enviou a semana, este não envia de novo. Se ninguém abrir o sistema na
/// sexta depois da hora, ele sai quando o sistema for aberto (até domingo).
/// </summary>
public sealed class RelatorioSemanalService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly ParquetStore _store;
    private readonly OutlookLocal _outlook;
    private readonly string _modelo;

    public RelatorioSemanalService(ParquetStore store, OutlookLocal outlook, Microsoft.AspNetCore.Hosting.IWebHostEnvironment ambiente)
    {
        _store = store;
        _outlook = outlook;
        _modelo = Path.Combine(ambiente.ContentRootPath, PropostaWord.CaminhoDoModelo);
    }

    /// <summary>A semana devida agora (a chave "yyyy-MM-dd" da sexta), ou null se ainda não é hora.</summary>
    public static string? SemanaDevida(DateTime agora, int hora)
    {
        var sexta = RelatorioSemanal.SextaDaSemana(agora);
        if (agora < sexta.AddHours(hora)) return null;   // segunda a sexta antes da hora
        return sexta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        if (!_outlook.Disponivel) return;   // só onde o Outlook de quem usa está à mão
        try { await Task.Delay(TimeSpan.FromMinutes(2), parar); } catch (TaskCanceledException) { return; }
        while (!parar.IsCancellationRequested)
        {
            try { Verificar(DateTime.Now, Environment.UserName); }
            catch (Exception) { /* tenta de novo na próxima volta */ }
            try { await Task.Delay(TimeSpan.FromMinutes(15), parar); } catch (TaskCanceledException) { return; }
        }
    }

    /// <summary>Envia se a semana está devida e ainda não saiu. Devolve o que aconteceu (null = nada a fazer).</summary>
    public string? Verificar(DateTime agora, string por)
    {
        var repo = new RelatorioSemanalRepository(_store);
        var c = repo.Config();
        if (!c.Ativo || c.Para.Trim().Length == 0) return null;
        if (SemanaDevida(agora, c.Hora) is not { } semana) return null;
        if (repo.Envios().Any(e => e.Semana == semana && e.Ok)) return null;
        return Enviar(agora, por, semana);
    }

    public string Enviar(DateTime ate, string por, string? semana = null)
    {
        var repo = new RelatorioSemanalRepository(_store);
        var (dados, email, anexo) = RelatorioSemanal.Gerar(_store, _modelo, ate);
        var erro = _outlook.Enviar(email, new[] { anexo });
        var chave = semana ?? RelatorioSemanal.SextaDaSemana(ate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        repo.RegistrarEnvio(new EnvioDoRelatorio(chave, DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), por, email.Para,
            erro is null ? "enviado" : erro));
        return erro ?? $"Relatório da semana {dados.Periodo} enviado para {email.Para}.";
    }
}
