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
    [Inject] private ReferenciaDeCustoRepository RefsCusto { get; set; } = default!;

    /// <summary>As referências de custo gravadas pelas propostas (lidas ao abrir e a cada salvar).</summary>
    private List<ReferenciaDeCusto> _refsCusto = new();

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

    /// <summary>O custo unitário (R$) do item sem o TA: importação, digitado ou Nacionalização; null = sem custo.</summary>
    private decimal? CustoBaseDoItem(CommercialOfferItem it) =>
        it == _itemDemais ? null : it.Importado ? CustoImportadoDo(it) : CustoNacionalDe(it);

    /// <summary>O custo unitário (R$) do item como entra no pricing: o base mais o TA do preço meta; null = sem custo (pendência).</summary>
    private decimal? CustoDoItem(CommercialOfferItem it) =>
        CustoBaseDoItem(it) is decimal b && b > 0m ? b + it.CustoTa : null;

    /// <summary>
    /// O preço de cada item = custo × fator do pricing no nível de impostos
    /// escolhido, na moeda do preço. Chamado a cada desenho: o preço nunca
    /// fica para trás do custo, da margem, do câmbio ou dos impostos.
    /// </summary>
    private void AtualizarPrecos()
    {
        CalcularTa();
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

    /// <summary>
    /// TA do preço meta: o custo que falta para o preço do item chegar na
    /// meta. Ele muda o custo total, e o fator do pricing pode andar um pouco:
    /// a conta repete até parar de mudar (duas ou três voltas). Só acrescenta —
    /// com o preço já acima da meta, o TA é zero (aí é a margem que baixa).
    /// </summary>
    private void CalcularTa()
    {
        foreach (var it in _items.Where(i => i.PrecoMeta <= 0m && i.CustoTa != 0m)) it.CustoTa = 0m;
        if (!_items.Any(i => i.PrecoMeta > 0m)) return;
        for (var volta = 0; volta < 12; volta++)
        {
            var c = Custos;
            var r = PricingAtual(c);
            var fator = r.CustoTotal > 0m && r.VendaLiquida > 0m ? PricingDaProposta.FatorNoNivel(r, _o.ImpostosNoPreco) : 0m;
            if (fator <= 0m) return;
            var mudou = false;
            foreach (var it in _items.Where(i => i.PrecoMeta > 0m && i != _itemDemais))
            {
                var ta = 0m;
                if (CustoBaseDoItem(it) is decimal b && b > 0m)
                {
                    var custoParaMeta = Cambio.ParaReais(_o, it.PrecoMeta) / fator;
                    ta = Math.Max(0m, Math.Ceiling((custoParaMeta - b) * 10000m) / 10000m);
                }
                if (Math.Abs(ta - it.CustoTa) >= 0.0001m) { it.CustoTa = ta; mudou = true; }
            }
            if (!mudou) return;
        }
    }

    private void MudarMeta(CommercialOfferItem it, ChangeEventArgs e)
    {
        it.PrecoMeta = Numero(e) is decimal v && v > 0m ? Math.Round(v, 2) : 0m;
        if (it.PrecoMeta <= 0m) it.CustoTa = 0m;
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

    private void MudarPrazoDoItem(CommercialOfferItem it, ChangeEventArgs e)
    {
        var texto = (e.Value?.ToString() ?? "").Trim();
        it.PrazoEntregaDias = Numero(e) is decimal d && d > 0 ? (int)Math.Round(d) : 0;
        if (texto != (it.PrazoEntregaDias > 0 ? it.PrazoEntregaDias.ToString() : "")) Redesenhar(it);
    }

    // ---- quantidade: o campo mostra sempre o que valeu ----
    //
    // O campo é atualizado pelo valor que vai na tela; quando a quantidade
    // digitada não vale (vazio, zero, "2,5") e o valor guardado continua o
    // mesmo, o Blazor não tinha o que atualizar e o campo ficava mostrando o
    // texto errado. A versão do item recria o campo nesse caso.

    private readonly Dictionary<CommercialOfferItem, int> _versoes = new(ReferenceEqualityComparer.Instance);

    private int Versao(CommercialOfferItem it) => _versoes.GetValueOrDefault(it);

    private void Redesenhar(CommercialOfferItem it) => _versoes[it] = Versao(it) + 1;

    private void MudarQtdDe(CommercialOfferItem it, ChangeEventArgs e)
    {
        var texto = (e.Value?.ToString() ?? "").Trim();
        if (Numero(e) is decimal v && v >= 1m) it.Quantity = (int)Math.Round(v, MidpointRounding.AwayFromZero);
        it.TotalPrice = it.Quantity * it.UnitPrice;
        if (texto != it.Quantity.ToString(CultureInfo.InvariantCulture)) Redesenhar(it);
    }

    private void MarcarImportadoDe(CommercialOfferItem it, ChangeEventArgs e) => it.Importado = e.Value is bool b && b;

    private void MudarValorMoedaDe(CommercialOfferItem it, ChangeEventArgs e) =>
        it.ValorMoeda = Numero(e) is decimal v && v > 0 ? v : 0;

    // ---- modelo, serial e TAG do item: os do equipamento, ou o que for digitado ----

    private EquipamentoDaProposta? EquipDoItem(CommercialOfferItem it) => PlantasDaProposta.DoItem(_o, it);

    private string ModeloDoItem(CommercialOfferItem it) =>
        it.Modelo.Trim().Length > 0 ? it.Modelo : EquipDoItem(it)?.Modelo ?? (it.EquipmentId.Length > 0 ? ModeloDe(it.EquipmentId) : "");

    private string SerialDoItem(CommercialOfferItem it) =>
        it.SerialNumber.Trim().Length > 0 ? it.SerialNumber : EquipDoItem(it)?.SerialNumber ?? "";

    private string TagDoItem(CommercialOfferItem it) =>
        it.Tag.Trim().Length > 0 ? it.Tag : EquipDoItem(it)?.Tag ?? "";

    /// <summary>
    /// O modelo digitado no item: igual ao do equipamento, segue o equipamento;
    /// sem equipamento, liga num equipamento da proposta com esse modelo ou cria
    /// um não cadastrado (aparece na lista de equipamentos); diferente do
    /// equipamento escolhido, fica só neste item.
    /// </summary>
    private void MudarModeloDoItem(CommercialOfferItem it, ChangeEventArgs e, bool novo)
    {
        var modelo = (e.Value?.ToString() ?? "").Trim();
        var eq = EquipDoItem(it);
        if (eq is not null)
        {
            it.Modelo = Textos.Igual(eq.Modelo, modelo) ? "" : modelo;
            return;
        }
        it.Modelo = modelo;
        if (!novo) LigarAoEquipamento(it);
    }

    private void MudarSerialDoItem(CommercialOfferItem it, ChangeEventArgs e)
    {
        var v = (e.Value?.ToString() ?? "").Trim();
        it.SerialNumber = EquipDoItem(it) is { } eq && Textos.Igual(eq.SerialNumber, v) ? "" : v;
    }

    private void MudarTagDoItem(CommercialOfferItem it, ChangeEventArgs e)
    {
        var v = (e.Value?.ToString() ?? "").Trim();
        it.Tag = EquipDoItem(it) is { } eq && Textos.Igual(eq.Tag, v) ? "" : v;
    }

    /// <summary>Item sem equipamento e com modelo: liga no equipamento da proposta com esse modelo, ou cria um não cadastrado.</summary>
    private void LigarAoEquipamento(CommercialOfferItem it)
    {
        if (it.EquipmentId.Length > 0 || it.Modelo.Trim().Length == 0) return;
        var eq = _o.EquipamentosDaProposta.FirstOrDefault(x => Textos.Igual(x.Modelo, it.Modelo)
                                                              && (it.SerialNumber.Length == 0 || Textos.Igual(x.SerialNumber, it.SerialNumber)));
        if (eq is null)
        {
            eq = new EquipamentoDaProposta
            {
                Id = PlantasDaProposta.NovoId("x"), Tipo = PlantasDaProposta.Compressor, Modelo = it.Modelo.Trim(),
                SerialNumber = it.SerialNumber.Trim(), Tag = it.Tag.Trim(), PlantaId = _o.Plantas.FirstOrDefault()?.Id ?? "",
            };
            _o.EquipamentosDaProposta.Add(eq);
        }
        it.EquipmentId = eq.Id;
        it.Modelo = Textos.Igual(eq.Modelo, it.Modelo) ? "" : it.Modelo;
        it.SerialNumber = Textos.Igual(eq.SerialNumber, it.SerialNumber) ? "" : it.SerialNumber;
        it.Tag = Textos.Igual(eq.Tag, it.Tag) ? "" : it.Tag;
    }

    /// <summary>Os modelos para a lista do campo: os da proposta e os do cadastro.</summary>
    private IEnumerable<string> ModelosConhecidos => _o.EquipamentosDaProposta.Select(e => e.Modelo)
        .Concat(_equipamentos.Select(e => e.Modelo))
        .Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(m => m);

    // ---- o item novo: os mesmos campos dos itens ----

    private CommercialOfferItem _novoItem = NovoItemVazio();

    private static CommercialOfferItem NovoItemVazio() => new() { Quantity = 1, Ncm = ListasDaProposta.NcmPadrao() };

    private void LimparNovoItem() => _novoItem = NovoItemVazio();

    private void AdicionarNovoItem()
    {
        var n = _novoItem;
        if (string.IsNullOrWhiteSpace(n.Description) && string.IsNullOrWhiteSpace(n.PartNumber))
        {
            _erro = "Informe a descrição ou o part number do novo item.";
            return;
        }
        _erro = null;
        var item = Clonar(n);
        item.Id = ""; item.OfferId = "";
        if (string.IsNullOrWhiteSpace(item.Description)) item.Description = item.PartNumber;
        if (string.IsNullOrWhiteSpace(item.Ncm)) item.Ncm = ListasDaProposta.NcmPadrao();
        item.Quantity = Math.Max(item.Quantity, 1);
        var r = Ref(item.PartNumber);
        item.Nacionalizado = r.Nacionalizado; item.CustoReferencia = r.Custo; item.CustoData = r.CustoData;
        item.UltimaVenda = r.UltimaVenda; item.UltimaVendaData = r.UltimaVendaData;
        LigarAoEquipamento(item);
        _items.Add(item);
        // o próximo item costuma ser do mesmo equipamento: ele fica escolhido
        _novoItem = NovoItemVazio();
        _novoItem.EquipmentId = item.EquipmentId;
        _novoItem.Ncm = item.Ncm;
        _novoItem.PrazoEntregaDias = item.PrazoEntregaDias;
        _msg = $"Item \"{item.Description}\" incluído.";
    }

    // ---- referências de custo: o que esta proposta usou, para a próxima ----

    private void LerReferenciasDeCusto() => _refsCusto = RefsCusto.Todas();

    /// <summary>
    /// Ao salvar: o custo digitado ou de importação de cada item com part
    /// number vira referência (valor, data, PN, modelo e serial number).
    /// O custo que veio da Nacionalização já é referência lá e não entra.
    /// </summary>
    private void GravarReferenciasDeCusto(IEnumerable<string> itensApagados)
    {
        var porId = _refsCusto.ToDictionary(r => r.Id);
        var hoje = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var it in _items.Where(i => i != _itemDemais && i.Id.Length > 0))
        {
            var custo = CustoBaseDoItem(it);
            var origem = it.Importado ? "importação" : it.CustoManual is > 0 ? "digitado" : "";
            if (it.PartNumber.Trim().Length == 0 || custo is not > 0m || origem.Length == 0)
            {
                if (porId.ContainsKey(it.Id)) RefsCusto.Apagar(it.Id);
                continue;
            }
            var nova = new ReferenciaDeCusto
            {
                Id = it.Id, PartNumber = it.PartNumber.Trim(), Descricao = DescricaoSemPn(it), Valor = Math.Round(custo.Value, 2),
                Ta = Math.Round(it.CustoTa, 2), Data = hoje, Modelo = ModeloDoItem(it), SerialNumber = SerialDoItem(it),
                Origem = origem, PropostaId = _o.Id, Proposta = PropostaWord.Referencia(_o), Cliente = _o.ClientName,
                Usuario = _usuario,
            };
            // sem mudança no que importa, a referência fica como estava (com a data dela)
            if (porId.TryGetValue(it.Id, out var antiga) && antiga.Valor == nova.Valor && antiga.Ta == nova.Ta
                && antiga.PartNumber == nova.PartNumber && antiga.Modelo == nova.Modelo && antiga.SerialNumber == nova.SerialNumber
                && antiga.Origem == nova.Origem && antiga.Proposta == nova.Proposta)
                continue;
            RefsCusto.Gravar(nova);
        }
        foreach (var id in itensApagados.Where(porId.ContainsKey)) RefsCusto.Apagar(id);
        LerReferenciasDeCusto();
    }

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
