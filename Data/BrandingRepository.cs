using AfmHsa.Models;

namespace AfmHsa.Data;

public class BrandingRepository
{
    private readonly ParquetStore _s;
    public BrandingRepository(ParquetStore s) => _s = s;

    public Branding Get()
    {
        var list = _s.ReadLatest("branding", "id, logo_data_uri, logo_bg",
            r => new Branding { Id = Fmt.S(r, 0), LogoDataUri = Fmt.S(r, 1), LogoBg = Fmt.S(r, 2) });
        return list.FirstOrDefault() ?? new Branding();
    }

    public void Save(Branding b) => _s.WriteRow("branding", new KeyValuePair<string, object?>[]
    {
        new("id", "app"),
        new("logo_data_uri", b.LogoDataUri),
        new("logo_bg", string.IsNullOrEmpty(b.LogoBg) ? "transparent" : b.LogoBg),
    });

    public void Reset() => Save(new Branding());
}
