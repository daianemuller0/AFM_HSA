namespace AfmHsa;

/// <summary>
/// Ícones SVG inline (estilo Feather/Lucide: traço, viewBox 24). Usados pelo
/// menu lateral (NavMenu) e pelas telas de módulo (ModuleScaffold), evitando
/// dependência de biblioteca de ícones no front-end.
/// </summary>
public static class Icons
{
    // Conteúdo interno de cada ícone (sem o wrapper <svg>).
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["dashboard"] = "<rect x='3' y='3' width='7' height='9'/><rect x='14' y='3' width='7' height='5'/><rect x='14' y='12' width='7' height='9'/><rect x='3' y='16' width='7' height='5'/>",
        ["gauge"] = "<path d='M12 15l3.5-3.5'/><path d='M2 12a10 10 0 1 1 20 0'/>",
        ["briefcase"] = "<rect x='2' y='7' width='20' height='14' rx='2'/><path d='M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16'/>",
        ["target"] = "<circle cx='12' cy='12' r='10'/><circle cx='12' cy='12' r='6'/><circle cx='12' cy='12' r='2'/>",
        ["receipt"] = "<path d='M4 2v20l2-1 2 1 2-1 2 1 2-1 2 1 2-1V2l-2 1-2-1-2 1-2-1-2 1-2-1Z'/><path d='M8 7h8'/><path d='M8 11h8'/><path d='M8 15h5'/>",
        ["map-pin"] = "<path d='M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0Z'/><circle cx='12' cy='10' r='3'/>",
        ["bell"] = "<path d='M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9'/><path d='M13.73 21a2 2 0 0 1-3.46 0'/>",
        ["trending"] = "<polyline points='22 7 13.5 15.5 8.5 10.5 2 17'/><polyline points='16 7 22 7 22 13'/>",
        ["calendar"] = "<rect x='3' y='4' width='18' height='18' rx='2'/><line x1='16' y1='2' x2='16' y2='6'/><line x1='8' y1='2' x2='8' y2='6'/><line x1='3' y1='10' x2='21' y2='10'/>",
        ["building"] = "<rect x='4' y='2' width='16' height='20' rx='2'/><path d='M9 22v-4h6v4'/><path d='M8 6h.01M16 6h.01M8 10h.01M16 10h.01M8 14h.01M16 14h.01'/>",
        ["boxes"] = "<path d='M2.97 12.92A2 2 0 0 0 2 14.63v3.24a2 2 0 0 0 1.03 1.75l3 1.65a2 2 0 0 0 1.94 0L10 20v-4.5'/><path d='m7 16.5-4.74-2.85'/><path d='M17 16.5v-4.5l-3 1.65'/><path d='M12 8 7.26 5.15a2 2 0 0 0-1.94 0L3.03 6.42'/><path d='M22 14.63a2 2 0 0 0-.97-1.71L14 8'/>",
        ["wrench"] = "<path d='M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76Z'/>",
        ["file-text"] = "<path d='M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z'/><polyline points='14 2 14 8 20 8'/><line x1='16' y1='13' x2='8' y2='13'/><line x1='16' y1='17' x2='8' y2='17'/>",
        ["users"] = "<path d='M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2'/><circle cx='9' cy='7' r='4'/><path d='M22 21v-2a4 4 0 0 0-3-3.87'/><path d='M16 3.13a4 4 0 0 1 0 7.75'/>",
        ["wallet"] = "<path d='M21 12V7H5a2 2 0 0 1 0-4h14v4'/><path d='M3 5v14a2 2 0 0 0 2 2h16v-5'/><path d='M18 12a2 2 0 0 0 0 4h4v-4Z'/>",
        ["check"] = "<path d='M22 11.08V12a10 10 0 1 1-5.93-9.14'/><polyline points='22 4 12 14.01 9 11.01'/>",
        ["refund"] = "<path d='M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8'/><path d='M3 3v5h5'/><path d='M12 7v5l3 2'/>",
        ["folder"] = "<path d='M4 20h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.93a2 2 0 0 1-1.66-.9l-.82-1.2A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13c0 1.1.9 2 2 2Z'/>",
        ["tags"] = "<path d='M9 5H2v7l6.29 6.29a1 1 0 0 0 1.42 0l4.58-4.58a1 1 0 0 0 0-1.42L9 5Z'/><path d='M6 9.01V9'/><path d='m15 5 6.3 6.3a1 1 0 0 1 0 1.4l-4.6 4.6'/>",
        ["pie"] = "<path d='M21.21 15.89A10 10 0 1 1 8 2.83'/><path d='M22 12A10 10 0 0 0 12 2v10z'/>",
        ["plug"] = "<path d='M12 22v-5'/><path d='M9 8V2'/><path d='M15 8V2'/><path d='M18 8v5a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V8Z'/>",
        ["cloud"] = "<path d='M17.5 19a4.5 4.5 0 1 0 0-9h-1.8A7 7 0 1 0 4 15.3'/><path d='M12 12v9'/><path d='m8 17 4 4 4-4'/>",
        ["upload"] = "<path d='M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4'/><polyline points='17 8 12 3 7 8'/><line x1='12' y1='3' x2='12' y2='15'/>",
        ["refresh"] = "<path d='M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8'/><path d='M21 3v5h-5'/><path d='M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16'/><path d='M8 16H3v5'/>",
        ["mail"] = "<rect x='2' y='4' width='20' height='16' rx='2'/><path d='m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7'/>",
        ["image"] = "<rect x='3' y='3' width='18' height='18' rx='2'/><circle cx='9' cy='9' r='2'/><path d='m21 15-3.09-3.09a2 2 0 0 0-2.82 0L6 21'/>",
        ["database"] = "<ellipse cx='12' cy='5' rx='9' ry='3'/><path d='M3 5v14a9 3 0 0 0 18 0V5'/><path d='M3 12a9 3 0 0 0 18 0'/>",
        ["search"] = "<circle cx='11' cy='11' r='8'/><path d='m21 21-4.35-4.35'/>",
        ["file-plus"] = "<path d='M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z'/><polyline points='14 2 14 8 20 8'/><line x1='12' y1='12' x2='12' y2='18'/><line x1='9' y1='15' x2='15' y2='15'/>",
        ["box"] = "<path d='M21 8V16a2 2 0 0 1-1 1.73l-7 4a2 2 0 0 1-2 0l-7-4A2 2 0 0 1 3 16V8a2 2 0 0 1 1-1.73l7-4a2 2 0 0 1 2 0l7 4A2 2 0 0 1 21 8z'/><polyline points='3.29 7 12 12 20.71 7'/><line x1='12' y1='22' x2='12' y2='12'/>",
    };

    /// <summary>Retorna o markup completo do ícone. Chave desconhecida cai no "box".</summary>
    public static string Svg(string key, string cls = "ico")
    {
        var inner = Paths.TryGetValue(key, out var p) ? p : Paths["box"];
        return $"<svg class=\"{cls}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" " +
               $"stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{inner}</svg>";
    }
}
