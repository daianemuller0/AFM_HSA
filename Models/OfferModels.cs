using AfmHsa.Data;

namespace AfmHsa.Models;

// Oferta comercial no template Howden/Chart (versão funcional da migração).
// Persistida em Parquet como as demais entidades.
public class CommercialOffer
{
    public string Id { get; set; } = "";
    public string OfferCode { get; set; } = "";
    public string ClientUnitId { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    // Cabeçalho
    public string ClientName { get; set; } = "";
    public string AttentionTo { get; set; } = "";
    public string City { get; set; } = "";
    public string OurReference { get; set; } = "";
    public string Project { get; set; } = "";
    public string OfferDate { get; set; } = "";
    public string PreparedBy { get; set; } = "";
    // Capa da proposta em Word (modelo da equipe): contato do cliente e a referência dele
    public string ContatoEmail { get; set; } = "";
    public string ContatoTelefone { get; set; } = "";
    public string SuaReferencia { get; set; } = "";
    /// <summary>Local de entrega: código de <see cref="ListasDaProposta.Incoterms"/> e o local, quando a linha pede.</summary>
    public string Incoterm { get; set; } = "EXW";
    public string IncotermLocal { get; set; } = "";

    /// <summary>
    /// As plantas que a proposta atende — da base (Clientes e Unidades) ou
    /// digitadas, uma ou várias. <see cref="ClientUnitId"/> fica com a primeira
    /// planta cadastrada (vendedor da unidade, última venda do cliente).
    /// </summary>
    public List<PlantaDaProposta> Plantas { get; set; } = new();

    /// <summary>
    /// Os equipamentos da proposta: instalados numa planta da proposta, de
    /// outra planta do cadastro ou não cadastrados. Os itens apontam para cá
    /// pelo <see cref="CommercialOfferItem.EquipmentId"/>. <see cref="EquipmentId"/>
    /// fica com os cadastrados, separados por ";".
    /// </summary>
    public List<EquipamentoDaProposta> EquipamentosDaProposta { get; set; } = new();

    /// <summary>
    /// Com que impostos o preço vai ao cliente (<see cref="PricingDaProposta.Niveis"/>):
    /// "liquido", "pis_cofins", "pis_cofins_icms" ou "todos". O preço dos itens
    /// e o total do Word saem nesse nível.
    /// </summary>
    public string ImpostosNoPreco { get; set; } = "liquido";

    /// <summary>O vendedor que assina a proposta (<see cref="ContatosHowden"/>): o contato da capa do Word.</summary>
    public string Vendedor { get; set; } = "";

    /// <summary>O segundo vendedor (opcional): o outro contato da capa, ao lado do primeiro.</summary>
    public string Vendedor2 { get; set; } = "";

    /// <summary>
    /// A taxa de câmbio que alguém confirmou como certa mesmo parecendo ao
    /// contrário (moedas, sentido e valor): mudou qualquer um, vale de novo o aviso.
    /// </summary>
    public string CambioConferido { get; set; } = "";
    /// <summary>O total líquido (sem impostos) na moeda da proposta; 0 = proposta gravada antes deste campo.</summary>
    public decimal TotalLiquido { get; set; }
    /// <summary>
    /// Protected Margin (F14:F33): a variação de cada linha de custo que não
    /// tem uma própria. null = proposta gravada antes dela (conta 0%).
    /// </summary>
    public decimal? ProtectedMargin { get; set; } = CalculoPricing.ProtectedMarginPadrao;

    /// <summary>Em que língua a proposta sai: "pt", "en" ou "es".</summary>
    public string Idioma { get; set; } = "pt";

    /// <summary>
    /// Frete da proposta (R$, total). Entra no custo dos itens, dividido pelo
    /// valor de cada um (custo unitário × quantidade).
    /// </summary>
    public decimal FreteCusto { get; set; }

    /// <summary>
    /// Campo antigo do Nº do CRM. O nº do CRM é o próprio código da proposta
    /// (<see cref="OfferCode"/>, a "nossa referência"): fica só para ler as gravadas.
    /// </summary>
    public string NumeroCrm { get; set; } = "";
    // Condições comerciais (editáveis)
    public string IntroText { get; set; } = "";
    public string TaxText { get; set; } = "";
    public string PaymentTerms { get; set; } = "";
    public string DeliveryTime { get; set; } = "";
    public string DeliveryConditions { get; set; } = "";
    public string ValidityText { get; set; } = "";
    public string Observations { get; set; } = "";
    public string SgiText { get; set; } = "";
    public string TermsText { get; set; } = "";
    // Cabeçalho da proposta (dados internos de controle comercial)
    public string Pv { get; set; } = "";
    public string Revisao { get; set; } = "0";
    public string Bu { get; set; } = "";
    public string NcmReducaoIcms { get; set; } = "";
    public string SegmentoMargem { get; set; } = "";
    public string VendaPara { get; set; } = "";
    public string Destino { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Reforma { get; set; } = "";
    public string PrazoEntrega { get; set; } = "";
    public string ClienteCom { get; set; } = "";
    public string FiancaSeguro { get; set; } = "";
    public string Portal { get; set; } = "";
    public string Rep1 { get; set; } = "";
    public string Rep2 { get; set; } = "";
    public string Categories { get; set; } = "";
    public string Product { get; set; } = "";
    public string MarketSegments { get; set; } = "";
    // Importação dos itens importados: "total" (uma importação com a soma) ou
    // "item" (uma importação para cada item), moeda e taxa cambial usadas.
    public string ImportacaoModo { get; set; } = "";
    public string Moeda { get; set; } = "EUR";
    public decimal TaxaCambio { get; set; }
    // Campos do cabeçalho do pricing que não estavam acima
    public string Ano { get; set; } = "";
    public string ContemProdutoIc { get; set; } = "";
    // Pricing (planilha R_HSAXYZ0000-0 — Data/CalculoPricing.cs). Percentuais em
    // fração: 0,52 = 52%.
    public decimal RiscoAdicional { get; set; } = CalculoPricing.RiscoPadrao;
    public decimal MargemNegociacao { get; set; } = CalculoPricing.MargemNegociacaoPadrao;
    /// <summary>Tipo de seguro garantia / carta fiança (R46), quando há fiança.</summary>
    public string TipoGarantia { get; set; } = "";
    /// <summary>Os 10 eventos de pagamento cobertos pela fiança (M49:O58).</summary>
    public List<CalculoPricing.EventoDePagamento> EventosFianca { get; set; } = CalculoPricing.EventosPadrao();
    /// <summary>Tipo das outras garantias (R61) e as linhas PB, PF, BD e OUTROS (M64:R67).</summary>
    public string TipoOutrasGarantias { get; set; } = "";
    public List<CalculoPricing.OutraGarantia> OutrasGarantias { get; set; } =
        CalculoPricing.TiposDeOutrasGarantias.Select(t => new CalculoPricing.OutraGarantia(t, 0m, 0)).ToList();
    /// <summary>
    /// A tabela CÁLCULO DE CUSTO (E14:F33): o que foi digitado em cada linha.
    /// PROD. COMPRADOS e PROD. IMPORTADOS ainda recebem, por cima, o custo dos
    /// itens da proposta (nacionalizados e importados).
    /// </summary>
    public List<CalculoPricing.Custo> CustosPricing { get; set; } = new();
    /// <summary>"margem" (calcula o preço pela margem alvo) ou "preco" (a margem sai do preço-meta).</summary>
    public string PricingModo { get; set; } = "margem";
    public decimal MargemAlvo { get; set; } = ListasDoPricing.MargemPadraoDo("AFM");
    /// <summary>O preço que o cliente aceita, no modo "preco", no nível de imposto <see cref="PrecoMetaNivel"/>.</summary>
    public decimal PrecoMeta { get; set; }
    /// <summary>
    /// "A partir do preço": com que impostos o preço digitado está (liquido,
    /// pis_cofins, pis_cofins_icms, todos — <see cref="PricingDaProposta.Niveis"/>).
    /// O pricing acha a venda líquida que chega nele e calcula os outros níveis.
    /// </summary>
    public string PrecoMetaNivel { get; set; } = "liquido";

    /// <summary>
    /// As importações da proposta, uma por origem (BU de fora): a moeda e a
    /// taxa de cada uma. A origem vazia é a importação antiga, de
    /// <see cref="Moeda"/> e <see cref="TaxaCambio"/>.
    /// </summary>
    public List<ImportacaoDaOrigem> Importacoes { get; set; } = new();

    /// <summary>As notas da proposta (o preço estourou, cuidado com…), para quem abrir depois.</summary>
    public List<NotaDaProposta> Notas { get; set; } = new();

    /// <summary>
    /// Embalagens, engenharia, exportação e outros: o padrão da aba Dados no
    /// dia em que a proposta nasceu e o valor trocado nela (vazio = proposta
    /// de antes destes custos).
    /// </summary>
    public List<CustoAdicional> CustosAdicionais { get; set; } = new();
    /// <summary>O custo puro dos itens (sem frete e TA) ao salvar: a base do "% do custo dos itens" fora da tela (Word, detalhe).</summary>
    public decimal BaseCustosAdicionais { get; set; }
    // Resultado do pricing no momento em que a proposta foi salva (para consulta).
    public decimal PricingCusto { get; set; }
    public decimal PricingMarkup { get; set; }
    public decimal PricingVendaLiquida { get; set; }
    public decimal PricingMargem { get; set; }
    public decimal PricingPrecoImpostos { get; set; }
    // Moeda do preço ao cliente (como no axiaisvent — Data/Cambio.cs): o custo e o
    // pricing ficam em R$; os preços dos itens e o total saem nesta moeda.
    public string MoedaVenda { get; set; } = "BRL";

    /// <summary>
    /// A moeda do custo nas BUs que compram de fora (HPU, HCHL): o preço de
    /// fora, o frete, o TA e o pricing ficam nela. Vazio = o padrão da BU.
    /// No Brasil (HSA-SP, HSA-ES) o custo é sempre em R$.
    /// </summary>
    public string MoedaCusto { get; set; } = "";
    /// <summary>A taxa digitada; o sentido vem de <see cref="CambioDirecao"/>.</summary>
    public decimal CambioTaxa { get; set; }
    /// <summary>"VendaEmCusto": 1 moeda do preço = X R$. "CustoEmVenda": 1 R$ = X da moeda do preço.</summary>
    public string CambioDirecao { get; set; } = "VendaEmCusto";
    /// <summary>% tirado da taxa ao converter (0 a 99).</summary>
    public decimal CambioSeguranca { get; set; }
    // Totais / controle
    /// <summary>Total da proposta na moeda do preço (<see cref="MoedaVenda"/>).</summary>
    public decimal TotalAmount { get; set; }
    /// <summary>O mesmo total em R$ (pela taxa efetiva), para somar propostas de moedas diferentes.</summary>
    public decimal TotalReais { get; set; }
    public string Status { get; set; } = "Rascunho";
    public string CreatedAt { get; set; } = "";
}

/// <summary>Uma planta da proposta: da base (<see cref="UnitId"/>) ou digitada (sem UnitId).</summary>
/// <summary>Uma importação da proposta: a origem (BU de fora) e a moeda e a taxa dela.</summary>
public class ImportacaoDaOrigem
{
    public string Origem { get; set; } = "";
    public string Moeda { get; set; } = "EUR";
    /// <summary>R$ por 1 da moeda.</summary>
    public decimal Taxa { get; set; }
}

/// <summary>Uma nota da proposta: informativa, de atenção ou crítica.</summary>
public class NotaDaProposta
{
    public const string Info = "info", Atencao = "atencao", Critica = "critica";
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Texto { get; set; } = "";
    public string Nivel { get; set; } = Info;
    public string Autor { get; set; } = "";
    /// <summary>yyyy-MM-dd HH:mm</summary>
    public string Data { get; set; } = "";
    public bool Resolvida { get; set; }
}

/// <summary>
/// Um custo adicional da proposta (embalagens, engenharia, exportação,
/// outros): o padrão (valor fixo em R$ ou % do custo dos itens) e o valor
/// digitado na proposta, que manda quando existe.
/// </summary>
public class CustoAdicional
{
    public const string Fixo = "valor", Percentual = "percentual";
    public string Codigo { get; set; } = "";
    public string Tipo { get; set; } = Fixo;
    public decimal Padrao { get; set; }
    /// <summary>O valor trocado na proposta (na moeda do custo); null = o padrão.</summary>
    public decimal? Valor { get; set; }
}

public class PlantaDaProposta
{
    /// <summary>Chave dentro da proposta ("p-1a2b3c4d"): os equipamentos apontam para ela.</summary>
    public string Id { get; set; } = "";
    /// <summary>A unidade cadastrada (ClientUnit.Id); vazio = planta não cadastrada.</summary>
    public string UnitId { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Cidade { get; set; } = "";
    public string Estado { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Cadastrada => UnitId.Length > 0;
}

/// <summary>
/// Um equipamento da proposta. O cadastrado guarda o <see cref="EquipmentId"/>
/// (e a BOM vem dele); o não cadastrado é só o que foi digitado. TAG, aplicação
/// e planta são da proposta: dá para corrigir sem mexer no cadastro.
/// </summary>
public class EquipamentoDaProposta
{
    /// <summary>A chave que os itens usam: o Equipment.Id do cadastrado, "x-…" do não cadastrado.</summary>
    public string Id { get; set; } = "";
    /// <summary>Equipment.Id; vazio = não cadastrado.</summary>
    public string EquipmentId { get; set; } = "";
    /// <summary>A planta da proposta (<see cref="PlantaDaProposta.Id"/>); vazio = sem planta.</summary>
    public string PlantaId { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Modelo { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Aplicacao { get; set; } = "";
    public string SerialNumber { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Cadastrado => EquipmentId.Length > 0;
}

// Item de uma oferta comercial.
public class CommercialOfferItem
{
    public string Id { get; set; } = "";
    public string OfferId { get; set; } = "";
    /// <summary>Posição do item na proposta (1, 2, 3…), para o documento sair na ordem montada.</summary>
    public int Ordem { get; set; }
    public string Description { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }

    // --- Origem do item e apoio à precificação (uso interno) ----------------
    // Ficam gravados como foram vistos ao montar a proposta, para auditoria.
    // NÃO aparecem no documento do cliente e nunca alteram o preço sozinhos.
    public string EquipmentId { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public bool Nacionalizado { get; set; }
    /// <summary>Custo de referência (Valor BR da lista de Nacionalização); null = não disponível.</summary>
    public decimal? CustoReferencia { get; set; }
    public string CustoData { get; set; } = "";
    /// <summary>Preço unitário da última venda do partnumber; null = sem venda.</summary>
    public decimal? UltimaVenda { get; set; }
    public string UltimaVendaData { get; set; } = "";

    /// <summary>NCM do item (lista do modelo de proposta, <see cref="ListasDaProposta.Ncms"/>); vazio = a definir.</summary>
    public string Ncm { get; set; } = "";

    /// <summary>
    /// Custo unitário digitado (R$) para o item que não é importado: vale no
    /// lugar da referência da Nacionalização (ou quando ela não existe). null = usa a referência.
    /// </summary>
    public decimal? CustoManual { get; set; }

    /// <summary>
    /// O custo unitário (R$) que foi para o pricing ao salvar — importação,
    /// digitado ou referência. É dele que sai o preço do item.
    /// </summary>
    public decimal? CustoUnitario { get; set; }

    /// <summary>Prazo de entrega do item, em dias; 0 = o prazo da proposta.</summary>
    public int PrazoEntregaDias { get; set; }

    // --- Do item no documento: vazio = o do equipamento da proposta ---------
    /// <summary>Modelo do compressor deste item, quando difere do equipamento (ou não há equipamento).</summary>
    public string Modelo { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string Tag { get; set; } = "";

    /// <summary>
    /// Preço meta do item (unitário, na moeda do preço): quando o pricing dá
    /// menos, entra no custo um TA (<see cref="CustoTa"/>) até o preço chegar nele.
    /// 0 = sem meta.
    /// </summary>
    public decimal PrecoMeta { get; set; }

    /// <summary>TA: o custo (R$, unitário) acrescentado para o preço chegar na meta. Calculado.</summary>
    public decimal CustoTa { get; set; }

    /// <summary>
    /// O TA foi digitado (e não calculado pelo preço meta): fica como está.
    /// Digitar o TA tira a meta; pôr uma meta volta o TA para o cálculo.
    /// </summary>
    public bool TaManual { get; set; }

    /// <summary>Peso unitário (kg). Não é mais usado (o frete se divide pelo valor); fica para as propostas já gravadas.</summary>
    public decimal PesoKg { get; set; }

    /// <summary>O custo é de cotação (fornecedor / intercompany) ou estimado: "cotação", "estimado" ou vazio.</summary>
    public string BaseCusto { get; set; } = "";

    /// <summary>Item importado: o custo vem da planilha de importação.</summary>
    public bool Importado { get; set; }
    /// <summary>Preço unitário cotado lá fora, na moeda da importação da origem dele.</summary>
    public decimal ValorMoeda { get; set; }
    /// <summary>A BU de fora de onde o item vem (ex.: Howden Burton Corblin (France)): cada origem é uma importação.</summary>
    public string OrigemImportacao { get; set; } = "";
    /// <summary>Custo unitário em R$ da importação (por item, ou rateio do total); null = não calculado.</summary>
    public decimal? CustoImportacao { get; set; }
}
