using AfmHsa.Models;

namespace AfmHsa.Data;

public class BrandingRepository
{
    private readonly ParquetStore _s;
    public BrandingRepository(ParquetStore s) => _s = s;

    public Branding Get()
    {
        var list = _s.ReadLatest("branding", "id, logo_data_uri, logo_login_uri, logo_bg",
            r => new Branding
            {
                Id = Fmt.S(r, 0),
                LogoMenu = Fmt.S(r, 1),      // coluna logo_data_uri (compatível com versão anterior)
                LogoLogin = Fmt.S(r, 2),
                LogoBg = Fmt.S(r, 3),
            });
        var b = list.FirstOrDefault() ?? new Branding();
        if (string.IsNullOrEmpty(b.LogoBg)) b.LogoBg = "transparent";
        return b;
    }

    public void Save(Branding b) => _s.WriteRow("branding", new KeyValuePair<string, object?>[]
    {
        new("id", "app"),
        new("logo_data_uri", b.LogoMenu),
        new("logo_login_uri", b.LogoLogin),
        new("logo_bg", string.IsNullOrEmpty(b.LogoBg) ? "transparent" : b.LogoBg),
    });

    public void Reset() => Save(new Branding());
}
