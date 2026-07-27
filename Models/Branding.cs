namespace AfmHsa.Models;

// Identidade visual configurável (logo enviado pelo usuário + fundo).
public class Branding
{
    public string Id { get; set; } = "app";
    public string LogoDataUri { get; set; } = ""; // data:image/...;base64,... ("" = logo padrão)
    public string LogoBg { get; set; } = "transparent"; // transparent | white
}
