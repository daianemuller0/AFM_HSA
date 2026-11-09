using AfmHsa.Data;
using AfmHsa.Models;
using Microsoft.AspNetCore.Components;

namespace AfmHsa.Components.Pages;

/// <summary>
/// As plantas e os equipamentos da proposta. A proposta não fica presa à
/// base instalada: a planta pode ser da base ou digitada (e pode haver mais
/// de uma); o equipamento pode ser o instalado numa planta da proposta, um
/// cadastrado de outra planta ou um não cadastrado, digitado.
/// </summary>
public partial class NovaOferta
{
    /// <summary>A primeira planta cadastrada: o vendedor da unidade e o "este cliente" da última venda vêm dela.</summary>
    private string _unitId => PlantasDaProposta.UnidadePrincipal(_o);

    /// <summary>Os equipamentos cadastrados da proposta, na ordem da lista (a BOM vem deles).</summary>
    private List<string> _equipIds => _o.EquipamentosDaProposta.Where(e => e.Cadastrado).Select(e => e.EquipmentId).ToList();

    // =====================================================================
    // plantas
    // =====================================================================

    /// <summary>A cidade da capa segue as plantas enquanto ninguém a digitou.</summary>
    private string _autoCidade = "";

    private void AdicionarPlanta()
    {
        _o.Plantas.Add(new PlantaDaProposta { Id = PlantasDaProposta.NovoId("p") });
        _irAoCampo = "f-plantas";
        _rolarTopo = false;
    }

    /// <summary>A planta da base entrando na proposta (etapa Começar ou a lista de plantas).</summary>
    private void AdicionarPlantaDaBase(string unitId)
    {
        if (_units.FirstOrDefault(u => u.Id == unitId) is not { } u) return;
        if (_o.Plantas.Any(p => p.UnitId == unitId)) return;
        _o.Plantas.Add(PlantasDaProposta.DaBase(u));
        PlantasMudaram();
    }

    private void TrocarUnidadeDaPlanta(PlantaDaProposta planta, ChangeEventArgs e)
    {
        var unitId = e.Value?.ToString() ?? "";
        if (unitId.Length == 0)
        {
            // de cadastrada para digitada: o que estava fica, para editar
            planta.UnitId = "";
        }
        else if (_units.FirstOrDefault(x => x.Id == unitId) is { } u)
        {
            if (_o.Plantas.Any(p => p != planta && p.UnitId == unitId))
            {
                _erro = $"A planta {Intel.UnitLabel(unitId)} já está na proposta.";
                return;
            }
            var daBase = PlantasDaProposta.DaBase(u);
            planta.UnitId = u.Id;
            planta.Nome = daBase.Nome;
            planta.Cidade = daBase.Cidade;
            planta.Estado = daBase.Estado;
        }
        PlantasMudaram();
    }

    private void MudarPlanta(PlantaDaProposta planta, ChangeEventArgs e, string campo)
    {
        var v = (e.Value?.ToString() ?? "").Trim();
        switch (campo)
        {
            case nameof(PlantaDaProposta.Nome): planta.Nome = v; break;
            case nameof(PlantaDaProposta.Cidade): planta.Cidade = v; break;
            case nameof(PlantaDaProposta.Estado): planta.Estado = v.ToUpperInvariant(); break;
        }
        PlantasMudaram();
    }

    private void RemoverPlanta(PlantaDaProposta planta)
    {
        _o.Plantas.Remove(planta);
        foreach (var eq in _o.EquipamentosDaProposta.Where(x => x.PlantaId == planta.Id)) eq.PlantaId = "";
        PlantasMudaram();
    }

    /// <summary>
    /// Depois de mexer nas plantas: o cliente (se ainda vazio), a cidade da
    /// capa, o estado do ICMS e o mercado acompanham a primeira planta — sem
    /// passar por cima do que foi digitado.
    /// </summary>
    private void PlantasMudaram()
    {
        _refs.Clear();
        CompletarDaFicha();
        var principal = _units.FirstOrDefault(u => u.Id == _unitId);
        if (principal is not null && string.IsNullOrWhiteSpace(_o.ClientName))
            _o.ClientName = Intel.CompanyName(principal.CompanyId);
        _o.City = Auto(_o.City, ref _autoCidade, PlantasDaProposta.CidadesDaCapa(_o));
        var uf = (_o.Plantas.Select(p => p.Estado).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "").Trim().ToUpperInvariant();
        if (uf.Length > 0 && ListasDoPricing.EstadosDo(_o.Destino).Contains(uf)) _o.Estado = Auto(_o.Estado, ref _autoUf, uf);
        if (principal is not null)
            _o.MarketSegments = Auto(_o.MarketSegments, ref _autoMercado, PricingDaProposta.MarketSegmentDo(principal.Segmento));
        // o vendedor da unidade, quando está na lista da equipe e ninguém escolheu outro
        if (principal is not null && string.IsNullOrWhiteSpace(_o.Vendedor)
            && Vendedores.All().FirstOrDefault(v => v.Id == principal.VendedorId) is { } daUnidade
            && ContatosHowden.Do(daUnidade.Nome) is { } contato)
            _o.Vendedor = contato.Nome;
    }

    /// <summary>As unidades da base do mesmo cliente (pelo nome digitado ou pelas plantas já escolhidas): aparecem primeiro na lista.</summary>
    private List<ClientUnit> UnidadesDoCliente()
    {
        var empresas = _o.Plantas.Where(p => p.Cadastrada)
            .Select(p => _units.FirstOrDefault(u => u.Id == p.UnitId)?.CompanyId ?? "").Where(c => c.Length > 0).ToHashSet();
        return _units.Where(u => empresas.Contains(u.CompanyId)
                                 || (!string.IsNullOrWhiteSpace(_o.ClientName) && Textos.Igual(Intel.CompanyName(u.CompanyId), _o.ClientName)))
            .ToList();
    }

    private IEnumerable<string> NomesDeClientes => _units.Select(u => Intel.CompanyName(u.CompanyId))
        .Concat(_anteriores.Select(a => a.ClientName))
        .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n);

    // =====================================================================
    // equipamentos
    // =====================================================================

    private bool _buscandoEquip;
    private string _buscaEquip = "";

    /// <summary>Os equipamentos cadastrados instalados numa unidade.</summary>
    private List<Equipment> EquipsDaUnidade(string unitId)
    {
        var ids = _base.Where(b => b.ClientUnitId == unitId).Select(b => b.EquipmentId).Distinct().ToHashSet();
        return _equipamentos.Where(e => ids.Contains(e.Id)).OrderBy(e => e.Modelo).ToList();
    }

    /// <summary>Marca/desmarca um equipamento instalado numa planta da proposta.</summary>
    private void AlternarEquipDaPlanta(string equipmentId, PlantaDaProposta planta)
    {
        var existente = _o.EquipamentosDaProposta.FirstOrDefault(x => x.EquipmentId == equipmentId);
        if (existente is not null) { RemoverEquip(existente); return; }
        if (_equipamentos.FirstOrDefault(e => e.Id == equipmentId) is { } e)
            _o.EquipamentosDaProposta.Add(PlantasDaProposta.DoCadastro(e, planta, _base, _o.Plantas));
    }

    /// <summary>Um cadastrado de outra planta: entra na planta onde está instalado, se ela estiver na proposta; senão na primeira.</summary>
    private void AdicionarDoCadastro(string equipmentId)
    {
        if (_o.EquipamentosDaProposta.Any(x => x.EquipmentId == equipmentId)) return;
        if (_equipamentos.FirstOrDefault(e => e.Id == equipmentId) is not { } e) return;
        var unidades = _base.Where(b => b.EquipmentId == equipmentId).Select(b => b.ClientUnitId).ToHashSet();
        var planta = _o.Plantas.FirstOrDefault(p => p.Cadastrada && unidades.Contains(p.UnitId)) ?? _o.Plantas.FirstOrDefault();
        _o.EquipamentosDaProposta.Add(PlantasDaProposta.DoCadastro(e, planta, _base, _o.Plantas));
        _msg = $"{RotuloDoEquip(_o.EquipamentosDaProposta[^1])} incluído — confira a planta, a TAG e a aplicação.";
    }

    // ---- serial, TAG e aplicação pela ficha do equipamento (base instalada e cadastro) ----

    /// <summary>Preenche o que estiver vazio nos equipamentos cadastrados da proposta.</summary>
    private void CompletarDaFicha()
    {
        foreach (var eq in _o.EquipamentosDaProposta.Where(x => x.Cadastrado))
            PlantasDaProposta.PreencherDaFicha(eq, _equipamentos.FirstOrDefault(x => x.Id == eq.EquipmentId), _o.Plantas, _base, sobrescrever: false);
    }

    /// <summary>"↺ puxar da ficha": troca serial, TAG e aplicação pelos da ficha (o que a ficha não tem fica como está).</summary>
    private void PuxarDaFicha(EquipamentoDaProposta eq)
    {
        var mudou = PlantasDaProposta.PreencherDaFicha(eq, _equipamentos.FirstOrDefault(x => x.Id == eq.EquipmentId), _o.Plantas, _base, sobrescrever: true);
        _msg = mudou ? $"{RotuloDoEquip(eq)}: serial, TAG e aplicação da ficha." 
                     : $"{RotuloDoEquip(eq)}: a ficha não tem serial, TAG ou aplicação diferentes para esta planta — escolha na lista do campo ou digite.";
    }

    /// <summary>A planta do equipamento mudou: serial e TAG passam a ser os da instalação nela (quando a ficha tem).</summary>
    private void MudarPlantaDoEquip(EquipamentoDaProposta eq, string plantaId)
    {
        eq.PlantaId = plantaId;
        PlantasDaProposta.PreencherDaFicha(eq, _equipamentos.FirstOrDefault(x => x.Id == eq.EquipmentId), _o.Plantas, _base, sobrescrever: true);
    }

    /// <summary>Os valores que a ficha tem para o campo, em todas as instalações (para escolher na lista).</summary>
    private IEnumerable<string> DaFicha(EquipamentoDaProposta eq, Func<InstalledBase, string?> campo) =>
        _base.Where(b => b.EquipmentId == eq.EquipmentId).Select(campo)
            .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(100);

    private void AdicionarEquipLivre()
    {
        _o.EquipamentosDaProposta.Add(new EquipamentoDaProposta
        {
            Id = PlantasDaProposta.NovoId("x"),
            Tipo = PlantasDaProposta.Compressor,
            PlantaId = _o.Plantas.FirstOrDefault()?.Id ?? "",
        });
        _irAoCampo = "f-equipamentos";
        _rolarTopo = false;
    }

    /// <summary>Tira o equipamento; os itens dele ficam na proposta, sem equipamento.</summary>
    private void RemoverEquip(EquipamentoDaProposta eq)
    {
        _o.EquipamentosDaProposta.Remove(eq);
        foreach (var it in _items.Where(i => i.EquipmentId == eq.Id)) it.EquipmentId = "";
        var validos = _bom.Where(b => _equipIds.Contains(b.EquipmentId)).Select(b => b.Id).ToHashSet();
        _bomMarcados.RemoveWhere(x => !validos.Contains(x));
    }

    /// <summary>Escolher o equipamento do item: modelo, serial e TAG passam a ser os dele (o que foi digitado no item sai).</summary>
    private void MudarEquipDoItem(CommercialOfferItem it, ChangeEventArgs e)
    {
        it.EquipmentId = e.Value?.ToString() ?? "";
        it.Modelo = ""; it.SerialNumber = ""; it.Tag = "";
        if (string.IsNullOrWhiteSpace(it.Ncm)) it.Ncm = ListasDaProposta.NcmPadrao();
    }

    private string ItensDoEquip(EquipamentoDaProposta eq) =>
        _items.Count(i => i.EquipmentId == eq.Id) is var n && n > 0 ? $"· {Plural(n, "item", "itens")}" : "· sem itens";

    /// <summary>"VX 2025.00.00 · TAG 5520-VE-01 · São Luís/MA".</summary>
    private string RotuloDoEquip(EquipamentoDaProposta eq)
    {
        var partes = new List<string> { string.IsNullOrWhiteSpace(eq.Modelo) ? (eq.Cadastrado ? "equipamento" : "não cadastrado") : eq.Modelo.Trim() };
        if (!string.IsNullOrWhiteSpace(eq.Tag)) partes.Add("TAG " + eq.Tag.Trim());
        if (_o.Plantas.Count > 1 && PlantasDaProposta.PlantaDe(_o, eq) is { } p) partes.Add(PlantasDaProposta.Rotulo(p));
        return string.Join(" · ", partes);
    }

    /// <summary>"Vale — São Luís (2 TAGs)": onde o cadastrado está instalado, para escolher o certo na busca.</summary>
    private string OndeEstaInstalado(string equipmentId)
    {
        var porUnidade = _base.Where(b => b.EquipmentId == equipmentId)
            .GroupBy(b => b.ClientUnitId)
            .Select(g =>
            {
                var tags = g.Select(b => (b.Tag ?? "").Trim()).Where(t => t.Length > 0).Distinct().ToList();
                var nome = _units.Any(u => u.Id == g.Key) ? Intel.UnitLabel(g.Key)
                    : (g.Select(b => b.PlantName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? g.Key);
                return tags.Count switch { 0 => nome, 1 => $"{nome} (TAG {tags[0]})", _ => $"{nome} ({tags.Count} TAGs)" };
            })
            .ToList();
        return string.Join(" · ", porUnidade);
    }

    /// <summary>Todo o cadastro, pelo texto: modelo, nome, documento, tipo, TAG, planta e cliente onde está instalado.</summary>
    private List<Equipment> EquipamentosDaBusca()
    {
        var termos = Textos.Simples(_buscaEquip).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _indiceEquip ??= _equipamentos.ToDictionary(e => e.Id, e => Textos.Simples(string.Join(' ',
            new[] { e.Modelo, e.Nome, e.DocNum, e.Tipo, e.Aplicacao, e.Fabricante }
            .Concat(_base.Where(b => b.EquipmentId == e.Id)
                .SelectMany(b => new[] { b.Tag, b.SerialNo, b.PlantName, b.City, b.SiteUnit, _units.Any(u => u.Id == b.ClientUnitId) ? Intel.UnitLabel(b.ClientUnitId) : "" })))));
        var ja = _o.EquipamentosDaProposta.Select(x => x.EquipmentId).ToHashSet();
        return _equipamentos
            .Where(e => !ja.Contains(e.Id) && _indiceEquip.TryGetValue(e.Id, out var t) && termos.All(t.Contains))
            .OrderBy(e => e.Modelo)
            .Take(50)
            .ToList();
    }

    private Dictionary<string, string>? _indiceEquip;
}
