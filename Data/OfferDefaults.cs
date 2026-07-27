namespace AfmHsa.Data;

// Textos padrão das condições comerciais da oferta (do app original,
// src/lib/offer/defaults.ts). Editáveis na tela.
public static class OfferDefaults
{
    public const string Intro =
        "Howden tem a satisfação de apresentar nossa oferta comercial conforme descrito neste documento.";

    public const string Tax =
        "Impostos ou retenções não incluídos, salvo quando expressamente indicado nesta oferta.";

    public const string Payment =
        "100% em até 30 dias da emissão da fatura, salvo negociação comercial específica.";

    public static readonly string[] PaymentOptions =
    {
        "100% antecipado",
        "100% em até 15 dias da emissão da fatura.",
        "100% em até 30 dias da emissão da fatura, salvo negociação comercial específica.",
        "50% no pedido e 50% na entrega.",
    };

    public const string DeliveryTime =
        "Prazo de entrega a ser confirmado após aceite formal do pedido de compra e esclarecimento de todos os dados técnicos e comerciais necessários.";

    public const string DeliveryConditions =
        "As condições de entrega serão definidas conforme negociação comercial e logística aplicável ao fornecimento.";

    public const string Validity = "A validade de nossa oferta é de 30 (trinta) dias.";

    public const string Observations =
        "1. Os preços indicados nesta oferta estão baseados nas condições econômicas da data de emissão e não incluem impostos, taxas, tributos, retenções ou encargos não expressamente indicados.\n" +
        "2. As condições técnicas e comerciais indicadas nesta oferta consideram somente as informações enviadas pelo cliente até a data de emissão da oferta.\n" +
        "3. Os valores apresentados são válidos para a quantidade total cotada. Em caso de alteração de quantidade, os valores poderão ser recalculados.\n" +
        "4. Solicitações adicionais, revisões ou alterações com impacto técnico ou comercial serão analisadas pela Howden.\n" +
        "5. A Howden não será, em nenhuma hipótese, responsável por perdas indiretas, lucros cessantes ou perdas de produtividade, conforme termos e condições aplicáveis.";

    public const string Sgi =
        "Howden é certificada no Sistema de Gestão Integrado de Qualidade, Meio Ambiente, Saúde e Segurança conforme normas aplicáveis (ISO 9001, ISO 14001 e ISO 45001).";

    public const string Terms =
        "Aplicam-se a esta oferta os Termos e Condições Gerais para a Venda de Bens e Serviços da Howden / Chart Industries.";
}
