using AfmHsa.Data;
using AfmHsa.Models;
using Microsoft.AspNetCore.Components;

namespace AfmHsa.Components.Pages;

/// <summary>
/// As listas suspensas dos itens, que aprendem com o que a equipe digita
/// (<see cref="ListasCompartilhadasRepository"/>): equipamento (um campo só —
/// equipamento e modelo são a mesma coisa), part number com a descrição,
/// serial e TAG de cada modelo. Ao salvar a proposta, o que foi digitado
/// entra nas listas e aparece para todos.
/// </summary>
public partial class NovaOferta
{
    [Inject] private ListasCompartilhadasRepository Listas { get; set; } = default!;

    private List<ValorDeLista> _listas = new();

    private void LerListas() { _listas = Listas.Todas(); _pnsConhecidos = null; }

    /// <summary>O que os itens desta proposta trazem de novo vai para as listas.</summary>
    private void GravarListas()
    {
        var novos = new List<(string, string, string, string)>();
        foreach (var it in _items.Where(i => i != _itemDemais))
        {
            var modelo = ModeloDoItem(it).Trim();
            novos.Add((ListasCompartilhadasRepository.Modelo, "", modelo, ""));
            if (it.Importado && it.OrigemImportacao.Trim().Length > 0)
                novos.Add((ListasCompartilhadasRepository.OrigemImportacao, "", it.OrigemImportacao.Trim(), ""));
            if (it.PartNumber.Trim().Length > 0)
                novos.Add((ListasCompartilhadasRepository.PartNumber, IntelligenceService.NormPn(it.PartNumber), it.PartNumber.Trim(), DescricaoSemPn(it)));
            if (modelo.Length > 0)
            {
                foreach (var sn in SerialDoItem(it).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    novos.Add((ListasCompartilhadasRepository.Serial, modelo, sn, ""));
                foreach (var tag in TagDoItem(it).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    novos.Add((ListasCompartilhadasRepository.Tag, modelo, tag, ""));
            }
        }
        try
        {
            Listas.Gravar(novos, _listas, _usuario);
            LerListas();
        }
        catch (Exception) { /* a lista é ajuda: não impede a proposta de salvar */ }
    }

    // ================= equipamento (= modelo) =================

    /// <summary>"VX 2025.00.00" — com o serial só quando a proposta tem dois equipamentos do mesmo modelo.</summary>
    private string RotuloCurto(EquipamentoDaProposta eq)
    {
        var modelo = (eq.Modelo ?? "").Trim();
        var repetido = _o.EquipamentosDaProposta.Count(x => Textos.Igual(x.Modelo, modelo)) > 1;
        return repetido && (eq.SerialNumber ?? "").Trim().Length > 0 ? $"{modelo} · SN {eq.SerialNumber!.Trim()}" : modelo;
    }

    /// <summary>O que o campo Equipamento mostra: o equipamento da proposta, ou o modelo digitado no item.</summary>
    private string EquipamentoDoItem(CommercialOfferItem it)
    {
        if (it.Modelo.Trim().Length > 0) return it.Modelo.Trim();
        return EquipDoItem(it) is { } eq ? RotuloCurto(eq) : it.EquipmentId.Length > 0 ? ModeloDe(it.EquipmentId) : "";
    }

    /// <summary>
    /// Um campo só para equipamento e modelo: o que for digitado ou escolhido
    /// liga o item no equipamento da proposta com esse modelo; se não houver,
    /// cria um não cadastrado (que passa a aparecer na lista).
    /// </summary>
    private void MudarEquipamentoDoItem(CommercialOfferItem it, ChangeEventArgs e, bool novo)
    {
        var t = (e.Value?.ToString() ?? "").Trim();
        if (t.Length == 0) { it.EquipmentId = ""; it.Modelo = ""; return; }
        var eq = _o.EquipamentosDaProposta.FirstOrDefault(x => Textos.Igual(RotuloCurto(x), t))
                 ?? _o.EquipamentosDaProposta.FirstOrDefault(x => Textos.Igual(x.Modelo, t));
        if (eq is not null)
        {
            it.EquipmentId = eq.Id;
            it.Modelo = ""; it.SerialNumber = ""; it.Tag = "";
            if (string.IsNullOrWhiteSpace(it.Ncm)) it.Ncm = ListasDaProposta.NcmPadrao();
            return;
        }
        it.EquipmentId = "";
        it.Modelo = t;
        if (!novo) LigarAoEquipamento(it);
    }

    /// <summary>A lista do campo Equipamento: os da proposta primeiro, depois os modelos já usados por alguém.</summary>
    private IEnumerable<(string Valor, string Rotulo)> OpcoesDeEquipamento()
    {
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var eq in _o.EquipamentosDaProposta.Where(x => (x.Modelo ?? "").Trim().Length > 0))
        {
            var v = RotuloCurto(eq);
            if (vistos.Add(v)) yield return (v, "nesta proposta" + (eq.Tag.Trim().Length > 0 ? $" · TAG {eq.Tag.Trim()}" : ""));
        }
        foreach (var m in ModelosConhecidos.Concat(_listas.Where(l => l.Lista == ListasCompartilhadasRepository.Modelo).Select(l => l.Valor)))
            if (vistos.Add(m.Trim())) yield return (m.Trim(), "");
    }

    // ================= part number (com a descrição) =================

    private List<(string Pn, string Descricao)>? _pnsConhecidos;

    /// <summary>Os part numbers já usados (listas e referências), com a última descrição de cada um.</summary>
    private List<(string Pn, string Descricao)> PartNumbersConhecidos() => _pnsConhecidos ??= _listas
        .Where(l => l.Lista == ListasCompartilhadasRepository.PartNumber).Select(l => (l.Valor, l.Extra, l.Data))
        .Concat(_refsCusto.Where(r => r.PartNumber.Trim().Length > 0).Select(r => (r.PartNumber.Trim(), r.Descricao, r.Data)))
        .GroupBy(x => IntelligenceService.NormPn(x.Item1))
        .Select(g => g.OrderByDescending(x => x.Item3).First())
        .Select(x => (x.Item1, x.Item2)).OrderBy(x => x.Item1).Take(3000).ToList();

    /// <summary>A descrição já conhecida do part number: a da BOM, senão a que alguém já digitou.</summary>
    private string DescricaoConhecida(string pn)
    {
        var n = IntelligenceService.NormPn(pn);
        if (n.Length == 0) return "";
        var bom = _bom.FirstOrDefault(b => IntelligenceService.NormPn(b.PartNumber) == n);
        var daBom = (bom?.DescricaoPt ?? "").Trim() is { Length: > 0 } pt ? pt : (bom?.DescricaoEn ?? "").Trim();
        if (daBom.Length > 0) return daBom;
        return PartNumbersConhecidos().FirstOrDefault(x => IntelligenceService.NormPn(x.Pn) == n).Descricao ?? "";
    }

    // ================= serial e TAG (os do modelo) =================

    /// <summary>Serial / TAG já usados com o modelo do equipamento não cadastrado.</summary>
    private IEnumerable<string> ValoresDoModeloEq(string lista, EquipamentoDaProposta eq) =>
        _listas.Where(l => l.Lista == lista && Textos.Igual(l.Chave, eq.Modelo) && (eq.Modelo ?? "").Trim().Length > 0)
            .Select(l => l.Valor).Distinct(StringComparer.OrdinalIgnoreCase).Take(100);

    private IEnumerable<string> ValoresDoModelo(string lista, CommercialOfferItem it)
    {
        var modelo = ModeloDoItem(it).Trim();
        if (modelo.Length == 0) return Array.Empty<string>();
        return _listas.Where(l => l.Lista == lista && Textos.Igual(l.Chave, modelo)).Select(l => l.Valor)
            .Concat(_o.EquipamentosDaProposta.Where(x => Textos.Igual(x.Modelo, modelo))
                .Select(x => lista == ListasCompartilhadasRepository.Serial ? x.SerialNumber : x.Tag))
            .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(200);
    }
}
