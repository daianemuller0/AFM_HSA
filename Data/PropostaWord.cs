using DocumentFormat.OpenXml.Packaging;
using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>O que a proposta em Word precisa, já lido da base.</summary>
public sealed record DadosDaPropostaWord(
    CommercialOffer Oferta,
    IReadOnlyList<CommercialOfferItem> Itens,
    Salesperson? Vendedor,
    IReadOnlyDictionary<string, Equipment> Equipamentos,
    IReadOnlyList<InstalledBase> BaseDaUnidade,
    IReadOnlyList<BomItem> Bom,
    IReadOnlyList<SalesRecord> VendasDaUnidade);

/// <summary>
/// Gera a proposta comercial PREENCHENDO o modelo em Word da equipe
/// (<c>wwwroot/modelos/proposta-comercial.docx</c>) — o mesmo caminho do
/// axiaisvent (PropostaWord/PropostaWordPreenchimento): o arquivo que sai é o
/// de vocês, com capa, logos, estilos, cabeçalho e rodapé, e daqui só entram
/// os dados.
///
/// <para>
/// O modelo é escrito para ser preenchido À MÃO, e por isso traz alternativas
/// com a instrução de escolher ou apagar: os representantes, os quinze
/// contatos, as faixas de pagamento, as linhas de ICMS/IPI/ISS, os seis
/// incoterms. Gerar a proposta é escolher e APAGAR. Este modelo é técnico e
/// comercial; a proposta gerada é só a COMERCIAL — a parte técnica (Proposta
/// Técnica, Escopo de Fornecimento e as informações técnicas) sai inteira.
/// </para>
///
/// <para>
/// A capa e o cabeçalho de todas as páginas são <i>content controls</i>
/// ligados às propriedades do documento (Empresa, Categoria, Título,
/// Comentários, Palavras-chave, Endereço da Empresa e Data de Publicação):
/// preencher é mexer nas duas pontas — a propriedade e o texto que o Word mostra.
/// </para>
/// </summary>
public static class PropostaWord
{
    public const string CaminhoDoModelo = "wwwroot/modelos/proposta-comercial.docx";

    /// <summary>A referência da proposta (o "Título" do modelo): número e revisão, "HSA123.AFM.000045-00".</summary>
    public static string Referencia(CommercialOffer o) =>
        $"{Ou(o.OfferCode, "SEM-NUMERO")}-{Revisao2(o.Revisao)}";

    /// <summary>"0" → "00", "1" → "01"; o que não for número fica como está.</summary>
    public static string Revisao2(string? revisao)
    {
        var r = Ou(revisao, "0");
        return int.TryParse(r, out var n) ? n.ToString("D2") : r;
    }

    /// <summary>O nome do arquivo, sem caractere que o Windows recuse.</summary>
    public static string NomeDoArquivo(CommercialOffer o)
    {
        var nome = $"{Referencia(o)} - Proposta Comercial.docx";
        foreach (var c in Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }))
            nome = nome.Replace(c, '_');
        return nome;
    }

    /// <summary>
    /// Junta o que o Word precisa, com as plantas e os equipamentos da
    /// proposta: o vendedor vem da primeira planta cadastrada; a base
    /// instalada, a BOM e as vendas, de todas as plantas cadastradas e de todos
    /// os equipamentos cadastrados (inclusive os de outra planta). O
    /// equipamento não cadastrado entra como foi digitado.
    /// </summary>
    public static DadosDaPropostaWord Dados(CommercialOffer o, IReadOnlyList<CommercialOfferItem> itens,
        IReadOnlyList<ClientUnit> unidades, IReadOnlyList<Salesperson> vendedores, IReadOnlyList<Equipment> equipamentos,
        IReadOnlyList<InstalledBase> baseInstalada, IReadOnlyList<BomItem> bom, IReadOnlyList<SalesRecord> vendas)
    {
        PlantasDaProposta.Completar(o, itens, unidades, equipamentos, baseInstalada);
        var principal = unidades.FirstOrDefault(u => u.Id == PlantasDaProposta.UnidadePrincipal(o));
        var vendedor = principal is null ? null : vendedores.FirstOrDefault(v => v.Id == principal.VendedorId);
        var unidadesDaProposta = PlantasDaProposta.Unidades(o);

        var porChave = new Dictionary<string, Equipment>();
        foreach (var eq in o.EquipamentosDaProposta)
        {
            var cadastro = eq.Cadastrado ? equipamentos.FirstOrDefault(e => e.Id == eq.EquipmentId) : null;
            porChave[eq.Id] = new Equipment
            {
                Id = eq.Id,
                Tipo = PropostaWord.Ou(eq.Tipo, cadastro?.Tipo ?? ""),
                Modelo = PropostaWord.Ou(eq.Modelo, cadastro?.Modelo ?? ""),
                Nome = cadastro?.Nome ?? "",
                Aplicacao = PropostaWord.Ou(eq.Aplicacao, cadastro?.Aplicacao ?? ""),
                DocNum = cadastro?.DocNum ?? "",
            };
        }
        // item ligado a um equipamento cadastrado que não está na lista (proposta antiga)
        foreach (var id in itens.Select(i => i.EquipmentId).Where(x => x.Length > 0 && !porChave.ContainsKey(x)).Distinct())
            if (equipamentos.FirstOrDefault(e => e.Id == id) is { } e) porChave[id] = e;

        var cadastrados = o.EquipamentosDaProposta.Where(e => e.Cadastrado).Select(e => e.EquipmentId)
            .Concat(itens.Select(i => i.EquipmentId)).Where(x => x.Length > 0).ToHashSet();
        return new DadosDaPropostaWord(
            o, itens, vendedor, porChave,
            baseInstalada.Where(b => unidadesDaProposta.Contains(b.ClientUnitId) || cadastrados.Contains(b.EquipmentId)).ToList(),
            bom.Where(b => cadastrados.Contains(b.EquipmentId)).ToList(),
            vendas.Where(v => unidadesDaProposta.Contains((v.ClientUnitId ?? "").Trim())).ToList());
    }

    public static byte[] Gerar(DadosDaPropostaWord dados, string caminhoDoModelo)
    {
        var fluxo = new MemoryStream();
        using (var modelo = File.OpenRead(caminhoDoModelo)) modelo.CopyTo(fluxo);
        fluxo.Position = 0;

        using (var doc = WordprocessingDocument.Open(fluxo, true))
        {
            new PreenchimentoDaProposta(doc, dados).Preencher();
        }
        return fluxo.ToArray();
    }

    public static string Ou(string? valor, string padrao) =>
        string.IsNullOrWhiteSpace(valor) ? padrao : valor.Trim();
}
