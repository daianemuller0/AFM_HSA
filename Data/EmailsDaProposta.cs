using System.Globalization;
using System.Net;
using System.Text;
using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>Um e-mail pronto: destinatários, assunto e corpo (HTML para o Outlook e texto para o mailto).</summary>
public sealed record EmailDaProposta(string Para, string Cc, string Assunto, string Html, string Texto);

public sealed record AnexoDoEmail(string Nome, string TipoMime, byte[] Conteudo);

/// <summary>O que a aprovação do pricing mostra, já calculado pela tela.</summary>
/// <param name="ClienteCompleto">o cliente com a planta ("Vale – São Luís/MA")</param>
/// <param name="Escopo">uma linha por equipamento ("VX 2025 · SN 123 — 3 itens")</param>
/// <param name="Itens">produto, modelo, quantidade e preços sem impostos, já formatados na moeda do preço</param>
/// <param name="PrazoDias">"45" ou "45 a 60"</param>
/// <param name="Dolar">R$ por USD (a taxa da proposta, se o preço é em dólar)</param>
/// <param name="Seguros">a descrição do seguro garantia / carta fiança e das outras garantias</param>
public sealed record ResumoDaAprovacao(
    string ClienteCompleto, IReadOnlyList<string> Escopo,
    IReadOnlyList<(string Item, string Produto, string Modelo, int Qtd, string UnitSemImpostos, string Total)> Itens,
    string TotalSemImpostos, string PrazoDias, decimal? Dolar, string Seguros, string Nota, string? Cambio = null);

/// <summary>
/// Os dois e-mails da proposta, os mesmos da aba "EMAILS AUTOMÁTICOS" da
/// planilha de pricing da equipe (macros enviar_email_pricingconsolidado e
/// enviar_email_proposta): a <b>aprovação do pricing</b> (para o Edson, com
/// cópia para o Marcos) e o <b>envio da proposta ao cliente</b>.
///
/// A planilha abria um rascunho no Outlook; aqui sai um arquivo .eml que o
/// Outlook abre como mensagem nova, já com destinatários, assunto, corpo
/// formatado e o anexo — é só conferir e clicar em Enviar.
/// </summary>
public static class EmailsDaProposta
{
    // os da macro da planilha; o appsettings (seção "Emails") pode trocar
    public const string AprovadorPadrao = "edson.geraldini@chartindustries.com";
    public const string CopiaDaAprovacaoPadrao = "marcos.pinto@chartindustries.com";

    private static readonly CultureInfo Br = new("pt-BR");
    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string Pct(decimal f) => (f * 100m).ToString("0.##", Br) + "%";
    private static string Brl(decimal v) => "R$ " + v.ToString("N2", Br);

    // ================= aprovação do pricing =================

    /// <summary>O texto padrão da garantia do projeto (o do modelo da equipe).</summary>
    public const string GarantiaPadrao =
        "12 meses após a primeira operação; ou 18 meses a partir da notificação da Howden sobre a prontidão dos Bens.";

    /// <summary>
    /// "Nº do CRM-revisão – PRICING – Cliente (planta) – Referência do cliente – APROVAÇÃO DE PRICING DE PROPOSTA"
    /// (o nº do CRM é o código da proposta, a "nossa referência").
    /// </summary>
    public static string AssuntoDaAprovacao(CommercialOffer o, string clienteCompleto)
    {
        var numero = $"{PropostaWord.Ou(o.OfferCode, o.NumeroCrm)}-{PropostaWord.Ou(o.Revisao, "0")}";
        var partes = new[] { numero, "PRICING", clienteCompleto, o.SuaReferencia, "APROVAÇÃO DE PRICING DE PROPOSTA" }
            .Select(p => (p ?? "").Trim()).Where(p => p.Length > 0);
        return string.Join(" – ", partes);
    }

    /// <summary>"edson.geraldini@..." → "Edson" (o "Prezado Edson," do começo do e-mail).</summary>
    public static string PrimeiroNome(string email)
    {
        var primeiro = (email ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        if (ContatosHowden.Todos.FirstOrDefault(c => c.Email.Equals(primeiro, StringComparison.OrdinalIgnoreCase)) is { } c)
            return c.Nome.Split(' ')[0];
        var nome = primeiro.Split('@')[0].Split('.', '_', '-')[0];
        return nome.Length == 0 ? "" : char.ToUpper(nome[0], Br) + nome[1..].ToLower(Br);
    }

    /// <summary>
    /// A aprovação no formato da equipe: saudação, cliente, projeto, escopo,
    /// nota, a tabela dos itens (preço sem impostos), o quadro GM / negociação /
    /// comissão / garantia / risco / prazo / dólar, condições de pagamento,
    /// garantia do projeto, seguros e a despedida. A assinatura é a do Outlook
    /// de quem envia; sem Outlook, <paramref name="assinatura"/> entra no lugar.
    /// </summary>
    public static EmailDaProposta Aprovacao(CommercialOffer o, CalculoPricing.Resultado r, ResumoDaAprovacao d,
        string para, string cc, string assinatura)
    {
        var moeda = Cambio.MoedaDoPreco(o).Rotulo();
        var nome = PrimeiroNome(para);
        var saudacao = nome.Length > 0 ? $"Prezado {nome}," : "Prezados,";
        const string Pedido = "Por gentileza, poderia verificar e liberar o envio da oferta conforme dados abaixo:";
        var quadro = new List<(string Rotulo, string Valor, string Unidade)>
        {
            ("GM:", r.VendaLiquida > 0m ? Num(r.Margem * 100m) : "—", "%"),
            ("Negociação:", Num(o.MargemNegociacao * 100m), "%"),
            ("Comissão:", Num(r.Comissoes * 100m), "%"),
            ("Garantia:", Num(CalculoPricing.GarantiaDoProjeto * 100m), "%"),
            ("Risco:", Num(o.RiscoAdicional * 100m), "%"),
            ("Prazo de entrega:", PropostaWord.Ou(d.PrazoDias, "—"), "Dias"),
            d.Cambio is { Length: > 0 } cambio
                ? ("Câmbio:", cambio, "")
                : ("Dólar:", d.Dolar is decimal usd && usd > 0m ? usd.ToString("N4", Br) : "—", "R$"),
        };

        const string Rot = "font-weight:bold";
        var sb = new StringBuilder("<div style=\"font-family:Calibri,Arial,Helvetica,sans-serif;font-size:14px;color:#1f2937;line-height:1.45\">");
        sb.Append($"<p>{H(saudacao)}</p><p>{H(Pedido)}</p>");
        sb.Append($"<p style=\"margin:4px 0\"><span style=\"{Rot}\">Cliente:</span> {H(d.ClienteCompleto)}</p>");
        sb.Append($"<p style=\"margin:4px 0\"><span style=\"{Rot}\">Projeto:</span> {H(PropostaWord.Ou(o.Project, "—"))}</p>");
        sb.Append($"<p style=\"margin:4px 0\"><span style=\"{Rot}\">Escopo:</span> Partes e peças para compressor</p>");
        if (d.Escopo.Count > 0) sb.Append("<ul style=\"margin:2px 0 6px\">" + string.Concat(d.Escopo.Select(e => $"<li>{H(e)}</li>")) + "</ul>");
        if (d.Nota.Trim().Length > 0) sb.Append($"<p style=\"margin:4px 0\">Nota: {H(d.Nota.Trim())}</p>");

        // ---- os itens ----
        const string Th = "background:#005B8F;color:#fff;padding:6px 8px;text-align:left;border:1px solid #7f9db9;font-weight:bold";
        const string Td = "padding:6px 8px;border:1px solid #c9c9c9;background:#ffffff;vertical-align:top";
        sb.Append("<table cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;margin:12px 0;font-size:13px\"><tr>");
        foreach (var c in new[] { "ITEM", "PRODUTO", "MODELO DO COMPRESSOR", "QUANT.", $"PREÇO UNIT S/IMP ({moeda})", $"PREÇO TOTAL ({moeda})" })
            sb.Append($"<th style=\"{Th}\">{H(c)}</th>");
        sb.Append("</tr>");
        foreach (var i in d.Itens)
        {
            sb.Append("<tr>");
            sb.Append($"<td style=\"{Td}\">{H(i.Item)}</td><td style=\"{Td}\">{H(i.Produto)}</td><td style=\"{Td}\">{H(i.Modelo)}</td>");
            sb.Append($"<td style=\"{Td};text-align:right\">{i.Qtd}</td><td style=\"{Td};text-align:right;white-space:nowrap\">{H(i.UnitSemImpostos)}</td><td style=\"{Td};text-align:right;white-space:nowrap\">{H(i.Total)}</td>");
            sb.Append("</tr>");
        }
        sb.Append($"<tr><td colspan=\"4\" style=\"{Td}\"></td><td style=\"{Td};font-weight:bold;text-align:right\">TOTAL</td><td style=\"{Td};font-weight:bold;text-align:right;white-space:nowrap\">{H(d.TotalSemImpostos)}</td></tr>");
        sb.Append("</table>");
        // uma linha em branco entre as duas tabelas (o Outlook ignora a margem da tabela)
        sb.Append("<p style=\"margin:0\">&nbsp;</p>");

        // ---- o quadro: GM, negociação, comissão, garantia, risco, prazo, dólar ----
        sb.Append("<table cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;margin:8px 0 14px;font-size:13px\">");
        foreach (var (rot, valor, unidade) in quadro)
            sb.Append($"<tr><td style=\"padding:5px 8px;border:1px solid #000;font-weight:bold;min-width:130px\">{H(rot)}</td>" +
                      $"<td style=\"padding:5px 8px;border:1px solid #000;background:#ddebf7;text-align:right;min-width:100px\">{H(valor)}</td>" +
                      $"<td style=\"padding:5px 8px;border:1px solid #000;min-width:36px\">{H(unidade)}</td></tr>");
        sb.Append("</table>");

        sb.Append($"<p style=\"{Rot}\">Condições de Pagamento: {H(PropostaWord.Ou(o.PaymentTerms, "—"))}</p>");
        sb.Append($"<p style=\"margin-bottom:2px;{Rot}\">Garantia do Projeto</p>");
        sb.Append($"<p style=\"margin-top:0;{Rot}\"><u>Padrão:</u> {H(GarantiaPadrao)}</p>");
        sb.Append($"<p style=\"margin-bottom:2px;{Rot}\">Seguros:</p><p style=\"margin-top:0\">{H(d.Seguros)}</p>");
        sb.Append($"<p style=\"margin-top:18px\">Atenciosamente,");
        if (assinatura.Trim().Length > 0) sb.Append($"<br>{H(assinatura)}");
        sb.Append("</p></div>");

        var texto = new StringBuilder();
        texto.AppendLine(saudacao).AppendLine().AppendLine(Pedido).AppendLine();
        texto.AppendLine($"Cliente: {d.ClienteCompleto}").AppendLine($"Projeto: {PropostaWord.Ou(o.Project, "—")}").AppendLine("Escopo: Partes e peças para compressor");
        foreach (var e in d.Escopo) texto.AppendLine($"  • {e}");
        if (d.Nota.Trim().Length > 0) texto.AppendLine($"Nota: {d.Nota.Trim()}");
        texto.AppendLine();
        texto.AppendLine($"ITEM | PRODUTO | MODELO DO COMPRESSOR | QUANT. | PREÇO UNIT S/IMP ({moeda}) | PREÇO TOTAL ({moeda})");
        foreach (var i in d.Itens) texto.AppendLine($"{i.Item} | {i.Produto} | {i.Modelo} | {i.Qtd} | {i.UnitSemImpostos} | {i.Total}");
        texto.AppendLine($"TOTAL: {d.TotalSemImpostos}").AppendLine();
        foreach (var (rot, valor, unidade) in quadro) texto.AppendLine($"{rot} {valor} {unidade}");
        texto.AppendLine().AppendLine($"Condições de Pagamento: {PropostaWord.Ou(o.PaymentTerms, "—")}").AppendLine();
        texto.AppendLine("Garantia do Projeto").AppendLine($"Padrão: {GarantiaPadrao}").AppendLine();
        texto.AppendLine("Seguros:").AppendLine(d.Seguros).AppendLine().AppendLine("Atenciosamente,");
        if (assinatura.Trim().Length > 0) texto.AppendLine(assinatura);

        return new EmailDaProposta(para, cc, AssuntoDaAprovacao(o, d.ClienteCompleto), sb.ToString(), texto.ToString());
    }

    private static string Num(decimal v) => v.ToString("0.##", Br);

    // ================= envio da proposta ao cliente =================

    private sealed record TextosDoEnvio(string Proposta, string Saudacao, string Anexo, string Contatar, string Por,
        string Condicoes, string Agradecimento);

    /// <summary>O texto do envio na língua da proposta (o espanhol é o da equipe; o português e o inglês, a versão dele).</summary>
    private static TextosDoEnvio TextosDe(string idioma) => IdiomasDaProposta.Normalizar(idioma) switch
    {
        IdiomasDaProposta.Ingles => new("Commercial Proposal", "Dear Sir or Madam,",
            "Please find attached our Commercial Proposal {0}, prepared for your review.",
            "To schedule a visit or for any technical or commercial matter, please contact:",
            "at",
            "The selling price presented in our Offer is subject to acceptance of Howden's standard Terms and Conditions for the Sale of Goods, included in our proposal. Any different condition will be subject to commercial renegotiation.",
            "Thank you for your attention. We remain at your disposal for any clarification you may need."),
        IdiomasDaProposta.Espanhol => new("Propuesta Comercial", "Estimado,",
            "Adjuntamos nuestra Propuesta Comercial {0}, preparada para su revisión.",
            "Para programar una visita o para cualquier asunto técnico o comercial, póngase en contacto con:",
            "por",
            "El precio de venta presentado en nuestra Oferta está sujeto a la aceptación de los Términos y Condiciones de Venta de Bienes, estándar de Howden, incluidos en nuestra propuesta. Cualquier condición diferente estará sujeta a renegociación comercial.",
            "Gracias por su atención. Quedamos a su disposición para cualquier aclaración que necesite."),
        _ => new("Proposta Comercial", "Prezado(a),",
            "Segue anexa nossa Proposta Comercial {0}, preparada para sua análise.",
            "Para agendar uma visita ou para qualquer assunto técnico ou comercial, entre em contato com:",
            "pelo telefone",
            "O preço de venda apresentado em nossa Oferta está sujeito à aceitação dos Termos e Condições de Venda de Bens, padrão da Howden, incluídos em nossa proposta. Qualquer condição diferente estará sujeita a renegociação comercial.",
            "Agradecemos a atenção. Ficamos à disposição para qualquer esclarecimento que necessite."),
    };

    /// <summary>
    /// "HOWDEN - Propuesta Comercial - Cliente – Cidade - HSAJOM.AFM.000457-C0 | referência do cliente":
    /// sem cidade ou sem a referência do cliente, a parte sai.
    /// </summary>
    public static string AssuntoDaProposta(CommercialOffer o)
    {
        var cliente = string.Join(" – ", new[] { o.ClientName, o.City }.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0));
        var partes = new[] { "HOWDEN", TextosDe(o.Idioma).Proposta, cliente, PropostaWord.Referencia(o) }.Where(x => x.Length > 0);
        var suaRef = (o.SuaReferencia ?? "").Trim();
        return string.Join(" - ", partes) + (suaRef.Length > 0 ? " | " + suaRef : "");
    }

    /// <summary>"José Ovídio Moura por +55 11 96858 2183 - jose.moura@chartindustries.com" (o primeiro telefone).</summary>
    public static string LinhaDeContato(string nome, ContatoHowden? c, string idioma)
    {
        var n = (c?.Nome ?? nome ?? "").Trim();
        if (n.Length == 0) return "";
        var fone = c?.Telefones.Select(t => t.Trim()).FirstOrDefault(t => t.Length > 0) ?? "";
        var email = (c?.Email ?? "").Trim();
        var linha = fone.Length > 0 ? $"{n} {TextosDe(idioma).Por} {fone}" : n;
        return email.Length > 0 ? $"{linha} - {email}" : linha;
    }

    /// <param name="contatos">uma linha por vendedor da proposta (<see cref="LinhaDeContato"/>)</param>
    /// <param name="remetente">"" quando o Outlook põe a assinatura de quem envia (no .eml, o nome de quem envia)</param>
    public static EmailDaProposta Proposta(CommercialOffer o, IReadOnlyList<string> contatos, string para, string cc, string remetente)
    {
        var t = TextosDe(o.Idioma);
        var anexo = string.Format(t.Anexo, PropostaWord.Referencia(o));
        var linhas = contatos.Where(c => c.Trim().Length > 0).ToList();

        var sb = new StringBuilder();
        sb.Append(Abrir());
        sb.Append($"<p>{H(t.Saudacao)}</p>");
        sb.Append($"<p>{H(anexo)}</p>");
        if (linhas.Count > 0)
        {
            sb.Append($"<p>{H(t.Contatar)}</p>");
            sb.Append("<p>" + string.Join("<br>", linhas.Select(H)) + "</p>");
        }
        sb.Append($"<p>{H(t.Condicoes)}</p>");
        sb.Append($"<p>{H(t.Agradecimento)}</p>");
        if (remetente.Trim().Length > 0) sb.Append($"<p>{H(remetente)}<br>Howden | Chart Industries</p>");
        sb.Append(Fechar());

        var texto = new StringBuilder();
        texto.AppendLine(t.Saudacao).AppendLine().AppendLine(anexo).AppendLine();
        if (linhas.Count > 0)
        {
            texto.AppendLine(t.Contatar).AppendLine();
            foreach (var l in linhas) texto.AppendLine(l);
            texto.AppendLine();
        }
        texto.AppendLine(t.Condicoes).AppendLine().AppendLine(t.Agradecimento);
        if (remetente.Trim().Length > 0) texto.AppendLine().AppendLine(remetente).AppendLine("Howden | Chart Industries");

        return new EmailDaProposta(para, cc, AssuntoDaProposta(o), sb.ToString(), texto.ToString());
    }

    // ================= HTML (estilos em linha: o Outlook ignora <style>) =================

    private const string EstiloTitulo = "font-size:15px;font-weight:bold;color:#004785;margin:0 0 12px";

    private static string Abrir() => "<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#1f2937;line-height:1.45\">";
    private static string Fechar() => "</div>";

    private static string Tabela(string[]? cabecalho, IEnumerable<string[]> linhas, int[]? direita = null, bool negritoNaPrimeira = false)
    {
        direita ??= Array.Empty<int>();
        var sb = new StringBuilder("<table cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;margin:10px 0;font-size:12.5px\">");
        if (cabecalho is not null)
        {
            sb.Append("<tr>");
            for (var i = 0; i < cabecalho.Length; i++)
                sb.Append($"<th style=\"background:#004785;color:#fff;padding:5px 10px;text-align:{(direita.Contains(i) ? "right" : "left")};border:1px solid #004785\">{H(cabecalho[i])}</th>");
            sb.Append("</tr>");
        }
        var n = 0;
        foreach (var l in linhas)
        {
            var fundo = n++ % 2 == 0 ? "#ffffff" : "#f3f7fb";
            sb.Append("<tr>");
            for (var i = 0; i < l.Length; i++)
            {
                var negrito = negritoNaPrimeira && i == 0 ? "font-weight:bold;color:#004785;" : "";
                sb.Append($"<td style=\"{negrito}padding:5px 10px;border:1px solid #d6dee8;background:{fundo};text-align:{(direita.Contains(i) ? "right" : "left")};vertical-align:top\">{H(l[i])}</td>");
            }
            sb.Append("</tr>");
        }
        return sb.Append("</table>").ToString();
    }

    // ================= o rascunho do Outlook (.eml) =================

    /// <summary>
    /// O e-mail como arquivo .eml com "X-Unsent: 1": o Outlook (e o Thunderbird)
    /// abre como mensagem nova, pronta para enviar, com o corpo formatado e os anexos.
    /// </summary>
    public static byte[] Eml(EmailDaProposta e, IReadOnlyList<AnexoDoEmail> anexos)
    {
        var misto = "=_misto_" + Guid.NewGuid().ToString("N");
        var alternativo = "=_alt_" + Guid.NewGuid().ToString("N");
        var sb = new StringBuilder();
        void L(string s = "") => sb.Append(s).Append("\r\n");

        L("MIME-Version: 1.0");
        L("X-Unsent: 1");
        if (e.Para.Trim().Length > 0) L("To: " + Enderecos(e.Para));
        if (e.Cc.Trim().Length > 0) L("Cc: " + Enderecos(e.Cc));
        L("Subject: " + Codificar(e.Assunto));
        L("Date: " + DataRfc());
        L($"Content-Type: multipart/mixed; boundary=\"{misto}\"");
        L();
        L($"--{misto}");
        L($"Content-Type: multipart/alternative; boundary=\"{alternativo}\"");
        L();
        L($"--{alternativo}");
        L("Content-Type: text/plain; charset=utf-8");
        L("Content-Transfer-Encoding: base64");
        L();
        L(Base64(Encoding.UTF8.GetBytes(e.Texto)));
        L($"--{alternativo}");
        L("Content-Type: text/html; charset=utf-8");
        L("Content-Transfer-Encoding: base64");
        L();
        L(Base64(Encoding.UTF8.GetBytes("<html><head><meta charset=\"utf-8\"></head><body>" + e.Html + "</body></html>")));
        L($"--{alternativo}--");
        foreach (var a in anexos)
        {
            L($"--{misto}");
            L($"Content-Type: {a.TipoMime}; name=\"{Codificar(a.Nome)}\"");
            L("Content-Transfer-Encoding: base64");
            L($"Content-Disposition: attachment; filename=\"{Codificar(a.Nome)}\"");
            L();
            L(Base64(a.Conteudo));
        }
        L($"--{misto}--");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string DataRfc() => DateTimeOffset.Now.ToString("ddd, dd MMM yyyy HH:mm:ss ", CultureInfo.InvariantCulture)
                                       + DateTimeOffset.Now.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", "");

    private static string Enderecos(string lista) =>
        string.Join(", ", lista.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>Cabeçalho com acento: =?utf-8?B?...?= (RFC 2047).</summary>
    private static string Codificar(string s) =>
        s.All(c => c < 128) ? s : $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(s))}?=";

    private static string Base64(byte[] b) => Convert.ToBase64String(b, Base64FormattingOptions.InsertLineBreaks);

    /// <summary>O mailto (sem anexo nem formatação): para quem usa o e-mail no navegador.</summary>
    public static string Mailto(EmailDaProposta e)
    {
        var corpo = e.Texto.Length > 1500 ? e.Texto[..1500] + "\n…" : e.Texto;
        var q = new List<string> { "subject=" + Uri.EscapeDataString(e.Assunto), "body=" + Uri.EscapeDataString(corpo) };
        if (e.Cc.Trim().Length > 0) q.Insert(0, "cc=" + Uri.EscapeDataString(Enderecos(e.Cc)));
        return $"mailto:{Uri.EscapeDataString(Enderecos(e.Para)).Replace("%40", "@").Replace("%2C", ",")}?{string.Join("&", q)}";
    }
}
