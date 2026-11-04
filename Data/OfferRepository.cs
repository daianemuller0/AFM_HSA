using System.Globalization;
using AfmHsa.Models;

namespace AfmHsa.Data;

public class OfferRepository
{
    private readonly ParquetStore _s;
    public OfferRepository(ParquetStore s) => _s = s;

    private static string D(decimal v) => v.ToString(CultureInfo.InvariantCulture);

    // ---- ofertas ---------------------------------------------------------
    public List<CommercialOffer> All() => _s.ReadLatest("commercialOffers",
        "id, offer_code, client_unit_id, equipment_id, client_name, attention_to, city, our_reference, project, " +
        "offer_date, prepared_by, intro_text, tax_text, payment_terms, delivery_time, delivery_conditions, " +
        "validity_text, observations, sgi_text, terms_text, total_amount, status, created_at, " +
        "pv, revisao, bu, ncm_reducao_icms, segmento_margem, venda_para, destino, estado, reforma, " +
        "prazo_entrega, cliente_com, fianca_seguro, portal, rep1, rep2, categories, product, market_segments, " +
        "importacao_modo, moeda, taxa_cambio, " +
        "ano, contem_produto_ic, tipo_garantia, fianca_percentual, risco_adicional, margem_negociacao, " +
        "pricing_modo, margem_alvo, preco_meta, " +
        "pricing_custo, pricing_markup, pricing_venda_liquida, pricing_margem, pricing_preco_impostos",
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
            TipoGarantia = Fmt.S(r, 46), FiancaPercentual = Fmt.Dec(Fmt.S(r, 47)),
            RiscoAdicional = Fmt.Dec(Fmt.S(r, 48)), MargemNegociacao = Fmt.Dec(Fmt.S(r, 49)),
            PricingModo = Fmt.S(r, 50), MargemAlvo = Fmt.Dec(Fmt.S(r, 51)), PrecoMeta = Fmt.Dec(Fmt.S(r, 52)),
            PricingCusto = Fmt.Dec(Fmt.S(r, 53)), PricingMarkup = Fmt.Dec(Fmt.S(r, 54)),
            PricingVendaLiquida = Fmt.Dec(Fmt.S(r, 55)), PricingMargem = Fmt.Dec(Fmt.S(r, 56)),
            PricingPrecoImpostos = Fmt.Dec(Fmt.S(r, 57)),
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
        new("tipo_garantia", o.TipoGarantia), new("fianca_percentual", D(o.FiancaPercentual)),
        new("risco_adicional", D(o.RiscoAdicional)), new("margem_negociacao", D(o.MargemNegociacao)),
        new("pricing_modo", o.PricingModo), new("margem_alvo", D(o.MargemAlvo)), new("preco_meta", D(o.PrecoMeta)),
        new("pricing_custo", D(o.PricingCusto)), new("pricing_markup", D(o.PricingMarkup)),
        new("pricing_venda_liquida", D(o.PricingVendaLiquida)), new("pricing_margem", D(o.PricingMargem)),
        new("pricing_preco_impostos", D(o.PricingPrecoImpostos)),
    });

    public void Delete(string id) => _s.WriteRow("commercialOffers",
        new KeyValuePair<string, object?>[] { new("id", id) }, deleted: true);

    // ---- itens -----------------------------------------------------------
    public List<CommercialOfferItem> AllItems() => _s.ReadLatest("commercialOfferItems",
        "id, offer_id, description, quantity, unit_price, total_price, " +
        "equipment_id, part_number, nacionalizado, custo_referencia, custo_data, ultima_venda, ultima_venda_data, " +
        "importado, valor_moeda, custo_importacao, ordem",
        r => new CommercialOfferItem
        {
            Id = Fmt.S(r, 0), OfferId = Fmt.S(r, 1), Description = Fmt.S(r, 2),
            Quantity = Fmt.Int(Fmt.S(r, 3)), UnitPrice = Fmt.Dec(Fmt.S(r, 4)), TotalPrice = Fmt.Dec(Fmt.S(r, 5)),
            EquipmentId = Fmt.S(r, 6), PartNumber = Fmt.S(r, 7), Nacionalizado = Fmt.Bool(Fmt.S(r, 8)),
            CustoReferencia = DecOuNull(Fmt.S(r, 9)), CustoData = Fmt.S(r, 10),
            UltimaVenda = DecOuNull(Fmt.S(r, 11)), UltimaVendaData = Fmt.S(r, 12),
            Importado = Fmt.Bool(Fmt.S(r, 13)), ValorMoeda = Fmt.Dec(Fmt.S(r, 14)),
            CustoImportacao = DecOuNull(Fmt.S(r, 15)), Ordem = Fmt.Int(Fmt.S(r, 16)),
        });

    /// <summary>Vazio continua vazio: "custo não disponível" não pode virar R$ 0,00.</summary>
    private static decimal? DecOuNull(string s) => string.IsNullOrWhiteSpace(s) ? null : Fmt.Dec(s);
    private static string DOuVazio(decimal? v) => v is decimal d ? D(d) : "";

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
        });
}
