using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace AfmHsa.Data;

/// <summary>
/// Edita a planilha .xlsm <b>direto no pacote</b>, sem regravá-la — o mesmo
/// caminho do axiaisvent (HowdenAxiais.Poc.Data.PacoteXlsm). É o que mantém a
/// planilha da equipe inteira: as macros, os botões, as caixas de seleção, as
/// listas e as outras abas. Reabrir e salvar por uma biblioteca de planilhas
/// descartava o que ela não conhece.
///
/// Só mexe no que é pedido: o valor ou a fórmula de algumas células.
/// </summary>
internal sealed class PacoteXlsm : IDisposable
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace NsR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace NsRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace NsTipos = "http://schemas.openxmlformats.org/package/2006/content-types";

    private readonly MemoryStream _memoria = new();
    private readonly ZipArchive _zip;
    private readonly Dictionary<string, XDocument> _documentos = new();

    public PacoteXlsm(string caminhoDoModelo)
    {
        var bytes = File.ReadAllBytes(caminhoDoModelo);
        _memoria.Write(bytes, 0, bytes.Length);
        _zip = new ZipArchive(_memoria, ZipArchiveMode.Update, leaveOpen: true);
    }

    private XDocument Doc(string caminho)
    {
        if (_documentos.TryGetValue(caminho, out var d)) return d;
        var entrada = _zip.GetEntry(caminho) ?? throw new InvalidOperationException($"Falta {caminho} na planilha.");
        using var s = entrada.Open();
        d = XDocument.Load(s, LoadOptions.PreserveWhitespace);
        _documentos[caminho] = d;
        return d;
    }

    private string CaminhoDaAba(string nome)
    {
        var rid = Doc("xl/workbook.xml").Root!.Element(Ns + "sheets")!.Elements(Ns + "sheet")
            .First(x => (string?)x.Attribute("name") == nome).Attribute(NsR + "id")!.Value;
        var alvo = Doc("xl/_rels/workbook.xml.rels").Root!.Elements(NsRel + "Relationship")
            .First(x => (string?)x.Attribute("Id") == rid).Attribute("Target")!.Value;
        return alvo.StartsWith('/') ? alvo.TrimStart('/') : "xl/" + alvo;
    }

    public Aba AbaDe(string nome) => new(Doc(CaminhoDaAba(nome)));

    public sealed class Aba
    {
        private readonly XDocument _doc;
        internal Aba(XDocument doc) => _doc = doc;

        public void Numero(string celula, double valor)
        {
            var c = Limpa(celula);
            c.Add(new XElement(Ns + "v", valor.ToString("R", CultureInfo.InvariantCulture)));
        }

        public void Numero(string celula, decimal valor) => Numero(celula, (double)valor);

        public void Numero(string celula, int valor) => Numero(celula, (double)valor);

        /// <summary>Texto na célula; vazio deixa a célula como o modelo tem.</summary>
        public void Texto(string celula, string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return;
            var c = Limpa(celula);
            c.SetAttributeValue("t", "inlineStr");
            c.Add(new XElement(Ns + "is",
                new XElement(Ns + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), valor.Trim())));
        }

        /// <summary>A fórmula da célula, como está no modelo (null = não tem).</summary>
        public string? FormulaDe(string celula) => Celula(celula).Element(Ns + "f")?.Value;

        /// <summary>Troca a fórmula, mantendo o estilo; o valor é recalculado ao abrir.</summary>
        public void Formula(string celula, string formula)
        {
            var c = Limpa(celula);
            c.Add(new XElement(Ns + "f", formula));
        }

        private XElement Limpa(string celula)
        {
            var c = Celula(celula);
            c.Elements(Ns + "f").Remove();
            c.Elements(Ns + "v").Remove();
            c.Elements(Ns + "is").Remove();
            c.Attribute("t")?.Remove();
            return c;
        }

        private XElement Celula(string referencia)
        {
            var (coluna, linha) = Dividir(referencia);
            var dados = _doc.Root!.Element(Ns + "sheetData")!;
            var row = dados.Elements(Ns + "row").FirstOrDefault(r => (int)r.Attribute("r")! == linha);
            if (row is null)
            {
                row = new XElement(Ns + "row", new XAttribute("r", linha));
                var depois = dados.Elements(Ns + "row").FirstOrDefault(r => (int)r.Attribute("r")! > linha);
                if (depois is null) dados.Add(row); else depois.AddBeforeSelf(row);
            }
            var c = row.Elements(Ns + "c").FirstOrDefault(x => (string)x.Attribute("r")! == referencia);
            if (c is null)
            {
                c = new XElement(Ns + "c", new XAttribute("r", referencia));
                var depois = row.Elements(Ns + "c")
                    .FirstOrDefault(x => Coluna(Dividir((string)x.Attribute("r")!).Coluna) > Coluna(coluna));
                if (depois is null) row.Add(c); else depois.AddBeforeSelf(c);
            }
            return c;
        }

        private static (string Coluna, int Linha) Dividir(string referencia)
        {
            var i = 0;
            while (i < referencia.Length && char.IsLetter(referencia[i])) i++;
            return (referencia[..i], int.Parse(referencia[i..], CultureInfo.InvariantCulture));
        }

        private static int Coluna(string letras) => letras.Aggregate(0, (n, ch) => n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1));
    }

    /// <summary>Grava tudo e devolve o arquivo pronto (a planilha recalcula ao abrir).</summary>
    public byte[] Concluir()
    {
        var calc = Doc("xl/workbook.xml").Root!.Element(Ns + "calcPr");
        calc?.SetAttributeValue("fullCalcOnLoad", "1");

        // a ordem de cálculo guardada não vale mais (células deixaram de ter fórmula)
        if (_zip.GetEntry("xl/calcChain.xml") is { } cadeia)
        {
            cadeia.Delete();
            Doc("xl/_rels/workbook.xml.rels").Root!.Elements(NsRel + "Relationship")
                .Where(r => ((string?)r.Attribute("Type") ?? "").EndsWith("/calcChain")).ToList()
                .ForEach(r => r.Remove());
            Doc("[Content_Types].xml").Root!.Elements(NsTipos + "Override")
                .Where(o => (string?)o.Attribute("PartName") == "/xl/calcChain.xml").ToList()
                .ForEach(o => o.Remove());
        }

        foreach (var (caminho, doc) in _documentos)
        {
            _zip.GetEntry(caminho)?.Delete();
            using var s = _zip.CreateEntry(caminho).Open();
            doc.Save(s);
        }
        _zip.Dispose();
        return _memoria.ToArray();
    }

    public void Dispose() => _memoria.Dispose();
}
