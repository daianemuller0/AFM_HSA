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

    /// <summary>Uma nota livre para a aprovação (aparece como "Nota:" no e-mail).</summary>
    private string _notaAprovacao = "";

    /// <summary>Cliente com a planta, escopo por equipamento, itens com preço sem impostos, prazo, dólar e seguros.</summary>
    private ResumoDaAprovacao ResumoParaAprovacao(CalculoPricing.Resultado r)
    {
        var plantas = string.Join("; ", _o.Plantas.Select(PlantasDaProposta.Rotulo).Where(t => t.Length > 0));
        if (plantas.Length == 0) plantas = (_o.City ?? "").Trim();
        var cliente = string.Join(" – ", new[] { _o.ClientName, plantas }.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0));

        var reais = _items.Where(i => i != _itemDemais).ToList();
        // escopo: um item de lista por equipamento, com quantos itens são dele
        var escopo = reais.GroupBy(i => EquipamentoDoItem(i).Trim())
            .Select(g => (g.Key.Length > 0 ? g.Key : "sem equipamento informado")
                         + (g.Select(SerialDoItem).FirstOrDefault(sn => sn.Length > 0) is { } sn ? $" · SN {sn}" : "")
                         + $" — {Plural(g.Count(), "item", "itens")}")
            .ToList();

        // preços sem impostos (os da tela estão no nível de impostos escolhido)
        var noNivel = PricingDaProposta.ValorNoNivel(r, PricingDaProposta.NivelPedido(_o)) ?? r.ValorDaProposta;
        var paraLiquido = noNivel > 0m && r.ValorDaProposta > 0m ? r.ValorDaProposta / noNivel : 1m;
        var itens = _items.Select((it, i) => (
            (i + 1).ToString("D2"),
            it == _itemDemais ? it.Description : string.Join(" – ", new[] { it.PartNumber.Trim(), DescricaoSemPn(it) }.Where(x => x.Length > 0)),
            it == _itemDemais ? "" : EquipamentoDoItem(it),
            it.Quantity,
            it.UnitPrice > 0 ? Venda(Math.Round(it.UnitPrice * paraLiquido, 2)) : "—",
            Venda(Math.Round(it.TotalPrice * paraLiquido, 2)))).ToList();
        var total = Venda(Math.Round(_items.Sum(i => i.TotalPrice) * paraLiquido, 2));

        var prazos = reais.Select(PrazoDoItem).Where(d => d > 0).Distinct().OrderBy(d => d).ToList();
        var prazo = prazos.Count switch { 0 => "a confirmar", 1 => prazos[0].ToString(), _ => $"{prazos[0]} a {prazos[^1]}" };

        // dólar: o da proposta, se o preço é em USD; senão o da tabela de conversão
        decimal? dolar = Cambio.MoedaDoPreco(_o) == Moeda.Usd && Cambio.MoedaDoCusto(_o) == Moeda.Brl && Cambio.Normalizada(_o) > 0m ? Cambio.Normalizada(_o)
            : Conversao.Ler().UsdEmReais is var usd && usd > 0m ? usd : null;

        return new ResumoDaAprovacao(cliente.Length > 0 ? cliente : "—", escopo, itens, total, prazo, dolar, Seguros(), _notaAprovacao);
    }

    /// <summary>"Seguros:" do e-mail: o seguro garantia / carta fiança e as outras garantias da proposta.</summary>
    private string Seguros()
    {
        var partes = new List<string>();
        if (Textos.Igual(_o.FiancaSeguro, "Sim"))
        {
            var eventos = _o.EventosFianca.Where(e => e.Percentual > 0m)
                .Select(e => $"{(e.Percentual * 100m).ToString("0.##", PtBr)}% por {e.PrazoDias} dias").ToList();
            partes.Add($"{PropostaWord.Ou(_o.TipoGarantia, "Seguro garantia / carta fiança")}" + (eventos.Count > 0 ? $": {string.Join("; ", eventos)}" : ""));
        }
        var outras = _o.OutrasGarantias.Where(g => g.Percentual > 0m)
            .Select(g => $"{g.Tipo} {(g.Percentual * 100m).ToString("0.##", PtBr)}% por {g.DiasCobertura} dias").ToList();
        if (outras.Count > 0) partes.Add($"{PropostaWord.Ou(_o.TipoOutrasGarantias, "Outras garantias")}: {string.Join("; ", outras)}");
        return partes.Count > 0 ? string.Join(". ", partes) + "." : "Sem seguro garantia ou carta fiança.";
    }

    /// <param name="assinatura">"" quando o Outlook põe a assinatura de quem envia</param>
    private EmailDaProposta EmailDeAprovacao(string assinatura)
    {
        var r = PricingAtual(Custos);
        return EmailsDaProposta.Aprovacao(_o, r, ResumoParaAprovacao(r), AprovPara, AprovCc, assinatura);
    }

    private EmailDaProposta EmailAoCliente(string assinatura)
    {
        var cartao = ContatosHowden.Do(_o.Vendedor) is { } c ? ContatosHowden.Cartao(c)
            : (_o.Vendedor ?? "").Trim().Length > 0 ? new List<string> { _o.Vendedor!.Trim() } : new List<string>();
        return EmailsDaProposta.Proposta(_o, cartao, PropPara, PropCc, assinatura);
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
            _msg = await BaixarEml(EmailDeAprovacao, anexos)
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
            _msg = await BaixarEml(EmailAoCliente, anexos)
                ? "E-mail ao cliente aberto no Outlook, com a proposta em Word anexa: anexe a T&C e envie."
                : "E-mail ao cliente pronto, com a proposta em Word anexa: abra o arquivo baixado no Outlook, anexe a T&C e envie.";
        }
        catch (Exception ex) { _erro = "Não deu para montar o e-mail ao cliente: " + ex.Message; }
        finally { _gerandoEmail = false; }
    }

    /// <summary>Abre direto no Outlook (como a macro do pricing); sem ele, baixa o .eml. true = abriu no Outlook.</summary>
    /// <param name="montar">o e-mail com a assinatura dada ("" no Outlook, que põe a de quem envia)</param>
    private async Task<bool> BaixarEml(Func<string, EmailDaProposta> montar, IReadOnlyList<AnexoDoEmail> anexos)
    {
        if (await Task.Run(() => Outlook.Abrir(montar(""), anexos)) is null) return true;
        var email = montar(Remetente);
        var nome = NomeDeArquivoSimples(email.Assunto) + ".eml";
        await JS.InvokeVoidAsync("appDownload", nome, "message/rfc822", Convert.ToBase64String(EmailsDaProposta.Eml(email, anexos)));
        return false;
    }

    /// <summary>O assunto como nome de arquivo sem acentos nem travessão (o navegador recusa e baixa "download").</summary>
    private static string NomeDeArquivoSimples(string assunto)
    {
        var sem = new string(assunto.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => c < 128 ? c : '-').ToArray());
        sem = System.Text.RegularExpressions.Regex.Replace(PricingExcel.Limpo(sem.TrimEnd('.')), @"\s*-\s*", "-");
        return sem.Length > 120 ? sem[..120] : sem;
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
