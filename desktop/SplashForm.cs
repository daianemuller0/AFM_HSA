namespace AfmHsa.Desktop;

/// <summary>
/// Tela de abertura: barra de progresso com a % de inicialização
/// (servidor interno → navegador embutido → sistema carregado).
/// </summary>
public sealed class SplashForm : Form
{
    private readonly ProgressBar _barra = new()
    {
        Style = ProgressBarStyle.Continuous,
        Minimum = 0,
        Maximum = 100,
        Height = 14,
    };
    private readonly Label _status = new()
    {
        Text = "Iniciando…",
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(70, 80, 110),
    };
    private readonly Label _pct = new()
    {
        Text = "0%",
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Color.FromArgb(20, 30, 50),
        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
    };

    public SplashForm()
    {
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(480, 180);
        BackColor = Color.White;
        TopMost = true;
        ShowInTaskbar = true;
        Font = new Font("Segoe UI", 9f);

        // faixa azul com o nome do sistema
        var faixa = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.FromArgb(0, 71, 133) };
        faixa.Controls.Add(new Label
        {
            Text = "AFM HSA · Aftermarket Intelligence",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(20, 0, 0, 0),
        });
        Controls.Add(faixa);

        _status.SetBounds(20, 84, 370, 22);
        _pct.SetBounds(392, 84, 66, 22);
        _barra.SetBounds(20, 112, 438, 14);
        Controls.Add(_status);
        Controls.Add(_pct);
        Controls.Add(_barra);

        Controls.Add(new Label
        {
            Text = "Howden — A Chart Industries Company",
            ForeColor = Color.FromArgb(150, 158, 175),
            AutoSize = false,
            Bounds = new Rectangle(20, 140, 438, 20),
        });

        // borda fininha em volta (form sem moldura)
        Paint += (_, e) => e.Graphics.DrawRectangle(
            new Pen(Color.FromArgb(213, 218, 228)), 0, 0, Width - 1, Height - 1);
    }

    /// <summary>Atualiza a barra (0–100) e a mensagem — pode ser chamado de qualquer thread.</summary>
    public void Reportar(int pct, string mensagem)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => Reportar(pct, mensagem)); return; }
        var v = Math.Clamp(pct, 0, 100);
        // truque para a animação do Windows não "atrasar" a barra
        if (v < 100) { _barra.Value = v + 1; }
        _barra.Value = v;
        _pct.Text = $"{v}%";
        _status.Text = mensagem;
    }
}
