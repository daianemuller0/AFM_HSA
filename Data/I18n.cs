namespace AfmHsa.Data;

// Dicionário de tradução leve (chave = texto em PT). Cobre a navegação e o
// cabeçalho. Para PT devolve o próprio texto; para EN/ES busca a tradução
// (se não houver, cai no PT). O conteúdo das páginas segue em PT por ora.
public static class I18n
{
    // pt => (en, es)
    private static readonly Dictionary<string, (string En, string Es)> Map = new()
    {
        // seções do menu
        ["Gestão de Vendas"] = ("Sales Management", "Gestión de Ventas"),
        ["Clientes & Base"] = ("Clients & Base", "Clientes y Base"),
        ["Comercial"] = ("Commercial", "Comercial"),
        ["Controle"] = ("Control", "Control"),
        ["Dados & Marca"] = ("Data & Brand", "Datos y Marca"),
        // itens do menu
        ["Central de Inteligência"] = ("Intelligence Center", "Centro de Inteligencia"),
        ["Painel Executivo"] = ("Executive Panel", "Panel Ejecutivo"),
        ["Plano de Ação Comercial"] = ("Commercial Action Plan", "Plan de Acción Comercial"),
        ["Oportunidades"] = ("Opportunities", "Oportunidades"),
        ["Previsão"] = ("Forecast", "Pronóstico"),
        ["Planejamento"] = ("Planning", "Planificación"),
        ["Histórico Aftermarket"] = ("Aftermarket History", "Historial Aftermarket"),
        ["Ações em Campo"] = ("Field Actions", "Acciones en Campo"),
        ["Alertas Preditivos"] = ("Predictive Alerts", "Alertas Predictivas"),
        ["Clientes e Unidades"] = ("Clients & Units", "Clientes y Unidades"),
        ["Base Instalada"] = ("Installed Base", "Base Instalada"),
        ["Equipamentos"] = ("Equipment", "Equipos"),
        ["Regras Técnicas de Troca"] = ("Technical Replacement Rules", "Reglas Técnicas de Cambio"),
        ["Ofertas"] = ("Offers", "Ofertas"),
        ["Vendedores"] = ("Salespeople", "Vendedores"),
        ["Dados e Importação"] = ("Data & Import", "Datos e Importación"),
        ["Importar"] = ("Import", "Importar"),
        ["Identidade Visual"] = ("Visual Identity", "Identidad Visual"),
        // topbar
        ["Howden Aftermarket Intelligence"] = ("Howden Aftermarket Intelligence", "Howden Aftermarket Intelligence"),
        ["AFM HSA · Inteligência comercial para base instalada industrial"] =
            ("AFM HSA · Commercial intelligence for the industrial installed base",
             "AFM HSA · Inteligencia comercial para la base instalada industrial"),
        ["Sair"] = ("Sign out", "Salir"),
    };

    public static string T(string lang, string pt)
    {
        if (lang == "pt" || string.IsNullOrEmpty(lang)) return pt;
        if (!Map.TryGetValue(pt, out var tr)) return pt;
        return lang == "en" ? tr.En : lang == "es" ? tr.Es : pt;
    }
}
