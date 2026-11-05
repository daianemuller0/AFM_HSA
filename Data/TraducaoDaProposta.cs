using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AfmHsa.Data;

/// <summary>
/// A proposta comercial em inglês e espanhol: todo o texto do modelo (que é
/// em português) com a tradução ao lado, num arquivo só — dá para conferir
/// sem ler o gerador, como os TextosDaProposta do axiaisvent.
///
/// O Word é preenchido em português, como sempre, e no fim cada parágrafo (ou
/// trecho de parágrafo, quando há campo no meio) passa por aqui: texto fixo do
/// modelo pela tabela <see cref="Fixos"/>; linha que o sistema monta com dado
/// (cliente, alíquota, local de entrega...) pelas <see cref="Regras"/>. O que
/// não está aqui sai como está — o nome do cliente, a descrição digitada.
///
/// <para>
/// <b>Atenção:</b> força maior, lucro cessante, notas e condições são texto
/// contratual. Onde o modelo espanhol da equipe (o do axiaisvent) já tinha o
/// mesmo texto, ele foi reaproveitado; o resto é tradução nossa e precisa do
/// aval de vocês antes de a primeira proposta em inglês ou espanhol sair.
/// </para>
/// </summary>
public static class TraducaoDaProposta
{
    /// <summary>
    /// Separa as partes de uma tradução para um parágrafo com formatação mista
    /// (o "Banco:" em negrito e o resto normal): uma parte por grupo de mesma
    /// formatação. Se o parágrafo não tiver esse número de grupos, tudo vai
    /// junto, na formatação do primeiro.
    /// </summary>
    public const char Parte = '‖';

    private sealed record Par(string En, string Es);

    // ================= texto fixo do modelo =================

    /// <summary>Português do modelo → inglês, espanhol. A comparação ignora maiúsculas, espaços e hífens.</summary>
    public static readonly (string Pt, string En, string Es)[] Fixos =
    {
        // ---------- capa, cabeçalho e rodapé ----------
        ("Proposta Comercial", "Commercial Proposal", "Oferta Comercial"),
        ("Proposta Técnica Comercial", "Technical and Commercial Proposal", "Oferta Técnica y Comercial"),
        // "Client:" e não "Customer:": no cabeçalho a coluna do rótulo é estreita e "Customer:" quebrava
        ("Cliente:", "Client:", "Cliente:"),
        ("Projeto:", "Project:", "Proyecto:"),
        ("Aos cuidados de:", "Attention:", "Al cuidado de:"),
        ("Nossa referência:", "Our reference:", "Nuestra referencia:"),
        ("Sua referência:", "Your reference:", "Su referencia:"),
        ("Cidade:", "City:", "Ciudad:"),
        ("Data:", "Date:", "Fecha:"),
        ("E-mail:", "E-mail:", "E-mail:"),
        ("Fone:", "Phone:", "Fono:"),
        ("Telefone:", "Phone:", "Fono:"),
        ("Preparada por:", "Prepared by:", "Preparada por:"),
        ("Contatos Howden", "Howden Contacts", "Contactos Howden"),
        ("Representante:", "Sales Agent:", "Sales Agent:"),
        ("Ref.:", "Ref.:", "Ref.:"),
        ("Sua Ref.:", "Your Ref.:", "Su Ref.:"),
        ("Nossa Ref.:", "Our Ref.:", "Nuestra Ref.:"),

        // ---------- índice e títulos ----------
        ("Índice", "Contents", "Índice"),
        ("Introdução", "Introduction", "Introducción"),
        ("Preço", "Price", "Precio"),
        ("Impostos Vigentes", "Applicable Taxes", "Impuestos Vigentes"),
        ("Condições De Pagamento", "Payment Terms", "Condiciones de Pago"),
        ("Prazo de Entrega", "Delivery Time", "Plazo de Entrega"),
        ("Local de Entrega (Incoterms 2010)", "Place of Delivery (Incoterms 2010)", "Lugar de Entrega (Incoterms 2010)"),
        ("Validade", "Validity", "Validez"),
        ("Dados Para Faturamento", "Invoicing Details", "Datos de Facturación"),
        ("Notas", "Notes", "Notas"),
        ("Sistema de Gestão", "Management System", "Sistema de Gestión"),
        ("Informações Adicionais", "Additional Information", "Información Adicional"),
        ("Anexos", "Attachments", "Anexos"),

        // ---------- introdução ----------
        ("Prezados Senhores,", "Dear Sirs,", "Estimados señores,"),
        ("A Howden tem mais de 160 anos de experiência em inovação, design e manufatura. Nosso vasto conhecimento não se limita apenas a nossos produtos, mas inclui também aplicações e soluções adjacentes que inclui nossos produtos. A Howden trabalha desde a concepção inicial do projeto até a partida e entrega do equipamento na planta há décadas, o que nos torna um fornecedor completo e um dos principais do mundo de ventiladores e compressores para Pelotização, Cimento, Papel & Celulose, Mineração, Fertilizantes, Etanol, Siderurgia, geração de Energia Elétrica, etc.",
         "Howden has more than 160 years of experience in innovation, design and manufacturing. Our extensive knowledge is not limited to our products, but also includes the applications and adjacent solutions that involve our products. For decades, Howden has worked from the initial conception of the project through to the start-up and handover of the equipment at the plant, which makes us a complete supplier and one of the world's leading suppliers of fans and compressors for Pelletising, Cement, Pulp & Paper, Mining, Fertilisers, Ethanol, Steel, Power Generation, etc.",
         "Howden tiene más de 160 años de experiencia en innovación, diseño y manufactura. Nuestro amplio conocimiento no se limita solo a nuestros productos, sino que incluye también las aplicaciones y soluciones adyacentes que involucran nuestros productos. Desde hace décadas, Howden trabaja desde la concepción inicial del proyecto hasta la puesta en marcha y entrega del equipo en planta, lo que nos convierte en un proveedor completo y uno de los principales del mundo de ventiladores y compresores para Peletización, Cemento, Papel y Celulosa, Minería, Fertilizantes, Etanol, Siderurgia, Generación de Energía Eléctrica, etc."),
        ("NOTA:", "NOTE:", "NOTA:"),
        // KYC: inglês e espanhol do modelo da equipe (axiaisvent)
        ("O presente orçamento ou a presente proposta estão sujeitos à conclusão satisfatória dos nossos procedimentos habituais de conhecimento do cliente e de conformidade (KYC). As condições contratuais finais serão posteriormente acordadas por escrito.",
         "This quotation or proposal is subject to the satisfactory completion of our usual customer identification and regulatory compliance (KYC) procedures. The final contractual terms will be agreed subsequently in writing.",
         "El presente presupuesto o la presente propuesta están sujetos a la finalización satisfactoria de nuestros procedimientos habituales de identificación del cliente y de cumplimiento normativo (KYC). Las condiciones contractuales definitivas se acordarán posteriormente por escrito."),

        // ---------- controle de revisões ----------
        ("CONTROLE DE REVISÕES:", "REVISION CONTROL:", "CONTROL DE REVISIONES:"),
        ("Exec", "By", "Ejec"),
        ("Exec.", "By", "Ejec."),
        ("Aprov.", "Appr.", "Aprob."),

        // ---------- tabela de preços ----------
        ("Item", "Item", "Ítem"),
        ("Descrição", "Description", "Descripción"),
        ("Qtd.", "Qty.", "Ctd."),
        ("Preço Unitário", "Unit Price", "Precio Unitario"),
        ("Preço Total", "Total Price", "Precio Total"),
        ("VALOR LÍQUIDO", "NET VALUE", "VALOR NETO"),
        ("VALOR COM PIS E COFINS", "VALUE INCLUDING PIS AND COFINS", "VALOR CON PIS Y COFINS"),
        ("VALOR COM PIS, COFINS E ICMS", "VALUE INCLUDING PIS, COFINS AND ICMS", "VALOR CON PIS, COFINS E ICMS"),
        ("VALOR COM PIS, COFINS, ICMS E IPI", "VALUE INCLUDING PIS, COFINS, ICMS AND IPI", "VALOR CON PIS, COFINS, ICMS E IPI"),
        ("REFERÊNCIAS:", "REFERENCES:", "REFERENCIAS:"),
        ("ESCOPO DE FORNECIMENTO:", "SCOPE OF SUPPLY:", "ALCANCE DEL SUMINISTRO:"),
        ("Pintura: Conforme projeto original", "Painting: as per original design", "Pintura: según el proyecto original"),
        ("Embalagem apropriada para transporte.", "Packaging suitable for transport.", "Embalaje apropiado para el transporte."),
        ("Balanceamento estático e dinâmico do conjunto rotor e eixo, conforme “ISO 21940 G 2,5 – Balanceamento de Corpos Rígidos em Rotação”;",
         "Static and dynamic balancing of the rotor and shaft assembly, according to “ISO 21940 G 2.5 – Balancing of Rigid Rotating Bodies”;",
         "Balanceo estático y dinámico del conjunto rotor y eje, según “ISO 21940 G 2,5 – Balanceo de Cuerpos Rígidos en Rotación”;"),
        ("Balanceamento estático e dinâmico do conjunto rotor e eixo, montados, conforme “ISO 21940 G 2,5 – Balanceamento de Corpos Rígidos em Rotação”;",
         "Static and dynamic balancing of the assembled rotor and shaft, according to “ISO 21940 G 2.5 – Balancing of Rigid Rotating Bodies”;",
         "Balanceo estático y dinámico del conjunto rotor y eje, montados, según “ISO 21940 G 2,5 – Balanceo de Cuerpos Rígidos en Rotación”;"),
        ("Teste não destrutivo no rotor (líquido penetrante, etc);", "Non-destructive testing of the rotor (dye penetrant, etc.);", "Ensayo no destructivo en el rotor (líquidos penetrantes, etc.);"),
        ("Prazo de entrega:", "Delivery time:", "Plazo de entrega:"),
        ("Dias", "Days", "Días"),
        ("(*) A descrição do produto deverá estar presente na Ordem de Compra.",
         "(*) The product description must appear on the Purchase Order.",
         "(*) La descripción del producto deberá estar en la Orden de Compra."),

        // ---------- notas comerciais ----------
        ("NOTAS COMERCIAIS:", "COMMERCIAL NOTES:", "NOTAS COMERCIALES:"),
        ("Excluso de nosso fornecimento qualquer item ou acessórios que não conste claramente em nossa proposta.",
         "Any item or accessory not clearly stated in our proposal is excluded from our supply.",
         "Queda excluido de nuestro suministro cualquier ítem o accesorio que no conste claramente en nuestra propuesta."),
        ("O preço de venda apresentado em nossa oferta está condicionado ao aceite das “Condições de Fornecimento” padrão Howden, anexa. Qualquer condição diferente está sujeita à renegociação comercial.",
         "The selling price presented in our proposal is conditional upon acceptance of Howden's standard “Conditions of Supply”, attached. Any different condition is subject to commercial renegotiation.",
         "El precio de venta presentado en nuestra oferta está condicionado a la aceptación de las “Condiciones de Suministro” estándar de Howden, adjuntas. Cualquier condición diferente está sujeta a renegociación comercial."),
        ("Exclusão de Lucros Cessantes. A Howden não será, em nenhuma hipótese, responsável por lucros cessantes e/ou danos indiretos de qualquer tipo incluindo, mas não se limitando a perda de negócio, lucro ou produtividade, conforme item 23 das condições de fornecimento.",
         "Exclusion of Loss of Profit. Howden shall in no event be liable for loss of profit and/or indirect damages of any kind, including but not limited to loss of business, profit or productivity, pursuant to item 23 of the conditions of supply.",
         "Exclusión del Lucro Cesante. Howden no será, en ningún caso, responsable por lucro cesante y/o daños indirectos de cualquier tipo, incluyendo, pero no limitándose a, pérdida de negocio, beneficio o productividad, según el ítem 23 de las condiciones de suministro."),
        // força maior: inglês e espanhol do modelo da equipe (axiaisvent)
        ("Força maior: Nenhuma das partes será considerada inadimplente ou em violação de suas obrigações nos termos do Contrato, na medida em que o cumprimento de tais obrigações seja impedido ou atrasado por quaisquer circunstâncias fora de seu controle razoável, incluindo, sem limitação: greves, bloqueios ou outras disputas trabalhistas, caso fortuito, guerra, tumulto, comoção civil, danos maliciosos, cumprimento de qualquer lei ou ordem governamental, regra, regulamento ou direção, embargos, sanções econômicas ou comerciais, incluindo quaisquer alterações a tais embargos e sanções econômicas e comerciais, avaria acidental de instalações ou máquinas, incêndio, inundação, tempestade, surto de doença ou epidemia e/ou quaisquer restrições de quarentena resultantes (\"Força Maior\").  Qualquer uma das partes terá o direito de rescindir o Contrato se a situação de Força Maior continuar, ou for óbvio que continuará, por mais de 180 (cento e oitenta) dias sem responsabilidade para a outra parte.  Além disso, se ambas as partes concordarem que desejam continuar o Contrato quando razoavelmente praticável, não obstante o período de 180 dias acima mencionado ter sido alcançado, as partes concordarão de boa fé em renegociar qualquer alteração necessária do Contrato para permitir que ele continue.",
         "Force majeure: neither party shall be considered in default or in breach of its obligations under the Contract to the extent that the performance of such obligations is prevented or delayed by circumstances beyond its reasonable control, including but not limited to: strikes, lockouts or other labour disputes, acts of God, war, riots, civil commotion, malicious damage, compliance with any law or governmental order, rule, regulation or direction, embargoes, economic or trade sanctions, including amendments to such embargoes and economic and trade sanctions, accidental breakdown of plant or machinery, fire, flood, storm, disease outbreaks or epidemics and/or any resulting quarantine restriction (“Force Majeure”). Either party shall have the right to terminate the Contract if the Force Majeure situation continues, or if it is evident that it will continue, for more than 180 (one hundred and eighty) days, without liability to the other party. Furthermore, if both parties agree that they wish to continue with the Contract as soon as reasonably possible, despite the 180-day period referred to above having been reached, the parties shall agree in good faith to renegotiate any necessary amendment to the Contract to allow it to continue.",
         "Fuerza mayor: Ninguna de las partes será considerada en incumplimiento o en violación de sus obligaciones según el Contrato, en la medida en que el cumplimiento de dichas obligaciones se vea impedido o retrasado por circunstancias fuera de su control razonable, incluyendo, entre otras: huelgas, bloqueos u otras disputas laborales, caso fortuito, guerra, disturbios, conmoción civil, daños maliciosos, cumplimiento de cualquier ley u orden gubernamental, regla, regulación o directiva, embargos, sanciones económicas o comerciales, incluidas las modificaciones a dichos embargos y sanciones económicas y comerciales, averías accidentales de instalaciones o maquinaria, incendios, inundaciones, tormentas, brotes de enfermedades o epidemias y/o cualquier restricción de cuarentena resultante (“Fuerza Mayor”). Cualquiera de las partes tendrá el derecho de rescindir el Contrato si la situación de Fuerza Mayor continúa, o si es evidente que continuará, durante más de 180 (ciento ochenta) días sin responsabilidad para la otra parte. Además, si ambas partes acuerdan que desean continuar con el Contrato cuando sea razonablemente posible, a pesar de que se haya alcanzado el período de 180 días mencionado anteriormente, las partes acordarán de buena fe renegociar cualquier modificación necesaria del Contrato para permitir que continúe."),

        // ---------- impostos ----------
        ("Venda para exportação: não incidem PIS, COFINS, ICMS e IPI.", "Export sale: PIS, COFINS, ICMS and IPI do not apply.", "Venta para exportación: no inciden PIS, COFINS, ICMS e IPI."),
        ("ICMS: a confirmar com o Financeiro (revenda ou cliente não-contribuinte);", "ICMS: to be confirmed with Finance (resale or non-taxpayer customer);", "ICMS: a confirmar con Finanzas (reventa o cliente no contribuyente);"),
        ("ICMS: (INDICAR ESTADO) não incluso", "ICMS: (STATE TO BE INDICATED) not included", "ICMS: (INDICAR ESTADO) no incluido"),
        ("IPI: não incide (Zona Franca de Manaus);", "IPI: not applicable (Manaus Free Trade Zone);", "IPI: no incide (Zona Franca de Manaus);"),
        // o "o" do "nº" é sobrescrito no modelo: são três partes
        ("Fundamento Legal :Decreto no 11.055/2022 de 28/04/2022.",
         "Legal Basis: Decree n‖o‖ 11.055/2022 of 28/04/2022.",
         "Fundamento Legal: Decreto n‖o‖ 11.055/2022 del 28/04/2022."),
        ("Nota sobre Legislação Tributária e Reforma Tributária:", "Note on Tax Legislation and Tax Reform:", "Nota sobre la Legislación Tributaria y la Reforma Tributaria:"),
        ("A presente proposta comercial foi elaborada com base na legislação tributária vigente até a data de sua emissão. Qualquer alteração decorrente da reforma tributária ou legislação vigente que venha a ser implementada poderá impactar os valores e condições aqui apresentados.",
         "This commercial proposal has been prepared on the basis of the tax legislation in force up to its date of issue. Any change resulting from the tax reform or from the legislation in force that may be implemented may affect the values and conditions presented herein.",
         "La presente propuesta comercial fue elaborada con base en la legislación tributaria vigente hasta la fecha de su emisión. Cualquier cambio derivado de la reforma tributaria o de la legislación vigente que llegue a implementarse podrá impactar los valores y condiciones aquí presentados."),
        ("Dessa forma, a Howden South America se reserva o direito de revisar e ajustar esta proposta conforme necessário, para refletir as mudanças na legislação tributária e seus respectivos impactos.",
         "Accordingly, Howden South America reserves the right to revise and adjust this proposal as necessary to reflect changes in the tax legislation and their respective impacts.",
         "De este modo, Howden South America se reserva el derecho de revisar y ajustar esta propuesta según sea necesario, para reflejar los cambios en la legislación tributaria y sus respectivos impactos."),
        ("Detalhamento para adição dos Impostos", "Breakdown for the Addition of Taxes", "Detalle para la Adición de Impuestos"),
        ("Valor Total Líquido:", "Total Net Value:", "Valor Total Neto:"),
        ("Valor Total c/ PIS e COFINS:", "Total Value incl. PIS and COFINS:", "Valor Total c/ PIS y COFINS:"),
        ("Valor Total c/ PIS, COFINS e ICMS Interestadual:", "Total Value incl. PIS, COFINS and Interstate ICMS:", "Valor Total c/ PIS, COFINS e ICMS Interestatal:"),
        ("Valor Total c/ PIS, COFINS, ICMS Interestadual e IPI:", "Total Value incl. PIS, COFINS, Interstate ICMS and IPI:", "Valor Total c/ PIS, COFINS, ICMS Interestatal e IPI:"),

        // ---------- pagamento, entrega e validade ----------
        ("100% antecipado", "100% in advance", "100% anticipado"),
        ("Prazos conforme mencionado a cada item tendo início após confirmação do pedido e todos os dados técnicos e comerciais esclarecidos.",
         "Delivery times as stated for each item, starting after confirmation of the order and clarification of all technical and commercial data.",
         "Plazos según lo indicado en cada ítem, contados a partir de la confirmación del pedido y de la aclaración de todos los datos técnicos y comerciales."),
        ("A COMBINAR", "TO BE AGREED", "A CONVENIR"),
        ("DESTINO (PAÍS DE DESTINO)", "DESTINATION (COUNTRY OF DESTINATION)", "DESTINO (PAÍS DE DESTINO)"),

        // ---------- notas ----------
        // a primeira nota tem a data (um campo) no meio: são dois trechos
        ("Os preços apresentados nesta proposta foram compostos de acordo com os custos vigentes no dia",
         "The prices presented in this proposal were composed according to the costs in effect on",
         "Los precios presentados en esta propuesta fueron compuestos de acuerdo con los costos vigentes el día"),
        ("e não preveem qualquer expectativa inflacionária. A HOWDEN reserva-se no direito de reavaliar e rediscutir com V.Sas.. as condições comerciais deste fornecimento caso haja uma comprovada elevação nos custos dos insumos e mão-de-obra que comprometam o seu equilíbrio econômico-financeiro.",
         "and do not provide for any inflationary expectation. HOWDEN reserves the right to reassess and renegotiate with you the commercial terms of this supply should there be a proven increase in the costs of materials and labour that compromises its economic and financial balance.",
         "y no prevén ninguna expectativa inflacionaria. HOWDEN se reserva el derecho de reevaluar y volver a discutir con ustedes las condiciones comerciales de este suministro en caso de un aumento comprobado en los costos de insumos y mano de obra que comprometa su equilibrio económico-financiero."),
        ("A HOWDEN SOUTH AMERICA utiliza as Normas Regulamentares brasileiras pertinentes aos produtos e/ou serviços ora ofertados, no entanto, Registros Regulamentares Internacionais aplicáveis no país de destino dos equipamentos e/ou serviços deverão ser enviados por V.Sas, durante as negociações, para que possamos fazer uma Análise Crítica dos mesmos e possível revisão das condições comerciais. A falta de resposta de V.Sas a esta solicitação será considerada como a não existência de tais regulamentos.",
         "HOWDEN SOUTH AMERICA applies the relevant Brazilian Regulatory Standards to the products and/or services offered herein; however, the International Regulatory Records applicable in the country of destination of the equipment and/or services must be sent by you during the negotiations, so that we can carry out a Critical Analysis of them and a possible revision of the commercial terms. Failure to respond to this request will be considered as the non-existence of such regulations.",
         "HOWDEN SOUTH AMERICA utiliza las Normas Reglamentarias brasileñas pertinentes a los productos y/o servicios ofertados; sin embargo, los Registros Reglamentarios Internacionales aplicables en el país de destino de los equipos y/o servicios deberán ser enviados por ustedes durante las negociaciones, para que podamos hacer un Análisis Crítico de los mismos y una posible revisión de las condiciones comerciales. La falta de respuesta de ustedes a esta solicitud será considerada como la no existencia de tales reglamentos."),
        ("As condições técnicas e comerciais desta proposta consideram apenas as revisões de especificações e normas técnicas de Clientes encaminhadas para elaboração desta proposta. Após o envio desta proposta, qualquer solicitação de revisão ou encaminhamento de especificações e normas técnicas pelo cliente, deverá ser novamente analisada pela Howden South America quanto a sua adequação para as condições técnicas e comerciais, inicialmente prevista nesta proposta.",
         "The technical and commercial terms of this proposal consider only the revisions of the Customer's specifications and technical standards sent for the preparation of this proposal. After this proposal has been sent, any request for revision or any submission of specifications and technical standards by the customer shall be analysed again by Howden South America as to its suitability for the technical and commercial terms initially provided for in this proposal.",
         "Las condiciones técnicas y comerciales de esta propuesta consideran solamente las revisiones de especificaciones y normas técnicas del Cliente enviadas para la elaboración de esta propuesta. Después del envío de esta propuesta, cualquier solicitud de revisión o envío de especificaciones y normas técnicas por parte del cliente deberá ser analizada nuevamente por Howden South America en cuanto a su adecuación a las condiciones técnicas y comerciales inicialmente previstas en esta propuesta."),
        ("Os valores aqui apresentados são válidos somente se adquiridos na quantidade total ofertada. Caso esta quantidade seja alterada, os valores citados deverão ser recalculados e apresentados em uma revisão de proposta.",
         "The values presented herein are valid only if the total quantity offered is purchased. Should this quantity change, the values stated shall be recalculated and presented in a revision of the proposal.",
         "Los valores aquí presentados son válidos solamente para la adquisición de la cantidad total ofertada. En caso de que esta cantidad sea modificada, los valores citados deberán ser recalculados y presentados en una revisión de la propuesta."),
        ("Revisões adicionais, comentários ou alterações solicitadas pelo cliente, que tenham com impacto direto ou mesmo que indireto, mas que afetem o prazo de entrega de documentação, deverão ser analisadas e seus impactos de prazo e custo serão informados pela Howden.",
         "Additional revisions, comments or changes requested by the customer that have a direct or even indirect impact on the delivery time of the documentation shall be analysed, and their time and cost impacts will be informed by Howden.",
         "Revisiones adicionales, comentarios o cambios solicitados por el cliente, que tengan impacto directo o incluso indirecto y que afecten el plazo de entrega de la documentación, deberán ser analizados y sus impactos en plazo y costo serán informados por Howden."),
        // sistema de gestão: inglês e espanhol do modelo da equipe (axiaisvent)
        ("A Howden possui certificação no Sistema de Gestão Integrado de Qualidade, Meio Ambiente, Saúde e Segurança em conformidade com as normas: ISO 9001, ISO 14001 e ISO 45000.",
         "Howden is certified under the Integrated Management System for Quality, Environment, Health and Safety according to the standards: ISO 9001, ISO 14001 and ISO 45000.",
         "Howden es certificada en el Sistema de Gestión Integrado de Calidad, Medio Ambiente, Salud y Seguridad según los estándares: ISO 9001, ISO 14001 y ISO 45000."),

        // ---------- anexos ----------
        ("Os documentos abaixo fazem parte integrante desta oferta:", "The documents below form an integral part of this proposal:", "Los documentos a continuación forman parte integral de esta oferta:"),
    };

    /// <summary>
    /// Pedaços que só se traduzem quando estão entre campos — o rodapé
    /// "Página {PAGE} de {NUMPAGES}". Soltos no texto seriam perigosos ("de").
    /// </summary>
    private static readonly (string Pt, string En, string Es)[] EntreCampos =
    {
        ("Página", "Page", "Página"),
        ("de", "of", "de"),
    };

    /// <summary>As NCMs da lista do modelo (<see cref="ListasDaProposta.Ncms"/>), pelo código.</summary>
    private static readonly Dictionary<string, Par> Ncms = new()
    {
        ["8414.90.20"] = new("8414.90.20 = 3.25% IPI (Parts of fans or extractor hoods)", "8414.90.20 = 3,25% IPI (Partes y piezas de ventiladores o campanas aspirantes)"),
        ["8414.59.90"] = new("8414.59.90 = 0% IPI (Complete fan)", "8414.59.90 = 0% IPI (Ventilador completo)"),
        ["8414.90.31"] = new("8414.90.31 = 5% IPI (Compressor part containing pistons or plungers)", "8414.90.31 = 5% IPI (Pieza de compresor que contiene pistones o émbolos)"),
        ["8414.90.32"] = new("8414.90.32 = 5% IPI (Compressor part containing piston rings)", "8414.90.32 = 5% IPI (Pieza de compresor que contiene anillos de segmento)"),
        ["8414.90.33"] = new("8414.90.33 = 5% IPI (Compressor part containing cylinder blocks, heads and crankcases)", "8414.90.33 = 5% IPI (Pieza de compresor que contiene bloques de cilindros, culatas y cárteres)"),
        ["8414.90.34"] = new("8414.90.34 = 5% IPI (Compressor part containing valves)", "8414.90.34 = 5% IPI (Pieza de compresor que contiene válvulas)"),
        ["8414.90.39"] = new("8414.90.39 = 0% IPI (Other compressor parts)", "8414.90.39 = 0% de IPI (Otras piezas de compresor)"),
        ["8483.40.10"] = new("8483.40.10 = 0% IPI (Gear reducers, speed increasers, gearboxes and speed variators, including torque converters)",
                             "8483.40.10 = 0% IPI (Reductores, multiplicadores, cajas de transmisión y variadores de velocidad, incluidos los convertidores de par)"),
    };

    /// <summary>As seis linhas de entrega (<see cref="ListasDaProposta.Incoterms"/>), pelo código; {0} é o local.</summary>
    private static readonly Dictionary<string, Par> Incoterms = new()
    {
        ["EXW"] = new(
            "EX-Works / FCA – Howden factory in Itatiba (SP, Brazil) or sub-supplier in the São Paulo region (SP), loaded on truck (lashing and securing by the customer).",
            "EX-Works / FCA – Fábrica Howden en Itatiba (SP, Brasil) o subproveedor en la región de São Paulo (SP), cargado sobre camión (amarre y trabado por cuenta del cliente)."),
        ["FCA-SANTOS"] = new("FCA – Cargo terminal (Port of Santos – SP, Brazil).", "FCA – Terminal de carga (Puerto de Santos – SP, Brasil)."),
        ["FCA-AEROPORTO"] = new("FCA – Cargo terminal (International Airport {0}).", "FCA – Terminal de carga (Aeropuerto Internacional {0})."),
        ["CIF"] = new("CIF – Port of {0}.", "CIF – Puerto de {0}."),
        ["DAP"] = new("DAP – {0}, loaded on truck (unloading by the customer).", "DAP – {0}, cargado sobre camión (descarga por cuenta del cliente)."),
        ["DDP"] = new("DDP – {0}, loaded on truck (unloading by the customer).", "DDP – {0}, cargado sobre camión (descarga por cuenta del cliente)."),
    };

    // ================= linhas montadas com dado =================

    private sealed record Regra(Regex Padrao, Func<Match, bool, string> Fazer);

    private static Regex R(string padrao) => new("^" + padrao + "$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static string Inc(string pt, bool en) => pt.Trim() == "incluso" ? (en ? "included" : "incluido") : (en ? "not included" : "no incluido");

    /// <summary>As linhas que o preenchimento escreve com dado da proposta (ver PropostaWordPreenchimento).</summary>
    private static readonly Regra[] Regras = MontarRegras();

    private static Regra[] MontarRegras()
    {
        var regras = new List<Regra>
        {
            // introdução
            new(R(@"Prezado\(a\) Sr\(a\)\. (.+),"), (m, en) => en ? $"Dear {m.Groups[1].Value}," : $"Estimado/a {m.Groups[1].Value},"),
            new(R(@"Howden tem a satisfação de apresentar para (.+) sua oferta para o fornecimento de partes e peças\."), (m, en) => en
                ? $"Howden is pleased to present {m.Groups[1].Value} with its proposal for the supply of parts and spare parts."
                : $"Howden tiene la satisfacción de presentar a {m.Groups[1].Value} su oferta para el suministro de partes y piezas."),

            // item
            new(R(@"Partes e Peças para Compressor (.+)"), (m, en) => (en ? "Parts and Spare Parts for Compressor " : "Partes y Piezas para Compresor ") + m.Groups[1].Value),
            new(R(@"Modelo do compressor: (.*)"), (m, en) => (en ? "Compressor model: " : "Modelo del compresor: ") + m.Groups[1].Value),
            new(R(@"Planta: (.*)"), (m, en) => (en ? "Plant: " : "Planta: ") + m.Groups[1].Value),
            new(R(@"Aplicação: (.*)"), (m, en) => (en ? "Application: " : "Aplicación: ") + m.Groups[1].Value),
            new(R(@"Último Fornecimento / Original: (.*)"), (m, en) =>
                (en ? "Last Supply / Original: " : "Último Suministro / Original: ") + (en ? m.Groups[1].Value.Replace("(pedido ", "(order ") : m.Groups[1].Value)),
            // o rótulo em negrito e o valor normal: duas partes
            new(R(@"NCM: ?(.+)"), (m, en) => "NCM: " + Parte + Ncm(m.Groups[1].Value, en)),
            new(R(@"Prazo de entrega: ?a confirmar"), (m, en) => en ? $"Delivery time:{Parte} to be confirmed" : $"Plazo de entrega:{Parte} a confirmar"),

            // valores (no inglês, 1,234.56)
            new(R(@"(R\$|US\$|[A-Z]{3}) (-?[\d.,]+)"), (m, en) => $"{m.Groups[1].Value} {(en ? NumeroEmIngles(m.Groups[2].Value) : m.Groups[2].Value)}"),

            // impostos
            new(R(@"(PIS|COFINS|IPI|ISS): ([\d.,]+%) (incluso|não incluso)(;?)(.*)"), (m, en) =>
                $"{m.Groups[1].Value}: {Pct(m.Groups[2].Value, en)} {Inc(m.Groups[3].Value, en)}{m.Groups[4].Value}{RestoDoImposto(m.Groups[5].Value, en)}"),
            new(R(@"ICMS: \(([\d.,]+%) - (.+)\) (incluso|não incluso)"), (m, en) =>
                $"ICMS: ({Pct(m.Groups[1].Value, en)} - {m.Groups[2].Value}) {Inc(m.Groups[3].Value, en)}"),

            // dados para faturamento (o "Banco: ..." é negrito, o resto normal)
            new(R(@"CNPJ: (.+) – Inscrição Estadual: (.+)"), (m, en) =>
                $"CNPJ{(en ? " (Tax ID)" : "")}: {m.Groups[1].Value} – {(en ? "State Registration" : "Inscripción Estatal")}: {m.Groups[2].Value}"),
            new(R(@"Banco: (.+?) – Agência: (.+?) Conta: (.+)"), (m, en) => en
                ? $"Bank: {m.Groups[1].Value}{Parte} – Branch: {m.Groups[2].Value} Account: {m.Groups[3].Value}"
                : $"Banco: {m.Groups[1].Value}{Parte} – Agencia: {m.Groups[2].Value} Cuenta: {m.Groups[3].Value}"),
            new(R(@"Município (.+), Brasil\."), (m, en) => $"{m.Groups[1].Value}, {(en ? "Brazil" : "Brasil")}."),

            // pagamento: as condições mais comuns da base (as outras saem como digitadas — a tela avisa)
            new(R(@"(\d+)% em até (\d+) dias da emissão da fatura(, salvo negociação comercial específica)?\.?"), (m, en) => en
                ? $"{m.Groups[1].Value}% within {m.Groups[2].Value} days of the invoice date{(m.Groups[3].Success ? ", unless otherwise agreed commercially" : "")}."
                : $"{m.Groups[1].Value}% hasta {m.Groups[2].Value} días desde la emisión de la factura{(m.Groups[3].Success ? ", salvo negociación comercial específica" : "")}."),
            new(R(@"(\d+)% no pedido e (\d+)% na entrega\.?"), (m, en) => en
                ? $"{m.Groups[1].Value}% with the purchase order and {m.Groups[2].Value}% on delivery."
                : $"{m.Groups[1].Value}% con la orden de compra y {m.Groups[2].Value}% a la entrega."),
            new(R(@"(\d+)% (?:–|-) (\d+) ddl da (?:retirada do equipamento da fábrica|entrega)\.?"), (m, en) => en
                ? $"{m.Groups[1].Value}% – {m.Groups[2].Value} days after collection of the equipment from the factory"
                : $"{m.Groups[1].Value}% – {m.Groups[2].Value} días después del retiro del equipo de la fábrica"),
            new(R(@"(\d+) ?ddl\.?"), (m, en) => en ? $"{m.Groups[1].Value} days net" : $"{m.Groups[1].Value} días netos"),

            // validade
            new(R(@"A validade de nossa oferta é de (\d+) \(([^)]+)\) dias\.?"), (m, en) => Validade(m.Groups[1].Value, en)),
            new(R(@"Esta proposta é válida por (\d+) dias\.?"), (m, en) => Validade(m.Groups[1].Value, en)),

            // anexos: a T&C na língua da proposta
            new(R(@"(T&C_.+)-PT-(.+)"), (m, en) => $"{m.Groups[1].Value}-{(en ? "EN" : "ES")}-{m.Groups[2].Value}"),
        };

        // as linhas de entrega, a partir das frases do modelo
        foreach (var i in ListasDaProposta.Incoterms)
        {
            if (!Incoterms.TryGetValue(i.Codigo, out var par)) continue;
            var padrao = Regex.Escape(i.Texto).Replace(@"\{0}", "(.+)");
            regras.Add(new(R(padrao), (m, en) =>
            {
                var local = m.Groups.Count > 1 ? m.Groups[1].Value : "";
                if (Fixo(local, en) is { } t) local = t;
                // "(Aeroporto Internacional de Guarulhos)": no inglês o "de" sobra
                else if (en && Regex.Match(local, @"^(?:de|do|da) (.+)$") is { Success: true } semDe) local = semDe.Groups[1].Value;
                return string.Format(en ? par.En : par.Es, local);
            }));
        }
        return regras.ToArray();
    }

    // ================= a consulta =================

    private static readonly Dictionary<string, Par> _fixos = Fixos
        .GroupBy(f => Chave(f.Pt)).ToDictionary(g => g.Key, g => new Par(g.First().En, g.First().Es));

    private static readonly Dictionary<string, Par> _entreCampos = EntreCampos
        .ToDictionary(f => Chave(f.Pt), f => new Par(f.En, f.Es));

    /// <summary>
    /// A tradução de um texto do Word português; null = fica como está (é dado,
    /// ou não há tradução). Pode vir com <see cref="Parte"/> separando as partes.
    /// </summary>
    /// <param name="entreCampos">o trecho está num parágrafo com campos (rodapé, índice)</param>
    public static string? Traduzir(string texto, string idioma, bool entreCampos = false)
    {
        if (idioma is not (IdiomasDaProposta.Ingles or IdiomasDaProposta.Espanhol)) return null;
        var en = idioma == IdiomasDaProposta.Ingles;
        var t = (texto ?? "").Trim();
        if (t.Length == 0) return null;

        if (Fixo(t, en) is { } fixo) return fixo;
        if (entreCampos && _entreCampos.TryGetValue(Chave(t), out var pedaco)) return en ? pedaco.En : pedaco.Es;
        foreach (var regra in Regras)
        {
            var m = regra.Padrao.Match(t);
            if (m.Success) return regra.Fazer(m, en);
        }
        return null;
    }

    /// <summary>O texto tem tradução (ou está vazio)? Para a tela avisar o que vai sair em português.</summary>
    public static bool TemTraducao(string? texto, string? idioma) =>
        IdiomasDaProposta.Normalizar(idioma) == IdiomasDaProposta.Portugues
        || (texto ?? "").Trim().Length == 0
        || Traduzir(texto!, IdiomasDaProposta.Normalizar(idioma)) is not null;

    private static string? Fixo(string texto, bool en) =>
        _fixos.TryGetValue(Chave(texto), out var par) ? (en ? par.En : par.Es) : null;

    /// <summary>
    /// A chave de comparação: sem maiúsculas, sem espaços e sem hífens — o
    /// modelo usa hífen inseparável ("reserva-se", "mão-de-obra"), que não é
    /// texto no Word, e o mesmo texto aparece com espaços diferentes.
    /// </summary>
    public static string Chave(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            if (char.IsWhiteSpace(c) || c is '-' or '‑' or '­' or Parte) continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static string Ncm(string texto, bool en)
    {
        var t = texto.Trim();
        if (t == "a definir") return en ? "to be defined" : "por definir";
        var codigo = t.Split(' ', 2)[0];
        return Ncms.TryGetValue(codigo, out var par) && ListasDaProposta.NcmDo(codigo)?.Texto == t ? (en ? par.En : par.Es) : t;
    }

    /// <summary>O que vem depois da alíquota: a NCM ("; NCM ...") ou o município do ISS.</summary>
    private static string RestoDoImposto(string resto, bool en)
    {
        var m = Regex.Match(resto, @"^(\s*NCM )(.+)$");
        return m.Success ? m.Groups[1].Value + Ncm(m.Groups[2].Value, en) : resto;
    }

    private static string Pct(string pct, bool en) => en ? pct.Replace(',', '.') : pct;

    /// <summary>"1.234,56" → "1,234.56".</summary>
    public static string NumeroEmIngles(string numero) =>
        decimal.TryParse(numero, NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out var v)
            ? v.ToString(numero.Contains(',') ? "N" + (numero.Length - numero.LastIndexOf(',') - 1) : "N0", CultureInfo.GetCultureInfo("en-US"))
            : numero;

    private static string Validade(string dias, bool en)
    {
        var extenso = PorExtenso(dias, en);
        var numero = extenso is null ? dias : $"{dias} ({extenso})";
        return en ? $"Our proposal is valid for {numero} days." : $"La validez de nuestra oferta es de {numero} días.";
    }

    private static string? PorExtenso(string numero, bool en) => (numero.Trim(), en) switch
    {
        ("1", true) => "one", ("1", false) => "uno",
        ("5", true) => "five", ("5", false) => "cinco",
        ("7", true) => "seven", ("7", false) => "siete",
        ("10", true) => "ten", ("10", false) => "diez",
        ("15", true) => "fifteen", ("15", false) => "quince",
        ("20", true) => "twenty", ("20", false) => "veinte",
        ("30", true) => "thirty", ("30", false) => "treinta",
        ("45", true) => "forty-five", ("45", false) => "cuarenta y cinco",
        ("60", true) => "sixty", ("60", false) => "sesenta",
        ("90", true) => "ninety", ("90", false) => "noventa",
        ("120", true) => "one hundred and twenty", ("120", false) => "ciento veinte",
        ("180", true) => "one hundred and eighty", ("180", false) => "ciento ochenta",
        _ => null,
    };
}
