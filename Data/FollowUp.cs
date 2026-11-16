using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AfmHsa.Models;

namespace AfmHsa.Data;

/// <summary>Um follow-up da proposta: agendado (uma data) e, depois, feito.</summary>
public sealed class EtapaDoFollowUp
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    /// <summary>yyyy-MM-dd</summary>
    public string Data { get; set; } = "";
    /// <summary>O do fim da validade, agendado ao marcar a proposta como enviada.</summary>
    public bool Automatico { get; set; }
    /// <summary>O que fazer (ao agendar) e o que aconteceu (ao registrar).</summary>
    public string Nota { get; set; } = "";
    /// <summary>yyyy-MM-dd HH:mm; vazio = ainda por fazer.</summary>
    public string FeitoEm { get; set; } = "";
    public string FeitoPor { get; set; } = "";
    /// <summary>Quando o e-mail de follow-up foi aberto no Outlook (yyyy-MM-dd HH:mm).</summary>
    public string EmailEm { get; set; } = "";

    public bool Feito => FeitoEm.Length > 0;
}

/// <summary>O follow-up de uma proposta enviada (entidade "followups", uma linha por proposta).</summary>
public sealed class FollowUpDaProposta
{
    public string OfferId { get; set; } = "";
    /// <summary>yyyy-MM-dd</summary>
    public string EnviadaEm { get; set; } = "";
    public string EnviadaPor { get; set; } = "";
    public List<EtapaDoFollowUp> Etapas { get; set; } = new();
    /// <summary>yyyy-MM-dd HH:mm; vazio = o follow-up continua.</summary>
    public string EncerradoEm { get; set; } = "";
    public string EncerradoPor { get; set; } = "";
    public string Motivo { get; set; } = "";
    /// <summary>E-mail do contato digitado aqui (quando a proposta não tem).</summary>
    public string Para { get; set; } = "";

    public bool Encerrado => EncerradoEm.Length > 0;

    /// <summary>O próximo follow-up por fazer (o mais antigo agendado), ou null.</summary>
    public EtapaDoFollowUp? Proxima => Etapas.Where(e => !e.Feito && e.Data.Length > 0).OrderBy(e => e.Data, StringComparer.Ordinal).FirstOrDefault();
}

/// <summary>O modelo do e-mail de follow-up numa língua (os campos entre chaves são trocados pelos da proposta).</summary>
public sealed class ModeloDeFollowUp
{
    public string Idioma { get; set; } = IdiomasDaProposta.Portugues;
    public string Assunto { get; set; } = "";
    public string Corpo { get; set; } = "";
}

/// <summary>
/// As regras do follow-up: a proposta enviada tem a validade (30 dias, ou o
/// que o texto de validade disser); no fim dela vem o primeiro follow-up,
/// com o e-mail cobrando uma posição do cliente. Proposta por portal (Portal
/// diferente de "não" e de "preencher") não tem follow-up nem e-mail.
/// </summary>
public static class FollowUps
{
    public const int DiasPadrao = 30;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Os dias da validade, lidos do texto ("… é de 30 (trinta) dias", "valid for 45 days"); sem número, 30.</summary>
    public static int DiasDeValidade(string? textoDaValidade)
    {
        var m = Regex.Match(textoDaValidade ?? "", @"(\d{1,3})\s*(?:\([^)]*\)\s*)?(?:dias|d[ií]as|days|dia|day)\b", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var d) && d > 0 ? d : DiasPadrao;
    }

    /// <summary>Proposta por portal: o campo Portal diferente de "não" e de "preencher" (vazio).</summary>
    public static bool PorPortal(CommercialOffer o)
    {
        var p = (o.Portal ?? "").Trim();
        return p.Length > 0 && !Textos.Igual(p, "não") && !Textos.Igual(p, "nao") && !Textos.Igual(p, "preencher");
    }

    public static bool Enviada(CommercialOffer o) => Textos.Igual(o.Status, "Enviada");

    public static DateTime? Data(string? texto) =>
        DateTime.TryParseExact((texto ?? "").Trim().Length >= 10 ? texto!.Trim()[..10] : "", "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d : null;

    public static string Texto(DateTime d) => d.ToString("yyyy-MM-dd", Inv);

    /// <summary>A data do envio: a registrada ao marcar como enviada; sem ela (enviada antes do follow-up), a data da proposta.</summary>
    public static DateTime DataDoEnvio(CommercialOffer o, FollowUpDaProposta? f) =>
        Data(f?.EnviadaEm) ?? Data(o.OfferDate) ?? DateTime.Today;

    public static DateTime FimDaValidade(CommercialOffer o, FollowUpDaProposta? f) =>
        DataDoEnvio(o, f).AddDays(DiasDeValidade(o.ValidityText));

    /// <summary>O follow-up novo de uma proposta enviada agora: o primeiro, no fim da validade.</summary>
    public static FollowUpDaProposta Novo(CommercialOffer o, DateTime enviadaEm, string usuario)
    {
        var f = new FollowUpDaProposta { OfferId = o.Id, EnviadaEm = Texto(enviadaEm), EnviadaPor = usuario };
        f.Etapas.Add(new EtapaDoFollowUp
        {
            Data = Texto(FimDaValidade(o, f)),
            Automatico = true,
            Nota = "Fim da validade: e-mail cobrando uma posição do cliente.",
        });
        return f;
    }

    // ================= a lista da guia =================

    public enum Situacao { Atrasado, Hoje, EmBreve, Agendado, SemData, Encerrado }

    /// <param name="Contato">nome de quem recebe (o "A/C" da proposta)</param>
    /// <param name="Email">o e-mail do contato: o da proposta, o digitado no follow-up ou o de outra proposta do mesmo contato</param>
    /// <param name="EmailDeOnde">"" = da proposta; senão de onde veio</param>
    public sealed record Linha(CommercialOffer Oferta, FollowUpDaProposta FollowUp, bool Gravado, Situacao Situacao,
        EtapaDoFollowUp? Proxima, string Contato, string Email, string EmailDeOnde);

    /// <summary>
    /// As propostas em follow-up: enviadas, fora de portal e só a última
    /// revisão enviada de cada número (a anterior foi substituída por ela).
    /// As enviadas antes do follow-up existir entram com o follow-up do fim
    /// da validade contado da data da proposta (gravado no primeiro uso).
    /// </summary>
    public static List<Linha> Montar(IReadOnlyList<CommercialOffer> ofertas, IReadOnlyDictionary<string, FollowUpDaProposta> gravados, DateTime hoje)
    {
        var enviadas = ofertas.Where(Enviada).ToList();
        var ultimas = enviadas
            .GroupBy(o => Chave(o.OfferCode, o.Id))
            .Select(g => g.OrderByDescending(o => int.TryParse(OfferRepository.RevisaoNormal(o.Revisao), out var n) ? n : 0)
                          .ThenByDescending(o => o.CreatedAt, StringComparer.Ordinal).First())
            .Where(o => !PorPortal(o))
            .ToList();

        return ultimas.Select(o =>
        {
            var gravado = gravados.TryGetValue(o.Id, out var f);
            f ??= Novo(o, DataDoEnvio(o, null), "");
            var proxima = f.Encerrado ? null : f.Proxima;
            var situacao = SituacaoDe(f, proxima, hoje);
            var (email, deOnde) = EmailDoContato(o, f, ofertas);
            return new Linha(o, f, gravado, situacao, proxima, (o.AttentionTo ?? "").Trim(), email, deOnde);
        })
        .OrderBy(l => l.Situacao)
        .ThenBy(l => l.Proxima?.Data ?? "9999", StringComparer.Ordinal)
        .ToList();
    }

    /// <summary>Por portal: as enviadas (última revisão) que ficam sem follow-up, para mostrar o porquê.</summary>
    public static List<CommercialOffer> PorPortalEnviadas(IReadOnlyList<CommercialOffer> ofertas) =>
        ofertas.Where(Enviada)
            .GroupBy(o => Chave(o.OfferCode, o.Id))
            .Select(g => g.OrderByDescending(o => int.TryParse(OfferRepository.RevisaoNormal(o.Revisao), out var n) ? n : 0).First())
            .Where(PorPortal)
            .OrderBy(o => o.OfferCode)
            .ToList();

    private static string Chave(string codigo, string id) => (codigo ?? "").Trim().Length > 0 ? codigo.Trim().ToUpperInvariant() : "#" + id;

    public static Situacao SituacaoDe(FollowUpDaProposta f, EtapaDoFollowUp? proxima, DateTime hoje)
    {
        if (f.Encerrado) return Situacao.Encerrado;
        if (proxima is null || Data(proxima.Data) is not { } d) return Situacao.SemData;
        if (d.Date < hoje.Date) return Situacao.Atrasado;
        if (d.Date == hoje.Date) return Situacao.Hoje;
        return (d.Date - hoje.Date).TotalDays <= 7 ? Situacao.EmBreve : Situacao.Agendado;
    }

    /// <summary>O e-mail do contato: o digitado no follow-up, o da proposta, ou o de outra proposta do mesmo cliente e contato.</summary>
    public static (string Email, string DeOnde) EmailDoContato(CommercialOffer o, FollowUpDaProposta f, IReadOnlyList<CommercialOffer> todas)
    {
        if (f.Para.Trim().Length > 0) return (f.Para.Trim(), "digitado no follow-up");
        if ((o.ContatoEmail ?? "").Trim().Length > 0) return (o.ContatoEmail.Trim(), "");
        var nome = (o.AttentionTo ?? "").Trim();
        if (nome.Length == 0) return ("", "");
        var outra = todas.Where(x => x.Id != o.Id && Textos.Igual(x.ClientName, o.ClientName) && Textos.Igual(x.AttentionTo, nome)
                                     && (x.ContatoEmail ?? "").Trim().Length > 0)
            .OrderByDescending(x => x.CreatedAt, StringComparer.Ordinal).FirstOrDefault();
        return outra is null ? ("", "") : (outra.ContatoEmail.Trim(), $"da proposta {outra.OfferCode} rev {PropostaWord.Ou(outra.Revisao, "0")}");
    }

    /// <summary>Quantos follow-ups estão para hoje ou atrasados (o número no menu).</summary>
    public static int Pendentes(IReadOnlyList<Linha> linhas) => linhas.Count(l => l.Situacao is Situacao.Atrasado or Situacao.Hoje);

    // ================= o e-mail =================

    /// <summary>Os campos que o modelo aceita, com o que cada um vira.</summary>
    public static readonly (string Campo, string Descricao)[] Campos =
    {
        ("{contato}", "o nome do contato (A/C da proposta)"),
        ("{cliente}", "o cliente"),
        ("{proposta}", "o número da proposta com a revisão (HSA…-C02)"),
        ("{sua_referencia}", "a referência do cliente, entre parênteses (some se não houver)"),
        ("{data_envio}", "a data em que a proposta foi enviada"),
        ("{validade}", "a data em que a validade termina"),
        ("{dias_validade}", "os dias de validade"),
        ("{vendedores}", "os vendedores da proposta, com telefone e e-mail"),
        ("{assunto_proposta}", "o assunto do e-mail em que a proposta foi enviada"),
    };

    public static ModeloDeFollowUp ModeloPadrao(string idioma) => IdiomasDaProposta.Normalizar(idioma) switch
    {
        IdiomasDaProposta.Ingles => new ModeloDeFollowUp
        {
            Idioma = IdiomasDaProposta.Ingles,
            Assunto = "Follow-up | {assunto_proposta}",
            Corpo = "Dear {contato},\n\n" +
                    "On {data_envio} we sent you our Commercial Proposal {proposta}{sua_referencia}, valid until {validade}.\n\n" +
                    "We would like to know whether you have had the opportunity to review it and if there is any feedback on the quotation. " +
                    "Should you need any technical or commercial clarification, or an adjustment to the proposal, we remain at your disposal.\n\n" +
                    "For your convenience, the proposal is attached again.\n\n" +
                    "For any technical or commercial matter, please contact:\n{vendedores}\n\n" +
                    "Thank you for your attention.",
        },
        IdiomasDaProposta.Espanhol => new ModeloDeFollowUp
        {
            Idioma = IdiomasDaProposta.Espanhol,
            Assunto = "Seguimiento | {assunto_proposta}",
            Corpo = "Estimado(a) {contato},\n\n" +
                    "El {data_envio} le enviamos nuestra Propuesta Comercial {proposta}{sua_referencia}, válida hasta el {validade}.\n\n" +
                    "Quisiéramos saber si ha tenido la oportunidad de analizarla y si hay alguna posición sobre la cotización. " +
                    "Si necesita alguna aclaración técnica o comercial, o un ajuste en la propuesta, quedamos a su disposición.\n\n" +
                    "Para su comodidad, adjuntamos nuevamente la propuesta.\n\n" +
                    "Para cualquier asunto técnico o comercial, póngase en contacto con:\n{vendedores}\n\n" +
                    "Gracias por su atención.",
        },
        _ => new ModeloDeFollowUp
        {
            Idioma = IdiomasDaProposta.Portugues,
            Assunto = "Follow-up | {assunto_proposta}",
            Corpo = "Prezado(a) {contato},\n\n" +
                    "Em {data_envio} enviamos nossa Proposta Comercial {proposta}{sua_referencia}, com validade até {validade}.\n\n" +
                    "Gostaríamos de saber se já foi possível analisá-la e se há alguma posição sobre a cotação. " +
                    "Caso seja necessário algum esclarecimento técnico ou comercial, ou um ajuste na proposta, estamos à disposição.\n\n" +
                    "Para facilitar, segue a proposta novamente em anexo.\n\n" +
                    "Para qualquer assunto técnico ou comercial, entre em contato com:\n{vendedores}\n\n" +
                    "Agradecemos a atenção.",
        },
    };

    /// <param name="contatos">uma linha por vendedor (<see cref="EmailsDaProposta.LinhaDeContato"/>)</param>
    /// <param name="remetente">"" quando o Outlook põe a assinatura de quem envia</param>
    public static EmailDaProposta Email(CommercialOffer o, FollowUpDaProposta f, ModeloDeFollowUp modelo, string contato,
        IReadOnlyList<string> contatos, string para, string cc, string remetente)
    {
        var idioma = IdiomasDaProposta.Normalizar(o.Idioma);
        var suaRef = (o.SuaReferencia ?? "").Trim();
        var rotuloRef = idioma switch { IdiomasDaProposta.Ingles => "your reference", IdiomasDaProposta.Espanhol => "su referencia", _ => "sua referência" };
        var valores = new Dictionary<string, string>
        {
            ["{contato}"] = contato.Trim(),
            ["{cliente}"] = (o.ClientName ?? "").Trim(),
            ["{proposta}"] = PropostaWord.Referencia(o),
            ["{sua_referencia}"] = suaRef.Length > 0 ? $" ({rotuloRef}: {suaRef})" : "",
            ["{data_envio}"] = DataDoEnvio(o, f).ToString("dd/MM/yyyy", Inv),
            ["{validade}"] = FimDaValidade(o, f).ToString("dd/MM/yyyy", Inv),
            ["{dias_validade}"] = DiasDeValidade(o.ValidityText).ToString(Inv),
            ["{vendedores}"] = string.Join("\n", contatos.Where(c => c.Trim().Length > 0)),
            ["{assunto_proposta}"] = EmailsDaProposta.AssuntoDaProposta(o),
        };
        string Trocar(string s)
        {
            foreach (var (k, v) in valores) s = s.Replace(k, v, StringComparison.OrdinalIgnoreCase);
            // sem o nome do contato: "Prezado(a) ," vira "Prezado(a),"
            return Regex.Replace(s, @"[ \t]+,", ",");
        }

        var assunto = Trocar(modelo.Assunto).Trim();
        var modeloDoCorpo = modelo.Corpo.Replace("\r\n", "\n");
        // proposta sem vendedor: sai o parágrafo dos contatos inteiro ("entre em contato com:" sem ninguém)
        if (valores["{vendedores}"].Length == 0)
            modeloDoCorpo = string.Join("\n\n", modeloDoCorpo.Split("\n\n")
                .Where(p => !p.Contains("{vendedores}", StringComparison.OrdinalIgnoreCase)));
        var corpo = Regex.Replace(Trocar(modeloDoCorpo).Trim(), @"\n{3,}", "\n\n");

        var html = new StringBuilder("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#1f2937;line-height:1.45\">");
        foreach (var p in corpo.Split("\n\n"))
            html.Append("<p>").Append(string.Join("<br>", p.Split('\n').Select(WebUtility.HtmlEncode))).Append("</p>");
        var texto = new StringBuilder(corpo).AppendLine();
        if (remetente.Trim().Length > 0)
        {
            html.Append($"<p>{WebUtility.HtmlEncode(remetente.Trim())}<br>Howden | Chart Industries</p>");
            texto.AppendLine().AppendLine(remetente.Trim()).AppendLine("Howden | Chart Industries");
        }
        html.Append("</div>");
        return new EmailDaProposta(para, cc, assunto, html.ToString(), texto.ToString());
    }
}

/// <summary>Grava o follow-up de cada proposta (entidade "followups") e o modelo do e-mail ("followup_modelo").</summary>
public sealed class FollowUpRepository
{
    private const string Entidade = "followups";
    private const string EntidadeModelo = "followup_modelo";
    private static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = false };
    private readonly ParquetStore _s;
    public FollowUpRepository(ParquetStore s) => _s = s;

    public Dictionary<string, FollowUpDaProposta> Todos() =>
        _s.ReadLatest(Entidade, "id, dados", r => (Id: Fmt.S(r, 0), Dados: Fmt.S(r, 1)))
            .Select(x => Ler(x.Id, x.Dados))
            .Where(f => f is not null)
            .ToDictionary(f => f!.OfferId, f => f!);

    private static FollowUpDaProposta? Ler(string id, string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var f = JsonSerializer.Deserialize<FollowUpDaProposta>(json, Opcoes);
            if (f is null) return null;
            f.OfferId = id;
            return f;
        }
        catch (JsonException) { return null; }
    }

    public FollowUpDaProposta? Do(string offerId) => Todos().GetValueOrDefault(offerId);

    public void Gravar(FollowUpDaProposta f)
    {
        _s.WriteRow(Entidade, new KeyValuePair<string, object?>[] { new("id", f.OfferId), new("dados", JsonSerializer.Serialize(f, Opcoes)) });
    }

    /// <summary>
    /// A proposta saiu para o cliente: registra o envio e agenda o primeiro
    /// follow-up no fim da validade. Se já havia follow-up (marcada de novo),
    /// fica o que estava. Devolve o follow-up (null = por portal, sem follow-up).
    /// </summary>
    public FollowUpDaProposta? MarcarEnviada(CommercialOffer o, string usuario, DateTime quando)
    {
        if (FollowUps.PorPortal(o) || o.Id.Length == 0) return null;
        if (Do(o.Id) is { } ja) return ja;
        var f = FollowUps.Novo(o, quando, usuario);
        Gravar(f);
        return f;
    }

    // ---- o número do menu: calculado em segundo plano, e só de novo quando algo mudou ----
    private static (string Chave, int Valor)? _pendentes;
    private static Task<int>? _calculando;
    private static readonly object _trava = new();

    /// <summary>O último número calculado (o menu mostra na hora, sem esperar).</summary>
    public static int? PendentesConhecidos => _pendentes?.Valor;

    public static void Esquecer() { }

    /// <summary>
    /// Follow-ups para hoje ou atrasados, fora da tela (em segundo plano).
    /// Só relê quando a pasta das propostas ou a dos follow-ups mudou, ou o dia
    /// virou (a leitura das propostas é a mesma das telas, guardada enquanto nada muda).
    /// </summary>
    public Task<int> PendentesAsync(OfferRepository ofertas)
    {
        lock (_trava)
        {
            if (_calculando is { IsCompleted: false } t) return t;
            _calculando = Task.Run(() =>
            {
                var chave = $"{DateTime.Today:yyyyMMdd}|{_s.Assinatura("commercialOffers")}|{_s.Assinatura(Entidade)}";
                if (_pendentes is { } p && p.Chave == chave) return p.Valor;
                var valor = FollowUps.Pendentes(FollowUps.Montar(ofertas.All(), Todos(), DateTime.Today));
                _pendentes = (chave, valor);
                return valor;
            });
            return _calculando;
        }
    }

    // ---- o modelo do e-mail ----

    public ModeloDeFollowUp Modelo(string idioma)
    {
        var lingua = IdiomasDaProposta.Normalizar(idioma);
        var gravado = _s.ReadLatest(EntidadeModelo, "id, assunto, corpo", r => (Id: Fmt.S(r, 0), Assunto: Fmt.S(r, 1), Corpo: Fmt.S(r, 2)))
            .FirstOrDefault(x => x.Id == lingua);
        return gravado.Id is null || gravado.Corpo.Trim().Length == 0
            ? FollowUps.ModeloPadrao(lingua)
            : new ModeloDeFollowUp { Idioma = lingua, Assunto = gravado.Assunto, Corpo = gravado.Corpo };
    }

    public void GravarModelo(ModeloDeFollowUp m) =>
        _s.WriteRow(EntidadeModelo, new KeyValuePair<string, object?>[]
        {
            new("id", IdiomasDaProposta.Normalizar(m.Idioma)), new("assunto", m.Assunto), new("corpo", m.Corpo),
        });

    /// <summary>Volta a língua ao modelo padrão.</summary>
    public void ModeloPadrao(string idioma) =>
        _s.WriteRow(EntidadeModelo, new KeyValuePair<string, object?>[] { new("id", IdiomasDaProposta.Normalizar(idioma)) }, deleted: true);
}
