using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AfmHsa.Data;
using AfmHsa.Models;
using Microsoft.AspNetCore.Components;

namespace AfmHsa.Components.Pages;

/// <summary>
/// As notas da proposta (o preço estourou, o cliente pediu desconto, cuidado
/// com…) — informativa, de atenção ou crítica — e os custos adicionais
/// (embalagens, engenharia, exportação, outros).
/// </summary>
public partial class NovaOferta
{
    [Inject] private CustosPadraoRepository CustosPadrao { get; set; } = default!;

    // ================= notas =================

    private bool _ehAdm;
    private bool _verNotas;
    private string _notaTexto = "";
    private string _notaNivel = NotaDaProposta.Atencao;
    private string? _msgNotas;

    private static readonly (string Nivel, string Icone, string Nome)[] NiveisDeNota =
    {
        (NotaDaProposta.Info, "ℹ️", "Informativa"),
        (NotaDaProposta.Atencao, "⚠️", "Atenção"),
        (NotaDaProposta.Critica, "⛔", "Crítica"),
    };

    private static (string Nivel, string Icone, string Nome) NivelDaNota(string? nivel) =>
        NiveisDeNota.FirstOrDefault(n => n.Nivel == nivel) is { Nivel: not null } n ? n : NiveisDeNota[0];

    /// <summary>As notas abertas que pedem atenção (crítica primeiro): elas ficam em destaque no topo da proposta.</summary>
    private List<NotaDaProposta> NotasEmDestaque => _o.Notas
        .Where(n => !n.Resolvida && n.Nivel is NotaDaProposta.Critica or NotaDaProposta.Atencao)
        .OrderBy(n => n.Nivel == NotaDaProposta.Critica ? 0 : 1).ToList();

    private int NotasAbertas => _o.Notas.Count(n => !n.Resolvida);

    private void AdicionarNota()
    {
        var texto = _notaTexto.Trim();
        if (texto.Length == 0) { _msgNotas = "Escreva a nota antes de adicionar."; return; }
        _o.Notas.Insert(0, new NotaDaProposta
        {
            Texto = texto, Nivel = _notaNivel, Autor = PropostaWord.Ou(_usuario, "—"),
            Data = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        });
        _notaTexto = "";
        GravarNotas("Nota adicionada.");
    }

    private void ResolverNota(NotaDaProposta n)
    {
        n.Resolvida = !n.Resolvida;
        GravarNotas(n.Resolvida ? "Nota marcada como resolvida." : "Nota reaberta.");
    }

    private bool PodeApagarNota(NotaDaProposta n) => _ehAdm || string.Equals(n.Autor, _usuario, StringComparison.OrdinalIgnoreCase);

    private void ApagarNota(NotaDaProposta n)
    {
        if (!PodeApagarNota(n)) return;
        _o.Notas.Remove(n);
        GravarNotas("Nota apagada.");
    }

    /// <summary>
    /// A nota é gravada na hora (só as notas: o resto da proposta continua
    /// esperando o Salvar). Proposta ainda não salva: a nota vai com ela.
    /// </summary>
    private void GravarNotas(string msg)
    {
        if (_o.Id.Length == 0 || OffersRepo.Get(_o.Id) is not { } gravada)
        {
            _msgNotas = msg + " Ela é gravada junto com a proposta, ao salvar.";
            return;
        }
        gravada.Notas = Clonar(_o.Notas);
        OffersRepo.Save(gravada);
        _msgNotas = msg;
    }

    private static string DataDaNota(string data) =>
        DateTime.TryParseExact(data, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : data;

    /// <summary>A foto que diz se há alteração não salva — sem as notas, que já se gravam sozinhas.</summary>
    private static readonly JsonSerializerOptions SemNotas = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                ti =>
                {
                    if (ti.Type != typeof(CommercialOffer)) return;
                    if (ti.Properties.FirstOrDefault(p => p.Name == nameof(CommercialOffer.Notas)) is { } p) ti.Properties.Remove(p);
                },
            },
        },
    };

    // ================= custos adicionais =================

    /// <summary>O custo dos itens (a base do "% do custo dos itens").</summary>
    private static decimal CustoDosItensParaAdicionais(CustoDosItens c) => c.Base;

    /// <summary>A proposta nasce (ou a de antes destes custos é revisada) com os padrões do dia.</summary>
    private void CustosAdicionaisPadrao()
    {
        try { _o.CustosAdicionais = CustosAdicionaisDaProposta.ParaProposta(CustosPadrao.Ler()); }
        catch (Exception) { _o.CustosAdicionais = CustosAdicionaisDaProposta.ParaProposta(Array.Empty<CustoAdicional>()); }
    }

    /// <summary>Digitar troca o valor; apagar (ou digitar o próprio padrão) volta ao padrão.</summary>
    private void MudarCustoAdicional(CustoAdicional c, ChangeEventArgs e, decimal custoDosItens)
    {
        var v = Numero(e);
        if (v is null || v < 0m) { c.Valor = null; return; }
        var padrao = CustosAdicionaisDaProposta.PadraoEmDinheiro(_o, c, custoDosItens);
        c.Valor = padrao is decimal p && Math.Abs(p - v.Value) < 0.005m ? null : Math.Round(v.Value, 2);
    }

    private string DescricaoDoPadrao(CustoAdicional c, decimal custoDosItens) =>
        c.Tipo == CustoAdicional.Percentual
            ? $"padrão {(c.Padrao * 100m).ToString("0.##", PtBr)}% dos itens = {Cu(Math.Round(c.Padrao * custoDosItens, 2))}"
            : CustosAdicionaisDaProposta.PadraoEmDinheiro(_o, c, custoDosItens) is decimal p
                ? $"padrão {Cu(p)}"
                : $"padrão {Fmt.Brl(c.Padrao)} (em R$): digite o valor em {Rc}";
}
