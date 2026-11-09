using AfmHsa.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AfmHsa.Components.Pages;

/// <summary>
/// Os e-mails da etapa Documentos — a aprovação do pricing e o envio da
/// proposta ao cliente —, como a aba "EMAILS AUTOMÁTICOS" da planilha de
/// pricing (ver <see cref="EmailsDaProposta"/>). Cada um sai como rascunho do
/// Outlook (.eml, já com o anexo), para copiar ou num mailto.
/// </summary>
public partial class NovaOferta
{
    [Inject] private IConfiguration Config { get; set; } = default!;
    [Inject] private OutlookLocal Outlook { get; set; } = default!;

    // o que foi digitado nos campos (null = o padrão)
    private string? _aprovParaDigitado, _aprovCcDigitado, _propParaDigitado, _propCcDigitado;
    private bool _gerandoEmail;
    private string? _emailAberto;

    private string AprovPara => _aprovParaDigitado ?? Config["Emails:AprovacaoPara"] ?? EmailsDaProposta.AprovadorPadrao;
    private string AprovCc => _aprovCcDigitado ?? Config["Emails:AprovacaoCc"] ?? EmailsDaProposta.CopiaDaAprovacaoPadrao;
    private string PropPara => _propParaDigitado ?? _o.ContatoEmail ?? "";

    /// <summary>Cópia do envio ao cliente: o vendedor da proposta (e quem o appsettings mandar).</summary>
    private string PropCc => _propCcDigitado ?? string.Join("; ", new[] { ContatosHowden.Do(_o.Vendedor)?.Email, Config["Emails:PropostaCc"] }
        .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase));

    private string Remetente => PropostaWord.Ou(_usuario, PropostaWord.Ou(_o.PreparedBy, "Equipe Howden"));

    /// <summary>Unidade, tipo de equipamento, prazo e itens, como a aprovação mostra.</summary>
    private ResumoDaAprovacao ResumoParaAprovacao(CalculoPricing.Resultado r)
    {
        var unidades = string.Join("; ", _o.Plantas.Select(PlantasDaProposta.Rotulo).Where(t => t.Length > 0));
        if (unidades.Length == 0) unidades = PropostaWord.Ou(_o.City, "—");

        // "Compressor VX 2025" (sem repetir o tipo quando o modelo já começa por ele)
        static string ComTipo(string tipo, string modelo) =>
            modelo.StartsWith(tipo, StringComparison.OrdinalIgnoreCase) ? modelo : $"{tipo} {modelo}".Trim();
        var tipos = _o.EquipamentosDaProposta
            .Select(e => ComTipo(PropostaWord.Ou(e.Tipo, PlantasDaProposta.Compressor), (e.Modelo ?? "").Trim())).Where(t => t.Length > 0)
            .Concat(_items.Where(i => i != _itemDemais).Select(ModeloDoItem).Where(m => m.Length > 0).Select(m => ComTipo(PlantasDaProposta.Compressor, m)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var prazos = _items.Where(i => i != _itemDemais).Select(PrazoDoItem).Where(d => d > 0).Distinct().OrderBy(d => d).ToList();
        var prazo = prazos.Count switch
        {
            0 => "a confirmar",
            1 => $"{prazos[0]} dias",
            _ => $"{prazos[0]} a {prazos[^1]} dias (por item)",
        };

        var itens = _items.Select((it, i) => (
            (i + 1).ToString("D2"), it.PartNumber, it == _itemDemais ? it.Description : DescricaoSemPn(it), it.Quantity,
            it.UnitPrice > 0 ? Venda(it.UnitPrice) : "—", Venda(it.TotalPrice),
            it == _itemDemais ? "" : PrazoDoItem(it) is > 0 and var d ? $"{d} dias" : "a confirmar")).ToList();

        return new ResumoDaAprovacao(unidades, tipos.Count > 0 ? string.Join("; ", tipos) : PlantasDaProposta.Compressor, prazo,
            r.CustoTotal, Cambio.Converte(_o) ? Math.Round(Cambio.ParaVenda(_o, r.ValorDaProposta), 2) : null, itens,
            PricingDaProposta.NivelEfetivo(r, _o.ImpostosNoPreco).Rotulo.ToLowerInvariant());
    }

    private EmailDaProposta EmailDeAprovacao()
    {
        var r = PricingAtual(Custos);
        return EmailsDaProposta.Aprovacao(_o, r, ResumoParaAprovacao(r), AprovPara, AprovCc, Remetente);
    }

    private EmailDaProposta EmailAoCliente()
    {
        var cartao = ContatosHowden.Do(_o.Vendedor) is { } c ? ContatosHowden.Cartao(c)
            : (_o.Vendedor ?? "").Trim().Length > 0 ? new List<string> { _o.Vendedor!.Trim() } : new List<string>();
        return EmailsDaProposta.Proposta(_o, cartao, PropPara, PropCc, Remetente);
    }

    /// <summary>A aprovação como rascunho do Outlook, com o pricing (P_…xlsm) anexo.</summary>
    private async Task EmlDeAprovacao()
    {
        if (_gerandoEmail) return;
        _gerandoEmail = true; _erro = null;
        try
        {
            AtualizarPrecos();
            var anexos = new List<AnexoDoEmail>();
            if (PricingEmBytes() is { } pricing) anexos.Add(new(PricingExcel.NomeDoArquivo(_o), PricingExcel.TipoMime, pricing));
            var email = EmailDeAprovacao();
            _msg = await BaixarEml(email, anexos)
                ? $"E-mail de aprovação aberto no Outlook{(anexos.Count > 0 ? ", com o pricing anexo" : "")}: confira e envie."
                : $"E-mail de aprovação pronto: abra o arquivo baixado no Outlook, confira e envie{(anexos.Count > 0 ? " (o pricing vai anexo)" : "")}.";
        }
        catch (Exception ex) { _erro = "Não deu para montar o e-mail de aprovação: " + ex.Message; }
        finally { _gerandoEmail = false; }
    }

    /// <summary>O envio ao cliente como rascunho do Outlook, com a proposta em Word anexa (salva antes).</summary>
    private async Task EmlAoCliente()
    {
        if (_gerandoEmail) return;
        if (_items.Count == 0) { _erro = "Inclua ao menos um item: a proposta comercial é a lista deles."; return; }
        if (!Salvar(explicito: false)) return;
        _gerandoEmail = true; _erro = null;
        try
        {
            var anexos = new List<AnexoDoEmail>();
            if (WordEmBytes() is { } word)
                anexos.Add(new(PropostaWord.NomeDoArquivo(_o), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", word));
            _msg = await BaixarEml(EmailAoCliente(), anexos)
                ? "E-mail ao cliente aberto no Outlook, com a proposta em Word anexa: anexe a T&C e envie."
                : "E-mail ao cliente pronto, com a proposta em Word anexa: abra o arquivo baixado no Outlook, anexe a T&C e envie.";
        }
        catch (Exception ex) { _erro = "Não deu para montar o e-mail ao cliente: " + ex.Message; }
        finally { _gerandoEmail = false; }
    }

    /// <summary>Abre direto no Outlook (como a macro do pricing); sem ele, baixa o .eml. true = abriu no Outlook.</summary>
    private async Task<bool> BaixarEml(EmailDaProposta email, IReadOnlyList<AnexoDoEmail> anexos)
    {
        if (await Task.Run(() => Outlook.Abrir(email, anexos)) is null) return true;
        var nome = PricingExcel.Limpo(email.Assunto.TrimEnd('.')) + ".eml";
        await JS.InvokeVoidAsync("appDownload", nome, "message/rfc822", Convert.ToBase64String(EmailsDaProposta.Eml(email, anexos)));
        return false;
    }

    private async Task Copiar(EmailDaProposta email)
    {
        var ok = await JS.InvokeAsync<bool>("appCopiarHtml", email.Html, email.Texto);
        _msg = ok ? "E-mail copiado: cole no corpo da mensagem (a formatação vai junto)." : null;
        if (!ok) _erro = "O navegador não deixou copiar; use o arquivo do Outlook.";
    }

    private byte[]? WordEmBytes()
    {
        var modelo = Path.Combine(Ambiente.ContentRootPath, PropostaWord.CaminhoDoModelo);
        if (!File.Exists(modelo)) return null;
        var dados = PropostaWord.Dados(_o, _items, _units, Vendedores.All(), _equipamentos, _base, _bom, _vendas);
        return PropostaWord.Gerar(dados, modelo);
    }

    private byte[]? PricingEmBytes()
    {
        var modelo = Path.Combine(Ambiente.ContentRootPath, PricingExcel.CaminhoDoModelo);
        if (!File.Exists(modelo)) return null;
        var c = Custos;
        var entrada = PricingDaProposta.EntradaDe(_o, c.Nacionais, c.Importados, PricingDaProposta.IpiDosItens(c.ComCusto.Select(x => (x.It, x.Unit))));
        return PricingExcel.Gerar(_o, entrada, PricingAtual(c), Conversao.Ler(), modelo);
    }
}
