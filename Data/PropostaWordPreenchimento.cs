using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>
/// O preenchimento do modelo em Word, seção por seção. Ver
/// <see cref="PropostaWord"/> para o porquê. As ferramentas (Escrever,
/// EscreverNaCelula, Achar, ApagarEntre, os campos ligados) são as do
/// axiaisvent, e as âncoras são os textos do modelo do AFM em português.
/// </summary>
internal sealed class PreenchimentoDaProposta
{
    private readonly WordprocessingDocument _doc;
    private readonly DadosDaPropostaWord _d;
    private readonly CommercialOffer _o;
    private readonly Body _corpo;
    private readonly CalculoPricing.Resultado _pricing;

    /// <summary>Com que impostos os preços da proposta estão (o total da tabela e o "incluso" de cada imposto).</summary>
    private readonly PricingDaProposta.NivelDeImposto _nivel;

    /// <summary>pt, en ou es: o documento é preenchido em português e traduzido no fim (<see cref="Traduzir"/>).</summary>
    private readonly string _idioma;

    /// <summary>Os Termos e Condições da HCHL / HPU (vazio = sem o arquivo).</summary>
    private readonly IReadOnlyList<TermosDasBusDeFora.Bloco> _termos;

    /// <summary>HCHL ou HPU: a proposta é da Howden Chile / Peru (líquido + IVA / IGV, termos no fim).</summary>
    private bool DeFora => FaturamentoDaBu.ImpostoLocal(_o.Bu) is not null;

    /// <summary>Venda para fora do Brasil, do Chile e do Peru: nenhum imposto se soma ao preço.</summary>
    private bool OutroPais => FaturamentoDaBu.OutroPais(_o.Estado);
    private static readonly CultureInfo Br = new("pt-BR");

    public PreenchimentoDaProposta(WordprocessingDocument doc, DadosDaPropostaWord dados, IReadOnlyList<TermosDasBusDeFora.Bloco>? termos = null)
    {
        _termos = termos ?? Array.Empty<TermosDasBusDeFora.Bloco>();
        _doc = doc;
        _d = dados;
        _o = dados.Oferta;
        _corpo = doc.MainDocumentPart!.Document.Body!;

        // as alíquotas da proposta (PIS, COFINS, ICMS, IPI, ISS) vêm do pricing
        var comCusto = dados.Itens.Select(i => (It: i, Custo: PricingDaProposta.CustoGravado(i) ?? 0m)).Where(x => x.Custo > 0m).ToList();
        var comprados = comCusto.Where(x => !x.It.Importado).Sum(x => x.Custo * x.It.Quantity);
        var importados = comCusto.Where(x => x.It.Importado).Sum(x => x.Custo * x.It.Quantity);
        _pricing = PricingDaProposta.Da(_o, comprados, importados, PricingDaProposta.IpiDosItens(comCusto));
        _nivel = PricingDaProposta.NivelEfetivo(_pricing, PricingDaProposta.NivelPedido(_o));
        _idioma = IdiomasDaProposta.Normalizar(_o.Idioma);
    }

    public void Preencher()
    {
        Propriedades();
        CamposLigados();
        Capa();
        EnderecoDaBu();
        Contatos();
        Representante();
        TirarPaginaDeConsulta();
        Revisoes();
        Introducao();
        Precos();
        NotasComerciais();
        Impostos();
        Pagamento();
        Entrega();
        Validade();
        Faturamento();
        SoAComercial();
        TirarAssessoria();
        TermosNoFim();
        Indice();
        Traduzir();
        TirarRealce();

        _doc.MainDocumentPart!.Document.Save();
    }

    private string Data() =>
        DateTime.TryParseExact(_o.OfferDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy") : DateTime.Now.ToString("dd/MM/yyyy");

    private static string Pct(decimal f) => (f * 100).ToString("0.##", Br) + "%";

    /// <summary>Tira o realce de todo o documento: no modelo ele é a marca de "preencher/escolher".</summary>
    private void TirarRealce()
    {
        foreach (var parte in Partes())
        {
            foreach (var realce in parte.Descendants<Highlight>().ToList()) realce.Remove();
            parte.Save();
        }
    }

    private List<OpenXmlPartRootElement> Partes()
    {
        var partes = new List<OpenXmlPartRootElement> { _doc.MainDocumentPart!.Document };
        partes.AddRange(_doc.MainDocumentPart.HeaderParts.Where(h => h.Header is not null).Select(h => (OpenXmlPartRootElement)h.Header));
        partes.AddRange(_doc.MainDocumentPart.FooterParts.Where(f => f.Footer is not null).Select(f => (OpenXmlPartRootElement)f.Footer));
        return partes;
    }

    // ================= propriedades e campos ligados =================

    /// <summary>As propriedades que a capa e o cabeçalho leem (valem se alguém atualizar os campos no Word).</summary>
    private void Propriedades()
    {
        var props = _doc.PackageProperties;
        props.Title = PropostaWord.Referencia(_o);
        props.Category = _o.Project;
        props.Description = _o.AttentionTo;
        props.Keywords = _o.SuaReferencia;
        props.Creator = _o.PreparedBy;

        if (_doc.ExtendedFilePropertiesPart?.Properties is { } extendidas)
        {
            extendidas.Company ??= new DocumentFormat.OpenXml.ExtendedProperties.Company();
            extendidas.Company.Text = _o.ClientName;
            extendidas.Save();
        }

        // cidade e data moram numa parte XML à parte (CoverPageProperties)
        foreach (var parte in _doc.MainDocumentPart!.CustomXmlParts)
        {
            string xml;
            using (var leitura = parte.GetStream(FileMode.Open, FileAccess.Read))
            using (var leitor = new StreamReader(leitura))
                xml = leitor.ReadToEnd();
            if (!xml.Contains("CoverPageProperties")) continue;

            xml = TrocarTag(xml, "PublishDate", Data());
            xml = TrocarTag(xml, "CompanyAddress", _o.City);

            using var escrita = parte.GetStream(FileMode.Create, FileAccess.Write);
            using var escritor = new StreamWriter(escrita);
            escritor.Write(xml);
        }
    }

    private static string TrocarTag(string xml, string tag, string valor)
    {
        var seguro = System.Security.SecurityElement.Escape(valor ?? "") ?? "";
        var vazia = $"<{tag}/>";
        if (xml.Contains(vazia)) return xml.Replace(vazia, $"<{tag}>{seguro}</{tag}>");
        var abre = xml.IndexOf($"<{tag}>", StringComparison.Ordinal);
        var fecha = xml.IndexOf($"</{tag}>", StringComparison.Ordinal);
        if (abre < 0 || fecha < abre) return xml;
        return xml[..(abre + tag.Length + 2)] + seguro + xml[fecha..];
    }

    /// <summary>
    /// Os campos da capa, do cabeçalho e da primeira nota, ligados às
    /// propriedades. Sem valor o campo SAI — senão o Word volta a mostrar o
    /// exemplo, e a proposta iria para o cliente escrito "[Categoria]".
    /// </summary>
    private void CamposLigados()
    {
        var valores = new Dictionary<string, string>
        {
            ["Empresa"] = _o.ClientName,
            ["Categoria"] = _o.Project,
            ["Comentários"] = _o.AttentionTo,
            ["Título"] = PropostaWord.Referencia(_o),
            ["Endereço da Empresa"] = _o.City,
            ["Palavras-chave"] = _o.SuaReferencia,
            ["Data de Publicação"] = Data(),
        };

        foreach (var parte in Partes())
        {
            foreach (var sdt in parte.Descendants<SdtElement>().ToList())
            {
                var alias = sdt.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value;
                if (alias is null || !valores.TryGetValue(alias, out var valor)) continue;

                if ((valor ?? "").Trim().Length == 0)
                {
                    Desembrulhar(sdt);
                    continue;
                }

                sdt.SdtProperties?.GetFirstChild<ShowingPlaceholder>()?.Remove();
                foreach (var cinza in sdt.Descendants<Color>().ToList()) cinza.Remove();

                var textos = sdt.Descendants<Text>().ToList();
                if (textos.Count == 0) continue;
                textos[0].Text = valor!;
                textos[0].Space = SpaceProcessingModeValues.Preserve;
                foreach (var sobra in textos.Skip(1)) sobra.Remove();
            }
            parte.Save();
        }
    }

    /// <summary>Tira o campo e deixa no lugar o que ele embrulhava, vazio.</summary>
    private static void Desembrulhar(SdtElement sdt)
    {
        var conteudo = sdt.ChildElements.FirstOrDefault(e =>
            e is SdtContentBlock or SdtContentRun or SdtContentCell or SdtContentRow);
        if (conteudo is null || sdt.Parent is null) { sdt.Remove(); return; }

        foreach (var texto in conteudo.Descendants<Text>().ToList()) texto.Text = "";
        foreach (var filho in conteudo.ChildElements.ToList())
        {
            filho.Remove();
            sdt.Parent.InsertBefore(filho, sdt);
        }
        sdt.Remove();
    }

    /// <summary>Tira o campo de escolha (lista) e deixa o conteúdo dele no lugar, COM o texto.</summary>
    private static void Abrir(SdtElement sdt)
    {
        var conteudo = sdt.ChildElements.FirstOrDefault(e =>
            e is SdtContentBlock or SdtContentRun or SdtContentCell or SdtContentRow);
        if (conteudo is null || sdt.Parent is null) { sdt.Remove(); return; }
        foreach (var filho in conteudo.ChildElements.ToList())
        {
            filho.Remove();
            sdt.Parent.InsertBefore(filho, sdt);
        }
        sdt.Remove();
    }

    // ================= capa =================

    private Table Capa0() => Tabelas().First();

    private void Capa()
    {
        var capa = Capa0();
        Rotulo(capa, "E-mail:", _o.ContatoEmail);
        Rotulo(capa, "Fone:", _o.ContatoTelefone);
        MesmaFonteDoCliente(capa);
        Rotulo(capa, "Preparada por:", _o.PreparedBy);
        // "Sua referência:" é um campo ligado (Palavras-chave), já escrito em CamposLigados

        // a faixa azul: Howden Chile / Howden Peru (a HSA fica com Howden South America)
        if (FaturamentoDaBu.NomeNaCapa(_o.Bu) is { } nome
            && capa.Descendants<Paragraph>().FirstOrDefault(p => Texto(p).Trim() == "Howden South America") is { } faixa)
            Escrever(faixa, nome);

        // só a comercial: o subtítulo perde o "Técnica"
        if (capa.Descendants<Paragraph>().FirstOrDefault(p => Texto(p).Trim() == "Proposta Técnica Comercial") is { } sub)
            Escrever(sub, "Proposta Comercial");
    }

    /// <summary>
    /// O cabeçalho da capa sai todo com a fonte do nome da empresa (Arial,
    /// negrito): do lado do cliente (empresa, A/C, cidade, e-mail, telefone,
    /// sua referência) e do outro lado (projeto, nossa referência, data,
    /// preparada por) — vários vinham com a fonte cinza do texto de exemplo.
    /// </summary>
    private void MesmaFonteDoCliente(Table capa)
    {
        var empresa = capa.Descendants<SdtElement>()
            .FirstOrDefault(s => s.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value == "Empresa");
        var molde = empresa?.Descendants<Run>().FirstOrDefault(r => r.Elements<Text>().Any(t => t.Text.Trim().Length > 0))?.RunProperties;
        if (molde is null) return;

        var alvos = new List<OpenXmlElement>();
        foreach (var rotulo in new[] { "E-mail:", "Fone:", "Preparada por:" })
            if (Celula(capa, rotulo)?.NextSibling<TableCell>() is { } valor) alvos.Add(valor);
        alvos.AddRange(capa.Descendants<SdtElement>()
            .Where(s => s.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value is
                "Comentários" or "Endereço da Empresa" or "Categoria" or "Título" or "Data de Publicação" or "Palavras-chave"));
        foreach (var alvo in alvos)
            foreach (var run in alvo.Descendants<Run>().Where(r => r.Elements<Text>().Any()))
            {
                run.RunProperties?.Remove();
                run.PrependChild((RunProperties)molde.CloneNode(true));
            }
    }

    /// <summary>Escreve o valor na célula seguinte à do rótulo.</summary>
    private static void Rotulo(Table tabela, string rotulo, string valor)
    {
        if (Celula(tabela, rotulo) is not { } celula) return;
        if (celula.NextSibling<TableCell>() is { } seguinte)
            EscreverNaCelula(seguinte, new[] { valor ?? "" });
    }

    /// <summary>
    /// O endereço da BU. O modelo traz os quatro (Itatiba, Serra, Chile e
    /// Peru), um bloco por empresa; fica o da BU da proposta e o "Web:".
    /// </summary>
    private void EnderecoDaBu()
    {
        var marcas = new Dictionary<string, string>
        {
            ["HSA-SP"] = "Osvaldo Berto", ["HSA-ES"] = "Rua 4E", ["HCHL"] = "Cordillera",
            ["HCHL_Custo_USD"] = "Cordillera", ["HPU"] = "Guillermo Marconi",
        };
        var escolhida = marcas.GetValueOrDefault((_o.Bu ?? "").Trim(), "Osvaldo Berto");

        var celula = Capa0().Descendants<TableCell>()
            .FirstOrDefault(c => Texto(c).Contains("Osvaldo Berto", StringComparison.Ordinal));
        if (celula is null) return;

        var blocos = new List<List<Paragraph>> { new() };
        foreach (var p in celula.Elements<Paragraph>())
        {
            if (Texto(p).Trim().Length == 0) blocos.Add(new());
            else blocos[^1].Add(p);
        }
        foreach (var bloco in blocos)
        {
            var texto = string.Join(" ", bloco.Select(Texto));
            if (texto.Contains(escolhida, StringComparison.Ordinal) || texto.Contains("Web:", StringComparison.Ordinal)) continue;
            foreach (var p in bloco) p.Remove();
        }

        // os brancos que separavam os quatro endereços: fica um, antes do "Web:"
        var brancos = celula.Elements<Paragraph>().Where(p => Texto(p).Trim().Length == 0).ToList();
        var web = celula.Elements<Paragraph>().FirstOrDefault(p => Texto(p).Contains("Web:", StringComparison.Ordinal));
        foreach (var b in brancos) b.Remove();
        if (web is not null && brancos.Count > 0) celula.InsertBefore((Paragraph)brancos[0].CloneNode(true), web);
    }

    /// <summary>
    /// Os contatos Howden. Na capa fica a linha do diretor de vendas; ao lado
    /// dele entra o vendedor da unidade do cliente — com os dados que o modelo
    /// já tem dele (a tabela dos quinze contatos), ou com os do cadastro.
    /// </summary>
    private void Contatos()
    {
        var capa = Capa0();
        var doDiretor = capa.Elements<TableRow>().FirstOrDefault(l => Texto(l).Contains("Geraldini", StringComparison.Ordinal));
        if (doDiretor is null) return;
        var celulas = doDiretor.Elements<TableCell>().ToList();
        if (celulas.Count < 2) return;
        var molde = celulas[^1];

        // o 1º vendedor no primeiro espaço, o 2º (opcional) no segundo; o diretor fica no último
        var linhas = LinhasDoVendedor();
        var linhas2 = _d.CartaoDoVendedor2 is { Count: > 0 } c2 ? c2.ToList() : new List<string>();
        for (var i = 0; i < celulas.Count - 1; i++)
        {
            if (i == 0 && linhas.Count > 0) ComoOMolde(celulas[i], molde, linhas);
            else if (i == 1 && linhas2.Count > 0) ComoOMolde(celulas[i], molde, linhas2);
            else EscreverNaCelula(celulas[i], new[] { "" });
        }
    }

    private List<string> LinhasDoVendedor()
    {
        // o vendedor escolhido na proposta (lista da equipe): o cartão sai dele
        if (_d.CartaoDoVendedor is { Count: > 0 } cartao) return cartao.ToList();
        if (_d.Vendedor is not { } v || (v.Nome ?? "").Trim().Length == 0) return new();

        // o modelo tem o cartão completo de cada um: nome, cargo, área, telefones e e-mail
        var nome = Textos.Simples(v.Nome);
        var partes = nome.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var contatos = Tabelas().Skip(1).FirstOrDefault(t => Texto(t).Contains("@chartindustries.com", StringComparison.Ordinal));
        if (contatos is not null && partes.Length > 0)
        {
            foreach (var cel in contatos.Descendants<TableCell>())
            {
                var ps = cel.Elements<Paragraph>().Select(Texto).Where(t => t.Trim().Length > 0).ToList();
                if (ps.Count == 0) continue;
                var doCartao = Textos.Simples(ps[0]);
                if (partes.All(doCartao.Contains) || doCartao == nome) return ps;
            }
        }
        return new[] { v.Nome, v.Cargo, v.Telefone, v.Email }.Where(s => (s ?? "").Trim().Length > 0).Select(s => s!.Trim()).ToList();
    }

    private static void ComoOMolde(TableCell celula, TableCell molde, IReadOnlyList<string> linhas)
    {
        foreach (var antigo in celula.Elements<Paragraph>().ToList()) antigo.Remove();
        foreach (var p in molde.Elements<Paragraph>()) celula.Append((Paragraph)p.CloneNode(true));
        if (molde.TableCellProperties?.GetFirstChild<TableCellVerticalAlignment>() is { } alinhamento)
        {
            celula.TableCellProperties ??= new TableCellProperties();
            celula.TableCellProperties.GetFirstChild<TableCellVerticalAlignment>()?.Remove();
            celula.TableCellProperties.Append((TableCellVerticalAlignment)alinhamento.CloneNode(true));
        }
        EscreverNaCelula(celula, linhas);
    }

    /// <summary>
    /// O representante. O modelo lista todos numa página de consulta; fica só
    /// o desta proposta (Rep 1 e Rep 2 do cabeçalho), com o contato que o
    /// modelo traz dele. Sem representante, a linha inteira sai.
    /// </summary>
    private void Representante()
    {
        var celula = Capa0().Descendants<TableCell>()
            .FirstOrDefault(c => Texto(c).TrimStart().StartsWith("Representante:", StringComparison.Ordinal));
        if (celula is null) return;

        var reps = new[] { _o.Rep1, _o.Rep2 }
            .Select(r => (r ?? "").Trim()).Where(r => r.Length > 0 && r != "-").Distinct().ToList();
        if (reps.Count == 0)
        {
            celula.Ancestors<TableRow>().FirstOrDefault()?.Remove();
            return;
        }

        var linhas = new List<string> { "Representante:" };
        linhas.AddRange(reps.Select(ContatoDoRepresentante));
        EscreverNaCelula(celula, linhas);
    }

    /// <summary>A linha do representante na página de consulta do modelo; fora dela, só o nome.</summary>
    private string ContatoDoRepresentante(string nome)
    {
        var chave = nome;
        foreach (var corte in new[] { " (", " <", " >" })
            if (chave.IndexOf(corte, StringComparison.Ordinal) is var i and > 0) chave = chave[..i];
        chave = Textos.Simples(chave);
        if (chave.Length < 3) return nome;

        var inicio = Achar("Representante:");
        var fim = Achar("Consulta REPRESENTANTES");
        if (inicio is null || fim is null) return nome;
        var linha = Entre(inicio, fim).OfType<Paragraph>()
            .FirstOrDefault(p => Textos.Simples(Texto(p)).Contains(chave));
        return linha is null ? nome : Texto(linha).Trim();
    }

    /// <summary>
    /// A segunda página do modelo é de CONSULTA para quem monta a proposta: os
    /// quinze contatos e todos os representantes, até a instrução "Consulta
    /// REPRESENTANTES". Depois de usada, sai inteira (com a quebra de página dela).
    /// </summary>
    private void TirarPaginaDeConsulta()
    {
        var contatos = Tabelas().Skip(1).FirstOrDefault(t => Texto(t).Contains("@chartindustries.com", StringComparison.Ordinal));
        var consulta = Achar("Consulta REPRESENTANTES");
        if (contatos is null || consulta is null) return;

        var fim = _corpo.ChildElements.SkipWhile(e => e != consulta).Skip(1)
            .OfType<Paragraph>().FirstOrDefault(p => p.Descendants<Break>().Any(b => b.Type?.Value == BreakValues.Page));

        var apagar = new List<OpenXmlElement>();
        var dentro = false;
        foreach (var e in _corpo.ChildElements)
        {
            if (e == contatos) dentro = true;
            if (dentro) apagar.Add(e);
            if (e == (fim ?? (OpenXmlElement)consulta)) break;
        }
        foreach (var e in apagar) e.Remove();

        // a quebra de página logo depois da capa vira "quebra antes" no parágrafo
        // seguinte: a capa ocupa a folha inteira, e um parágrafo só de quebra
        // cairia no alto da página 2 e empurraria o índice para a 3 (página em branco)
        if (Capa0().NextSibling<Paragraph>() is { } quebra &&
            quebra.Descendants<Break>().Any(b => b.Type?.Value == BreakValues.Page) && Texto(quebra).Trim().Length == 0 &&
            quebra.NextSibling<Paragraph>() is { } seguinte)
        {
            quebra.Remove();
            seguinte.ParagraphProperties ??= new ParagraphProperties();
            seguinte.ParagraphProperties.PageBreakBefore = new PageBreakBefore();
        }
    }

    private void Revisoes()
    {
        var tabela = Tabelas().FirstOrDefault(t => Texto(t).StartsWith("Rev.", StringComparison.Ordinal));
        if (tabela is null) return;
        var linhas = tabela.Elements<TableRow>().ToList();
        if (linhas.Count < 2) return;

        var celulas = linhas[1].Elements<TableCell>().ToList();
        if (celulas.Count < 4) return;
        var revisao = PropostaWord.Ou(_o.Revisao, "0");
        EscreverNaCelula(celulas[0], new[] { revisao });
        EscreverNaCelula(celulas[1], new[] { _o.PreparedBy ?? "" });
        EscreverNaCelula(celulas[2], new[] { "RRT" });

        // as linhas de revisões futuras do modelo saem vazias
        foreach (var vazia in linhas.Skip(2)) vazia.Remove();

        // a coluna "Descrição:" sai da proposta (pedido da equipe): a largura dela vai para "Exec"
        var grade = tabela.GetFirstChild<TableGrid>();
        var colunas = grade?.Elements<GridColumn>().ToList();
        if (colunas is { Count: 4 })
        {
            int N(StringValue? v) => int.TryParse(v?.Value, out var n) ? n : 0;
            colunas[1].Width = (N(colunas[1].Width) + N(colunas[3].Width)).ToString();
            colunas[3].Remove();
            foreach (var linha in tabela.Elements<TableRow>())
                if (linha.Elements<TableCell>().ToList() is { Count: 4 } cels) cels[3].Remove();
            LargurasPelaGrade(tabela);
        }
    }

    /// <summary>A largura de cada célula, em porcentagem, pela grade da tabela.</summary>
    private static void LargurasPelaGrade(Table tabela)
    {
        var colunas = tabela.GetFirstChild<TableGrid>()?.Elements<GridColumn>().ToList() ?? new();
        int N(StringValue? v) => int.TryParse(v?.Value, out var n) ? n : 0;
        var total = colunas.Sum(c => N(c.Width));
        if (total <= 0) return;
        foreach (var linha in tabela.Elements<TableRow>())
        {
            var cels = linha.Elements<TableCell>().ToList();
            if (cels.Count != colunas.Count) continue;
            for (var i = 0; i < cels.Count; i++)
            {
                var props = cels[i].TableCellProperties ??= new TableCellProperties();
                props.TableCellWidth = new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = (N(colunas[i].Width) * 5000 / total).ToString() };
            }
        }
    }

    /// <summary>Alarga uma coluna até o texto caber numa linha, tirando a diferença da última (como no axiaisvent).</summary>
    private static void Alargar(Table tabela, int coluna, string texto)
    {
        const int PorLetra = 120, Margem = 280;
        var grade = tabela.GetFirstChild<TableGrid>();
        if (grade is null) return;
        var colunas = grade.Elements<GridColumn>().ToList();
        if (coluna >= colunas.Count || colunas.Count < 2) return;

        int N(StringValue? v) => int.TryParse(v?.Value, out var n) ? n : 0;
        var atual = N(colunas[coluna].Width);
        var preciso = texto.Trim().Length * PorLetra + Margem;
        if (preciso <= atual) return;
        var ultima = colunas.Count - 1;
        var sobra = N(colunas[ultima].Width);
        var ganho = Math.Min(preciso - atual, sobra / 2);
        if (ganho <= 0) return;
        colunas[coluna].Width = (atual + ganho).ToString();
        colunas[ultima].Width = (sobra - ganho).ToString();

        var total = colunas.Sum(c => N(c.Width));
        if (total <= 0) return;
        foreach (var linha in tabela.Elements<TableRow>())
        {
            var cels = linha.Elements<TableCell>().ToList();
            if (cels.Count != colunas.Count) continue;
            for (var i = 0; i < cels.Count; i++)
            {
                var props = cels[i].TableCellProperties ??= new TableCellProperties();
                props.TableCellWidth = new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = (N(colunas[i].Width) * 5000 / total).ToString() };
            }
        }
    }

    // ================= introdução e preço =================

    private void Introducao()
    {
        if (Achar("Prezado Sr.") is { } saudacao)
            Escrever(saudacao, (_o.AttentionTo ?? "").Trim().Length > 0 ? $"Prezado(a) Sr(a). {_o.AttentionTo!.Trim()}," : "Prezados Senhores,");

        if (Achar("Howden tem a satisfação de apresentar") is { } apresentacao)
            Escrever(apresentacao, $"Howden tem a satisfação de apresentar para {PropostaWord.Ou(_o.ClientName, "—")} " +
                                   "sua oferta para o fornecimento de partes e peças.");
    }

    private void Precos()
    {
        // não há proposta técnica: a frase que apontava para ela sai
        Achar("Preços conforme descrição da Proposta Técnica")?.Remove();
        TabelaDePrecos();

        // a quebra de página depois da tabela de preços (no parágrafo vazio após a nota "(*)") sai:
        // as Notas Comerciais seguem logo abaixo do preço
        if (Achar("A descrição do produto deverá estar presente na Ordem de Compra") is { } nota
            && nota.NextSibling() is Paragraph seguinte && Texto(seguinte).Trim().Length == 0)
        {
            foreach (var quebra in seguinte.Descendants<Break>().Where(b => b.Type?.Value == BreakValues.Page).ToList()) quebra.Remove();
            foreach (var p in seguinte.Descendants<ParagraphProperties>()) p.RemoveAllChildren<PageBreakBefore>();
        }
    }

    /// <summary>
    /// HCHL / HPU: as Notas Comerciais começam com duas notas a mais (preço só
    /// comprando todos os itens; documentos de importação não inclusos). A HSA
    /// fica como o modelo.
    /// </summary>
    private void NotasComerciais()
    {
        // as notas ficam numa tabela do modelo: procura em todos os parágrafos
        if (!DeFora || _corpo.Descendants<Paragraph>().FirstOrDefault(p => Texto(p).Contains("Excluso de nosso fornecimento qualquer item", StringComparison.Ordinal)) is not { } primeira)
            return;
        foreach (var texto in new[]
        {
            "Os preços e condições considerados nesta oferta aplicam-se somente se forem comprados todos os itens aqui ofertados. Caso contrário, os preços e condições podem estar sujeitos a alterações.",
            "Não está incluído no escopo qualquer documento associado para realizar a importação (certificado EUR1, catálogos, ficha técnica etc.). Caso necessário, solicitar junto com o pedido ou com a ordem de compra para ser incorporado ao pedido.",
        })
        {
            var nota = (Paragraph)primeira.CloneNode(true);
            Escrever(nota, texto);
            primeira.InsertBeforeSelf(nota);
        }
    }

    private decimal Total => _d.Itens.Sum(i => i.TotalPrice);

    private string Dinheiro(decimal valorNaMoedaDoPreco) => Cambio.Dinheiro(_o, valorNaMoedaDoPreco);

    private void TabelaDePrecos()
    {
        var tabela = Tabelas().FirstOrDefault(t => Texto(t).StartsWith("Item", StringComparison.Ordinal) && Texto(t).Contains("Descrição"));
        if (tabela is null) return;
        var linhas = tabela.Elements<TableRow>().ToList();
        if (linhas.Count < 3) return;

        // o cabeçalho do modelo termina em "Preço | Item": são o unitário e o total
        var cab = linhas[0].Elements<TableCell>().ToList();
        if (cab.Count >= 5)
        {
            EscreverNaCelula(cab[3], new[] { "Preço Unitário" });
            EscreverNaCelula(cab[4], new[] { "Preço Total" });
        }

        var total = linhas[^1];
        foreach (var sdt in total.Descendants<SdtElement>().ToList()) Abrir(sdt);
        var celTotal = total.Elements<TableCell>().ToList();
        if (celTotal.Count >= 2 && FaturamentoDaBu.ImpostoLocal(_o.Bu) is { } local)
        {
            // BU de fora: "Precio Total (USD)  38.557,00 / + IGV" — o líquido, com o imposto local à parte
            var moeda = Cambio.MoedaDoPreco(_o).Rotulo();
            var rotulo = _idioma == IdiomasDaProposta.Ingles ? "Total Price" : _idioma == IdiomasDaProposta.Espanhol ? "Precio Total" : "Preço Total";
            var numero = Total.ToString(Moedas.Formato(Cambio.MoedaDoPreco(_o)), Br);
            if (_idioma == IdiomasDaProposta.Ingles) numero = TraducaoDaProposta.NumeroEmIngles(numero);
            EscreverNaCelula(celTotal[0], new[] { $"{rotulo} ({moeda})" });
            // para outro país (Colômbia, Panamá...) nada se soma ao preço
            EscreverNaCelula(celTotal[^1], OutroPais ? new[] { numero } : new[] { numero, $"+ {local}" });
        }
        else if (celTotal.Count >= 2)
        {
            // a opção da lista do modelo com os impostos em que os preços estão
            EscreverNaCelula(celTotal[0], new[] { _nivel.NoWord });
            EscreverNaCelula(celTotal[^1], new[] { Dinheiro(Total) });
        }

        // o rótulo do total ("VALOR LÍQUIDO", "Precio Total (USD)") em negrito, na cor do valor ao lado
        if (celTotal.Count >= 2) RotuloEmNegrito(celTotal[0], celTotal[^1]);

        // todas as linhas de item saem de uma cópia da primeira (é ela que tem os marcadores)
        var modelo = (TableRow)linhas[1].CloneNode(true);
        // um item não se parte entre duas páginas
        modelo.TableRowProperties ??= new TableRowProperties();
        if (!modelo.TableRowProperties.Elements<CantSplit>().Any()) modelo.TableRowProperties.Append(new CantSplit());
        for (var i = 0; i < _d.Itens.Count; i++)
        {
            var linha = (TableRow)modelo.CloneNode(true);
            PreencherItem(linha, i);
            tabela.InsertBefore(linha, total);
        }
        foreach (var antiga in linhas.Skip(1).Take(linhas.Count - 2)) antiga.Remove();
    }

    private static void RotuloEmNegrito(TableCell rotulo, TableCell valor)
    {
        var cor = valor.Descendants<Run>().FirstOrDefault(r => r.Elements<Text>().Any(t => t.Text.Trim().Length > 0))
            ?.RunProperties?.GetFirstChild<Color>();
        foreach (var run in rotulo.Descendants<Run>().Where(r => r.Elements<Text>().Any()))
        {
            run.RunProperties ??= new RunProperties();
            var rp = run.RunProperties;
            if (rp.GetFirstChild<Bold>() is null) rp.Append(new Bold());
            if (rp.GetFirstChild<BoldComplexScript>() is null) rp.Append(new BoldComplexScript());
            rp.GetFirstChild<Color>()?.Remove();
            rp.Append(cor is not null ? (Color)cor.CloneNode(true) : new Color { Val = "000000" });
            NaOrdem(rp);
        }
    }

    /// <summary>A ordem que o Word exige dentro da formatação do texto (w:rPr): sem ela, o Word reclama ao abrir.</summary>
    private static readonly Type[] OrdemDoRPr =
    {
        typeof(RunStyle), typeof(RunFonts), typeof(Bold), typeof(BoldComplexScript), typeof(Italic), typeof(ItalicComplexScript),
        typeof(Caps), typeof(SmallCaps), typeof(Strike), typeof(DoubleStrike), typeof(Outline), typeof(Shadow), typeof(Emboss),
        typeof(Imprint), typeof(NoProof), typeof(SnapToGrid), typeof(Vanish), typeof(WebHidden), typeof(Color), typeof(Spacing),
        typeof(CharacterScale), typeof(Kern), typeof(Position), typeof(FontSize), typeof(FontSizeComplexScript), typeof(Highlight),
        typeof(Underline), typeof(TextEffect), typeof(Border), typeof(Shading), typeof(FitText), typeof(VerticalTextAlignment),
        typeof(RightToLeftText), typeof(ComplexScript), typeof(Emphasis), typeof(Languages),
    };

    private static void NaOrdem(RunProperties rp)
    {
        var filhos = rp.ChildElements.ToList();
        int Posicao(OpenXmlElement e) => Array.IndexOf(OrdemDoRPr, e.GetType()) is var i and >= 0 ? i : OrdemDoRPr.Length;
        var ordenados = filhos.Select((e, i) => (e, i)).OrderBy(x => Posicao(x.e)).ThenBy(x => x.i).Select(x => x.e).ToList();
        if (ordenados.SequenceEqual(filhos)) return;
        foreach (var f in filhos) f.Remove();
        foreach (var f in ordenados) rp.Append(f);
    }

    private void PreencherItem(TableRow linha, int indice)
    {
        var item = _d.Itens[indice];
        var celulas = linha.Elements<TableCell>().ToList();
        if (celulas.Count < 5) return;

        EscreverNaCelula(celulas[0], new[] { (indice + 1).ToString("D2") });
        Descricao(celulas[1], item);
        EscreverNaCelula(celulas[2], new[] { item.Quantity.ToString() });
        EscreverNaCelula(celulas[3], new[] { Dinheiro(item.UnitPrice) });
        EscreverNaCelula(celulas[4], new[] { Dinheiro(item.TotalPrice) });
    }

    /// <summary>
    /// A descrição do item, nos parágrafos do modelo (é deles que vêm o
    /// negrito, os marcadores e o recuo): título, peça, REFERÊNCIAS (modelo e
    /// TAG, aplicação, último fornecimento / original, part number), ESCOPO DE
    /// FORNECIMENTO, prazo de entrega e NCM. Linha sem dado sai.
    /// </summary>
    private void Descricao(TableCell celula, CommercialOfferItem item)
    {
        foreach (var sdt in celula.Elements<SdtElement>().ToList()) Abrir(sdt);

        var eq = item.EquipmentId.Length > 0 && _d.Equipamentos.TryGetValue(item.EquipmentId, out var e) ? e : null;
        var pn = (item.PartNumber ?? "").Trim();
        var bom = pn.Length == 0 ? null
            : _d.Bom.FirstOrDefault(b => b.EquipmentId == item.EquipmentId && IntelligenceService.NormPn(b.PartNumber) == IntelligenceService.NormPn(pn))
              ?? _d.Bom.FirstOrDefault(b => IntelligenceService.NormPn(b.PartNumber) == IntelligenceService.NormPn(pn));
        // a peça na língua da proposta: a BOM tem português e inglês (o espanhol fica com o português, mais próximo)
        var peca = _idioma == IdiomasDaProposta.Ingles
            ? PrimeiroCom(bom?.DescricaoEn, bom?.DescricaoPt, DescricaoSemPn(item.Description, pn))
            : PrimeiroCom(bom?.DescricaoPt, bom?.DescricaoEn, DescricaoSemPn(item.Description, pn));
        var servico = item.Description.StartsWith("Serviços", StringComparison.OrdinalIgnoreCase);
        var paragrafos = celula.Elements<Paragraph>().ToList();
        Paragraph? P(string ancora) => paragrafos.FirstOrDefault(p => Texto(p).Contains(ancora, StringComparison.Ordinal));
        void Tirar(Paragraph? p) { if (p is not null) { p.Remove(); paragrafos.Remove(p); } }

        // o modelo do compressor: o do item, senão o do equipamento da proposta
        var modelo = PrimeiroCom(item.Modelo, eq?.Modelo);

        // título: o sistema é de compressores ("Partes e Peças para Compressor <modelo>") e, embaixo, a peça
        var titulo = paragrafos.FirstOrDefault(p => Texto(p).Contains("PREENCHER!!", StringComparison.Ordinal));
        if (titulo is not null)
            Escrever(titulo, modelo.Length > 0 ? $"Partes e Peças para Compressor {modelo}" : peca);
        var pPeca = paragrafos.FirstOrDefault(p => Texto(p).Trim() == "XXXXXX");
        if (pPeca is not null)
        {
            if (modelo.Length > 0) Escrever(pPeca, peca);
            else Tirar(pPeca);
        }
        Tirar(P("(EX: Rotor, Eixo, Cone, Selo)"));

        // REFERÊNCIAS — do equipamento da proposta (a ficha do cadastrado ou o
        // que foi digitado na etapa Itens), nesta ordem: modelo do compressor,
        // Serial Number, TAG, Part Number; depois a planta (com mais de uma),
        // a aplicação e o último fornecimento, quando há. As linhas nascem da
        // primeira linha de referência do modelo (marcador e recuo dela).
        var daProposta = PlantasDaProposta.DoItem(_o, item);
        var planta = PlantasDaProposta.PlantaDe(_o, daProposta);
        var cadastroId = daProposta is { Cadastrado: true } ? daProposta.EquipmentId : daProposta is null ? item.EquipmentId : "";
        var instalados = cadastroId.Length == 0 ? new List<InstalledBase>()
            : _d.BaseDaUnidade.Where(b => b.EquipmentId == cadastroId
                                          && (planta is not { Cadastrada: true } || b.ClientUnitId == planta.UnitId)).ToList();
        string DaBase(Func<InstalledBase, string?> campo) =>
            string.Join(", ", instalados.Select(b => (campo(b) ?? "").Trim()).Where(t => t.Length > 0).Distinct());
        var tags = PrimeiroCom(item.Tag, daProposta is not null ? daProposta.Tag : DaBase(b => b.Tag));
        var serial = PrimeiroCom(item.SerialNumber, daProposta is not null ? daProposta.SerialNumber : DaBase(b => b.SerialNo));
        var aplicacao = PrimeiroCom(eq?.Aplicacao, instalados.Select(b => b.Process).FirstOrDefault(x => (x ?? "").Trim().Length > 0));
        var ultima = UltimoFornecimento(item, pn);
        var original = DaBase(b => b.ContractNo);

        var linhasDoModelo = new[] { "Modelo do ventilador", "Aplicação:", "Fornecimento / Original:", "Desenho de Conjunto:", "Part Number:" }
            .Select(P).Where(p => p is not null).Select(p => p!).ToList();
        var referencias = new List<string>();
        void Ref(string rotulo, string valor) { if (valor.Length > 0) referencias.Add($"{rotulo}: {valor}"); }
        Ref("Modelo do compressor", modelo);
        Ref("Serial Number", serial);
        Ref("TAG", tags);
        // o Part Number fica no escopo, na linha debaixo da descrição da peça (sem escopo, nas referências)
        var pnNoEscopo = !servico && pn.Length > 0 && P("Xxxxxxxxxxxx") is not null;
        if (!pnNoEscopo) Ref("Part Number", pn);
        if (planta is not null && _o.Plantas.Count > 1) Ref("Planta", PlantasDaProposta.Rotulo(planta));
        Ref("Aplicação", aplicacao);
        if (ultima.Length > 0 || original.Length > 0)
            Ref("Último Fornecimento / Original", PropostaWord.Ou(ultima, "—") + (original.Length > 0 ? $" / {original}" : ""));
        if (linhasDoModelo.Count > 0)
        {
            var molde = linhasDoModelo[0];
            foreach (var texto in referencias)
            {
                var linha = (Paragraph)molde.CloneNode(true);
                molde.InsertBeforeSelf(linha);
                Escrever(linha, texto);
            }
            foreach (var antiga in linhasDoModelo) Tirar(antiga);
        }
        if (referencias.Count == 0) Tirar(P("REFERÊNCIAS"));

        // ESCOPO DE FORNECIMENTO
        var material = (bom?.Material ?? "").Trim();
        if (servico)
        {
            // serviço: o título já diz o que é; o escopo de peça não se aplica
            Tirar(P("ESCOPO DE FORNECIMENTO"));
            Tirar(P("Xxxxxxxxxxxx"));
        }
        else if (P("Xxxxxxxxxxxx") is { } pEscopo)
        {
            Escrever(pEscopo, material.Length > 0 ? $"{peca} – material: {material}" : peca);
            if (pnNoEscopo)
            {
                var linhaPn = (Paragraph)pEscopo.CloneNode(true);
                Escrever(linhaPn, $"Part Number: {pn}");
                pEscopo.InsertAfterSelf(linhaPn);
                paragrafos.Insert(paragrafos.IndexOf(pEscopo) + 1, linhaPn);
            }
        }
        var rotor = !servico && Textos.Simples(peca).Contains("rotor");
        if (P("Pintura:") is { } pintura)
        {
            if (servico || pn.Length == 0) Tirar(pintura);
            else Escrever(pintura, "Pintura: Conforme projeto original");
        }
        if (!rotor)
        {
            Tirar(P("Balanceamento estático e dinâmico"));
            Tirar(P("Teste não destrutivo no rotor"));
        }
        if (servico) Tirar(P("mbalagem apropriada para transporte"));

        // prazo de entrega: a tabelinha "Prazo de entrega: XXXX Dias"
        var dias = item.PrazoEntregaDias > 0 ? item.PrazoEntregaDias : PricingDaProposta.PrazoEmDias(_o.PrazoEntrega);
        var tabPrazo = celula.Descendants<Table>().FirstOrDefault(tb => tb.Descendants<Text>().Any(t => t.Text.Trim() == "XXXX"));
        if (tabPrazo is not null && dias > 0)
        {
            foreach (var t in tabPrazo.Descendants<Text>().Where(t => t.Text.Trim() == "XXXX").ToList())
                t.Text = dias.ToString();
        }
        else if (tabPrazo is not null)
        {
            // sem prazo, "a confirmar" não cabe na coluna do número (quebrava
            // em "a / confir / mar"): a tabelinha vira uma linha só
            var rotulo = tabPrazo.Descendants<Paragraph>().FirstOrDefault(p => Texto(p).Contains("Prazo de entrega"));
            if (rotulo is not null)
            {
                var linha = (Paragraph)rotulo.CloneNode(true);
                var runs = linha.Elements<Run>().ToList();
                var molde = runs.LastOrDefault();
                var novo = new Run();
                if (molde?.RunProperties is { } rp)
                {
                    var rpNovo = (RunProperties)rp.CloneNode(true);
                    rpNovo.RemoveAllChildren<Bold>();
                    rpNovo.RemoveAllChildren<BoldComplexScript>();
                    novo.RunProperties = rpNovo;
                }
                novo.Append(new Text(" a confirmar") { Space = SpaceProcessingModeValues.Preserve });
                linha.Append(novo);
                tabPrazo.InsertBeforeSelf(linha);
                tabPrazo.Remove();
            }
        }

        // NCM (a lista do modelo)
        if (P("NCM:") is { } pNcm)
        {
            if (servico) Tirar(pNcm);
            // HCHL / HPU: só o código (o IPI é do Brasil); peça de compressor sem NCM = 8414.90.39
            else if (FaturamentoDaBu.ImpostoLocal(_o.Bu) is not null)
                EscreverRotulo(pNcm, "NCM: ", ListasDaProposta.NcmDo(item.Ncm)?.Codigo ?? PropostaWord.Ou(item.Ncm, ListasDaProposta.NcmDeCompressor));
            else EscreverRotulo(pNcm, "NCM: ", ListasDaProposta.NcmDo(item.Ncm)?.Texto ?? "a definir");
        }

        // a célula do Word tem de terminar num parágrafo: sem o do NCM, ela
        // acabaria na tabelinha do prazo, e a linha se desmonta ao abrir
        if (celula.LastChild is Table)
            celula.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" })));
    }

    private static string PrimeiroCom(params string?[] valores) =>
        valores.Select(v => (v ?? "").Trim()).FirstOrDefault(v => v.Length > 0) ?? "";

    /// <summary>"PN — descrição" (como o item entra na proposta) → "descrição".</summary>
    private static string DescricaoSemPn(string descricao, string pn)
    {
        var d = (descricao ?? "").Trim();
        if (pn.Length > 0 && d.StartsWith(pn, StringComparison.OrdinalIgnoreCase))
            d = d[pn.Length..].TrimStart(' ', '—', '-', '–').Trim();
        return d;
    }

    /// <summary>A última venda do partnumber para esta unidade (data e pedido); sem venda, a referência gravada no item.</summary>
    private string UltimoFornecimento(CommercialOfferItem item, string pn)
    {
        if (pn.Length > 0)
        {
            var venda = _d.VendasDaUnidade
                .Where(v => IntelligenceService.NormPn(v.PartNumber) == IntelligenceService.NormPn(pn))
                .Select(v => (Venda: v, Data: IntelligenceService.DataDaVenda(v.DataVenda)))
                .Where(x => x.Data is not null)
                .OrderByDescending(x => x.Data)
                .FirstOrDefault();
            if (venda.Venda is not null)
                return venda.Data!.Value.ToString("dd/MM/yyyy") + ((venda.Venda.Pedido ?? "").Trim() is { Length: > 0 } ped ? $" (pedido {ped})" : "");
        }
        return IntelligenceService.DataDaVenda(item.UltimaVendaData) is { } d ? d.ToString("dd/MM/yyyy") : "";
    }

    // ================= impostos =================

    /// <summary>
    /// Impostos Vigentes: o modelo traz todas as alternativas (Itatiba e
    /// Vitória, cinco linhas de ICMS, três de IPI, duas de ISS). Ficam as da
    /// proposta, com as alíquotas do pricing e o IPI da NCM de cada item; e a
    /// tabela "Detalhamento para adição dos Impostos" recebe os valores.
    /// </summary>
    private void Impostos()
    {
        var r = _pricing;
        var pis = Achar("PIS: 1,65%");
        var cofins = Achar("COFINS: 7,6%");
        var icmsSp = Achar("ICMS: (18% - SP)");
        var ipiModelo = Achar("IPI: 3,25%");
        var issItatiba = Achar("ISS: 2%");
        var issSerra = Achar("ISS: 5%");
        var apagar = new List<Paragraph?>
        {
            Achar("ITATIBA:"), Achar("ICMS: (12% - MG"), Achar("ICMS: (7% - INDICAR"), Achar("VITORIA:"),
            Achar("ICMS: (12% - INDICAR"), Achar("ICMS: (17% - ES)"), Achar("IPI: 0%"), Achar("IPI: 9,75%"),
        };

        Paragraph?[] LeiBrasileira() => new[]
        {
            Achar("Fundamento Legal"), Achar("Nota sobre Legislação Tributária"),
            Achar("A presente proposta comercial foi elaborada com base na legislação tributária"),
            Achar("Dessa forma, a Howden South America se reserva o direito de revisar"),
        };

        if (OutroPais)
        {
            // venda para outro país (fora Brasil, Chile e Peru): nada se soma — valores netos
            if (pis is not null) Escrever(pis, "Valores líquidos: Impostos e Retenções não inclusos.");
            apagar.AddRange(new[] { cofins, icmsSp, ipiModelo, issItatiba, issSerra });
            apagar.AddRange(LeiBrasileira());
        }
        else if (FaturamentoDaBu.ImpostoLocal(_o.Bu) is { } local)
        {
            // BU de fora: os impostos do Brasil não se aplicam; o IVA / IGV vai somado ao valor líquido
            if (pis is not null) Escrever(pis, _idioma switch
            {
                IdiomasDaProposta.Ingles => $"Net values: {local} not included (added to the price).",
                IdiomasDaProposta.Espanhol => $"Valores netos: {local} no incluido (se suma al precio).",
                _ => $"Valores líquidos: {local} não incluso (somado ao preço).",
            });
            apagar.AddRange(new[] { cofins, icmsSp, ipiModelo, issItatiba, issSerra });
            // a lei brasileira e a reforma tributária não valem para o Chile nem o Peru
            apagar.AddRange(LeiBrasileira());
        }
        else if (r.SemImpostos)
        {
            if (pis is not null) Escrever(pis, "Venda para exportação: não incidem PIS, COFINS, ICMS e IPI.");
            apagar.AddRange(new[] { cofins, icmsSp, ipiModelo, issItatiba, issSerra });
        }
        else
        {
            if (pis is not null) Escrever(pis, $"PIS: {Pct(r.AliqPis)} {Incluso(_nivel.PisCofins)};");
            if (cofins is not null) Escrever(cofins, $"COFINS: {Pct(r.AliqCofins)} {Incluso(_nivel.PisCofins)};");

            var uf = (_o.Estado ?? "").Trim();
            if (icmsSp is not null)
                Escrever(icmsSp, r.ProcurarFinanceiro ? "ICMS: a confirmar com o Financeiro (revenda ou cliente não-contribuinte);"
                    : r.AliqIcms is decimal icms ? $"ICMS: ({Pct(icms)} - {PropostaWord.Ou(uf, "ESTADO")}) {Incluso(_nivel.Icms)}"
                    : "ICMS: (INDICAR ESTADO) não incluso");

            // IPI: uma linha por NCM dos itens (a NCM diz a alíquota); sem NCM, a do pricing
            if (ipiModelo is not null)
            {
                var ncms = _d.Itens.Select(i => ListasDaProposta.NcmDo(i.Ncm)).Where(n => n is not null).Distinct().ToList();
                var linhas = r.AliqIpi == 0m && Textos.Igual(_o.ClienteCom, "Zona Franca de Manaus")
                    ? new List<string> { "IPI: não incide (Zona Franca de Manaus);" }
                    : ncms.Count > 0
                        ? ncms.Select(n => $"IPI: {Pct(n!.Ipi)} {Incluso(_nivel.Ipi)}; NCM {n!.Texto}").ToList()
                        : new List<string> { $"IPI: {Pct(r.AliqIpi)} {Incluso(_nivel.Ipi)};" };
                foreach (var l in linhas)
                {
                    var novo = (Paragraph)ipiModelo.CloneNode(true);
                    Escrever(novo, l);
                    _corpo.InsertBefore(novo, ipiModelo);
                }
                apagar.Add(ipiModelo);
            }

            // ISS só na MO de Reforma / Service, do município da BU (o vermelho do modelo é marca de escolher)
            if (r.ServicoOuReforma && Textos.Igual(_o.Bu, "HSA-SP"))
            {
                if (issItatiba is not null) Escrever(issItatiba, $"ISS: {Pct(r.AliqIss)} não incluso; ITATIBA/SP");
                apagar.Add(issSerra);
            }
            else if (r.ServicoOuReforma && Textos.Igual(_o.Bu, "HSA-ES"))
            {
                if (issSerra is not null) Escrever(issSerra, $"ISS: {Pct(r.AliqIss)} não incluso; SERRA/ES");
                apagar.Add(issItatiba);
            }
            else apagar.AddRange(new[] { issItatiba, issSerra });
        }
        foreach (var p in apagar) p?.Remove();

        DetalhamentoDosImpostos(r);
    }

    /// <summary>
    /// "Detalhamento para adição dos Impostos": o total líquido da proposta e
    /// ele com PIS/COFINS, com ICMS e com IPI — o mesmo gross-up do pricing
    /// (S75, S76, S77), item a item com o IPI da NCM dele, na moeda do preço.
    /// </summary>
    private static string Incluso(bool sim) => sim ? "incluso" : "não incluso";

    private void DetalhamentoDosImpostos(CalculoPricing.Resultado r)
    {
        var tabela = Tabelas().FirstOrDefault(t => Texto(t).StartsWith("Valor Total Líquido", StringComparison.Ordinal));
        if (tabela is null) return;
        if (FaturamentoDaBu.ImpostoLocal(_o.Bu) is not null || OutroPais)
        {
            // HCHL / HPU: sem o detalhamento (o preço é o líquido + IVA / IGV); outro país: nada se soma
            Achar("Detalhamento para adição dos Impostos")?.Remove();
            tabela.Remove();
            return;
        }
        var linhas = tabela.Elements<TableRow>().ToList();
        if (linhas.Count < 4) return;

        var industrializacao = Textos.Igual(_o.VendaPara, "Industrialização");
        // os preços podem estar com impostos: o detalhamento parte do líquido de cada item
        var noNivel = PricingDaProposta.ValorNoNivel(r, _nivel.Codigo) ?? r.ValorDaProposta;
        var paraLiquido = noNivel > 0m && r.ValorDaProposta > 0m ? r.ValorDaProposta / noNivel : 1m;
        // o mesmo gross-up do pricing (S75, S76, S77) sobre o total, com as alíquotas DO PRICING:
        // o IPI é o ponderado dos itens (zero em NB, exportação e Zona Franca) — item a item com o
        // IPI de cada NCM o "com IPI" saía diferente do pricing
        decimal liquido = Total * paraLiquido, comIcms = 0, comIpi = 0;
        var fatorPisCofins = 1m - r.AliqPis - r.AliqCofins;
        var ipi = r.AliqIpi;
        var comPisCofins = fatorPisCofins > 0 ? liquido / fatorPisCofins : liquido;
        if (r.AliqIcms is decimal icms)
        {
            comIcms = comPisCofins / (1m - icms - icms * (industrializacao ? 0m : ipi));
            comIpi = comIcms * (1m + ipi);
        }

        void Valor(TableRow linha, decimal v) => EscreverNaCelula(linha.Elements<TableCell>().Last(), new[] { Dinheiro(Math.Round(v, 2)) });
        Valor(linhas[0], liquido);
        if (r.SemImpostos || FaturamentoDaBu.ImpostoLocal(_o.Bu) is not null)
        {
            foreach (var l in linhas.Skip(1)) l.Remove();
            return;
        }
        Valor(linhas[1], comPisCofins);
        if (r.ProcurarFinanceiro || r.AliqIcms is null)
        {
            foreach (var l in linhas.Skip(2)) l.Remove();
            return;
        }
        Valor(linhas[2], comIcms);
        Valor(linhas[3], comIpi);
    }

    // ================= dados para faturamento =================

    /// <summary>
    /// "Dados para faturamento": no lugar do que o modelo traz (Howden South
    /// America, CNPJ e o Itaú), a empresa da BU da proposta e, embaixo, a
    /// conta bancária na moeda do preço — como estão em Dados e Importação →
    /// Dados bancários (<see cref="DadosBancariosRepository"/>). Sem conta nessa moeda, sai só a empresa.
    /// </summary>
    private void Faturamento()
    {
        if (_d.Faturamento is not { } fat) return;
        var empresa = fat.Dados;
        // a seção fica no fim: a razão social e o endereço também estão na capa (por isso o último)
        var titulo = Ultimo("Dados Para Faturamento");
        var razao = Ultimo("Howden South America Ventiladores e Compressores");
        var endereco = Ultimo("Av. Osvaldo Berto, 475");
        // o "Banco:" fica dentro de uma tabela: procura em todos os parágrafos
        var banco = _corpo.Descendants<Paragraph>().LastOrDefault(p => Texto(p).Contains("Banco: Banco Itaú", StringComparison.Ordinal));
        if (titulo is null || razao is null || endereco is null || banco is null) return;
        // razão social, endereço e CNPJ são parágrafos do corpo logo depois do título; o banco fica numa tabela
        OpenXmlElement fim = banco.Ancestors<Table>().FirstOrDefault() is { } tabelaDoBanco ? tabelaDoBanco : banco;
        if (titulo.Parent != _corpo || razao.Parent != _corpo || endereco.Parent != _corpo || fim.Parent != _corpo) return;
        var ordem = _corpo.ChildElements.ToList();
        int i(OpenXmlElement x) => ordem.IndexOf(x);
        if (!(i(titulo) < i(razao) && i(razao) < i(endereco) && i(endereco) < i(fim) && i(fim) - i(titulo) <= 8)) return;

        var moldeNegrito = (Paragraph)razao.CloneNode(true);
        var moldeNormal = (Paragraph)endereco.CloneNode(true);
        var moldeTitulo = (Paragraph)banco.CloneNode(true);
        ApagarEntre(razao, fim);
        razao.Remove();

        OpenXmlElement depois = titulo;
        void Inserir(OpenXmlElement e) { depois.InsertAfterSelf(e); depois = e; }

        if (empresa.Nome.Trim().Length > 0)
        {
            var nome = (Paragraph)moldeNegrito.CloneNode(true);
            Escrever(nome, empresa.Nome);
            Inserir(nome);
        }
        foreach (var l in empresa.Linhas.Where(l => !l.Vazia))
        {
            var p = (Paragraph)moldeNormal.CloneNode(true);
            Escrever(p, l.Rotulo.Length > 0 ? l.Rotulo + " " : "");
            var valor = (Run)p.Elements<Run>().Last().CloneNode(true);
            valor.RemoveAllChildren<Text>();
            valor.RunProperties ??= new RunProperties();
            if (l.Negrito && !valor.RunProperties.Elements<Bold>().Any()) { valor.RunProperties.Append(new Bold()); NaOrdem(valor.RunProperties); }
            valor.Append(new Text(l.Valor) { Space = SpaceProcessingModeValues.Preserve });
            if (l.Rotulo.Length == 0) foreach (var r in p.Elements<Run>().ToList()) r.Remove();   // sem rótulo: só o valor
            p.Append(valor);
            Inserir(p);
        }

        // embaixo, só a conta na moeda do preço: o título ("Dólar Americano - USD") em negrito e a tabela
        if (fat.Conta is { } contaDaMoeda && contaDaMoeda.Any(l => !l.Vazia))
        {
            var moeda = fat.Moeda;
            var conta = contaDaMoeda.Where(l => !l.Vazia).ToList();
            var moedaConta = (Paragraph)moldeTitulo.CloneNode(true);
            Escrever(moedaConta, FaturamentoDaBu.TituloDaConta(moeda));
            moedaConta.ParagraphProperties?.RemoveAllChildren<Indentation>();
            moedaConta.ParagraphProperties?.Append(new SpacingBetweenLines { Before = "240", After = "120" });
            Inserir(moedaConta);
            Inserir(TabelaDaConta(conta));
        }
    }

    /// <summary>A conta bancária: rótulo em negrito e valor, sem bordas, recuada como no modelo da equipe.</summary>
    private static Table TabelaDaConta(IEnumerable<LinhaDeFaturamento> linhas)
    {
        Run Texto(string t, bool negrito)
        {
            var props = new RunProperties(new RunFonts { Ascii = "Arial", HighAnsi = "Arial", ComplexScript = "Arial" });
            if (negrito) props.Append(new Bold());
            props.Append(new FontSize { Val = "16" }, new FontSizeComplexScript { Val = "16" });
            return new Run(props, new Text(t) { Space = SpaceProcessingModeValues.Preserve });
        }
        TableCell Celula(string t, bool negrito, int largura) => new(
            new TableCellProperties(new TableCellWidth { Width = largura.ToString(), Type = TableWidthUnitValues.Dxa }),
            new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "20" }), Texto(t, negrito)));

        var tabela = new Table(new TableProperties(
            new TableIndentation { Width = 720, Type = TableWidthUnitValues.Dxa },
            new TableBorders(
                new TopBorder { Val = BorderValues.None }, new BottomBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.None }, new RightBorder { Val = BorderValues.None },
                new InsideHorizontalBorder { Val = BorderValues.None }, new InsideVerticalBorder { Val = BorderValues.None }),
            new TableLayout { Type = TableLayoutValues.Fixed }));
        tabela.Append(new TableGrid(new GridColumn { Width = "2700" }, new GridColumn { Width = "4200" }));
        foreach (var l in linhas)
            tabela.Append(new TableRow(Celula(l.Rotulo, true, 2700), Celula(l.Valor, l.Negrito, 4200)));
        return tabela;
    }

    // ================= pagamento, entrega e validade =================

    /// <summary>O modelo traz as faixas de pagamento por valor; fica só a condição desta proposta.</summary>
    private void Pagamento()
    {
        var faixa = Achar("Propostas com valor < USD 75 mil");
        var escolhida = Achar("100% – 30 ddl da retirada");
        var fim = Ultimo("aprovação do data book");
        if (escolhida is null || fim is null) return;
        ApagarEntre(escolhida, fim);
        faixa?.Remove();
        Escrever(escolhida, PropostaWord.Ou(_o.PaymentTerms, "—"));
    }

    /// <summary>Local de entrega: fica a linha do incoterm escolhido; a instrução e as linhas de montagem do equipamento saem.</summary>
    private void Entrega()
    {
        Achar("APAGAR QUADRO INCOTERMS")?.Remove();

        var paragrafos = new[]
        {
            Achar("EX-Works / FCA"), Achar("FCA – Terminal de carga (Porto de Santos"),
            Achar("FCA – Terminal de carga (Aeroporto"), Achar("CIF – Porto de"),
            Achar("DAP – LOCAL"), Achar("DDP – LOCAL"),
        };
        if (_o.Incoterm == ListasDaProposta.Livre)
        {
            var primeira = paragrafos.FirstOrDefault(p => p is not null);
            foreach (var p in paragrafos) if (p is not null && p != primeira) p.Remove();
            if (primeira is not null) Escrever(primeira, PropostaWord.Ou(_o.IncotermLocal, "A COMBINAR"));
            Achar("Equipamento montado sobre base")?.Remove();
            Achar("Equipamento fornecido desmontado")?.Remove();
            return;
        }
        var escolhido = Array.FindIndex(ListasDaProposta.Incoterms, i => i.Codigo == _o.Incoterm);
        var fica = escolhido >= 0 ? escolhido : 0;
        var codigo = ListasDaProposta.Incoterms[fica].Codigo;
        for (var i = 0; i < paragrafos.Length; i++)
        {
            if (paragrafos[i] is null) continue;
            if (i == fica) Escrever(paragrafos[i]!, ListasDaProposta.LinhaDoIncoterm(codigo, _o.IncotermLocal));
            else paragrafos[i]!.Remove();
        }

        // montado / desmontado é de ventilador completo; peça não leva
        Achar("Equipamento montado sobre base")?.Remove();
        Achar("Equipamento fornecido desmontado")?.Remove();
    }

    private void Validade()
    {
        if (Achar("Esta proposta é válida por") is { } texto)
            Escrever(texto, PropostaWord.Ou(_o.ValidityText, "Esta proposta é válida por 30 dias."));
    }

    // ================= só a comercial =================

    /// <summary>Os títulos que saem com a parte técnica (também do índice).</summary>
    private static readonly string[] TitulosTecnicos =
    {
        "Proposta Técnica", "Dados Técnicos", "Curva de Performance", "Curva de Torque", "Características Construtivas",
        "Nível de Ruído", "Escopo de Fornecimento", "Limite de bateria", "Condições de processo", "Itens inclusos",
        "Documentação Incluída", "Descrição - Assessoria Técnica", "Itens Opcionais", "Exclusos do Fornecimento",
        "Desenho de linha de eixo", "Comentários e Desvios", "Tolerâncias De Performance", "Efeito Coanda",
        "Padrões e Códigos", "Mancais Monobloco", "Assessoria Técnica No Campo",
    };

    /// <summary>
    /// "Assessoria Técnica no Campo para Ventiladores" sai inteira: do título
    /// até a NOTA do fim (diárias, notas gerais, documentação, acréscimos).
    /// </summary>
    private void TirarAssessoria()
    {
        var titulo = Paragrafos().FirstOrDefault(p => Texto(p).Trim().StartsWith("Assessoria Técnica No Campo", StringComparison.OrdinalIgnoreCase));
        var nota = Achar("É facultado ao Cliente acionar formalmente a Howden");
        if (titulo is null || nota is null) return;
        // a quebra de página que abria a seção sai junto: senão sobra uma página em branco
        if (titulo.PreviousSibling<Paragraph>() is { } quebra && Texto(quebra).Trim().Length == 0
            && quebra.Descendants<Break>().Any(b => b.Type?.Value == BreakValues.Page))
            quebra.Remove();
        foreach (var elemento in Entre(titulo, nota)) elemento.Remove();
        nota.Remove();
        titulo.Remove();
    }

    /// <summary>
    /// Tira a parte técnica: Proposta Técnica e Escopo de Fornecimento
    /// inteiros, e de Informações Adicionais fica só "Anexos" com as
    /// Condições de Fornecimento (T&amp;C), que a nota comercial cita como anexa.
    /// </summary>
    private void SoAComercial()
    {
        var tecnica = Titulo1("Proposta Técnica");
        var adicionais = Titulo1("Informações Adicionais");
        if (tecnica is null || adicionais is null) return;

        var numeroTecnica = Anterior(tecnica);
        var numeroAdicionais = Anterior(adicionais);
        var de = numeroTecnica ?? tecnica;
        var ate = numeroAdicionais ?? adicionais;

        foreach (var e in Entre(de, ate)) e.Remove();
        de.Remove();
        if (numeroAdicionais is not null) Escrever(numeroAdicionais, "3");

        // em Anexos fica a T&C; o resto (LTSA, desenhos, FD, cronograma, documentos do cliente e as notas técnicas) sai
        if (Achar("LTSA - Long Term") is { } ltsa)
        {
            var depois = _corpo.ChildElements.SkipWhile(e => e != ltsa).ToList();
            foreach (var e in depois.Where(e => e is not SectionProperties)) e.Remove();
        }
    }

    // ================= HCHL / HPU: os Termos e Condições no fim =================

    /// <summary>Os títulos que saem do fim da proposta da HCHL / HPU (e do índice).</summary>
    private static readonly string[] TitulosDepoisDoFaturamento = { "Notas", "Sistema de Gestão", "Informações Adicionais", "Anexos" };

    /// <summary>
    /// HCHL / HPU: embaixo dos dados bancários entram os Termos y Condiciones
    /// Generales (<see cref="TermosDasBusDeFora"/>), e a proposta termina neles —
    /// Notas, Sistema de Gestão, Informações Adicionais e Anexos saem.
    /// </summary>
    private void TermosNoFim()
    {
        // "Notas" é título 2 (HSA-Título 2), logo depois da conta bancária
        var notas = Paragrafos().FirstOrDefault(p => Texto(p).Trim() == "Notas"
            && (p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "").Contains("TTULO", StringComparison.OrdinalIgnoreCase));
        if (!DeFora || _termos.Count == 0 || notas is null) return;
        OpenXmlElement de = Anterior(notas) is { } numero ? numero : notas;
        // a quebra de página que abria a seção: os termos seguem logo embaixo do banco
        if (de.PreviousSibling() is Paragraph quebra && Texto(quebra).Trim().Length == 0
            && quebra.Descendants<Break>().Any(b => b.Type?.Value == BreakValues.Page)
            && quebra.ParagraphProperties?.SectionProperties is null)
            quebra.Remove();

        OpenXmlElement? ancora = null;
        foreach (var e in _corpo.ChildElements.SkipWhile(e => e != de).ToList())
        {
            if (e is SectionProperties) { ancora ??= e; continue; }
            if (e is Paragraph p && p.ParagraphProperties?.SectionProperties is not null)
            {
                // a quebra de seção fica (vazia): ela guarda o tamanho e as margens das páginas de cima
                foreach (var filho in p.ChildElements.Where(c => c is not ParagraphProperties).ToList()) filho.Remove();
                ancora ??= p;
                continue;
            }
            e.Remove();
        }

        char anterior = ' ';
        foreach (var b in _termos)
        {
            var p = ParagrafoDosTermos(b, anterior);
            if (ancora is not null) ancora.InsertBeforeSelf(p); else _corpo.Append(p);
            anterior = b.Tipo;
        }
    }

    private static Paragraph ParagrafoDosTermos(TermosDasBusDeFora.Bloco b, char anterior)
    {
        const int Texto = 17, Secao = 18, Titulo = 22;   // meios-pontos
        Run R(string t, bool negrito = false, int tamanho = Texto, string? cor = null)
        {
            var rp = new RunProperties();
            if (negrito) { rp.Append(new Bold()); rp.Append(new BoldComplexScript()); }
            if (cor is not null) rp.Append(new Color { Val = cor });
            rp.Append(new FontSize { Val = tamanho.ToString(CultureInfo.InvariantCulture) });
            rp.Append(new FontSizeComplexScript { Val = tamanho.ToString(CultureInfo.InvariantCulture) });
            return new Run(rp, new Text(t) { Space = SpaceProcessingModeValues.Preserve });
        }
        Run Tab()
        {
            var r = R("");
            r.RemoveAllChildren<Text>();
            r.Append(new TabChar());
            return r;
        }
        Paragraph P(int antes, int depois, int esquerda, int pendurado, bool juntoAoProximo, JustificationValues alinhar, params Run[] runs)
        {
            var pp = new ParagraphProperties();
            if (juntoAoProximo) pp.Append(new KeepNext());
            pp.Append(new SpacingBetweenLines { Before = antes.ToString(CultureInfo.InvariantCulture), After = depois.ToString(CultureInfo.InvariantCulture) });
            if (esquerda > 0 || pendurado > 0)
                pp.Append(new Indentation { Left = esquerda.ToString(CultureInfo.InvariantCulture), Hanging = pendurado > 0 ? pendurado.ToString(CultureInfo.InvariantCulture) : null });
            pp.Append(new Justification { Val = alinhar });
            var p = new Paragraph(pp);
            foreach (var r in runs) p.Append(r);
            return p;
        }

        switch (b.Tipo)
        {
            case 'T':
                return P(anterior == ' ' ? 360 : 480, 160, 0, 0, true, JustificationValues.Left, R(b.Texto, true, Titulo, "1F4E79"));
            case 'S':
                return P(200, 80, 0, 0, true, JustificationValues.Left, R(b.Texto, true, Secao));
            case 'D':
                return P(0, 100, 567, 0, false, JustificationValues.Both, R(b.Termo + ": ", true), R(b.Texto));
            case 'I':
            {
                var m = System.Text.RegularExpressions.Regex.Match(b.Texto, @"^(\([ivx]+\))\s+(.*)$");
                return m.Success
                    ? P(0, 100, 1134, 567, false, JustificationValues.Both, R(m.Groups[1].Value), Tab(), R(m.Groups[2].Value))
                    : P(0, 100, 1134, 0, false, JustificationValues.Both, R(b.Texto));
            }
            case 'B':
                return P(0, 60, 1134, 283, false, JustificationValues.Both, R("–"), Tab(), R(b.Texto));
            default:
            {
                // cláusula "9.1 ...": o número em negrito, pendurado; parágrafo solto: recuado como o texto das cláusulas
                var m = System.Text.RegularExpressions.Regex.Match(b.Texto, @"^(\d{1,2}\.\d{1,2}\.?)\s+(.*)$");
                if (m.Success)
                    return P(0, 100, 567, 567, false, JustificationValues.Both, R(m.Groups[1].Value, true), Tab(), R(m.Groups[2].Value));
                return P(0, 100, anterior == 'T' ? 0 : 567, 0, false, JustificationValues.Both, R(b.Texto));
            }
        }
    }

    /// <summary>Um título de seção (estilo HSA-Título 1) pelo texto exato.</summary>
    private Paragraph? Titulo1(string texto) => Paragrafos().FirstOrDefault(p =>
        Texto(p).Trim() == texto &&
        (p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "").Contains("Ttulo1", StringComparison.OrdinalIgnoreCase));

    /// <summary>O número da seção, que o modelo põe num parágrafo próprio logo acima do título.</summary>
    private static Paragraph? Anterior(Paragraph titulo) =>
        titulo.PreviousSibling<Paragraph>() is { } p &&
        (p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "") == "sectionnumber" ? p : null;

    /// <summary>
    /// O índice: as entradas da parte técnica saem, e o documento pede ao Word
    /// para atualizar os campos ao abrir — as páginas mudaram.
    /// </summary>
    private void Indice()
    {
        var toc = _corpo.Elements<SdtBlock>().FirstOrDefault(s => Texto(s).Contains("Proposta Comercial", StringComparison.Ordinal)
                                                                   && Texto(s).Contains("Introdução", StringComparison.Ordinal));
        if (toc?.SdtContentBlock is { } conteudo)
        {
            var entradas = conteudo.Elements<Paragraph>().ToList();
            // a primeira abre o campo do índice e a última fecha: essas ficam
            foreach (var p in entradas.Skip(1).Take(Math.Max(0, entradas.Count - 2)))
            {
                var t = Texto(p).Trim();
                if (TitulosTecnicos.Any(tt => t.StartsWith(tt, StringComparison.Ordinal))) p.Remove();
                // HCHL / HPU: a proposta termina nos Termos e Condições
                else if (DeFora && _termos.Count > 0 && TitulosDepoisDoFaturamento.Any(tt => t.StartsWith(tt, StringComparison.Ordinal))) p.Remove();
            }
        }

        var settings = _doc.MainDocumentPart!.DocumentSettingsPart?.Settings;
        if (settings is not null)
        {
            settings.RemoveAllChildren<UpdateFieldsOnOpen>();
            // o esquema do Word fixa a ordem: o updateFields vem antes destes
            var depois = settings.ChildElements.FirstOrDefault(e => e is HeaderShapeDefaults or FootnoteDocumentWideProperties
                or EndnoteDocumentWideProperties or Compatibility or DocumentVariables or Rsids
                or DocumentFormat.OpenXml.Math.MathProperties or AttachedSchema or ThemeFontLanguages or ColorSchemeMapping
                or DoNotIncludeSubdocsInStats or DoNotAutoCompressPictures or ForceUpgrade or Captions or ReadModeInkLockDown
                or ShapeDefaults or DecimalSymbol or ListSeparator);
            var atualizar = new UpdateFieldsOnOpen { Val = true };
            if (depois is not null) settings.InsertBefore(atualizar, depois);
            else settings.Append(atualizar);
            settings.Save();
        }
    }

    // ================= inglês e espanhol =================

    /// <summary>
    /// A proposta em inglês ou espanhol: cada parágrafo do corpo, do
    /// cabeçalho e do rodapé — ou cada trecho dele, quando há campo no meio
    /// (a data da primeira nota, o número da página, a página do índice) —
    /// passa por <see cref="TraducaoDaProposta"/>. A formatação fica a do
    /// trecho; o que não tem tradução (nome do cliente, descrição digitada)
    /// fica como está.
    /// </summary>
    private void Traduzir()
    {
        if (_idioma == IdiomasDaProposta.Portugues) return;
        foreach (var parte in Partes())
        {
            foreach (var p in parte.Descendants<Paragraph>().Where(p => !p.Descendants<Paragraph>().Any()).ToList())
            {
                var comCampo = p.Descendants<FieldChar>().Any() || p.Descendants<SimpleField>().Any();
                foreach (var trecho in Trechos(p))
                {
                    var texto = TextoDos(trecho);
                    if (TraducaoDaProposta.Traduzir(texto, _idioma, comCampo) is not { } traducao) continue;
                    if (traducao == texto.Trim()) continue;
                    Reescrever(trecho, texto, traducao);
                }
            }
            parte.Save();
        }
        Lingua();
    }

    /// <summary>
    /// Os trechos de texto corrido de um parágrafo: runs seguidos, fora de
    /// campo. Campo (código ou resultado) e controle de conteúdo cortam o trecho.
    /// </summary>
    private static List<List<Run>> Trechos(Paragraph p)
    {
        var trechos = new List<List<Run>>();
        var atual = new List<Run>();
        // a pilha dos campos abertos: true = ainda no código (o "PAGEREF ..."), false = já no resultado.
        // O resultado é texto como outro qualquer: as entradas do índice moram no resultado do campo TOC.
        var campos = new Stack<bool>();
        void Fechar()
        {
            if (atual.Count > 0 && atual.Any(r => r.Elements<Text>().Any(t => t.Text.Length > 0))) trechos.Add(atual);
            atual = new List<Run>();
        }
        void Andar(OpenXmlElement pai)
        {
            foreach (var e in pai.ChildElements)
            {
                switch (e)
                {
                    case Run r:
                        if (r.GetFirstChild<FieldChar>() is { } fc)
                        {
                            Fechar();
                            var tipo = fc.FieldCharType?.Value;
                            if (tipo == FieldCharValues.Begin) campos.Push(true);
                            else if (tipo == FieldCharValues.Separate && campos.Count > 0) { campos.Pop(); campos.Push(false); }
                            else if (tipo == FieldCharValues.End && campos.Count > 0) campos.Pop();
                        }
                        else if (campos.Contains(true) || r.GetFirstChild<FieldCode>() is not null) Fechar();
                        else if (r.Elements<Text>().Any() || r.Elements<TabChar>().Any() || r.Elements<NoBreakHyphen>().Any()) atual.Add(r);
                        break;
                    case Hyperlink or InsertedRun or CustomXmlRun:
                        Andar(e);
                        break;
                    case SdtElement or SimpleField:
                        Fechar();
                        break;
                }
            }
        }
        Andar(p);
        Fechar();
        return trechos;
    }

    private static string TextoDos(List<Run> runs) => string.Concat(runs.SelectMany(r => r.ChildElements)
        .Select(e => e switch { Text t => t.Text, TabChar => "\t", NoBreakHyphen => "-", _ => "" }));

    /// <summary>
    /// Escreve a tradução no trecho. Com partes (‖) e o mesmo número de grupos
    /// de formatação, cada parte vai no seu grupo (o "Banco:" continua em
    /// negrito); senão tudo vai no primeiro. Os espaços das pontas ficam.
    /// </summary>
    private static void Reescrever(List<Run> trecho, string original, string traducao)
    {
        var partes = traducao.Split(TraducaoDaProposta.Parte);
        if (original.Length > 0 && char.IsWhiteSpace(original[0]) && !char.IsWhiteSpace(partes[0].FirstOrDefault(' ')))
            partes[0] = " " + partes[0];
        if (original.Length > 0 && original[^1] == ' ' && partes[^1].Length > 0 && partes[^1][^1] != ' ')
            partes[^1] += " ";

        var comTexto = trecho.Where(r => r.Elements<Text>().Any() || r.Elements<NoBreakHyphen>().Any()).ToList();
        var grupos = new List<List<Run>>();
        string? anterior = null;
        foreach (var r in comTexto)
        {
            var formato = Formato(r);
            if (grupos.Count == 0 || formato != anterior) grupos.Add(new List<Run>());
            grupos[^1].Add(r);
            anterior = formato;
        }
        if (grupos.Count == 0) return;
        if (partes.Length != grupos.Count)
        {
            partes = new[] { string.Concat(partes) };
            grupos = new List<List<Run>> { comTexto };
        }
        for (var i = 0; i < grupos.Count; i++)
        {
            var primeiro = true;
            foreach (var r in grupos[i])
            {
                foreach (var h in r.Elements<NoBreakHyphen>().ToList()) h.Remove();
                foreach (var h in r.Elements<SoftHyphen>().ToList()) h.Remove();
                var textos = r.Elements<Text>().ToList();
                if (primeiro && textos.Count == 0)
                {
                    r.Append(new Text());
                    textos = r.Elements<Text>().ToList();
                }
                foreach (var t in textos)
                {
                    if (primeiro)
                    {
                        t.Text = partes[i];
                        t.Space = SpaceProcessingModeValues.Preserve;
                        primeiro = false;
                    }
                    else t.Remove();
                }
            }
        }
    }

    /// <summary>O que se vê da formatação de um run (negrito, itálico, sublinhado, sobrescrito, cor, tamanho).</summary>
    private static string Formato(Run r)
    {
        var rp = r.RunProperties;
        if (rp is null) return "";
        return string.Join("|", new OpenXmlElement?[] { rp.Bold, rp.Italic, rp.Underline, rp.VerticalTextAlignment, rp.Color, rp.FontSize, rp.Highlight }
            .Select(e => e?.OuterXml ?? "-"));
    }

    /// <summary>A língua de revisão do Word (corretor ortográfico) passa a ser a da proposta.</summary>
    private void Lingua()
    {
        var lingua = _idioma == IdiomasDaProposta.Ingles ? "en-US" : "es-ES";
        var raizes = new List<OpenXmlPartRootElement>(Partes());
        if (_doc.MainDocumentPart!.StyleDefinitionsPart?.Styles is { } estilos) raizes.Add(estilos);
        foreach (var raiz in raizes)
        {
            foreach (var l in raiz.Descendants<Languages>())
                if (l.Val?.Value is { } v && v.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) l.Val = lingua;
            raiz.Save();
        }
    }

    // ================= ferramentas (as do axiaisvent) =================

    private List<Paragraph> Paragrafos() => _corpo.Elements<Paragraph>().ToList();
    private List<Table> Tabelas() => _corpo.Elements<Table>().ToList();

    private Paragraph? Achar(string trecho) => Paragrafos()
        .FirstOrDefault(p => Texto(p).Contains(trecho, StringComparison.Ordinal));

    private Paragraph? Ultimo(string trecho) => Paragrafos()
        .LastOrDefault(p => Texto(p).Contains(trecho, StringComparison.Ordinal));

    private static TableCell? Celula(Table tabela, string rotulo) => tabela.Descendants<TableCell>()
        .FirstOrDefault(c => Texto(c).Trim() == rotulo.Trim());

    private static string Texto(OpenXmlElement e)
    {
        var partes = new List<string>();
        foreach (var n in e.Descendants())
        {
            if (n is Text t) partes.Add(t.Text);
            else if (n is TabChar) partes.Add(" ");
        }
        return string.Join("", partes);
    }

    /// <summary>Escreve o texto no parágrafo com a formatação do primeiro run que tinha texto; "\n" vira quebra de linha.</summary>
    private static void Escrever(Paragraph p, string texto)
    {
        // o texto pode estar dentro de um hiperlink (o e-mail do cartão de contato): a formatação vem de lá
        var modelo = p.Elements<Run>().FirstOrDefault(r => r.Elements<Text>().Any())
                     ?? p.Descendants<Run>().FirstOrDefault(r => r.Elements<Text>().Any())
                     ?? p.Elements<Run>().FirstOrDefault();
        var formato = modelo?.RunProperties?.CloneNode(true) as RunProperties;

        p.RemoveAllChildren<Run>();
        p.RemoveAllChildren<Hyperlink>();
        foreach (var campo in p.ChildElements.OfType<SdtElement>().ToList()) campo.Remove();

        var run = new Run();
        if (formato is not null) run.Append(formato);
        var primeiro = true;
        foreach (var linha in texto.Split('\n'))
        {
            if (!primeiro) run.Append(new Break());
            run.Append(new Text(linha) { Space = SpaceProcessingModeValues.Preserve });
            primeiro = false;
        }
        p.Append(run);
    }

    /// <summary>Rótulo com a formatação do primeiro run (o negrito do "NCM:") e o valor sem negrito.</summary>
    private static void EscreverRotulo(Paragraph p, string rotulo, string valor)
    {
        var modelo = p.Elements<Run>().FirstOrDefault(r => r.Elements<Text>().Any()) ?? p.Elements<Run>().FirstOrDefault();
        var formato = modelo?.RunProperties?.CloneNode(true) as RunProperties;
        Escrever(p, rotulo);
        var run = new Run();
        if (formato?.CloneNode(true) is RunProperties semNegrito)
        {
            semNegrito.Bold = null;
            semNegrito.BoldComplexScript = null;
            run.Append(semNegrito);
        }
        run.Append(new Text(valor) { Space = SpaceProcessingModeValues.Preserve });
        p.Append(run);
    }

    /// <summary>Escreve as linhas numa célula reaproveitando os parágrafos que já estão lá.</summary>
    private static void EscreverNaCelula(TableCell celula, IReadOnlyList<string> linhas)
    {
        var paragrafos = celula.Elements<Paragraph>().ToList();
        if (paragrafos.Count == 0)
        {
            celula.Append(new Paragraph());
            paragrafos = celula.Elements<Paragraph>().ToList();
        }
        for (var i = 0; i < linhas.Count; i++)
        {
            if (i < paragrafos.Count) { Escrever(paragrafos[i], linhas[i]); continue; }
            var novo = (Paragraph)paragrafos[^1].CloneNode(true);
            Escrever(novo, linhas[i]);
            celula.Append(novo);
        }
        foreach (var sobra in paragrafos.Skip(Math.Max(linhas.Count, 1))) sobra.Remove();
    }

    /// <summary>Tudo que está entre dois elementos de primeiro nível, sem eles.</summary>
    private List<OpenXmlElement> Entre(OpenXmlElement de, OpenXmlElement ate)
    {
        var achou = false;
        var lista = new List<OpenXmlElement>();
        foreach (var filho in _corpo.ChildElements)
        {
            if (filho == de) { achou = true; continue; }
            if (filho == ate) break;
            if (achou) lista.Add(filho);
        }
        return lista;
    }

    /// <summary>Apaga tudo entre os dois, inclusive o de baixo.</summary>
    private void ApagarEntre(OpenXmlElement de, OpenXmlElement ate)
    {
        foreach (var elemento in Entre(de, ate)) elemento.Remove();
        ate.Remove();
    }
}
