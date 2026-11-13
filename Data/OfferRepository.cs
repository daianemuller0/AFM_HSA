using System.Globalization;
using System.Text.Json;
using AfmHsa.Models;

namespace AfmHsa.Data;

public class OfferRepository
{
    private readonly ParquetStore _s;
    public OfferRepository(ParquetStore s) => _s = s;

    private static string D(decimal v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Lista gravada como JSON numa coluna; vazia ou ilegível = o padrão.</summary>
    private static T Json<T>(string texto, T padrao)
    {
        if (string.IsNullOrWhiteSpace(texto)) return padrao;
        try { return JsonSerializer.Deserialize<T>(texto) ?? padrao; }
        catch (JsonException) { return padrao; }
    }

    // ---- ofertas ---------------------------------------------------------
    public List<CommercialOffer> All() => _s.ReadLatest("commercialOffers",
        "id, offer_code, client_unit_id, equipment_id, client_name, attention_to, city, our_reference, project, " +
        "offer_date, prepared_by, intro_text, tax_text, payment_terms, delivery_time, delivery_conditions, " +
        "validity_text, observations, sgi_text, terms_text, total_amount, status, created_at, " +
        "pv, revisao, bu, ncm_reducao_icms, segmento_margem, venda_para, destino, estado, reforma, " +
        "prazo_entrega, cliente_com, fianca_seguro, portal, rep1, rep2, categories, product, market_segments, " +
        "importacao_modo, moeda, taxa_cambio, " +
        "ano, contem_produto_ic, tipo_garantia, fianca_eventos, risco_adicional, margem_negociacao, " +
        "pricing_modo, margem_alvo, preco_meta, " +
        "pricing_custo, pricing_markup, pricing_venda_liquida, pricing_margem, pricing_preco_impostos, " +
        "tipo_outras_garantias, outras_garantias, pricing_custos, moeda_venda, cambio_taxa, cambio_direcao, " +
        "cambio_seguranca, total_reais, contato_email, contato_telefone, sua_referencia, incoterm, incoterm_local, " +
        "plantas, equipamentos_proposta, impostos_no_preco, vendedor, idioma, frete_custo, numero_crm, moeda_custo, vendedor2, cambio_conferido, total_liquido, protected_margin, preco_meta_nivel",
        r => new CommercialOffer
        {
            Id = Fmt.S(r, 0), OfferCode = Fmt.S(r, 1), ClientUnitId = Fmt.S(r, 2), EquipmentId = Fmt.S(r, 3),
            ClientName = Fmt.S(r, 4), AttentionTo = Fmt.S(r, 5), City = Fmt.S(r, 6), OurReference = Fmt.S(r, 7),
            Project = Fmt.S(r, 8), OfferDate = Fmt.S(r, 9), PreparedBy = Fmt.S(r, 10),
            IntroText = Fmt.S(r, 11), TaxText = Fmt.S(r, 12), PaymentTerms = Fmt.S(r, 13),
            DeliveryTime = Fmt.S(r, 14), DeliveryConditions = Fmt.S(r, 15), ValidityText = Fmt.S(r, 16),
            Observations = Fmt.S(r, 17), SgiText = Fmt.S(r, 18), TermsText = Fmt.S(r, 19),
            TotalAmount = Fmt.Dec(Fmt.S(r, 20)), Status = Fmt.S(r, 21), CreatedAt = Fmt.S(r, 22),
            Pv = Fmt.S(r, 23), Revisao = Fmt.S(r, 24), Bu = Fmt.S(r, 25), NcmReducaoIcms = Fmt.S(r, 26),
            SegmentoMargem = Fmt.S(r, 27), VendaPara = Fmt.S(r, 28), Destino = Fmt.S(r, 29), Estado = Fmt.S(r, 30),
            Reforma = Fmt.S(r, 31), PrazoEntrega = Fmt.S(r, 32), ClienteCom = Fmt.S(r, 33), FiancaSeguro = Fmt.S(r, 34),
            Portal = Fmt.S(r, 35), Rep1 = Fmt.S(r, 36), Rep2 = Fmt.S(r, 37), Categories = Fmt.S(r, 38),
            Product = Fmt.S(r, 39), MarketSegments = Fmt.S(r, 40),
            ImportacaoModo = Fmt.S(r, 41), Moeda = Fmt.S(r, 42), TaxaCambio = Fmt.Dec(Fmt.S(r, 43)),
            // propostas gravadas antes do pricing não têm estes campos: ficam vazios/zero
            Ano = Fmt.S(r, 44), ContemProdutoIc = Fmt.S(r, 45),
            TipoGarantia = Fmt.S(r, 46), EventosFianca = Json(Fmt.S(r, 47), CalculoPricing.EventosPadrao()),
            RiscoAdicional = Fmt.Dec(Fmt.S(r, 48)), MargemNegociacao = Fmt.Dec(Fmt.S(r, 49)),
            PricingModo = Fmt.S(r, 50), MargemAlvo = Fmt.Dec(Fmt.S(r, 51)), PrecoMeta = Fmt.Dec(Fmt.S(r, 52)),
            PricingCusto = Fmt.Dec(Fmt.S(r, 53)), PricingMarkup = Fmt.Dec(Fmt.S(r, 54)),
            PricingVendaLiquida = Fmt.Dec(Fmt.S(r, 55)), PricingMargem = Fmt.Dec(Fmt.S(r, 56)),
            PricingPrecoImpostos = Fmt.Dec(Fmt.S(r, 57)),
            TipoOutrasGarantias = Fmt.S(r, 58),
            OutrasGarantias = Json(Fmt.S(r, 59), new CommercialOffer().OutrasGarantias),
            CustosPricing = Json(Fmt.S(r, 60), new List<CalculoPricing.Custo>()),
            MoedaVenda = Fmt.S(r, 61) is { Length: > 0 } mv ? mv : "BRL", CambioTaxa = Fmt.Dec(Fmt.S(r, 62)),
            CambioDirecao = Fmt.S(r, 63) is { Length: > 0 } cd ? cd : "VendaEmCusto",
            CambioSeguranca = Fmt.Dec(Fmt.S(r, 64)),
            // propostas de antes da moeda do preço: o total já era em R$
            TotalReais = Fmt.S(r, 65) is { Length: > 0 } tr ? Fmt.Dec(tr) : Fmt.Dec(Fmt.S(r, 20)),
            ContatoEmail = Fmt.S(r, 66), ContatoTelefone = Fmt.S(r, 67), SuaReferencia = Fmt.S(r, 68),
            Incoterm = Fmt.S(r, 69), IncotermLocal = Fmt.S(r, 70),
            // propostas de antes das plantas: as listas ficam vazias e são montadas da unidade e dos equipamentos (PlantasDaProposta.Completar)
            Plantas = Json(Fmt.S(r, 71), new List<PlantaDaProposta>()),
            EquipamentosDaProposta = Json(Fmt.S(r, 72), new List<EquipamentoDaProposta>()),
            ImpostosNoPreco = Fmt.S(r, 73) is { Length: > 0 } ip ? ip : "liquido",
            Vendedor = Fmt.S(r, 74), Idioma = Fmt.S(r, 75) is { Length: > 0 } id ? id : "pt", FreteCusto = Fmt.Dec(Fmt.S(r, 76)), NumeroCrm = Fmt.S(r, 77), MoedaCusto = Fmt.S(r, 78), Vendedor2 = Fmt.S(r, 79), CambioConferido = Fmt.S(r, 80), TotalLiquido = Fmt.Dec(Fmt.S(r, 81)),
            ProtectedMargin = Fmt.S(r, 82) is { Length: > 0 } pm ? Fmt.Dec(pm) : null,
            PrecoMetaNivel = Fmt.S(r, 83) is { Length: > 0 } pn ? pn : "liquido",
        }, orderBy: "created_at DESC");

    public CommercialOffer? Get(string id) => All().FirstOrDefault(o => o.Id == id);

    public void Save(CommercialOffer o) => _s.WriteRow("commercialOffers", new KeyValuePair<string, object?>[]
    {
        new("id", o.Id), new("offer_code", o.OfferCode), new("client_unit_id", o.ClientUnitId),
        new("equipment_id", o.EquipmentId), new("client_name", o.ClientName), new("attention_to", o.AttentionTo),
        new("city", o.City), new("our_reference", o.OurReference), new("project", o.Project),
        new("offer_date", o.OfferDate), new("prepared_by", o.PreparedBy), new("intro_text", o.IntroText),
        new("tax_text", o.TaxText), new("payment_terms", o.PaymentTerms), new("delivery_time", o.DeliveryTime),
        new("delivery_conditions", o.DeliveryConditions), new("validity_text", o.ValidityText),
        new("observations", o.Observations), new("sgi_text", o.SgiText), new("terms_text", o.TermsText),
        new("total_amount", D(o.TotalAmount)), new("status", o.Status), new("created_at", o.CreatedAt),
        new("pv", o.Pv), new("revisao", o.Revisao), new("bu", o.Bu), new("ncm_reducao_icms", o.NcmReducaoIcms),
        new("segmento_margem", o.SegmentoMargem), new("venda_para", o.VendaPara), new("destino", o.Destino),
        new("estado", o.Estado), new("reforma", o.Reforma), new("prazo_entrega", o.PrazoEntrega),
        new("cliente_com", o.ClienteCom), new("fianca_seguro", o.FiancaSeguro), new("portal", o.Portal),
        new("rep1", o.Rep1), new("rep2", o.Rep2), new("categories", o.Categories), new("product", o.Product),
        new("market_segments", o.MarketSegments),
        new("importacao_modo", o.ImportacaoModo), new("moeda", o.Moeda), new("taxa_cambio", D(o.TaxaCambio)),
        new("ano", o.Ano), new("contem_produto_ic", o.ContemProdutoIc),
        new("tipo_garantia", o.TipoGarantia), new("fianca_eventos", JsonSerializer.Serialize(o.EventosFianca)),
        new("tipo_outras_garantias", o.TipoOutrasGarantias),
        new("outras_garantias", JsonSerializer.Serialize(o.OutrasGarantias)),
        new("pricing_custos", JsonSerializer.Serialize(o.CustosPricing)),
        new("moeda_venda", o.MoedaVenda), new("cambio_taxa", D(o.CambioTaxa)),
        new("cambio_direcao", o.CambioDirecao), new("cambio_seguranca", D(o.CambioSeguranca)),
        new("total_reais", D(o.TotalReais)),
        new("contato_email", o.ContatoEmail), new("contato_telefone", o.ContatoTelefone),
        new("sua_referencia", o.SuaReferencia), new("incoterm", o.Incoterm), new("incoterm_local", o.IncotermLocal),
        new("plantas", JsonSerializer.Serialize(o.Plantas)),
        new("equipamentos_proposta", JsonSerializer.Serialize(o.EquipamentosDaProposta)),
        new("impostos_no_preco", o.ImpostosNoPreco),
        new("vendedor", o.Vendedor), new("idioma", o.Idioma), new("frete_custo", D(o.FreteCusto)), new("numero_crm", o.NumeroCrm), new("moeda_custo", o.MoedaCusto), new("vendedor2", o.Vendedor2), new("cambio_conferido", o.CambioConferido), new("total_liquido", D(o.TotalLiquido)), new("protected_margin", o.ProtectedMargin is decimal pm ? D(pm) : ""), new("preco_meta_nivel", o.PrecoMetaNivel),
        new("risco_adicional", D(o.RiscoAdicional)), new("margem_negociacao", D(o.MargemNegociacao)),
        new("pricing_modo", o.PricingModo), new("margem_alvo", D(o.MargemAlvo)), new("preco_meta", D(o.PrecoMeta)),
        new("pricing_custo", D(o.PricingCusto)), new("pricing_markup", D(o.PricingMarkup)),
        new("pricing_venda_liquida", D(o.PricingVendaLiquida)), new("pricing_margem", D(o.PricingMargem)),
        new("pricing_preco_impostos", D(o.PricingPrecoImpostos)),
    });

    public void Delete(string id) => _s.WriteRow("commercialOffers",
        new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);

    /// <summary>Apaga as propostas e os itens delas, num lote só (exclusão pelo administrador).</summary>
    public void ApagarPropostas(IReadOnlyCollection<string> ids)
    {
        if (ids.Count == 0) return;
        var conjunto = ids.ToHashSet();
        var itens = AllItems().Where(i => conjunto.Contains(i.OfferId)).Select(i => i.Id).ToList();
        IReadOnlyList<KeyValuePair<string, object?>> Linha(string id) => new KeyValuePair<string, object?>[] { new("id", id) };
        if (itens.Count > 0) _s.WriteBatch("commercialOfferItems", itens.Select(Linha).ToList(), deleted: true);
        _s.WriteBatch("commercialOffers", ids.Select(Linha).ToList(), deleted: true);
    }

    // ---- itens -----------------------------------------------------------
    public List<CommercialOfferItem> AllItems() => _s.ReadLatest("commercialOfferItems",
        "id, offer_id, description, quantity, unit_price, total_price, " +
        "equipment_id, part_number, nacionalizado, custo_referencia, custo_data, ultima_venda, ultima_venda_data, " +
        "importado, valor_moeda, custo_importacao, ordem, ncm, custo_manual, custo_unitario, prazo_entrega_dias, " +
        "modelo, serial_number, tag, preco_meta, custo_ta, peso_kg, base_custo, ta_manual",
        r => new CommercialOfferItem
        {
            Id = Fmt.S(r, 0), OfferId = Fmt.S(r, 1), Description = Fmt.S(r, 2),
            Quantity = Fmt.Int(Fmt.S(r, 3)), UnitPrice = Fmt.Dec(Fmt.S(r, 4)), TotalPrice = Fmt.Dec(Fmt.S(r, 5)),
            EquipmentId = Fmt.S(r, 6), PartNumber = Fmt.S(r, 7), Nacionalizado = Fmt.Bool(Fmt.S(r, 8)),
            CustoReferencia = DecOuNull(Fmt.S(r, 9)), CustoData = Fmt.S(r, 10),
            UltimaVenda = DecOuNull(Fmt.S(r, 11)), UltimaVendaData = Fmt.S(r, 12),
            Importado = Fmt.Bool(Fmt.S(r, 13)), ValorMoeda = Fmt.Dec(Fmt.S(r, 14)),
            CustoImportacao = DecOuNull(Fmt.S(r, 15)), Ordem = Fmt.Int(Fmt.S(r, 16)), Ncm = Fmt.S(r, 17),
            CustoManual = DecOuNull(Fmt.S(r, 18)), CustoUnitario = DecOuNull(Fmt.S(r, 19)), PrazoEntregaDias = Fmt.Int(Fmt.S(r, 20)),
            Modelo = Fmt.S(r, 21), SerialNumber = Fmt.S(r, 22), Tag = Fmt.S(r, 23),
            PrecoMeta = Fmt.Dec(Fmt.S(r, 24)), CustoTa = Fmt.Dec(Fmt.S(r, 25)), PesoKg = Fmt.Dec(Fmt.S(r, 26)), BaseCusto = Fmt.S(r, 27), TaManual = Fmt.S(r, 28) == "1",
        });

    /// <summary>Vazio continua vazio: "custo não disponível" não pode virar R$ 0,00.</summary>
    private static decimal? DecOuNull(string s) => string.IsNullOrWhiteSpace(s) ? null : Fmt.Dec(s);
    private static string DOuVazio(decimal? v) => v is decimal d ? D(d) : "";

    /// <summary>Tira o item da proposta (a tela por etapas regrava a proposta: o item removido sai daqui).</summary>
    public void DeleteItem(string id) => _s.WriteRow("commercialOfferItems",
        new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);

    public List<CommercialOfferItem> ItemsFor(string offerId) =>
        AllItems().Where(i => i.OfferId == offerId).OrderBy(i => i.Ordem).ToList();

    public void SaveItem(CommercialOfferItem it) => _s.WriteRow("commercialOfferItems",
        new KeyValuePair<string, object?>[]
        {
            new("id", it.Id), new("offer_id", it.OfferId), new("description", it.Description),
            new("quantity", it.Quantity.ToString()), new("unit_price", D(it.UnitPrice)),
            new("total_price", D(it.TotalPrice)),
            new("equipment_id", it.EquipmentId), new("part_number", it.PartNumber),
            new("nacionalizado", it.Nacionalizado ? "true" : "false"),
            new("custo_referencia", DOuVazio(it.CustoReferencia)), new("custo_data", it.CustoData),
            new("ultima_venda", DOuVazio(it.UltimaVenda)), new("ultima_venda_data", it.UltimaVendaData),
            new("importado", it.Importado ? "true" : "false"), new("valor_moeda", D(it.ValorMoeda)),
            new("custo_importacao", DOuVazio(it.CustoImportacao)), new("ordem", it.Ordem.ToString()),
            new("ncm", it.Ncm),
            new("custo_manual", DOuVazio(it.CustoManual)), new("custo_unitario", DOuVazio(it.CustoUnitario)),
            new("prazo_entrega_dias", it.PrazoEntregaDias.ToString()),
            new("modelo", it.Modelo), new("serial_number", it.SerialNumber), new("tag", it.Tag),
            new("preco_meta", D(it.PrecoMeta)), new("custo_ta", D(it.CustoTa)), new("peso_kg", D(it.PesoKg)), new("base_custo", it.BaseCusto), new("ta_manual", it.TaManual ? "1" : "0"),
        });
}
