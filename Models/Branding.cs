namespace AfmHsa.Models;

// Identidade visual configurável: logo do programa (menu lateral) e logo da
// capa de login, cada um enviado separadamente.
public class Branding
{
    public string Id { get; set; } = "app";
    public string LogoMenu { get; set; } = "";   // logo aplicado no menu lateral
    public string LogoLogin { get; set; } = "";  // logo aplicado na tela de login
    public string LogoBg { get; set; } = "transparent"; // fundo do logo no menu (transparent | white)
}
