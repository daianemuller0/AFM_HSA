using System.Diagnostics;

namespace AfmHsa.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new FormAtualizacao());
    }
}

/// <summary>
/// Fluxo de publicação dinâmica:
///  - na REDE (\\...\HSA_AFM\): AFM_HSA.exe + app\versao.txt + app\&lt;versão&gt;\ (imutável);
///  - na MÁQUINA (%LOCALAPPDATA%\HowdenAFM\): cópia local do app E do próprio
///    AFM_HSA.exe — na primeira abertura pela rede, o lançador SE INSTALA aqui e
///    cria um atalho na área de trabalho apontando para a cópia local.
/// As aberturas seguintes rodam tudo do disco local (rápido): só o
/// versao.txt é lido da rede para saber se tem atualização.
/// </summary>
public sealed class FormAtualizacao : Form
{
    private const string ExeDoApp = "AfmHsaApp.exe";
    private const string NomeAtalho = "AFM HSA - Aftermarket Intelligence.lnk";

    private readonly ProgressBar _barra = new() { Style = ProgressBarStyle.Continuous, Minimum = 0, Maximum = 100 };
    private readonly Label _status = new() { Text = "Verificando a versão…", ForeColor = Color.FromArgb(70, 80, 110) };
    private readonly Label _pct = new() { Text = "0%", TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };

    public FormAtualizacao()
    {
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(480, 150);
        BackColor = Color.White;
        TopMost = true;
        Font = new Font("Segoe UI", 9f);

        var faixa = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Color.FromArgb(0, 71, 133) };
        faixa.Controls.Add(new Label
        {
            Text = "AFM HSA · Aftermarket Intelligence",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(20, 0, 0, 0),
        });
        Controls.Add(faixa);

        _status.SetBounds(20, 70, 370, 20);
        _pct.SetBounds(392, 70, 66, 20);
        _barra.SetBounds(20, 96, 438, 14);
        Controls.Add(_status);
        Controls.Add(_pct);
        Controls.Add(_barra);
        Paint += (_, e) => e.Graphics.DrawRectangle(new Pen(Color.FromArgb(213, 218, 228)), 0, 0, Width - 1, Height - 1);

        Shown += async (_, _) => await ExecutarAsync();
    }

    private void Reportar(int pct, string msg)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => Reportar(pct, msg)); return; }
        var v = Math.Clamp(pct, 0, 100);
        if (v < 100) _barra.Value = v + 1;
        _barra.Value = v;
        _pct.Text = $"{v}%";
        _status.Text = msg;
    }

    private async Task ExecutarAsync()
    {
        try
        {
            var meuExe = Environment.ProcessPath ?? Application.ExecutablePath;
            var raizLocal = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HowdenAFM");
            Directory.CreateDirectory(raizLocal);
            var svLocal = Path.Combine(raizLocal, "AFM_HSA.exe");
            var arquivoOrigem = Path.Combine(raizLocal, "origem.txt");

            var rodandoLocal = string.Equals(Path.GetFullPath(meuExe), Path.GetFullPath(svLocal),
                StringComparison.OrdinalIgnoreCase);

            // Descobre a pasta da REDE: rodando de lá, é a pasta deste exe;
            // rodando da cópia local, é o que ficou anotado na instalação.
            string? raizRede = rodandoLocal
                ? (File.Exists(arquivoOrigem) ? File.ReadAllText(arquivoOrigem).Trim() : null)
                : AppContext.BaseDirectory;

            // Lançador da rede mais novo que a cópia local? Passa a bola para
            // ele (ele roda uma vez da rede e se reinstala aqui atualizado).
            if (rodandoLocal && raizRede is not null)
            {
                var svRede = Path.Combine(raizRede, "AFM_HSA.exe");
                try
                {
                    if (File.Exists(svRede) &&
                        File.GetLastWriteTimeUtc(svRede) > File.GetLastWriteTimeUtc(meuExe).AddMinutes(1))
                    {
                        Reportar(20, "Atualizando o iniciador…");
                        Process.Start(new ProcessStartInfo(svRede) { UseShellExecute = true });
                        Close();
                        return;
                    }
                }
                catch { /* rede fora: segue com o local */ }
            }

            Reportar(5, "Verificando a versão…");
            string? versao = null;
            if (raizRede is not null)
            {
                try { versao = File.ReadAllText(Path.Combine(raizRede, "app", "versao.txt")).Trim(); }
                catch { /* rede fora do ar: plano B abaixo */ }
            }

            string exe;
            if (string.IsNullOrWhiteSpace(versao))
            {
                // Sem acesso à rede: abre a última versão já instalada na máquina.
                var ultima = Directory.GetDirectories(raizLocal, "v*")
                    .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(d => File.Exists(Path.Combine(d, ExeDoApp)));
                if (ultima is null)
                    throw new Exception(
                        "Sem acesso à pasta de rede do AFM HSA e nenhuma versão instalada neste computador ainda.\n" +
                        "Conecte-se à rede e tente de novo.");
                Reportar(70, "Sem rede — abrindo a última versão instalada…");
                exe = Path.Combine(ultima, ExeDoApp);
            }
            else
            {
                var destino = Path.Combine(raizLocal, versao);
                exe = Path.Combine(destino, ExeDoApp);
                if (!File.Exists(exe))
                {
                    Reportar(8, $"Baixando a versão {versao} da rede…");
                    await Task.Run(() => CopiarComProgresso(Path.Combine(raizRede!, "app", versao!), destino));
                    LimparVersoesAntigas(raizLocal, versao!);
                }
                else
                {
                    Reportar(80, "Versão em dia — abrindo…");
                }
            }

            // Primeira vez pela rede: instala o lançador local + atalho na
            // área de trabalho (as próximas aberturas ficam rápidas).
            if (!rodandoLocal && raizRede is not null)
            {
                Reportar(90, "Criando o atalho rápido na área de trabalho…");
                try
                {
                    File.WriteAllText(arquivoOrigem, raizRede);
                    File.Copy(meuExe, svLocal, overwrite: true);
                    CriarAtalho(svLocal, raizLocal);
                }
                catch { /* sem atalho ainda funciona — só fica mais lento */ }
            }

            Reportar(95, "Abrindo o AFM HSA…");
            Process.Start(new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = true,
            });
            Reportar(100, "Pronto!");
            await Task.Delay(300);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível abrir o AFM HSA:\n\n" + ex.Message,
                "AFM HSA", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        Close();
    }

    /// <summary>Atalho "AFM HSA - Aftermarket Intelligence" na área de trabalho, apontando para o AFM_HSA.exe LOCAL.</summary>
    private static void CriarAtalho(string alvo, string pastaTrabalho)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var caminhoLnk = Path.Combine(desktop, NomeAtalho);

        var tipoShell = Type.GetTypeFromProgID("WScript.Shell");
        if (tipoShell is null) return;
        dynamic shell = Activator.CreateInstance(tipoShell)!;
        dynamic atalho = shell.CreateShortcut(caminhoLnk);
        atalho.TargetPath = alvo;
        atalho.WorkingDirectory = pastaTrabalho;
        atalho.Description = "AFM HSA · Aftermarket Intelligence — Howden";
        atalho.IconLocation = alvo + ",0";   // a logo da Howden, que está dentro do AFM_HSA.exe
        atalho.Save();
    }

    /// <summary>
    /// Copia a pasta da versão para uma pasta temporária local (barra de 10% a
    /// 90%, proporcional aos bytes) e só no fim renomeia para o nome definitivo —
    /// se a cópia falhar no meio, nada fica pela metade.
    /// </summary>
    private void CopiarComProgresso(string origem, string destino)
    {
        var arquivos = Directory.GetFiles(origem, "*", SearchOption.AllDirectories);
        if (arquivos.Length == 0)
            throw new Exception($"A pasta da versão está vazia na rede: {origem}");
        var totalBytes = arquivos.Sum(f => new FileInfo(f).Length);

        var tmp = destino + ".baixando";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);

        long copiados = 0;
        foreach (var arq in arquivos)
        {
            var relativo = Path.GetRelativePath(origem, arq);
            var alvo = Path.Combine(tmp, relativo);
            Directory.CreateDirectory(Path.GetDirectoryName(alvo)!);
            File.Copy(arq, alvo, overwrite: true);
            copiados += new FileInfo(arq).Length;
            var pct = 10 + (int)(copiados * 80 / Math.Max(totalBytes, 1));
            Reportar(pct, $"Baixando a versão… {copiados / 1048576} de {totalBytes / 1048576} MB");
        }

        if (Directory.Exists(destino)) Directory.Delete(destino, recursive: true);
        Directory.Move(tmp, destino);
    }

    /// <summary>Apaga versões locais antigas (mantém a atual e a anterior).</summary>
    private static void LimparVersoesAntigas(string raizLocal, string versaoAtual)
    {
        try
        {
            var antigas = Directory.GetDirectories(raizLocal, "v*")
                .Where(d => !d.EndsWith(versaoAtual, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .Skip(1);   // deixa a anterior como reserva
            foreach (var d in antigas)
                Directory.Delete(d, recursive: true);
        }
        catch { /* limpeza é melhor esforço — versões antigas não atrapalham */ }
    }
}
