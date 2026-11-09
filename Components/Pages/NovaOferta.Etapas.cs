using System.Globalization;
using System.Text.Json;
using AfmHsa.Data;
using AfmHsa.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AfmHsa.Components.Pages;

/// <summary>
/// A Gerar Proposta por etapas — o mesmo caminho da "Proposta v2" do
/// axiaisvent (PropostaPage.razor.cs): Começar → Cliente e condições → Itens →
/// Preço → Documentos, com uma barra que diz em que pé está cada etapa, um
/// painel do lado com o total e TODAS as pendências (cada uma leva ao campo),
/// Salvar a qualquer momento (Ctrl+S), abrir/copiar/nova revisão de uma
/// proposta gravada, e o cronômetro que mede onde a proposta demora.
/// </summary>
public partial class NovaOferta : IDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IWebHostEnvironment Ambiente { get; set; } = default!;
    [Inject] private TempoRepository Tempos { get; set; } = default!;

    /// <summary>A proposta a abrir (/gerar-proposta/{Id}). Vazio = proposta nova.</summary>
    [Parameter] public string? Id { get; set; }

    // =====================================================================
    // etapas
    // =====================================================================

    private static readonly (int Numero, string Nome)[] Etapas =
    {
        (1, "Começar"), (2, "Cliente e condições"), (3, "Itens"), (4, "Preço"), (5, "Documentos"),
    };

    private int _etapa = 1;

    /// <summary>Trocou de etapa: depois de desenhar, a tela volta ao topo.</summary>
    private bool _rolarTopo;

    /// <summary>O campo para onde a pendência leva, depois de desenhar.</summary>
    private string? _irAoCampo;

    /// <summary>Os campos do cabeçalho que quase nunca mudam (número, PV, ano, NCM c/redução…).</summary>
    private bool _maisCampos;

    private static string NomeDaEtapa(int n) => Etapas.FirstOrDefault(e => e.Numero == n).Nome ?? "";

    private void IrParaEtapa(int n)
    {
        _etapa = Math.Clamp(n, 1, Etapas.Length);
        _rolarTopo = true;
        if (_etapa == 1) _anteriores = OffersRepo.All();
    }

    /// <summary>"Salvar e continuar": sem cliente ainda não há o que gravar, e a etapa segue mesmo assim.</summary>
    private void SalvarEContinuar()
    {
        if (string.IsNullOrWhiteSpace(_o.ClientName) || Salvar(explicito: false)) IrParaEtapa(_etapa + 1);
    }

    // =====================================================================
    // pendências: tudo o que o Word deixaria em branco, de todas as etapas
    // =====================================================================

    /// <param name="SoAviso">sai em branco ou "a confirmar" no documento, mas não impede a proposta de estar pronta</param>
    private sealed record Pendencia(string Texto, int Etapa, string Alvo = "", bool SoAviso = false);

    /// <summary>Os campos que moram no "Mais campos" da etapa 2: a pendência abre o grupo antes de ir.</summary>
    private static readonly HashSet<string> AlvosDosMaisCampos = new() { "f-ncmred", "f-ic", "f-ano", "f-segmento" };

    private List<Pendencia> Pendencias()
    {
        var lista = new List<Pendencia>();
        void Falta(bool falta, string texto, int etapa, string alvo) { if (falta) lista.Add(new(texto, etapa, alvo)); }
        void Avisa(bool falta, string texto, int etapa, string alvo) { if (falta) lista.Add(new(texto, etapa, alvo, true)); }
        static bool V(string? s) => string.IsNullOrWhiteSpace(s);
        var comFianca = Textos.Igual(_o.FiancaSeguro, "Sim");

        // 2 — cliente e condições
        Falta(V(_o.ClientName), "Nome do cliente", 2, "f-cliente");
        Falta(V(_o.AttentionTo), "Aos cuidados de (contato)", 2, "f-ac");
        Falta(V(_o.City), "Cidade do cliente", 2, "f-cidade");
        Falta(V(_o.OfferCode), "Nº do CRM (nossa referência)", 2, "f-numero");
        Falta(V(_o.PaymentTerms), "Condição de pagamento", 2, "f-pagamento");
        Falta(ListasDaProposta.Incoterms.FirstOrDefault(x => x.Codigo == _o.Incoterm) is { PedeLocal: true } && V(_o.IncotermLocal),
            "Local do incoterm", 2, "f-incoterm-local");
        Falta(comFianca && PricingDaProposta.PrazoEmDias(_o.PrazoEntrega) <= 0, "Prazo de entrega (dias) — a fiança usa", 2, "f-prazo");
        Falta(V(_o.SegmentoMargem), "Segmento (margem)", 2, "f-segmento");
        Falta(V(_o.Bu), "BU", 2, "f-bu");
        Falta(V(_o.VendaPara), "Venda para", 2, "f-vendapara");
        Falta(V(_o.Destino), "Destino", 2, "f-destino");
        Falta(ListasDoPricing.EstadosDo(_o.Destino).Length > 0 && V(_o.Estado), "Estado (ICMS)", 2, "f-uf");
        Falta(V(_o.ClienteCom), "Cliente com", 2, "f-clientecom");
        Falta(V(_o.FiancaSeguro), "Fiança/Seguro Garantia", 2, "f-fianca");
        Falta(V(_o.Portal), "Portal", 2, "f-portal");
        Falta(V(_o.NcmReducaoIcms), "NCM c/redução base ICMS", 2, "f-ncmred");
        Avisa(!comFianca && PricingDaProposta.PrazoEmDias(_o.PrazoEntrega) <= 0, "Prazo de entrega (sai \"a confirmar\")", 2, "f-prazo");
        Avisa(V(_o.ContatoEmail), "E-mail do contato", 2, "f-email");
        Avisa(V(_o.ContatoTelefone), "Telefone do contato", 2, "f-telefone");
        Avisa(V(_o.SuaReferencia), "Sua referência (do cliente)", 2, "f-suaref");
        Avisa(V(_o.Project), "Projeto", 2, "f-projeto");
        Avisa(V(_o.Vendedor), "Vendedor (contato na capa)", 2, "f-vendedor");
        if (IdiomasDaProposta.Normalizar(_o.Idioma) != IdiomasDaProposta.Portugues)
        {
            // o que a equipe digita livre só se traduz quando é uma das frases conhecidas
            var lingua = IdiomasDaProposta.Nome(_o.Idioma).ToLowerInvariant();
            var para = "para o " + lingua;
            Avisa(!TraducaoDaProposta.TemTraducao(_o.PaymentTerms, _o.Idioma),
                $"Condição de pagamento sem tradução {para} (sai como digitada — pode escrevê-la em {lingua})", 2, "f-pagamento");
            Avisa(_o.Incoterm == ListasDaProposta.Livre && !TraducaoDaProposta.TemTraducao(_o.IncotermLocal, _o.Idioma),
                $"Local de entrega sem tradução {para} (sai como digitado)", 2, "f-incoterm");
            Avisa(!TraducaoDaProposta.TemTraducao(_o.ValidityText, _o.Idioma),
                $"Validade sem tradução {para} (sai como digitada)", 2, "f-validade");
        }
        Avisa(_o.Plantas.Count == 0, "Nenhuma planta (sai só a cidade da capa)", 2, "f-plantas");
        for (var n = 0; n < _o.Plantas.Count; n++)
        {
            var pl = _o.Plantas[n];
            Falta(!pl.Cadastrada && V(pl.Nome) && V(pl.Cidade), $"Planta {n + 1}: nome ou cidade", 2, "f-plantas");
        }

        // 3 — itens
        Falta(_items.Count == 0, "Nenhum item na proposta", 3, "f-itens");
        var semModelo = _o.EquipamentosDaProposta.Count(e => !e.Cadastrado && V(e.Modelo));
        Falta(semModelo > 0, $"{Plural(semModelo, "equipamento não cadastrado", "equipamentos não cadastrados")} sem modelo", 3, "f-equipamentos");
        var semPlanta = _o.Plantas.Count > 1 ? _o.EquipamentosDaProposta.Count(e => V(e.PlantaId)) : 0;
        Avisa(semPlanta > 0, $"{Plural(semPlanta, "equipamento", "equipamentos")} sem planta", 3, "f-equipamentos");
        if (_items.Any(Imp))
        {
            Falta(string.IsNullOrEmpty(_o.ImportacaoModo), "Importação: por total ou por item", 3, "f-importacao");
            foreach (var p in Importacoes.Pendencias) Falta(true, "Importação: " + p, 3, "f-importacao");
        }
        var semNcm = _items.Count(i => i != _itemDemais && V(i.Ncm));
        Avisa(semNcm > 0, $"{Plural(semNcm, "item", "itens")} sem NCM (sai \"a definir\")", 3, "f-itens");
        var semCusto = Custos.SemCusto.Count(i => i != _itemDemais);
        Falta(semCusto > 0, $"{Plural(semCusto, "item", "itens")} sem custo ({(DeFora ? "digite o preço de fora" : "digite o custo ou marque importado")})", 3, "f-itens");

        // 4 — preço
        Falta(Cambio.FaltaTaxa(_o), $"Taxa de câmbio do {Cambio.MoedaDoPreco(_o).Rotulo()}", 4, "f-taxa");
        Falta(Cambio.TaxaSuspeita(_o),
            $"Taxa de câmbio {Cambio.MoedaDoCusto(_o).Rotulo()} → {Cambio.MoedaDoPreco(_o).Rotulo()} parece ao contrário: confira o sentido", 4, "f-taxa");
        if (Bancos.ParaProposta(_o) is { Conta: null } semConta)
            Avisa(true, $"Sem conta bancária em {semConta.Moeda.Rotulo()} da {semConta.Dados.Bu} (Dados e Importação → Dados bancários): o Word sai sem a conta", 5, "");
        var acimaDaMeta = _items.Count(i => i.PrecoMeta > 0m && i.UnitPrice > i.PrecoMeta + 0.01m);
        Avisa(acimaDaMeta > 0, $"{Plural(acimaDaMeta, "item", "itens")} com preço acima da meta (o TA só sobe; para baixar, reduza a margem)", 3, "f-itens");
        Falta(comFianca && V(_o.TipoGarantia), "Tipo de garantia (fiança)", 4, "f-tipogarantia");
        var c = Custos;
        if (c.Total > 0 || _o.CustosPricing.Any(x => x.Valor != 0m))
        {
            var r = PricingAtual(c);
            Falta(r.CustoTotal > 0 && r.VendaLiquida <= 0,
                PelaMargem ? "Margem pedida alta demais" : "Preço de venda do pricing", 4, "f-margem");
        }
        return lista;
    }

    private static string Plural(int n, string um, string varios) => $"{n} {(n == 1 ? um : varios)}";

    private void IrPara(Pendencia p)
    {
        IrParaEtapa(p.Etapa);
        if (AlvosDosMaisCampos.Contains(p.Alvo)) _maisCampos = true;
        if (p.Alvo.Length == 0) return;
        _rolarTopo = false;
        _irAoCampo = p.Alvo;
    }

    private enum Selo { Neutro, Pendente, Ok }

    /// <summary>
    /// O selo de uma etapa na barra (a regra do axiaisvent): o visto só com
    /// conteúdo e nada pendente nela; sem conteúdo, a etapa ainda não começou
    /// e fica sem selo.
    /// </summary>
    private Selo SeloDaEtapa(int etapa, int faltamNela, int faltamNoTotal)
    {
        var temConteudo = etapa switch
        {
            2 => !string.IsNullOrWhiteSpace(_o.ClientName),
            3 or 5 => _items.Count > 0,
            4 => Total > 0m,
            _ => false,
        };
        if (!temConteudo) return Selo.Neutro;
        if (etapa == 5) return faltamNoTotal == 0 ? Selo.Ok : Selo.Neutro;
        return faltamNela > 0 ? Selo.Pendente : Selo.Ok;
    }

    // =====================================================================
    // salvar, abrir, nova, copiar, nova revisão
    // =====================================================================

    /// <summary>O retrato do que está na tela, para saber se há o que gravar.</summary>
    private string Foto() => JsonSerializer.Serialize(new { o = _o, i = _items });

    private string _baseSalva = "";

    private bool TemAlteracao => Foto() != _baseSalva;

    /// <summary>Os itens que estão gravados: o que sair da lista é apagado ao salvar.</summary>
    private HashSet<string> _idsGravados = new();

    private string? _msg;
    private bool _wordGerado;

    /// <summary>
    /// Grava a proposta como está — rascunho incompleto também: as pendências
    /// ficam no painel e não impedem de salvar. Só não grava sem cliente ou
    /// com um número e revisão que já são de OUTRA proposta.
    /// </summary>
    private bool Salvar(bool explicito)
    {
        _erro = null; _msg = null;
        if (string.IsNullOrWhiteSpace(_o.ClientName)) { _erro = "Informe o cliente para salvar a proposta."; return false; }
        _o.OfferCode = (_o.OfferCode ?? "").Trim();
        _o.Revisao = string.IsNullOrWhiteSpace(_o.Revisao) ? "0" : _o.Revisao.Trim();
        if (_o.OfferCode.Length == 0) { _erro = "Informe o número da proposta."; return false; }
        _anteriores = OffersRepo.All();
        if (_anteriores.Any(a => a.Id != _o.Id
                              && string.Equals(a.OfferCode, _o.OfferCode, StringComparison.OrdinalIgnoreCase)
                              && string.Equals(a.Revisao, _o.Revisao, StringComparison.OrdinalIgnoreCase)))
        {
            _erro = $"Já existe a proposta {_o.OfferCode} revisão {_o.Revisao}. Mude o número ou a revisão (Mais campos, na etapa 2).";
            return false;
        }

        AtualizarPrecos();
        foreach (var it in _items)
        {
            it.CustoImportacao = Imp(it) ? CustoImportadoDo(it) : null;
            // o item "Serviços e demais custos" já está na tabela de custo do pricing: não leva custo próprio
            it.CustoUnitario = it == _itemDemais ? null : CustoDoItem(it);
        }
        if (!_items.Any(i => i.Importado)) { _o.ImportacaoModo = ""; _o.TaxaCambio = 0; }

        // retrato do pricing no momento de salvar (consulta na tela da oferta), na moeda do custo
        var custos = Custos;
        var pr = PricingAtual(custos);
        var temPricing = pr.CustoTotal > 0 && pr.VendaLiquida > 0;
        _o.PricingCusto = temPricing ? Math.Round(pr.CustoTotal, 2) : 0;
        _o.PricingMarkup = temPricing ? Math.Round(pr.Markup, 6) : 0;
        _o.PricingVendaLiquida = temPricing ? Math.Round(pr.VendaLiquida, 2) : 0;
        _o.PricingMargem = temPricing ? Math.Round(pr.Margem, 6) : 0;
        _o.PricingPrecoImpostos = temPricing && pr.PrecoAoCliente is decimal pci ? Math.Round(pci, 2) : 0;
        if (!Textos.Igual(_o.FiancaSeguro, "Sim")) _o.TipoGarantia = "";
        if (!Cambio.Converte(_o)) { _o.CambioTaxa = 0; _o.CambioSeguranca = 0; }

        var now = DateTime.Now;
        var nova = _o.Id.Length == 0;
        if (nova)
        {
            _o.Id = Guid.NewGuid().ToString("N");
            _o.CreatedAt = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        // a primeira planta cadastrada e os equipamentos cadastrados, como antes, para as outras telas
        _o.ClientUnitId = _unitId;
        _o.EquipmentId = string.Join(";", _equipIds);
        if (string.IsNullOrWhiteSpace(_o.OfferDate)) _o.OfferDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // "Nossa referência" é o código da proposta
        _o.OurReference = _o.OfferCode;
        if (string.IsNullOrWhiteSpace(_o.Status)) _o.Status = "Rascunho";
        foreach (var it in _items) it.TotalPrice = it.Quantity * it.UnitPrice;
        _o.TotalAmount = Total;
        // a carteira soma em R$: nas BUs de fora, pela aba Conversão
        var taxasConv = Conversao.Ler();
        _o.TotalReais = Math.Round(Cambio.EmReais(_o, Total, taxasConv)
            ?? (taxasConv.ReaisPor(Cambio.MoedaDoPreco(_o)) is decimal rp ? Total * rp : 0m), 2);
        OffersRepo.Save(_o);

        var agora = new HashSet<string>();
        for (var n = 0; n < _items.Count; n++)
        {
            var it = _items[n];
            if (it.Id.Length == 0) it.Id = Guid.NewGuid().ToString("N");
            it.OfferId = _o.Id;
            it.Ordem = n + 1;
            OffersRepo.SaveItem(it);
            agora.Add(it.Id);
        }
        var apagados = _idsGravados.Where(id => !agora.Contains(id)).ToList();
        foreach (var id in apagados) OffersRepo.DeleteItem(id);
        _idsGravados = agora;
        GravarReferenciasDeCusto(apagados);
        GravarListas();

        _baseSalva = Foto();
        _anteriores = OffersRepo.All();
        if (explicito) _msg = $"Salvo às {now:HH:mm}.";
        if (nova) _ = TrocarUrl();
        return true;
    }

    /// <summary>A barra de endereço passa a apontar para a proposta gravada (F5 reabre a mesma), sem recarregar a tela.</summary>
    private async Task TrocarUrl()
    {
        try { await JS.InvokeVoidAsync("appTrocarUrl", $"/gerar-proposta/{_o.Id}"); }
        catch (Exception) { /* sem JS a tela funciona igual */ }
    }

    /// <summary>Ctrl+S, de qualquer etapa (appAtalhos no app.js).</summary>
    [JSInvokable]
    public Task SalvarPeloTeclado() => InvokeAsync(() =>
    {
        Salvar(explicito: true);
        StateHasChanged();
    });

    /// <summary>Uma proposta nova, já com os padrões da última proposta de quem está montando.</summary>
    private void NovaComPadroes()
    {
        _o = new CommercialOffer
        {
            Status = "Rascunho",
            IntroText = OfferDefaults.Intro, TaxText = OfferDefaults.Tax, PaymentTerms = OfferDefaults.Payment,
            DeliveryTime = OfferDefaults.DeliveryTime, DeliveryConditions = OfferDefaults.DeliveryConditions,
            ValidityText = OfferDefaults.Validity, Observations = OfferDefaults.Observations,
            SgiText = OfferDefaults.Sgi, TermsText = OfferDefaults.Terms,
        };
        _items = new();
        _idsGravados = new();
        _bomMarcados.Clear(); _refs.Clear(); _bomBusca = "";
        _buscandoEquip = false; _buscaEquip = ""; _novoItem = NovoItemVazio(); _autoCidade = "";
        _itemDemais = null; _taxaDoDia = false; _wordGerado = false; _erro = null; _msg = null;
        _anteriores = OffersRepo.All();

        _o.PreparedBy = _usuario;
        _o.OfferDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _o.OfferCode = NumeroSugerido();
        _o.Revisao = "0";
        _o.Ano = _autoAno = DateTime.Today.Year.ToString(CultureInfo.InvariantCulture);
        // padrões do cabeçalho do pricing para o AFM (o resto fica "preencher")
        _o.SegmentoMargem = "AFM"; _o.Destino = "Nacional"; _o.Reforma = "Não"; _o.NcmReducaoIcms = "Não";
        _o.FiancaSeguro = "Não"; _o.ClienteCom = "Sem benefício"; _o.ContemProdutoIc = "Não";
        _o.Rep1 = "-"; _o.Rep2 = "-";

        // o que a pessoa repete em toda proposta vem da última dela (o axiaisvent
        // faz o mesmo com preparada por, BU, validade e prazo)
        var ultima = UltimaDoUsuario();
        _padroesDe = ultima?.OfferCode;
        if (ultima is not null)
        {
            string Ou(string valor, string padrao) => string.IsNullOrWhiteSpace(valor) ? padrao : valor;
            _o.Bu = Ou(ultima.Bu, _o.Bu);
            _o.SegmentoMargem = Ou(ultima.SegmentoMargem, _o.SegmentoMargem);
            _o.VendaPara = Ou(ultima.VendaPara, _o.VendaPara);
            _o.NcmReducaoIcms = Ou(ultima.NcmReducaoIcms, _o.NcmReducaoIcms);
            _o.ContemProdutoIc = Ou(ultima.ContemProdutoIc, _o.ContemProdutoIc);
            _o.Categories = Ou(ultima.Categories, _o.Categories);
            _o.Product = ListasDoPricing.ProdutosDa(_o.Categories).Contains(ultima.Product) ? ultima.Product : "";
            _o.Portal = Ou(ultima.Portal, _o.Portal);
            _o.PaymentTerms = Ou(ultima.PaymentTerms, _o.PaymentTerms);
            _o.ValidityText = Ou(ultima.ValidityText, _o.ValidityText);
            _o.DeliveryTime = Ou(ultima.DeliveryTime, _o.DeliveryTime);
            _o.Incoterm = Ou(ultima.Incoterm, _o.Incoterm);
            // o local de entrega escrito à mão (incluído pelo "+") vem com o texto dele
            if (_o.Incoterm == ListasDaProposta.Livre) _o.IncotermLocal = ultima.IncotermLocal;
            _o.IntroText = Ou(ultima.IntroText, _o.IntroText);
            _o.Observations = Ou(ultima.Observations, _o.Observations);
            _o.TermsText = Ou(ultima.TermsText, _o.TermsText);
        }
        _autoRisco = CalculoPricing.RiscoPadraoDo(_o.SegmentoMargem, _o.Product);
        _autoMargem = ListasDoPricing.MargemPadraoDo(_o.SegmentoMargem);
        _o.RiscoAdicional = _autoRisco; _o.MargemAlvo = _autoMargem;
        _autoUf = ""; _autoMercado = "";

        _baseSalva = Foto();
        _origem = "nova";
        ZerarCronometro();
    }

    /// <summary>A número da proposta de onde vieram os padrões (para a etapa Começar dizer).</summary>
    private string? _padroesDe;

    private CommercialOffer? UltimaDoUsuario() => _anteriores
        .Where(a => _usuario.Length > 0 && Textos.Igual(a.PreparedBy, _usuario))
        .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.OfferCode).ThenByDescending(a => NumeroDaRevisao(a.Revisao))
        .FirstOrDefault()
        ?? _anteriores.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.OfferCode).ThenByDescending(a => NumeroDaRevisao(a.Revisao)).FirstOrDefault();

    /// <summary>Abre uma proposta gravada para continuar ou corrigir.</summary>
    private void Abrir(string id)
    {
        var o = OffersRepo.Get(id);
        if (o is null) { _erro = "Essa proposta não existe mais."; return; }
        CarregarDe(o, OffersRepo.ItemsFor(id), manterId: true);
        _origem = "aberta";
        ZerarCronometro();
        IrParaEtapa(2);
    }

    private void CarregarDe(CommercialOffer o, List<CommercialOfferItem> itens, bool manterId)
    {
        _o = Clonar(o);
        _items = itens.Select(Clonar).ToList();
        if (!manterId)
        {
            _o.Id = "";
            foreach (var it in _items) { it.Id = ""; it.OfferId = ""; }
        }
        _idsGravados = manterId ? _items.Select(i => i.Id).ToHashSet() : new();
        // proposta de antes das plantas: as listas saem da unidade e dos equipamentos gravados
        PlantasDaProposta.Completar(_o, _items, _units, _equipamentos, _base);
        CompletarDaFicha();
        // a cópia e a revisão guardam as mesmas chaves de planta e equipamento: os itens continuam apontando certo
        _bomMarcados.Clear(); _refs.Clear(); _bomBusca = "";
        _buscandoEquip = false; _buscaEquip = ""; _novoItem = NovoItemVazio();
        _autoCidade = PlantasDaProposta.CidadesDaCapa(_o);
        _itemDemais = _items.FirstOrDefault(i => i.PartNumber.Length == 0 && i.EquipmentId.Length == 0
                                                 && i.Description.StartsWith("Serviços", StringComparison.Ordinal));
        _taxaDoDia = false; _wordGerado = false; _erro = null; _msg = null;
        _autoRisco = CalculoPricing.RiscoPadraoDo(_o.SegmentoMargem, _o.Product);
        _autoMargem = ListasDoPricing.MargemPadraoDo(_o.SegmentoMargem);
        _autoUf = ""; _autoMercado = ""; _autoAno = _o.Ano;
        AtualizarPrecos();
        _baseSalva = manterId ? Foto() : "";
    }

    private static int NumeroDaRevisao(string? r) => int.TryParse(r, out var n) ? n : 0;

    private static T Clonar<T>(T valor) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(valor))!;

    /// <summary>Uma proposta nova a partir de uma gravada: cliente, condições, itens e preços copiados.</summary>
    private void CopiarComoNova(string id)
    {
        var o = OffersRepo.Get(id);
        if (o is null) return;
        CarregarDe(o, OffersRepo.ItemsFor(id), manterId: false);
        _o.OfferCode = NumeroSugerido();
        _o.Revisao = "0";
        ComoNovaDoDia();
        _origem = "copia";
        ZerarCronometro();
        _msg = $"Cópia da {o.OfferCode}: confira o cliente, as condições e os preços.";
        IrParaEtapa(2);
    }

    private void ComoNovaDoDia()
    {
        _o.Status = "Rascunho";
        _o.SuaReferencia = "";
        _o.OurReference = "";
        _o.PreparedBy = _usuario.Length > 0 ? _usuario : _o.PreparedBy;
        _o.OfferDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _o.Ano = _autoAno = DateTime.Today.Year.ToString(CultureInfo.InvariantCulture);
        _o.CreatedAt = "";
    }

    /// <summary>A próxima revisão da proposta aberta: mesmo número, a anterior fica como o cliente recebeu.</summary>
    private void NovaRevisao()
    {
        if (_o.Id.Length == 0) return;
        PedirConfirmacao(() =>
        {
            var o = OffersRepo.Get(_o.Id);
            if (o is null) return;
            CarregarDe(o, OffersRepo.ItemsFor(o.Id), manterId: false);
            _o.Revisao = ProximaRevisao(o.OfferCode);
            var sua = o.SuaReferencia;
            ComoNovaDoDia();
            _o.SuaReferencia = sua;
            _origem = "revisao";
            ZerarCronometro();
            _msg = $"Revisão {_o.Revisao} da {_o.OfferCode}: o que mudar vai para ela; a revisão {o.Revisao} fica como está.";
            IrParaEtapa(2);
        });
    }

    private string ProximaRevisao(string numero)
    {
        var maior = _anteriores.Where(a => string.Equals(a.OfferCode, numero, StringComparison.OrdinalIgnoreCase))
            .Select(a => int.TryParse(a.Revisao, out var r) ? r : 0).DefaultIfEmpty(0).Max();
        return (maior + 1).ToString(CultureInfo.InvariantCulture);
    }

    private void MarcarEnviada()
    {
        _o.Status = "Enviada";
        if (Salvar(explicito: false)) _msg = "Marcada como enviada ao cliente.";
    }

    // ---- a etapa Começar ----

    private string _buscaGravadas = "";

    private IEnumerable<CommercialOffer> GravadasDaBusca()
    {
        var s = Textos.Simples(_buscaGravadas);
        return _anteriores
            .Where(a => s.Length == 0 || Textos.Simples($"{a.OfferCode} {a.ClientName} {a.Project} {a.SuaReferencia}").Contains(s))
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.OfferCode).ThenByDescending(a => NumeroDaRevisao(a.Revisao))
            .Take(14);
    }

    private void ComecarPeloCliente(ChangeEventArgs e)
    {
        var id = e.Value?.ToString() ?? "";
        if (id.Length == 0) return;
        PedirConfirmacao(() =>
        {
            if (_o.Id.Length > 0 || TemAlteracao) NovaComPadroes();
            AdicionarPlantaDaBase(id);
            IrParaEtapa(2);
        });
    }

    private void ComecarEmBranco() => PedirConfirmacao(() =>
    {
        if (_o.Id.Length > 0 || TemAlteracao) NovaComPadroes();
        IrParaEtapa(2);
    });

    private void NovaEmBranco() => PedirConfirmacao(() =>
    {
        NovaComPadroes();
        IrParaEtapa(1);
    });

    private void AbrirGravada(string id) => PedirConfirmacao(() => Abrir(id));

    private void CopiarGravada(string id) => PedirConfirmacao(() => CopiarComoNova(id));

    private void IrParaPropostas() => PedirConfirmacao(() => Nav.NavigateTo("/ofertas"));

    // ---- lembrança do cliente: o contato e as condições da última proposta dele ----

    private string _lembrancaUsada = "";

    private CommercialOffer? UltimaDoCliente()
    {
        var unidades = PlantasDaProposta.Unidades(_o);
        if (unidades.Count == 0 && string.IsNullOrWhiteSpace(_o.ClientName)) return null;
        return _anteriores
            .Where(a => a.Id != _o.Id && (unidades.Count > 0
                ? unidades.Contains(a.ClientUnitId) || a.Plantas.Any(p => unidades.Contains(p.UnitId))
                : Textos.Igual(a.ClientName, _o.ClientName)))
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.OfferCode).ThenByDescending(a => NumeroDaRevisao(a.Revisao))
            .FirstOrDefault();
    }

    private void UsarDaUltima(CommercialOffer u)
    {
        string Ou(string valor, string atual) => string.IsNullOrWhiteSpace(valor) ? atual : valor;
        _o.AttentionTo = Ou(u.AttentionTo, _o.AttentionTo);
        _o.ContatoEmail = Ou(u.ContatoEmail, _o.ContatoEmail);
        _o.ContatoTelefone = Ou(u.ContatoTelefone, _o.ContatoTelefone);
        if (_o.Plantas.Count == 0 && u.Plantas.Count > 0)
        {
            _o.Plantas.AddRange(u.Plantas.Select(p => new PlantaDaProposta
                { Id = PlantasDaProposta.NovoId("p"), UnitId = p.UnitId, Nome = p.Nome, Cidade = p.Cidade, Estado = p.Estado }));
            PlantasMudaram();
        }
        _o.City = Ou(u.City, _o.City);
        _o.Destino = Ou(u.Destino, _o.Destino);
        _o.Estado = ListasDoPricing.EstadosDo(_o.Destino).Contains(u.Estado) ? u.Estado : _o.Estado;
        _o.VendaPara = Ou(u.VendaPara, _o.VendaPara);
        _o.ClienteCom = Ou(u.ClienteCom, _o.ClienteCom);
        _o.Portal = Ou(u.Portal, _o.Portal);
        _o.Rep1 = Ou(u.Rep1, _o.Rep1);
        _o.Rep2 = Ou(u.Rep2, _o.Rep2);
        _o.Vendedor = Ou(u.Vendedor, _o.Vendedor);
        _o.Vendedor2 = Ou(u.Vendedor2, _o.Vendedor2);
        _o.Idioma = Ou(u.Idioma, _o.Idioma);
        _o.MarketSegments = Ou(u.MarketSegments, _o.MarketSegments);
        _o.PaymentTerms = Ou(u.PaymentTerms, _o.PaymentTerms);
        _o.Incoterm = Ou(u.Incoterm, _o.Incoterm);
        _o.IncotermLocal = Ou(u.IncotermLocal, _o.IncotermLocal);
        if (Moedas.Ler(u.MoedaVenda) != Cambio.MoedaDoPreco(_o)) EscolherMoedaVenda(Moedas.Ler(u.MoedaVenda));
        _lembrancaUsada = u.Id;
        _msg = $"Contato e condições da {u.OfferCode} trazidos — confira.";
    }

    // ---- guarda de alteração não salva ----

    private Action? _depoisDeConfirmar;

    private void PedirConfirmacao(Action depois)
    {
        if (TemAlteracao && !string.IsNullOrWhiteSpace(_o.ClientName)) _depoisDeConfirmar = depois;
        else depois();
    }

    private void GuardaSalvar()
    {
        var depois = _depoisDeConfirmar;
        _depoisDeConfirmar = null;
        if (Salvar(explicito: true)) depois?.Invoke();
    }

    private void GuardaDescartar()
    {
        var depois = _depoisDeConfirmar;
        _depoisDeConfirmar = null;
        _baseSalva = Foto();
        depois?.Invoke();
    }

    // =====================================================================
    // documentos
    // =====================================================================

    private bool _gerando;

    /// <summary>Salva e baixa a proposta comercial em Word (o mesmo gerador do detalhe da oferta).</summary>
    private async Task SalvarEGerarWord()
    {
        if (_gerando) return;
        if (Cambio.BloqueioDosDocumentos(_o) is { } bloqueio) { _erro = bloqueio; return; }
        if (_items.Count == 0) { _erro = "Inclua ao menos um item: a proposta comercial é a lista deles."; return; }
        if (!Salvar(explicito: false)) return;
        _gerando = true;
        try
        {
            var modelo = Path.Combine(Ambiente.ContentRootPath, PropostaWord.CaminhoDoModelo);
            if (!File.Exists(modelo)) { _erro = $"O modelo não está em {PropostaWord.CaminhoDoModelo}."; return; }
            var dados = PropostaWord.Dados(_o, _items, _units, Vendedores.All(), _equipamentos, _base, _bom, _vendas)
                with { Faturamento = Bancos.ParaProposta(_o) };
            var bytes = PropostaWord.Gerar(dados, modelo);
            await JS.InvokeVoidAsync("appDownload", PropostaWord.NomeDoArquivo(_o),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Convert.ToBase64String(bytes));
            RegistrarTempo("word");
            _wordGerado = true;
            _msg = $"Proposta {PropostaWord.Referencia(_o)} salva e baixada em Word.";
        }
        catch (Exception ex)
        {
            _erro = "Não deu para gerar a proposta em Word: " + ex.Message;
        }
        finally { _gerando = false; }
    }

    /// <summary>Salva e baixa a proposta em PDF: o mesmo Word, convertido (Word do computador ou LibreOffice).</summary>
    private async Task SalvarEGerarPdf()
    {
        if (_gerando) return;
        if (Cambio.BloqueioDosDocumentos(_o) is { } bloqueio) { _erro = bloqueio; return; }
        if (_items.Count == 0) { _erro = "Inclua ao menos um item: a proposta comercial é a lista deles."; return; }
        if (!Salvar(explicito: false)) return;
        _gerando = true; _erro = null;
        try
        {
            if (WordEmBytes() is not { } word) { _erro = $"O modelo não está em {PropostaWord.CaminhoDoModelo}."; return; }
            var pdf = await Task.Run(() => PdfDaProposta.Converter(word));
            await JS.InvokeVoidAsync("appDownload", PropostaWord.NomeDoArquivo(_o, "pdf"), "application/pdf", Convert.ToBase64String(pdf));
            RegistrarTempo("word");
            _wordGerado = true;
            _msg = $"Proposta {PropostaWord.Referencia(_o)} salva e baixada em PDF.";
        }
        catch (Exception ex)
        {
            _erro = "Não deu para gerar a proposta em PDF: " + ex.Message;
        }
        finally { _gerando = false; }
    }

    // =====================================================================
    // cronômetro (o do axiaisvent): quanto tempo a proposta leva, e em que etapa
    // =====================================================================

    /// <summary>Uma pausa maior que isto (café, telefone) não conta como trabalho na proposta.</summary>
    private const double PausaQueNaoConta = 180;

    /// <summary>Desenhos mais próximos que isto são a mesma ação.</summary>
    private const double MesmaInteracao = 0.4;

    private DateTime _cronInicio = DateTime.Now;
    private DateTime? _cronUltimo;
    private double _cronSegundos;
    private int _cronInteracoes;
    private Dictionary<string, double> _segundosPorEtapa = new();
    private string _etapaDoUltimo = "";

    /// <summary>De onde a proposta partiu: "nova", "copia", "aberta" ou "revisao".</summary>
    private string _origem = "nova";

    private string _usuario = "";

    private string TempoNaTela
    {
        get
        {
            var minutos = (int)Math.Round(_cronSegundos / 60);
            return minutos < 1 ? "menos de 1 min" : $"{minutos} min";
        }
    }

    /// <summary>Chamado a cada desenho: o tempo desde o anterior vai para a etapa que estava na tela.</summary>
    private void TickCronometro()
    {
        var agora = DateTime.Now;
        if (_cronUltimo is { } ultimo)
        {
            var passou = (agora - ultimo).TotalSeconds;
            if (passou < PausaQueNaoConta)
            {
                _cronSegundos += passou;
                if (_etapaDoUltimo.Length > 0)
                    _segundosPorEtapa[_etapaDoUltimo] = _segundosPorEtapa.GetValueOrDefault(_etapaDoUltimo) + passou;
            }
            if (passou >= MesmaInteracao) _cronInteracoes++;
        }
        else _cronInteracoes++;
        _cronUltimo = agora;
        _etapaDoUltimo = _etapa.ToString(CultureInfo.InvariantCulture);
    }

    private void ZerarCronometro()
    {
        _cronInicio = DateTime.Now;
        _cronUltimo = null;
        _cronSegundos = 0;
        _cronInteracoes = 0;
        _segundosPorEtapa = new();
        _etapaDoUltimo = "";
    }

    private void RegistrarTempo(string evento)
    {
        try
        {
            Tempos.Registrar(new TempoProposta
            {
                PropostaId = _o.Id,
                Numero = _o.OfferCode,
                Revisao = _o.Revisao.Trim(),
                Usuario = _usuario,
                Inicio = _cronInicio.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                Fim = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                SegundosAtivos = (int)Math.Round(_cronSegundos),
                Interacoes = _cronInteracoes,
                Evento = evento,
                PorEtapa = JsonSerializer.Serialize(_segundosPorEtapa.Where(e => e.Value >= 0.5)
                    .ToDictionary(e => e.Key, e => (int)Math.Round(e.Value))),
                Origem = _origem,
                Itens = _items.Count,
            });
        }
        catch (Exception)
        {
            // o cronômetro mede; nunca impede um documento de sair
        }
    }

    // =====================================================================
    // depois de desenhar: atalhos, rolagem, ir ao campo
    // =====================================================================

    private DotNetObjectReference<NovaOferta>? _ref;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _ref ??= DotNetObjectReference.Create(this);
                await JS.InvokeVoidAsync("appAtalhos.ligar", _ref);
            }
            catch (Exception) { /* sem JS, a tela funciona igual */ }
            return;
        }

        TickCronometro();
        try
        {
            if (_irAoCampo is { } alvo)
            {
                _irAoCampo = null;
                await JS.InvokeVoidAsync("appIrAoCampo", alvo);
            }
            else if (_rolarTopo)
            {
                _rolarTopo = false;
                await JS.InvokeVoidAsync("appTopo");
            }
        }
        catch (Exception) { /* idem */ }
    }

    public void Dispose()
    {
        try { _ = JS.InvokeVoidAsync("appAtalhos.desligar"); } catch (Exception) { }
        _ref?.Dispose();
    }

    private static string OuTraco(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s.Trim();

    /// <summary>"PN — descrição" (como o item entra na proposta) → "descrição".</summary>
    private static string DescricaoSemPn(CommercialOfferItem it)
    {
        var prefixo = it.PartNumber + " — ";
        return it.Description.StartsWith(prefixo, StringComparison.Ordinal) ? it.Description[prefixo.Length..] : it.Description;
    }

    private void EscolherUnidade() => IrPara(new Pendencia("", 2, "f-plantas"));

    /// <summary>
    /// Margem, risco ou negociação fora do padrão do segmento (o mesmo aviso do
    /// axiaisvent): a margem é a dos preços quando dá para medir, senão a do pricing.
    /// </summary>
    private bool PrecisaAprovacao
    {
        get
        {
            var margemPadrao = ListasDoPricing.MargemPadraoDo(_o.SegmentoMargem);
            var c = Custos;
            var margem = PelaMargem ? _o.MargemAlvo : PricingAtual(c).Margem;
            return margem < margemPadrao - 0.0005m
                || Math.Abs(_o.RiscoAdicional - CalculoPricing.RiscoPadraoDo(_o.SegmentoMargem, _o.Product)) > 0.00001m
                || Math.Abs(_o.MargemNegociacao - CalculoPricing.MargemNegociacaoPadrao) > 0.00001m;
        }
    }

    /// <summary>"Vale · Reposição AFM": o que a proposta é, embaixo do número.</summary>
    private string ClienteEProjeto => string.Join(" · ", new[] { _o.ClientName, _o.Project }.Where(s => !string.IsNullOrWhiteSpace(s)))
        is { Length: > 0 } t ? t : "escolha o cliente para começar";
}
