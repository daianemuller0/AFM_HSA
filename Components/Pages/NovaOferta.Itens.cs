using System.Globalization;
using AfmHsa.Data;
using AfmHsa.Models;
using Microsoft.AspNetCore.Components;

namespace AfmHsa.Components.Pages;

/// <summary>
/// Os itens e o preço: todo item tem custo (importação, a referência da
/// Nacionalização ou o digitado), e o preço é sempre o custo × pricing, no
/// nível de impostos escolhido — nada de preço digitado que fica para trás.
/// Também as condições comerciais que a equipe inclui na base pelo "+".
/// </summary>
public partial class NovaOferta
{
    [Inject] private CondicoesComerciaisRepository Condicoes { get; set; } = default!;

    // =====================================================================
    // custo e preço dos itens
    // =====================================================================

    /// <summary>O custo unitário (R$) de um item que não é importado: o digitado, ou a referência da Nacionalização.</summary>
    private decimal? CustoNacionalDe(CommercialOfferItem it)
    {
        if (it.CustoManual is > 0) return it.CustoManual;
        var r = RefDoItem(it);
        return r.Nacionalizado && r.Custo is > 0 ? r.Custo : null;
    }

    /// <summary>O custo unitário (R$) do item, como entra no pricing; null = sem custo (pendência).</summary>
    private decimal? CustoDoItem(CommercialOfferItem it) =>
        it == _itemDemais ? null : it.Importado ? CustoImportadoDo(it) : CustoNacionalDe(it);

    /// <summary>
    /// O preço de cada item = custo × fator do pricing no nível de impostos
    /// escolhido, na moeda do preço. Chamado a cada desenho: o preço nunca
    /// fica para trás do custo, da margem, do câmbio ou dos impostos.
    /// </summary>
    private void AtualizarPrecos()
    {
        var c = Custos;
        var r = PricingAtual(c);
        var fator = r.CustoTotal > 0m && r.VendaLiquida > 0m ? PricingDaProposta.FatorNoNivel(r, _o.ImpostosNoPreco) : 0m;
        var custos = c.ComCusto.ToDictionary(x => x.It, x => x.Unit);
        foreach (var it in _items)
        {
            if (it == _itemDemais) continue;
            var custo = custos.TryGetValue(it, out var u) ? u : 0m;
            it.UnitPrice = custo > 0m && fator > 0m ? Math.Round(Cambio.ParaVenda(_o, custo * fator), 2) : 0m;
            it.TotalPrice = it.Quantity * it.UnitPrice;
        }
        if (_itemDemais is not null && _items.Contains(_itemDemais))
        {
            var demais = DemaisCustos(c, r);
            _itemDemais.Description = DescricaoDosDemais();
            _itemDemais.Quantity = 1;
            _itemDemais.UnitPrice = demais > 0.005m && fator > 0m ? Math.Round(Cambio.ParaVenda(_o, demais * fator), 2) : 0m;
            _itemDemais.TotalPrice = _itemDemais.UnitPrice;
        }
    }

    private void MudarCustoDoItem(CommercialOfferItem it, ChangeEventArgs e)
    {
        var v = Numero(e);
        var referencia = RefDoItem(it);
        // digitar o mesmo valor da referência não "fixa" o custo: ele continua seguindo a Nacionalização
        it.CustoManual = v is > 0 && !(referencia.Nacionalizado && referencia.Custo == v) ? v : null;
    }

    /// <summary>Trocar o part number refaz a referência (nacionalizado, custo, última venda).</summary>
    private void MudarPn(CommercialOfferItem it, ChangeEventArgs e)
    {
        it.PartNumber = (e.Value?.ToString() ?? "").Trim();
        var r = Ref(it.PartNumber);
        it.Nacionalizado = r.Nacionalizado;
        it.CustoReferencia = r.Custo; it.CustoData = r.CustoData;
        it.UltimaVenda = r.UltimaVenda; it.UltimaVendaData = r.UltimaVendaData;
    }

    private void MudarPrazoDoItem(CommercialOfferItem it, ChangeEventArgs e) =>
        it.PrazoEntregaDias = Numero(e) is decimal d && d > 0 ? (int)Math.Round(d) : 0;

    /// <summary>O prazo que vale para o item: o dele, ou o da proposta.</summary>
    private int PrazoDoItem(CommercialOfferItem it) =>
        it.PrazoEntregaDias > 0 ? it.PrazoEntregaDias : PricingDaProposta.PrazoEmDias(_o.PrazoEntrega);

    /// <summary>Uma cópia da linha logo abaixo, já preenchida (equipamento, PN, NCM, custo, importação, prazo).</summary>
    private void DuplicarItem(int idx)
    {
        if (idx < 0 || idx >= _items.Count) return;
        var copia = Clonar(_items[idx]);
        copia.Id = "";
        copia.OfferId = "";
        _items.Insert(idx + 1, copia);
    }

    // =====================================================================
    // Segmento (margem): campo livre, 52% já digitado
    // =====================================================================

    private void MudarMargemDoCabecalho(ChangeEventArgs e)
    {
        _o.MargemAlvo = Fracao(e, _o.MargemAlvo);
        _o.PricingModo = "margem";
    }

    // =====================================================================
    // condições comerciais: o que não está na lista entra na base pelo "+"
    // =====================================================================

    private List<CondicaoComercial> _pagamentosDaBase = new();
    private List<CondicaoComercial> _entregasDaBase = new();
    private string? _novaCondicao;
    private string _textoNovaCondicao = "";

    private void LerCondicoes()
    {
        _pagamentosDaBase = Condicoes.Do(CondicoesComerciaisRepository.Pagamento);
        _entregasDaBase = Condicoes.Do(CondicoesComerciaisRepository.Entrega);
    }

    /// <summary>As condições de pagamento do modelo e, depois, as que a equipe incluiu.</summary>
    private List<string> OpcoesDePagamento => OfferDefaults.PaymentOptions
        .Concat(_pagamentosDaBase.Select(c => c.Texto))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private void AbrirNovaCondicao(string tipo)
    {
        _novaCondicao = _novaCondicao == tipo ? null : tipo;
        _textoNovaCondicao = "";
    }

    private void IncluirCondicao()
    {
        var texto = _textoNovaCondicao.Trim();
        if (_novaCondicao is null || texto.Length == 0) return;
        var nova = Condicoes.Incluir(_novaCondicao, texto);
        LerCondicoes();
        if (_novaCondicao == CondicoesComerciaisRepository.Pagamento) _o.PaymentTerms = nova.Texto;
        else { _o.Incoterm = ListasDaProposta.Livre; _o.IncotermLocal = nova.Texto; }
        _msg = $"\"{nova.Texto}\" incluída na base: aparece nas próximas propostas também.";
        _novaCondicao = null;
        _textoNovaCondicao = "";
    }

    /// <summary>O valor do select do local de entrega: o código do modelo, ou "extra:id" do que foi incluído na base.</summary>
    private string ValorDoIncoterm => _o.Incoterm != ListasDaProposta.Livre ? _o.Incoterm
        : _entregasDaBase.FirstOrDefault(c => Textos.Igual(c.Texto, _o.IncotermLocal)) is { } extra ? "extra:" + extra.Id
        : ListasDaProposta.Livre;

    private void EscolherIncoterm(ChangeEventArgs e)
    {
        var valor = e.Value?.ToString() ?? "";
        if (valor.StartsWith("extra:", StringComparison.Ordinal))
        {
            if (_entregasDaBase.FirstOrDefault(c => "extra:" + c.Id == valor) is { } extra)
            {
                _o.Incoterm = ListasDaProposta.Livre;
                _o.IncotermLocal = extra.Texto;
            }
            return;
        }
        if (valor == ListasDaProposta.Livre) return;
        var antes = _o.Incoterm;
        _o.Incoterm = valor;
        // o local digitado só serve para as linhas que pedem local
        if (antes == ListasDaProposta.Livre || ListasDaProposta.Incoterms.FirstOrDefault(i => i.Codigo == valor) is { PedeLocal: false })
            _o.IncotermLocal = "";
    }

    // =====================================================================
    // a lista de equipamentos do cadastro (para incluir de outra planta)
    // =====================================================================

    private int TotalParaIncluir
    {
        get
        {
            var ja = _o.EquipamentosDaProposta.Select(x => x.EquipmentId).ToHashSet();
            return _equipamentos.Count(e => !ja.Contains(e.Id));
        }
    }
}
