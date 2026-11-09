using System.Globalization;
using AfmHsa.Data;
using AfmHsa.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

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

    // =====================================================================
    // BU que compra de fora (HPU, HCHL): sem a tabela de importação — o
    // preço de fora + frete + margem, e o custo na moeda estrangeira
    // =====================================================================

    /// <summary>A BU compra de fora e revende: não há importação, o custo é o preço de fora, na moeda estrangeira.</summary>
    private bool DeFora => Cambio.CompraDeFora(_o.Bu);

    /// <summary>A moeda do custo, do frete, do TA e do pricing (R$ no Brasil).</summary>
    private Moeda MoedaCusto => Cambio.MoedaDoCusto(_o);

    /// <summary>"R$", "USD"…: o rótulo da moeda do custo.</summary>
    private string Rc => MoedaCusto.Rotulo();

    /// <summary>Valor na moeda do custo ("R$ 1.234,56", "USD 1.234,56").</summary>
    private string Cu(decimal v) => Cambio.DinheiroDoCusto(_o, v);

    /// <summary>O item passa pela tabela de importação (nunca nas BUs de fora).</summary>
    private bool Imp(CommercialOfferItem it) => it.Importado && !DeFora;

    /// <summary>
    /// O custo de um item numa BU de fora: o preço de fora digitado (na moeda
    /// do custo). O item que já tinha o preço do fornecedor na mesma moeda usa
    /// esse preço — sem a importação.
    /// </summary>
    private decimal? CustoDeFora(CommercialOfferItem it) =>
        it.CustoManual is > 0 ? it.CustoManual
        : it.Importado && it.ValorMoeda > 0m && Moedas.Ler(_o.Moeda) == MoedaCusto ? it.ValorMoeda : null;

    private void MudarBu(ChangeEventArgs e)
    {
        var antes = DeFora;
        var custoAntes = MoedaCusto;
        _o.Bu = e.Value?.ToString() ?? "";
        if (MoedaCusto != custoAntes) _o.CambioTaxa = 0;   // a taxa era de outro par de moedas
        // passou a comprar de fora com o preço em R$: o preço vai para a moeda do custo (dá para trocar no pricing)
        if (!antes && DeFora && Cambio.MoedaDoPreco(_o) == Moeda.Brl) EscolherMoedaVenda(MoedaCusto);
    }

    private void MudarMoedaDoCusto(ChangeEventArgs e)
    {
        var m = Moedas.Ler(e.Value?.ToString());
        if (m == Moeda.Brl || m == MoedaCusto) return;
        _o.MoedaCusto = m.Codigo();
        _o.CambioTaxa = 0;   // a taxa era do par anterior
    }

    /// <summary>O custo unitário (na moeda do custo) de um item que não é importado: o digitado, ou a referência da Nacionalização.</summary>
    private decimal? CustoNacionalDe(CommercialOfferItem it)
    {
        if (DeFora) return CustoDeFora(it);
        if (it.CustoManual is > 0) return it.CustoManual;
        var r = RefDoItem(it);
        return r.Nacionalizado && r.Custo is > 0 ? r.Custo : null;
    }

    /// <summary>O custo unitário (R$) do item sem o TA: importação, digitado ou Nacionalização; null = sem custo.</summary>
    private decimal? CustoBaseDoItem(CommercialOfferItem it) =>
        it == _itemDemais ? null : Imp(it) ? CustoImportadoDo(it) : CustoNacionalDe(it);

    /// <summary>O custo unitário (R$) do item como entra no pricing: o base, o frete dele e o TA do preço meta; null = sem custo (pendência).</summary>
    private decimal? CustoDoItem(CommercialOfferItem it) =>
        CustoBaseDoItem(it) is decimal b && b > 0m ? b + FreteDo(it) + it.CustoTa : null;

    // =====================================================================
    // Frete: o valor total da proposta diluído no custo dos itens pelo VALOR
    // =====================================================================

    /// <param name="ValorTotal">a soma de custo unitário × quantidade dos itens com custo (a base da divisão)</param>
    private sealed record RateioDeFrete(Dictionary<CommercialOfferItem, decimal> Unitario, decimal ValorTotal)
    {
        /// <summary>Quanto o frete acrescenta ao custo, em proporção (R$ 1.000 de frete em R$ 50.000 = 2%).</summary>
        public decimal Percentual(decimal frete) => ValorTotal > 0m ? frete / ValorTotal : 0m;
    }

    /// <summary>
    /// O frete unitário (R$) de cada item: o frete da proposta dividido na
    /// proporção do valor de cada item (custo unitário × quantidade) — o mesmo
    /// percentual sobre o custo de todos. Item sem custo não recebe (é pendência).
    /// </summary>
    private RateioDeFrete Rateio()
    {
        var unit = new Dictionary<CommercialOfferItem, decimal>();
        var valores = _items.Where(i => i != _itemDemais && i.Quantity > 0)
            .Select(i => (It: i, Base: CustoBaseDoItem(i) ?? 0m)).Where(x => x.Base > 0m).ToList();
        var valorTotal = valores.Sum(x => x.Base * x.It.Quantity);
        if (_o.FreteCusto > 0m && valorTotal > 0m)
            foreach (var (it, b) in valores) unit[it] = _o.FreteCusto * b / valorTotal;   // (frete × base × qtd ÷ total) ÷ qtd
        return new(unit, valorTotal);
    }

    private decimal FreteDo(CommercialOfferItem it) => Rateio().Unitario.GetValueOrDefault(it);

    private void MudarFrete(ChangeEventArgs e) => _o.FreteCusto = Numero(e) is decimal v && v > 0m ? Math.Round(v, 2) : 0m;


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
        // o TA digitado fica; o da meta some quando a meta sai
        foreach (var it in _items.Where(i => i.PrecoMeta <= 0m && !i.TaManual && i.CustoTa != 0m)) it.CustoTa = 0m;
        if (!_items.Any(i => i.PrecoMeta > 0m)) return;
        for (var volta = 0; volta < 12; volta++)
        {
            var c = Custos;
            var r = PricingAtual(c);
            var fator = r.CustoTotal > 0m && r.VendaLiquida > 0m ? PricingDaProposta.FatorNoNivel(r, _o.ImpostosNoPreco) : 0m;
            if (fator <= 0m) return;
            var mudou = false;
            var frete = Rateio().Unitario;
            foreach (var it in _items.Where(i => i.PrecoMeta > 0m && i != _itemDemais))
            {
                var ta = 0m;
                if (CustoBaseDoItem(it) is decimal b && b > 0m)
                {
                    // o frete já está no custo: o TA só completa o que falta depois dele
                    var custoParaMeta = Cambio.ParaCusto(_o, it.PrecoMeta) / fator;
                    ta = Math.Max(0m, Math.Ceiling((custoParaMeta - b - frete.GetValueOrDefault(it)) * 10000m) / 10000m);
                }
                if (Math.Abs(ta - it.CustoTa) >= 0.0001m) { it.CustoTa = ta; mudou = true; }
            }
            if (!mudou) return;
        }
    }

    private void MudarMeta(CommercialOfferItem it, ChangeEventArgs e)
    {
        it.PrecoMeta = Numero(e) is decimal v && v > 0m ? Math.Round(v, 2) : 0m;
        if (it.PrecoMeta > 0m) it.TaManual = false;           // com meta, o TA volta a ser calculado
        else if (!it.TaManual) it.CustoTa = 0m;
    }

    /// <summary>
    /// TA digitado (R$ por unidade, no custo): vale como está e tira a meta do
    /// item (os dois juntos se contradizem). Vazio ou zero = sem TA.
    /// </summary>
    private void MudarTaDoItem(CommercialOfferItem it, ChangeEventArgs e)
    {
        var v = Numero(e) is decimal d && d > 0m ? Math.Round(d, 2) : 0m;
        it.CustoTa = v;
        it.TaManual = v > 0m;
        if (v > 0m) it.PrecoMeta = 0m;
        Redesenhar(it);
    }

    private void MudarCustoDoItem(CommercialOfferItem it, ChangeEventArgs e)
    {
        var v = Numero(e);
        if (DeFora) { it.CustoManual = v is > 0 ? v : null; return; }
        var referencia = RefDoItem(it);
        // digitar o mesmo valor da referência não "fixa" o custo: ele continua seguindo a Nacionalização
        it.CustoManual = v is > 0 && !(referencia.Nacionalizado && referencia.Custo == v) ? v : null;
    }

    /// <summary>Trocar o part number refaz a referência (nacionalizado, custo, última venda).</summary>
    private void MudarPn(CommercialOfferItem it, ChangeEventArgs e)
    {
        var antes = it.PartNumber;
        it.PartNumber = (e.Value?.ToString() ?? "").Trim();
        // descrição em branco (ou que era só o PN antigo): vem a que já se conhece deste part number
        var desc = (it.Description ?? "").Trim();
        if ((desc.Length == 0 || Textos.Igual(desc, antes)) && DescricaoConhecida(it.PartNumber) is { Length: > 0 } conhecida)
            it.Description = conhecida;
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
        var data = DateTime.TryParseExact(_o.OfferDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // o preço do item está no nível de impostos escolhido: a referência guarda o líquido
        var r = PricingAtual(Custos);
        var noNivel = PricingDaProposta.ValorNoNivel(r, _o.ImpostosNoPreco) ?? r.ValorDaProposta;
        var paraLiquido = noNivel > 0m && r.ValorDaProposta > 0m ? r.ValorDaProposta / noNivel : 1m;
        var todasAsPlantas = string.Join("; ", _o.Plantas.Select(PlantasDaProposta.Rotulo).Where(t => t.Length > 0));

        foreach (var it in _items.Where(i => i != _itemDemais && i.Id.Length > 0))
        {
            var custo = CustoBaseDoItem(it);
            if (it.PartNumber.Trim().Length == 0 || (custo is not > 0m && it.UnitPrice <= 0m))
            {
                if (porId.ContainsKey(it.Id)) RefsCusto.Apagar(it.Id);
                continue;
            }
            var origem = DeFora ? "preço de fora" : it.Importado ? "importação" : it.CustoManual is > 0 ? "digitado" : RefDoItem(it).Nacionalizado ? "nacionalização" : "proposta";
            // a referência guarda o custo em R$: o de fora vai pela aba Conversão (sem a taxa lá, fica sem custo)
            var paraReais = MoedaCusto == Moeda.Brl ? 1m : Conversao.Ler().ReaisPor(MoedaCusto) ?? 0m;
            if (custo is decimal cf) custo = cf * paraReais;
            var planta = PlantasDaProposta.PlantaDe(_o, PlantasDaProposta.DoItem(_o, it));
            var unit = Math.Round(it.UnitPrice * paraLiquido, 2);
            var nova = new ReferenciaDeCusto
            {
                Id = it.Id, PartNumber = it.PartNumber.Trim(), Descricao = DescricaoSemPn(it), Valor = Math.Round(custo ?? 0m, 2),
                Ta = Math.Round(it.CustoTa * paraReais, 2), Data = data, Modelo = ModeloDoItem(it), SerialNumber = SerialDoItem(it),
                Origem = origem, PropostaId = _o.Id, Proposta = _o.OfferCode.Trim(), Revisao = PropostaWord.Ou(_o.Revisao, "0"),
                Cliente = _o.ClientName, Usuario = _usuario,
                Plantas = planta is not null ? PlantasDaProposta.Rotulo(planta) : todasAsPlantas,
                Estado = _o.Estado ?? "", Intercompany = _o.Bu ?? "", Quantidade = it.Quantity,
                PrecoUnitLiquido = unit, PrecoTotalLiquido = Math.Round(unit * it.Quantity, 2), Moeda = Cambio.MoedaDoPreco(_o).Codigo(),
            };
            nova.BaseDoCusto = it.BaseCusto;
            // a venda (AF, por item ou pacote, valor do pacote) é marcada depois, nas Referências: fica a que já estava
            if (porId.TryGetValue(it.Id, out var antiga))
            {
                nova.NumeroAf = antiga.NumeroAf; nova.VendaPor = antiga.VendaPor; nova.ValorPacote = antiga.ValorPacote;
                if (Igual(antiga, nova)) continue;   // sem mudança, a referência fica como estava
            }
            RefsCusto.Gravar(nova);
        }
        foreach (var id in itensApagados.Where(porId.ContainsKey)) RefsCusto.Apagar(id);
        LerReferenciasDeCusto();
    }

    private static bool Igual(ReferenciaDeCusto a, ReferenciaDeCusto b) =>
        a.Valor == b.Valor && a.Ta == b.Ta && a.PartNumber == b.PartNumber && a.Modelo == b.Modelo && a.SerialNumber == b.SerialNumber
        && a.Origem == b.Origem && a.Proposta == b.Proposta && a.Revisao == b.Revisao && a.Cliente == b.Cliente && a.Plantas == b.Plantas
        && a.Estado == b.Estado && a.Intercompany == b.Intercompany && a.Quantidade == b.Quantidade && a.PrecoUnitLiquido == b.PrecoUnitLiquido
        && a.Moeda == b.Moeda && a.Data == b.Data && a.Descricao == b.Descricao && a.BaseDoCusto == b.BaseDoCusto;

    /// <summary>
    /// O último preço enviado ao cliente como meta do item, na moeda desta
    /// proposta (de R$ converte; de outra moeda, só se for a mesma).
    /// </summary>
    private decimal? MetaDoPrecoEnviado(ReferenciaDeCusto refPreco)
    {
        var moedaDaProposta = Cambio.MoedaDoPreco(_o);
        if (refPreco.MoedaDoPreco == moedaDaProposta) return Math.Round(refPreco.PrecoUnitLiquido, 2);
        if (refPreco.MoedaDoPreco == Moeda.Brl && MoedaCusto == Moeda.Brl) return Math.Round(Cambio.ParaVenda(_o, refPreco.PrecoUnitLiquido), 2);
        return null;
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

    // =====================================================================
    // arquivos do pricing: a planilha da equipe preenchida e o custo por item
    // =====================================================================

    private bool _gerandoExcel;

    /// <summary>A planilha de pricing (R_HSAXYZ0000-0.xlsm) preenchida com esta proposta.</summary>
    private async Task GerarPricingExcel()
    {
        if (_gerandoExcel) return;
        _gerandoExcel = true; _erro = null;
        try
        {
            var modelo = Path.Combine(Ambiente.ContentRootPath, PricingExcel.CaminhoDoModelo);
            if (!File.Exists(modelo)) { _erro = $"A planilha de pricing não está em {PricingExcel.CaminhoDoModelo}."; return; }
            var c = Custos;
            var ipi = PricingDaProposta.IpiDosItens(c.ComCusto.Select(x => (x.It, x.Unit)));
            var entrada = PricingDaProposta.EntradaDe(_o, c.Nacionais, c.Importados, ipi);
            var r = PricingAtual(c);
            var bytes = PricingExcel.Gerar(_o, entrada, r, Conversao.Ler(), modelo);
            await JS.InvokeVoidAsync("appDownload", PricingExcel.NomeDoArquivo(_o), PricingExcel.TipoMime, Convert.ToBase64String(bytes));
            _msg = "Pricing em Excel gerado: ao abrir, habilite o conteúdo (macros) para a planilha recalcular.";
        }
        catch (Exception ex) { _erro = "Não deu para gerar o pricing em Excel: " + ex.Message; }
        finally { _gerandoExcel = false; }
    }

    /// <summary>O custo de cada item (com o TA, quando há) e o preço dele, numa planilha.</summary>
    private async Task GerarCustoPorItem()
    {
        if (_gerandoExcel) return;
        if (_items.Count == 0) { _erro = "Nenhum item na proposta."; return; }
        _gerandoExcel = true; _erro = null;
        try
        {
            AtualizarPrecos();
            var c = Custos;
            var r = PricingAtual(c);
            var linhas = new List<CustoDoItemNaPlanilha>();
            for (var i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                var demais = it == _itemDemais;
                var rf = RefDoItem(it);
                var origem = demais ? "tabela do pricing"
                    : DeFora ? "preço de fora"
                    : it.Importado ? (_o.ImportacaoModo == "item" ? "importado (por item)" : "importado (por total)")
                    : it.CustoManual is > 0 ? "digitado"
                    : rf.Nacionalizado && rf.Custo is > 0 ? "Nacionalização" : "sem custo";
                linhas.Add(new CustoDoItemNaPlanilha(
                    i + 1, it.PartNumber, demais ? it.Description : DescricaoSemPn(it), demais ? "" : ModeloDoItem(it),
                    demais ? "" : SerialDoItem(it), demais ? "" : TagDoItem(it), origem,
                    demais ? 0 : PrazoDoItem(it), it.Quantity,
                    demais ? DemaisCustos(c, r) : CustoBaseDoItem(it), demais ? 0m : Math.Round(FreteDo(it), 2),
                    demais ? 0m : Math.Round(it.CustoTa, 2),
                    it.PrecoMeta, it.UnitPrice, it.TotalPrice));
            }
            var nivel = PricingDaProposta.NivelEfetivo(r, _o.ImpostosNoPreco).Rotulo;
            var bytes = CustoPorItemExcel.Gerar(_o, linhas, r, nivel);
            await JS.InvokeVoidAsync("appDownload", CustoPorItemExcel.NomeDoArquivo(_o), CustoPorItemExcel.TipoMime, Convert.ToBase64String(bytes));
            var comTa = _items.Count(x => x.CustoTa > 0m);
            _msg = $"Custo por item gerado{(comTa > 0 ? $" — {Plural(comTa, "item", "itens")} com TA, marcado(s) em amarelo" : "")}.";
        }
        catch (Exception ex) { _erro = "Não deu para gerar o custo por item: " + ex.Message; }
        finally { _gerandoExcel = false; }
    }

}
