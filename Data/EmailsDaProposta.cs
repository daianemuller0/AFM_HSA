using System.Globalization;
using System.Net;
using System.Text;
using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>Um e-mail pronto: destinatários, assunto e corpo (HTML para o Outlook e texto para o mailto).</summary>
public sealed record EmailDaProposta(string Para, string Cc, string Assunto, string Html, string Texto);

public sealed record AnexoDoEmail(string Nome, string TipoMime, byte[] Conteudo);

/// <summary>O que a aprovação do pricing mostra, já calculado pela tela.</summary>
/// <param name="ValorLiquidoNaVenda">o total líquido na moeda do preço (null quando o preço é em R$)</param>
public sealed record ResumoDaAprovacao(
    string Unidades, string TiposDeEquipamento, string PrazoDeEntrega, decimal CustoTotal, decimal? ValorLiquidoNaVenda,
    IReadOnlyList<(string Item, string PartNumber, string Descricao, int Qtd, string Unitario, string Total, string Prazo)> Itens,
    string NivelDosPrecos);

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

    /// <summary>
    /// Assunto da planilha: "PV-código-revisão-PRICING CONSOLIDADO-cliente.";
    /// com produto Intercompany, "IC - " na frente ("IC REVISADO - " a partir da revisão 1).
    /// </summary>
    public static string AssuntoDaAprovacao(CommercialOffer o)
    {
        var partes = new[] { o.Pv, o.OfferCode, PropostaWord.Ou(o.Revisao, "0"), "PRICING CONSOLIDADO", o.ClientName }
            .Select(p => (p ?? "").Trim()).Where(p => p.Length > 0);
        var assunto = string.Join("-", partes) + ".";
        if (Textos.Igual(o.ContemProdutoIc, "Sim"))
            assunto = (int.TryParse(o.Revisao, out var rev) && rev > 0 ? "IC REVISADO - " : "IC - ") + assunto;
        return assunto;
    }

    /// <summary>"edson.geraldini@..." → "Edson" (o "Edson," do começo do e-mail).</summary>
    public static string PrimeiroNome(string email)
    {
        var primeiro = (email ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        if (ContatosHowden.Todos.FirstOrDefault(c => c.Email.Equals(primeiro, StringComparison.OrdinalIgnoreCase)) is { } c)
            return c.Nome.Split(' ')[0];
        var nome = primeiro.Split('@')[0].Split('.', '_', '-')[0];
        return nome.Length == 0 ? "" : char.ToUpper(nome[0], Br) + nome[1..].ToLower(Br);
    }

    public static EmailDaProposta Aprovacao(CommercialOffer o, CalculoPricing.Resultado r, ResumoDaAprovacao d,
        string para, string cc, string remetente)
    {
        var moeda = Cambio.MoedaDoPreco(o);
        var referencia = PropostaWord.Referencia(o);
        var deQue = PropostaWord.Ou(o.Pv, referencia);

        // ---- o resumo pedido: cliente, unidade, equipamento, margem, valor líquido, prazo ----
        var resumo = new List<(string, string)>
        {
            ("Cliente", o.ClientName),
            ("Unidade", d.Unidades),
            ("Tipo de equipamento", d.TiposDeEquipamento),
            ("Margem (GM)", r.VendaLiquida > 0m ? Pct(r.Margem) : "—"),
            ("Valor líquido (sem impostos)", Brl(r.ValorDaProposta) + (d.ValorLiquidoNaVenda is decimal naVenda ? $"  ·  {Moedas.Dinheiro(moeda, naVenda)}" : "")),
            ("Prazo de entrega", d.PrazoDeEntrega),
            ("Proposta", referencia + (o.Pv.Trim().Length > 0 ? $" · PV {o.Pv}" : "")),
            ("Custo total", Brl(r.CustoTotal)),
            ("Condição de pagamento", PropostaWord.Ou(o.PaymentTerms, "—")),
            ("Local de entrega", PropostaWord.Ou(ListasDaProposta.LinhaDoIncoterm(o.Incoterm, o.IncotermLocal),
                o.Incoterm == ListasDaProposta.Livre ? "A COMBINAR" : "—")),
        };
        if (Cambio.Converte(o) && Cambio.Normalizada(o) > 0m)
            resumo.Add(("Câmbio", $"1 {moeda.Rotulo()} = R$ {Cambio.Efetivo(o).ToString("N4", Br)} (com a segurança)"));
        if (o.FreteCusto > 0m) resumo.Add(("Frete (no custo dos itens)", Brl(o.FreteCusto)));
        if (Textos.Igual(o.ContemProdutoIc, "Sim")) resumo.Add(("Intercompany", "contém produto IC"));

        // ---- preço de venda por nível de impostos (K13:Q16 do pricing) ----
        var precos = new List<(string, string, string)> { ("Venda líquida (sem impostos)", "—", Brl(r.ValorDaProposta)) };
        if (!r.SemImpostos)
        {
            if (r.VendaComPisCofins is decimal pc) precos.Add(("Venda com PIS e COFINS, sem ICMS", Pct(r.AliqPis + r.AliqCofins), Brl(pc)));
            if (r.VendaComIcms is decimal vi) precos.Add(("Venda com PIS, COFINS e ICMS", r.AliqIcms is decimal ic ? $"{Pct(ic)} ({PropostaWord.Ou(o.Estado, "UF")})" : "—", Brl(vi)));
            if (r.PrecoComImpostos is decimal pi) precos.Add(("Venda com PIS, COFINS, ICMS e IPI", Pct(r.AliqIpi), Brl(pi)));
        }
        else precos.Add(("Exportação: sem PIS, COFINS, ICMS e IPI", "—", "—"));

        var margens = new List<(string, string)>
        {
            ("GM", r.VendaLiquida > 0m ? Pct(r.Margem) : "—"),
            ("Risco", Pct(o.RiscoAdicional)),
            ("MN (margem de negociação)", Pct(o.MargemNegociacao)),
            ("Comissões", Pct(r.Comissoes)),
        };

        var nome = PrimeiroNome(para);
        var sb = new StringBuilder();
        sb.Append(Abrir());
        sb.Append($"<p style=\"{EstiloTitulo}\">APROVAÇÃO DE PRICING CONSOLIDADO</p>");
        sb.Append($"<p>{H(nome.Length > 0 ? nome + "," : "Prezados,")}</p>");
        sb.Append($"<p>Por gentileza aprovar os valores da <b>{H(deQue)}</b> conforme abaixo:</p>");
        sb.Append(Tabela(null, resumo.Select(x => new[] { x.Item1, x.Item2 }), negritoNaPrimeira: true));
        sb.Append(Tabela(new[] { "PREÇO DE VENDA", "IMPOSTOS", "VALORES" }, precos.Select(x => new[] { x.Item1, x.Item2, x.Item3 }), direita: new[] { 2 }));
        sb.Append(Tabela(new[] { "MARGENS", "" }, margens.Select(x => new[] { x.Item1, x.Item2 }), direita: new[] { 1 }));
        if (d.Itens.Count > 0)
        {
            sb.Append($"<p style=\"margin:14px 0 4px\"><b>Itens vendidos</b> <span style=\"color:#64748b\">(preços {H(d.NivelDosPrecos)})</span></p>");
            sb.Append(Tabela(new[] { "Item", "Part Number", "Descrição", "Qtd", "Preço unit.", "Preço total", "Prazo" },
                d.Itens.Select(i => new[] { i.Item, i.PartNumber, i.Descricao, i.Qtd.ToString(), i.Unitario, i.Total, i.Prazo }), direita: new[] { 3, 4, 5 }));
        }
        sb.Append($"<p style=\"margin-top:18px\">Atenciosamente,<br>{H(remetente)}</p>");
        sb.Append(Fechar());

        var texto = new StringBuilder();
        texto.AppendLine("APROVAÇÃO DE PRICING CONSOLIDADO").AppendLine();
        texto.AppendLine(nome.Length > 0 ? nome + "," : "Prezados,").AppendLine();
        texto.AppendLine($"Por gentileza aprovar os valores da {deQue} conforme abaixo:").AppendLine();
        foreach (var (k, v) in resumo) texto.AppendLine($"{k}: {v}");
        texto.AppendLine();
        foreach (var (k, imp, v) in precos) texto.AppendLine($"{k}: {v}{(imp != "—" ? $" (impostos {imp})" : "")}");
        texto.AppendLine();
        foreach (var (k, v) in margens) texto.AppendLine($"{k}: {v}");
        texto.AppendLine().AppendLine("Atenciosamente,").AppendLine(remetente);

        return new EmailDaProposta(para, cc, AssuntoDaAprovacao(o), sb.ToString(), texto.ToString());
    }

    // ================= envio da proposta ao cliente =================

    private sealed record TextosDoEnvio(string Assunto, string Saudacao, string SaudacaoSemNome, string Anexo, string Contatar, string Condicoes, string Despedida);

    private static TextosDoEnvio TextosDe(string idioma) => IdiomasDaProposta.Normalizar(idioma) switch
    {
        IdiomasDaProposta.Ingles => new("HOWDEN - Commercial Proposal {0} - {1}", "Dear {0},", "Dear Sirs,",
            "Please find attached our Commercial Proposal {0}, prepared for your evaluation.",
            "To schedule a visit, or for technical and/or commercial matters, please contact:",
            "The selling price presented in our proposal is conditional upon acceptance of Howden's standard “Conditions of Supply”, attached. Any different condition is subject to commercial renegotiation.",
            "Best regards,"),
        IdiomasDaProposta.Espanhol => new("HOWDEN - Oferta Comercial {0} - {1}", "Estimado/a {0},", "Estimados señores,",
            "Adjuntamos nuestra Oferta Comercial {0}, elaborada para su evaluación.",
            "Para agendar una visita, asuntos técnicos y/o comerciales, por favor contactar:",
            "El precio de venta presentado en nuestra oferta está condicionado a la aceptación de las “Condiciones de Suministro” estándar de Howden, adjuntas. Cualquier condición diferente está sujeta a renegociación comercial.",
            "Saludos cordiales,"),
        _ => new("HOWDEN - Proposta Comercial {0} - {1}", "Prezado(a) {0},", "Prezados Senhores,",
            "Segue em anexo nossa Proposta Comercial {0}, elaborada para sua avaliação.",
            "Para agendamento de visita, assuntos técnicos e/ou comerciais, solicitamos contatar:",
            "O preço de venda apresentado em nossa Oferta está condicionado ao aceite das “Condições de Fornecimento” padrão Howden, anexa. Qualquer condição diferente está sujeita à renegociação comercial.",
            "Atenciosamente,"),
    };

    public static string AssuntoDaProposta(CommercialOffer o) =>
        string.Format(TextosDe(o.Idioma).Assunto, PropostaWord.Referencia(o), PropostaWord.Ou(o.ClientName, "")).TrimEnd(' ', '-');

    /// <param name="cartao">as linhas do contato Howden (o vendedor da proposta)</param>
    public static EmailDaProposta Proposta(CommercialOffer o, IReadOnlyList<string> cartao, string para, string cc, string remetente)
    {
        var t = TextosDe(o.Idioma);
        var nome = (o.AttentionTo ?? "").Trim();
        var saudacao = nome.Length > 0 ? string.Format(t.Saudacao, nome) : t.SaudacaoSemNome;
        var anexo = string.Format(t.Anexo, PropostaWord.Referencia(o));

        var sb = new StringBuilder();
        sb.Append(Abrir());
        sb.Append($"<p>{H(saudacao)}</p>");
        sb.Append($"<p>{H(anexo)}</p>");
        if (cartao.Count > 0)
        {
            sb.Append($"<p>{H(t.Contatar)}</p>");
            sb.Append("<p style=\"margin-left:12px;border-left:3px solid #004785;padding-left:10px\">");
            sb.Append(string.Join("<br>", cartao.Select((l, i) => i == 0 ? $"<b>{H(l)}</b>" : H(l))));
            sb.Append("</p>");
        }
        sb.Append($"<p>{H(t.Condicoes)}</p>");
        sb.Append($"<p style=\"margin-top:18px\">{H(t.Despedida)}<br>{H(remetente)}<br>Howden South America · Chart Industries</p>");
        sb.Append(Fechar());

        var texto = new StringBuilder();
        texto.AppendLine(saudacao).AppendLine().AppendLine(anexo).AppendLine();
        if (cartao.Count > 0)
        {
            texto.AppendLine(t.Contatar);
            foreach (var l in cartao) texto.AppendLine("  " + l);
            texto.AppendLine();
        }
        texto.AppendLine(t.Condicoes).AppendLine().AppendLine(t.Despedida).AppendLine(remetente).AppendLine("Howden South America · Chart Industries");

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
