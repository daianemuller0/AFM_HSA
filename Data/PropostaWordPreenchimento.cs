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
    private static readonly CultureInfo Br = new("pt-BR");

    public PreenchimentoDaProposta(WordprocessingDocument doc, DadosDaPropostaWord dados)
    {
        _doc = doc;
        _d = dados;
        _o = dados.Oferta;
        _corpo = doc.MainDocumentPart!.Document.Body!;

        // as alíquotas da proposta (PIS, COFINS, ICMS, IPI, ISS) vêm do pricing
        var comprados = dados.Itens.Where(i => !i.Importado && i.Nacionalizado && i.CustoReferencia is > 0)
            .Sum(i => i.CustoReferencia!.Value * i.Quantity);
        var importados = dados.Itens.Where(i => i.Importado && i.CustoImportacao is > 0)
            .Sum(i => i.CustoImportacao!.Value * i.Quantity);
        _pricing = PricingDaProposta.Da(_o, comprados, importados);
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
        Impostos();
        Pagamento();
        Entrega();
        Validade();
        SoAComercial();
        Indice();
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
        Rotulo(capa, "Preparada por:", _o.PreparedBy);
        // "Sua referência:" é um campo ligado (Palavras-chave), já escrito em CamposLigados

        // só a comercial: o subtítulo perde o "Técnica"
        if (capa.Descendants<Paragraph>().FirstOrDefault(p => Texto(p).Trim() == "Proposta Técnica Comercial") is { } sub)
            Escrever(sub, "Proposta Comercial");
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

        var linhas = LinhasDoVendedor();
        for (var i = 0; i < celulas.Count - 1; i++)
        {
            if (i == 0 && linhas.Count > 0) ComoOMolde(celulas[i], molde, linhas);
            else EscreverNaCelula(celulas[i], new[] { "" });
        }
    }

    private List<string> LinhasDoVendedor()
    {
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
        Alargar(tabela, coluna: 1, texto: _o.PreparedBy ?? "");
        EscreverNaCelula(celulas[3], new[] { revisao.TrimStart('0').Length == 0 ? "Emissão inicial" : $"Revisão {revisao} da proposta" });

        // as linhas de revisões futuras do modelo saem vazias
        foreach (var vazia in linhas.Skip(2)) vazia.Remove();
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
        if (celTotal.Count >= 2)
        {
            // os preços da proposta são sem impostos (a opção "VALOR LÍQUIDO" da lista do modelo)
            EscreverNaCelula(celTotal[0], new[] { "VALOR LÍQUIDO" });
            EscreverNaCelula(celTotal[^1], new[] { Dinheiro(Total) });
        }

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
        var peca = PrimeiroCom(bom?.DescricaoPt, bom?.DescricaoEn, DescricaoSemPn(item.Description, pn));
        var servico = item.Description.StartsWith("Serviços", StringComparison.OrdinalIgnoreCase);
        var tipo = Textos.Simples(eq?.Tipo).Contains("compress") ? "Compressor"
            : Textos.Igual(eq?.Tipo, "Outro") ? "Equipamento" : "Ventilador";

        var paragrafos = celula.Elements<Paragraph>().ToList();
        Paragraph? P(string ancora) => paragrafos.FirstOrDefault(p => Texto(p).Contains(ancora, StringComparison.Ordinal));
        void Tirar(Paragraph? p) { if (p is not null) { p.Remove(); paragrafos.Remove(p); } }

        // título (a lista "Partes e Peças de Ventilador Howden Modelo") e a peça
        var titulo = paragrafos.FirstOrDefault(p => Texto(p).Contains("PREENCHER!!", StringComparison.Ordinal));
        if (titulo is not null)
            Escrever(titulo, eq is not null && eq.Modelo.Trim().Length > 0
                ? (tipo == "Equipamento" ? $"Partes e Peças – Modelo {eq.Modelo.Trim()}" : $"Partes e Peças de {tipo} Howden Modelo {eq.Modelo.Trim()}")
                : peca);
        var pPeca = paragrafos.FirstOrDefault(p => Texto(p).Trim() == "XXXXXX");
        if (pPeca is not null)
        {
            if (eq is not null && eq.Modelo.Trim().Length > 0) Escrever(pPeca, peca);
            else Tirar(pPeca);
        }
        Tirar(P("(EX: Rotor, Eixo, Cone, Selo)"));

        // REFERÊNCIAS — do equipamento da proposta (TAG, aplicação e planta
        // digitados ou trazidos do cadastro); a base instalada completa o que faltar
        var daProposta = PlantasDaProposta.DoItem(_o, item);
        var planta = PlantasDaProposta.PlantaDe(_o, daProposta);
        var cadastroId = daProposta is { Cadastrado: true } ? daProposta.EquipmentId : daProposta is null ? item.EquipmentId : "";
        var instalados = cadastroId.Length == 0 ? new List<InstalledBase>()
            : _d.BaseDaUnidade.Where(b => b.EquipmentId == cadastroId
                                          && (planta is not { Cadastrada: true } || b.ClientUnitId == planta.UnitId)).ToList();
        var tags = daProposta is not null ? (daProposta.Tag ?? "").Trim()
            : string.Join(", ", instalados.Select(b => (b.Tag ?? "").Trim()).Where(t => t.Length > 0).Distinct());
        var modelo = (eq?.Modelo ?? "").Trim();
        var aplicacao = PrimeiroCom(eq?.Aplicacao, instalados.Select(b => b.Process).FirstOrDefault(x => (x ?? "").Trim().Length > 0));
        var ultima = UltimoFornecimento(item, pn);
        var original = string.Join(", ", instalados.Select(b => (b.ContractNo ?? "").Trim()).Where(t => t.Length > 0).Distinct());

        // com mais de uma planta, cada item diz a sua (a capa só tem a cidade):
        // a linha nasce da "Aplicação:" do modelo, com o mesmo marcador
        var comPlanta = false;
        if (planta is not null && _o.Plantas.Count > 1 && P("Aplicação:") is { } pAplicacao)
        {
            comPlanta = true;
            var pPlanta = (Paragraph)pAplicacao.CloneNode(true);
            pAplicacao.InsertBeforeSelf(pPlanta);
            Escrever(pPlanta, $"Planta: {PlantasDaProposta.Rotulo(planta)}");
            paragrafos.Insert(paragrafos.IndexOf(pAplicacao), pPlanta);
        }

        var linhasRef = 0;
        void Ref(string ancora, string texto)
        {
            var p = P(ancora);
            if (p is null) return;
            if (texto.Length == 0) { Tirar(p); return; }
            Escrever(p, texto);
            linhasRef++;
        }
        Ref("Modelo do ventilador", modelo.Length == 0 && tags.Length == 0 ? ""
            : $"Modelo do {tipo.ToLowerInvariant()}: {PropostaWord.Ou(modelo, "—")}" + (tags.Length > 0 ? $" / TAG: {tags}" : ""));
        Ref("Aplicação:", aplicacao.Length > 0 ? $"Aplicação: {aplicacao}" : "");
        Ref("Fornecimento / Original:", ultima.Length == 0 && original.Length == 0 ? ""
            : $"Último Fornecimento / Original: {PropostaWord.Ou(ultima, "—")}" + (original.Length > 0 ? $" / {original}" : ""));
        Ref("Desenho de Conjunto:", "");
        Ref("Part Number:", pn.Length > 0 ? $"Part Number: {pn}" : "");
        if (linhasRef == 0 && !comPlanta) Tirar(P("REFERÊNCIAS"));

        // ESCOPO DE FORNECIMENTO
        var material = (bom?.Material ?? "").Trim();
        if (servico)
        {
            // serviço: o título já diz o que é; o escopo de peça não se aplica
            Tirar(P("ESCOPO DE FORNECIMENTO"));
            Tirar(P("Xxxxxxxxxxxx"));
        }
        else if (P("Xxxxxxxxxxxx") is { } pEscopo)
            Escrever(pEscopo, material.Length > 0 ? $"{peca} – material: {material}" : peca);
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
        var dias = PricingDaProposta.PrazoEmDias(_o.PrazoEntrega);
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

        if (r.SemImpostos)
        {
            if (pis is not null) Escrever(pis, "Venda para exportação: não incidem PIS, COFINS, ICMS e IPI.");
            apagar.AddRange(new[] { cofins, icmsSp, ipiModelo, issItatiba, issSerra });
        }
        else
        {
            if (pis is not null) Escrever(pis, $"PIS: {Pct(r.AliqPis)} não incluso;");
            if (cofins is not null) Escrever(cofins, $"COFINS: {Pct(r.AliqCofins)} não incluso;");

            var uf = (_o.Estado ?? "").Trim();
            if (icmsSp is not null)
                Escrever(icmsSp, r.ProcurarFinanceiro ? "ICMS: a confirmar com o Financeiro (revenda ou cliente não-contribuinte);"
                    : r.AliqIcms is decimal icms ? $"ICMS: ({Pct(icms)} - {PropostaWord.Ou(uf, "ESTADO")}) não incluso"
                    : "ICMS: (INDICAR ESTADO) não incluso");

            // IPI: uma linha por NCM dos itens (a NCM diz a alíquota); sem NCM, a do pricing
            if (ipiModelo is not null)
            {
                var ncms = _d.Itens.Select(i => ListasDaProposta.NcmDo(i.Ncm)).Where(n => n is not null).Distinct().ToList();
                var linhas = r.AliqIpi == 0m && Textos.Igual(_o.ClienteCom, "Zona Franca de Manaus")
                    ? new List<string> { "IPI: não incide (Zona Franca de Manaus);" }
                    : ncms.Count > 0
                        ? ncms.Select(n => $"IPI: {Pct(n!.Ipi)} não incluso; NCM {n!.Texto}").ToList()
                        : new List<string> { $"IPI: {Pct(r.AliqIpi)} não incluso;" };
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
    private void DetalhamentoDosImpostos(CalculoPricing.Resultado r)
    {
        var tabela = Tabelas().FirstOrDefault(t => Texto(t).StartsWith("Valor Total Líquido", StringComparison.Ordinal));
        if (tabela is null) return;
        var linhas = tabela.Elements<TableRow>().ToList();
        if (linhas.Count < 4) return;

        var industrializacao = Textos.Igual(_o.VendaPara, "Industrialização");
        decimal liquido = Total, comPisCofins = 0, comIcms = 0, comIpi = 0;
        var fatorPisCofins = 1m - r.AliqPis - r.AliqCofins;
        foreach (var it in _d.Itens)
        {
            var ipi = ListasDaProposta.NcmDo(it.Ncm)?.Ipi ?? r.AliqIpi;
            if (Textos.Igual(_o.ClienteCom, "Zona Franca de Manaus")) ipi = 0m;
            var s75 = fatorPisCofins > 0 ? it.TotalPrice / fatorPisCofins : it.TotalPrice;
            comPisCofins += s75;
            if (r.AliqIcms is decimal icms)
            {
                var s76 = s75 / (1m - icms - icms * (industrializacao ? 0m : ipi));
                comIcms += s76;
                comIpi += s76 * (1m + ipi);
            }
        }

        void Valor(TableRow linha, decimal v) => EscreverNaCelula(linha.Elements<TableCell>().Last(), new[] { Dinheiro(Math.Round(v, 2)) });
        Valor(linhas[0], liquido);
        if (r.SemImpostos)
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
        "Padrões e Códigos", "Mancais Monobloco",
    };

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
        var modelo = p.Elements<Run>().FirstOrDefault(r => r.Elements<Text>().Any()) ?? p.Elements<Run>().FirstOrDefault();
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
