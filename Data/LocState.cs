namespace AfmHsa.Data;

// Estado de idioma por sessão (circuito Blazor). Componentes assinam Changed
// para re-renderizar quando o idioma muda. Scoped.
public class LocState
{
    public string Lang { get; private set; } = "pt";

    public event Action? Changed;

    public void Set(string lang)
    {
        if (lang != "pt" && lang != "en" && lang != "es") lang = "pt";
        if (lang == Lang) return;
        Lang = lang;
        Changed?.Invoke();
    }

    // Traduz um texto em PT para o idioma atual.
    public string T(string pt) => I18n.T(Lang, pt);

    public string Nome => Lang switch { "en" => "English", "es" => "Español", _ => "Português" };
}
