using System.Globalization;
using AfmHsa.Data;
using AfmHsa.Models;

namespace AfmHsa.Components.Pages;

/// <summary>
/// Consolidar: a proposta foi vendida e passa para o próximo setor. Um
/// assistente pede, nesta ordem, o número do projeto (vai para o PV), o número
/// do pedido (vira a "Sua referência"), o que fazer com a margem de
/// negociação, o valor do pedido (conferido contra a proposta), a data de
/// entrega e o pagamento. Nada muda até o resumo; "Consolidar" grava.
///
/// O preço de venda fica travado no valor do pedido (pricing "a partir do
/// preço", no nível de impostos da proposta). Assim:
/// <list type="bullet">
/// <item>zerar a margem de negociação sem levar ao TA: o preço não muda e a
///   margem total sobe;</item>
/// <item>levar a margem de negociação ao TA: o valor dela vira custo nos itens
///   (TA), na proporção do custo de cada um — o preço de cada item não muda e
///   a margem total fica a mesma;</item>
/// <item>levar a margem de negociação ao frete: o valor dela entra no frete da
///   proposta (dividido pelos itens pelo valor de cada um) — o total não muda e
///   a margem total fica a mesma;</item>
/// <item>preço novo por item (o pedido não bateu): o TA de cada item é
///   ajustado para o preço dele cair exatamente no valor do pedido.</item>
/// </list>
/// </summary>
public partial class NovaOferta
{
    private bool _consolidando;
    private int _passoC = 1;
    private const int PassosC = 6;
    private static readonly string[] NomesDosPassosC = { "Projeto e pedido", "Margem de negociação", "Valor do pedido", "Entrega", "Pagamento", "Resumo" };

    private string _cProjeto = "", _cPedido = "";
    private bool? _cZerarMn;
    /// <summary>Para onde vai a margem de negociação zerada: "ta" (TA dos itens), "frete" (frete da proposta), "nao" (a margem total sobe); null = sem resposta.</summary>
    private string? _cMnDestino;
    private decimal? _cValor;
    private string _cMoeda = "";
    private bool _cConferido;          // o valor foi conferido (bate, ou os preços por item fecham)
    private bool _cNaoBate;            // conferiu e não bateu: revisar o valor ou ajustar os itens
    private bool _cAjustarItens;
    private readonly Dictionary<int, decimal?> _cPrecos = new();   // índice do item → preço unitário novo (moeda do preço)
    private DateOnly? _cDataTodos;
    private bool _cDatasPorItem;
    private readonly Dictionary<int, DateOnly?> _cDatas = new();
    private bool? _cPagamentoIgual;
    private string _cPagamento = "";
    private string? _cErro;
    private string _cRetrato = "";     // a proposta como estava ao abrir o assistente
    private ResultadoDaConsolidacao? _cResultado;
    private bool _cConfirmarDesfazer;

    /// <param name="CustoDaMn">o valor da margem de negociação levado ao custo (TA dos itens ou frete), na moeda do custo</param>
    /// <param name="MnDestino">"ta", "frete" ou "nao"</param>
    /// <param name="TaDoAjuste">o TA acrescentado para os itens caírem nos preços do pedido (moeda do custo)</param>
    private sealed record ResultadoDaConsolidacao(decimal TotalAntes, decimal TotalDepois, decimal MargemAntes, decimal MargemDepois,
        decimal MnAntes, decimal MnValor, decimal CustoDaMn, string MnDestino, decimal TaDoAjuste, List<(int Indice, decimal Antes, decimal Depois, decimal? Alvo)> Itens);

    /// <summary>Os itens que o pedido paga (sem a linha de demais custos e sem quantidade zero).</summary>
    private List<(int Indice, CommercialOfferItem It)> ItensDoPedido() =>
        _items.Select((it, i) => (i, it)).Where(x => x.it != _itemDemais && x.it.Quantity > 0).ToList();

    private Moeda MoedaDaProposta => Cambio.MoedaDoPreco(_o);

    private string DinheiroC(decimal v) => Cambio.Dinheiro(_o, v);

    private string NivelDoPedidoRotulo =>
        PricingDaProposta.NivelDe(PricingDaProposta.NivelPedido(_o)).Rotulo.ToLowerInvariant();

    /// <summary>Diferença aceita entre o pedido e a proposta: o arredondamento dos centavos de cada linha.</summary>
    private decimal ToleranciaC()
    {
        var linhas = Math.Max(1, _items.Count(i => i.UnitPrice != 0m));
        return MoedaDaProposta == Moeda.Clp ? linhas : Math.Max(0.01m, 0.005m * linhas);
    }

    /// <summary>A margem de negociação em dinheiro (moeda do custo): % × venda líquida.</summary>
    private decimal ValorDaMn(CalculoPricing.Resultado r) => _o.MargemNegociacao * r.VendaLiquida;

    private void AbrirConsolidacao()
    {
        if (_o.Id.Length == 0) { _erro = "Salve a proposta antes de consolidar."; return; }
        if (Consolidacao.Consolidada(_o)) { _msg = $"Esta proposta já foi consolidada em {ConsolidadaEmTexto}."; return; }
        AtualizarPrecos();
        _cRetrato = Consolidacao.Guardar(_o, _items);
        _cProjeto = _o.Pv ?? "";
        _cPedido = _o.NumeroPedido.Length > 0 ? _o.NumeroPedido : "";
        _cZerarMn = _o.MargemNegociacao > 0m ? null : false;
        _cMnDestino = null;
        _cValor = null;
        _cMoeda = MoedaDaProposta.Codigo();
        _cConferido = _cNaoBate = _cAjustarItens = false;
        _cPrecos.Clear();
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var itens = ItensDoPedido();
        _cDatas.Clear();
        foreach (var (i, it) in itens) _cDatas[i] = Consolidacao.DataPeloPrazo(_o, it, hoje);
        var sugestoes = _cDatas.Values.Where(d => d is not null).Select(d => d!.Value).Distinct().ToList();
        _cDataTodos = sugestoes.Count > 0 ? sugestoes.Max() : null;
        _cDatasPorItem = sugestoes.Count > 1;
        _cPagamentoIgual = null;
        _cPagamento = _o.PaymentTerms ?? "";
        _cErro = null;
        _cResultado = null;
        _passoC = 1;
        _consolidando = true;
    }

    /// <summary>Fecha sem consolidar: se o resumo já tinha aplicado as mudanças, a proposta volta como estava.</summary>
    private void CancelarConsolidacao()
    {
        if (_cResultado is not null) VoltarAoRetrato();
        _consolidando = false;
        _cErro = null;
    }

    private void VoltarAoRetrato()
    {
        var r = Consolidacao.Ler(_cRetrato);
        if (r is null) return;
        var indiceDemais = _itemDemais is null ? -1 : _items.IndexOf(_itemDemais);
        _o = r.Oferta;
        _items = r.Itens;
        _itemDemais = indiceDemais >= 0 && indiceDemais < _items.Count ? _items[indiceDemais] : null;
        _cResultado = null;
        AtualizarPrecos();
    }

    private void PassoAnteriorC()
    {
        _cErro = null;
        if (_passoC == PassosC) VoltarAoRetrato();
        if (_passoC > 1) _passoC--;
        if (_passoC == 2 && _o.MargemNegociacao <= 0m) _passoC = 1;
    }

    private void ProximoPassoC()
    {
        _cErro = null;
        switch (_passoC)
        {
            case 1:
                if (_cProjeto.Trim().Length == 0) { _cErro = "Informe o número do projeto."; return; }
                if (_cPedido.Trim().Length == 0) { _cErro = "Informe o número do pedido."; return; }
                _passoC = _o.MargemNegociacao > 0m ? 2 : 3;
                if (_passoC == 3) _cZerarMn = false;
                return;
            case 2:
                if (_cZerarMn is null) { _cErro = "Responda: zerar a margem de negociação?"; return; }
                if (_cZerarMn == true && _cMnDestino is null) { _cErro = "Responda: adicionar a margem de negociação no TA, no frete, ou não?"; return; }
                _passoC = 3;
                return;
            case 3:
                if (!_cConferido) { ConferirValor(); if (!_cConferido) return; }
                if (_cAjustarItens && SomaDosPrecosNovos() is var soma && !Bate(soma)) { _cErro = $"Os preços por item somam {DinheiroC(soma)}; o pedido é {DinheiroC(_cValor ?? 0m)}."; return; }
                _passoC = 4;
                return;
            case 4:
                if (!_cDatasPorItem && _cDataTodos is null) { _cErro = "Confirme a data de entrega."; return; }
                if (_cDatasPorItem && ItensDoPedido().Any(x => _cDatas.GetValueOrDefault(x.Indice) is null))
                { _cErro = "Confirme a data de entrega de todos os itens."; return; }
                _passoC = 5;
                return;
            case 5:
                if (_cPagamentoIgual is null) { _cErro = "Confirme o pagamento: está igual ao do pedido?"; return; }
                if (_cPagamentoIgual == false && _cPagamento.Trim().Length == 0) { _cErro = "Escreva o pagamento do pedido."; return; }
                AplicarConsolidacao();
                _passoC = 6;
                return;
        }
    }

    private string TextoDoDestinoDaMn(ResultadoDaConsolidacao r) =>
        r.MnAntes <= _o.MargemNegociacao ? ""
        : r.MnDestino == "ta" && r.CustoDaMn > 0.005m ? $" · {Cambio.DinheiroDoCusto(_o, r.CustoDaMn)} levados ao TA dos itens"
        : r.MnDestino == "frete" && r.CustoDaMn > 0.005m ? $" · {Cambio.DinheiroDoCusto(_o, r.CustoDaMn)} levados ao frete (agora {Cambio.DinheiroDoCusto(_o, _o.FreteCusto)})"
        : " · a margem total sobe";

    private bool Bate(decimal valor) => _cValor is decimal v && Math.Abs(v - valor) <= ToleranciaC();

    /// <summary>Confere o valor e a moeda do pedido contra a proposta.</summary>
    private void ConferirValor()
    {
        _cErro = null;
        _cConferido = false;
        if (_cValor is not decimal v || v <= 0m) { _cErro = "Informe o preço total do pedido."; return; }
        if (Moedas.Ler(_cMoeda) != MoedaDaProposta)
        {
            _cErro = $"A moeda do pedido ({Moedas.Ler(_cMoeda).Codigo()}) não é a da proposta ({MoedaDaProposta.Codigo()}). " +
                     "Confira o pedido: o valor tem de estar na moeda da proposta.";
            _cNaoBate = false;
            return;
        }
        if (Bate(Total))
        {
            _cNaoBate = false;
            _cAjustarItens = false;
            _cConferido = true;
            return;
        }
        _cNaoBate = true;
    }

    private void MudarValorDoPedido(Microsoft.AspNetCore.Components.ChangeEventArgs e)
    {
        _cValor = Numero(e) is decimal v && v > 0m ? v : null;
        _cConferido = _cNaoBate = _cAjustarItens = false;
        _cErro = null;
    }

    private void MudarMoedaDoPedido(Microsoft.AspNetCore.Components.ChangeEventArgs e)
    {
        _cMoeda = e.Value?.ToString() ?? "";
        _cConferido = _cNaoBate = _cAjustarItens = false;
        _cErro = null;
    }

    private void RevisarValor()
    {
        _cNaoBate = _cAjustarItens = false;
        _cValor = null;
        _cErro = null;
    }

    /// <summary>"Colocar o novo preço meta dos itens": começa com os preços de hoje, e o pedido manda no total.</summary>
    private void AjustarItens()
    {
        _cAjustarItens = true;
        _cConferido = true;
        _cPrecos.Clear();
        foreach (var (i, it) in ItensDoPedido()) _cPrecos[i] = it.UnitPrice;
    }

    private decimal SomaDosPrecosNovos() =>
        ItensDoPedido().Sum(x => (_cPrecos.GetValueOrDefault(x.Indice) ?? 0m) * x.It.Quantity)
        + (_itemDemais is not null && _items.Contains(_itemDemais) ? _itemDemais.TotalPrice : 0m);

    private void MudarPrecoNovo(int indice, Microsoft.AspNetCore.Components.ChangeEventArgs e)
    {
        _cPrecos[indice] = Numero(e) is decimal v && v > 0m ? Math.Round(v, 2) : null;
        _cErro = null;
    }

    /// <summary>
    /// Aplica tudo (o resumo mostra o resultado; cancelar ou voltar desfaz
    /// pelo retrato). O preço trava no valor do pedido e os TAs são ajustados
    /// para cada item ficar no preço certo.
    /// </summary>
    private void AplicarConsolidacao()
    {
        AtualizarPrecos();
        var cAntes = Custos;
        var rAntes = PricingAtual(cAntes);
        var totalAntes = Total;
        var margemAntes = rAntes.MargemTotal;
        var mnAntes = _o.MargemNegociacao;
        var mnValor = ValorDaMn(rAntes);
        var precosAntes = _items.Select(i => i.UnitPrice).ToList();
        var taAntes = _items.Sum(i => i.CustoTa * i.Quantity);

        _o.Pv = _cProjeto.Trim();
        _o.NumeroPedido = _cPedido.Trim();
        _o.SuaReferencia = _o.NumeroPedido;

        // os TAs de hoje ficam como estão (o do preço meta vira fixo: a meta agora é o pedido)
        foreach (var it in _items)
        {
            it.PrecoMeta = 0m;
            it.TaManual = it.CustoTa > 0m;
        }

        // o preço trava no valor do pedido, no nível de impostos em que a proposta sai
        _o.PricingModo = "preco";
        _o.PrecoMetaNivel = NivelDaProposta(rAntes).Codigo;
        var demais = _itemDemais is not null && _items.Contains(_itemDemais);
        _o.PrecoMeta = Cambio.ParaCusto(_o, _cValor ?? totalAntes);

        // preço novo por item: o custo de cada item fica na proporção do preço dele
        // (o preço de cada item = custo × o mesmo fator), acrescentando TA
        if (_cAjustarItens)
        {
            var custo = Custos.ComCusto.ToDictionary(x => x.It, x => x.Unit);
            var alvos = ItensDoPedido()
                .Where(x => custo.ContainsKey(x.It) && _cPrecos.GetValueOrDefault(x.Indice) is > 0m)
                .Select(x => (x.It, Alvo: Cambio.ParaCusto(_o, _cPrecos[x.Indice]!.Value)))
                .ToList();
            if (alvos.Count > 0)
            {
                var lambda = alvos.Max(a => custo[a.It] / a.Alvo);
                foreach (var (it, alvo) in alvos)
                {
                    var acrescimo = lambda * alvo - custo[it];
                    if (acrescimo > 0.00005m) { it.CustoTa += acrescimo; it.TaManual = true; }
                }
            }
        }

        var taDepoisDoAjuste = _items.Sum(i => i.CustoTa * i.Quantity);
        var freteAntes = _o.FreteCusto;

        // a margem de negociação
        if (_cZerarMn == true && mnAntes > 0m)
        {
            if (_cMnDestino == "ta")
            {
                // o valor dela vira custo (TA) na proporção do custo de cada item, até a margem total ficar a mesma
                var alvoMargem = PricingAtual(Custos).MargemTotal;
                var custo = Custos.ComCusto.Where(x => x.It != _itemDemais).ToList();
                var taBase = custo.ToDictionary(x => x.It, x => x.It.CustoTa);
                var somaCusto = custo.Sum(x => x.Unit * x.It.Quantity);
                _o.MargemNegociacao = 0m;
                decimal Diferenca(decimal k)
                {
                    foreach (var (it, unit) in custo) it.CustoTa = taBase[it] + k * unit;
                    return PricingAtual(Custos).MargemTotal - alvoMargem;
                }
                if (somaCusto > 0m)
                {
                    decimal k0 = 0m, f0 = Diferenca(0m);
                    decimal k1 = mnValor / somaCusto, f1 = Diferenca(k1);
                    for (var volta = 0; volta < 40 && Math.Abs(f1) > 0.0000001m && f1 != f0; volta++)
                    {
                        var k2 = k1 - f1 * (k1 - k0) / (f1 - f0);
                        if (k2 < 0m) k2 = k1 / 2m;
                        (k0, f0) = (k1, f1);
                        k1 = k2;
                        f1 = Diferenca(k1);
                    }
                    foreach (var (it, _) in custo) it.TaManual = it.CustoTa > 0m;
                }
            }
            else if (_cMnDestino == "frete")
            {
                // o valor dela entra no frete da proposta (que se divide pelos itens pelo valor de cada um), até a margem total ficar a mesma
                var alvoMargem = PricingAtual(Custos).MargemTotal;
                var freteBase = _o.FreteCusto;
                _o.MargemNegociacao = 0m;
                decimal Diferenca(decimal d)
                {
                    _o.FreteCusto = freteBase + d;
                    return PricingAtual(Custos).MargemTotal - alvoMargem;
                }
                decimal d0 = 0m, f0 = Diferenca(0m);
                decimal d1 = mnValor, f1 = Diferenca(d1);
                for (var volta = 0; volta < 40 && Math.Abs(f1) > 0.0000001m && f1 != f0; volta++)
                {
                    var d2 = d1 - f1 * (d1 - d0) / (f1 - f0);
                    if (d2 < 0m) d2 = d1 / 2m;
                    (d0, f0) = (d1, f1);
                    d1 = d2;
                    f1 = Diferenca(d1);
                }
                _o.FreteCusto = Math.Round(freteBase + d1, 2);
            }
            else _o.MargemNegociacao = 0m;
        }

        // entrega e pagamento
        _o.DataEntrega = Consolidacao.Texto(_cDataTodos);
        foreach (var (i, it) in ItensDoPedido())
            it.DataEntrega = _cDatasPorItem ? Consolidacao.Texto(_cDatas.GetValueOrDefault(i)) : "";
        if (_cDatasPorItem && _o.DataEntrega.Length == 0)
            _o.DataEntrega = Consolidacao.Texto(_cDatas.Values.Where(d => d is not null).Select(d => d!.Value).DefaultIfEmpty().Max());
        if (_cPagamentoIgual == false) _o.PaymentTerms = _cPagamento.Trim();

        // a marca
        _o.ValorPedido = _cValor ?? 0m;
        _o.MoedaPedido = Moedas.Ler(_cMoeda).Codigo();
        _o.ConsolidadaEm = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        _o.ConsolidadaPor = _usuario;
        _o.Status = "Enviada";
        _o.ConsolidacaoAntes = _cRetrato;

        AtualizarPrecos();
        var rDepois = PricingAtual(Custos);
        var itens = ItensDoPedido().Select(x => (x.Indice, precosAntes[x.Indice], x.It.UnitPrice,
            _cAjustarItens ? _cPrecos.GetValueOrDefault(x.Indice) : (decimal?)null)).ToList();
        _cResultado = new ResultadoDaConsolidacao(totalAntes, Total, margemAntes, rDepois.MargemTotal, mnAntes, mnValor,
            _items.Sum(i => i.CustoTa * i.Quantity) - taDepoisDoAjuste + (_o.FreteCusto - freteAntes), _cMnDestino ?? "nao",
            taDepoisDoAjuste - taAntes, itens);
        if (demais && (_cAjustarItens || _cMnDestino is "ta" or "frete"))
            _cErro = "Esta proposta tem a linha \"Serviços e demais custos\": o total bate com o pedido, mas a divisão entre os itens pode mudar alguns centavos. Confira no resumo.";
    }

    /// <summary>Grava a consolidação (a proposta, os itens e as referências de preço, como o Salvar).</summary>
    private void ConfirmarConsolidacao()
    {
        if (_cResultado is null) return;
        if (!Salvar(true)) { _cErro = _erro ?? "Não deu para gravar."; return; }
        _consolidando = false;
        _cErro = null;
        _msg = $"Proposta consolidada: projeto {_o.Pv}, pedido {_o.NumeroPedido}. A revisão agora aparece como \"{Consolidacao.Revisao(_o)}\".";
    }

    private string ConsolidadaEmTexto =>
        DateTime.TryParseExact(_o.ConsolidadaEm, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) + (_o.ConsolidadaPor.Length > 0 ? $" por {_o.ConsolidadaPor}" : "")
            : _o.ConsolidadaEm;

    /// <summary>Volta a proposta como estava antes de consolidar (preço, margem, TA, referências e campos).</summary>
    private void DesfazerConsolidacao()
    {
        _cConfirmarDesfazer = false;
        var r = Consolidacao.Ler(_o.ConsolidacaoAntes);
        if (r is null) { _erro = "Não há como desfazer: a proposta de antes da consolidação não foi guardada."; return; }
        var indiceDemais = _itemDemais is null ? -1 : _items.IndexOf(_itemDemais);
        _o = r.Oferta;
        _items = r.Itens;
        _itemDemais = _items.FirstOrDefault(i => i.PartNumber.Length == 0 && i.EquipmentId.Length == 0
                                              && i.Description.StartsWith("Serviços", StringComparison.Ordinal))
                      ?? (indiceDemais >= 0 && indiceDemais < _items.Count ? _items[indiceDemais] : null);
        Consolidacao.Limpar(_o, _items);
        if (Salvar(true)) _msg = "Consolidação desfeita: a proposta voltou como estava antes.";
    }

    /// <summary>Abre o assistente quando a proposta chega por Propostas → Consolidar (?consolidar=1).</summary>
    private void ConsolidarPeloEndereco()
    {
        var uri = new Uri(Nav.Uri);
        if (!uri.Query.Contains("consolidar=1", StringComparison.OrdinalIgnoreCase) || _o.Id.Length == 0) return;
        AbrirConsolidacao();
    }
}
