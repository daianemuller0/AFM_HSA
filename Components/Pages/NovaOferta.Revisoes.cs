using System.Globalization;
using AfmHsa.Data;
using AfmHsa.Models;
using Microsoft.AspNetCore.Components;

namespace AfmHsa.Components.Pages;

/// <summary>
/// Revisão nunca grava por cima da anterior: trocar o número da revisão de
/// uma proposta já gravada (campo Revisão, em Cliente e condições) faz dela
/// um registro NOVO ao salvar — proposta, itens e referências —, e a revisão
/// gravada fica como o cliente recebeu (como o botão "Nova revisão").
/// </summary>
public partial class NovaOferta
{
    /// <summary>A revisão gravada de onde esta saiu (antes de salvar): para voltar atrás se o número voltar.</summary>
    private sealed record RevisaoDeOrigem(string Id, string Revisao, string Status, string OfferDate, string Ano, string CreatedAt,
        string PreparedBy, string OurReference, decimal RiscoAdicional, decimal? ProtectedMargin, List<CustoAdicional> CustosAdicionais,
        string AutoAno, Dictionary<CommercialOfferItem, string> IdsDosItens);

    private RevisaoDeOrigem? _revisaoDe;

    /// <summary>Troca quando o número digitado é recusado: o campo é refeito e volta a mostrar o gravado.</summary>
    private int _chaveRevisao;

    private void MudarRevisao(ChangeEventArgs e)
    {
        var nova = (e.Value?.ToString() ?? "").Trim();
        _erro = null;

        // voltou ao número da revisão gravada antes de salvar: volta a ser a mesma proposta
        if (_revisaoDe is { } de && Igual(nova, de.Revisao))
        {
            _o.Revisao = nova;
            _o.Id = de.Id;
            foreach (var it in _items)
            {
                if (de.IdsDosItens.TryGetValue(it, out var id)) { it.Id = id; it.OfferId = de.Id; }
            }
            _idsGravados = de.IdsDosItens.Values.Where(id => id.Length > 0).ToHashSet();
            (_o.Status, _o.OfferDate, _o.Ano, _o.CreatedAt, _o.PreparedBy) = (de.Status, de.OfferDate, de.Ano, de.CreatedAt, de.PreparedBy);
            (_o.OurReference, _o.RiscoAdicional, _o.ProtectedMargin, _o.CustosAdicionais, _autoAno) =
                (de.OurReference, de.RiscoAdicional, de.ProtectedMargin, de.CustosAdicionais, de.AutoAno);
            // a consolidação da gravada volta junto (a revisão nova tinha saído sem ela)
            if (OffersRepo.Get(de.Id) is { } gravadaDe)
            {
                Consolidacao.CopiarMarcas(gravadaDe, _o);
                var datas = OffersRepo.ItemsFor(de.Id).ToDictionary(i => i.Id, i => i.DataEntrega);
                foreach (var it in _items) it.DataEntrega = datas.GetValueOrDefault(it.Id, "");
            }
            _revisaoDe = null;
            _msg = $"De volta à revisão {nova}: ao salvar, grava nela mesma.";
            return;
        }

        if (_o.Id.Length == 0 || _revisaoDe is not null) { _o.Revisao = nova; return; }   // proposta nova, ou já separada
        var gravada = OffersRepo.Get(_o.Id);
        var revGravada = PropostaWord.Ou(gravada?.Revisao, "0");
        if (gravada is null || Igual(PropostaWord.Ou(nova, "0"), revGravada)) { _o.Revisao = nova; return; }

        // o número já é de outra revisão gravada: não deixa duplicar
        _anteriores = OffersRepo.All();
        if (_anteriores.Any(a => a.Id != _o.Id && Igual(a.OfferCode, _o.OfferCode) && Igual(PropostaWord.Ou(a.Revisao, "0"), PropostaWord.Ou(nova, "0"))))
        {
            _erro = $"A revisão {nova} da {_o.OfferCode} já existe: abra ela em Propostas, ou use outro número (a próxima livre é a {ProximaRevisao(_o.OfferCode)}).";
            _o.Revisao = revGravada;
            _chaveRevisao++;
            return;
        }

        // separa: daqui em diante é a revisão nova — ao salvar vira um registro novo, e a gravada não muda
        _revisaoDe = new RevisaoDeOrigem(_o.Id, revGravada, _o.Status, _o.OfferDate, _o.Ano, _o.CreatedAt, _o.PreparedBy,
            _o.OurReference, _o.RiscoAdicional, _o.ProtectedMargin, _o.CustosAdicionais, _autoAno,
            _items.ToDictionary(i => i, i => i.Id, ReferenceEqualityComparer.Instance as IEqualityComparer<CommercialOfferItem>
                                                     ?? EqualityComparer<CommercialOfferItem>.Default));
        _o.Revisao = nova;
        _o.Id = "";
        foreach (var it in _items) { it.Id = ""; it.OfferId = ""; }
        _idsGravados = new();
        var sua = _o.SuaReferencia;
        ComoNovaDoDia();
        _o.SuaReferencia = sua;
        _origem = "revisao";
        _msg = $"Revisão {nova}: ao salvar, vira um registro novo da {_o.OfferCode}. A revisão {revGravada} continua gravada como está.";

        static bool Igual(string? a, string? b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
