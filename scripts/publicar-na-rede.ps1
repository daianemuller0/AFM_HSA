# =====================================================================
#  AFM HSA - Publicacao na rede (como o axiaisvent: lancador + versoes)
# ---------------------------------------------------------------------
#  Estrutura na rede:
#
#   \\BZVCPFIL003\proj_ramires$\HSA_AFM\
#     AFM_HSA.exe                              <- lancador (o atalho aponta pra ele)
#     AFM HSA - Aftermarket Intelligence.lnk   <- atalho com a logo
#     app\versao.txt                           <- nome da versao atual
#     app\v2026-11-09_1530\                    <- arquivos desta versao
#
#  O lancador copia a versao para o computador de quem abre
#  (%LOCALAPPDATA%\HowdenAFM) e abre de la: o programa e uma JANELA
#  (sem a janela preta) e fechar a janela encerra tudo. Ninguem roda nada
#  direto da rede, entao da para publicar a qualquer hora - quem esta com
#  o programa aberto pega a versao nova ao fechar e abrir.
#
#  A base de dados (Parquet) NAO e tocada: fica em Data:Folder do
#  appsettings.json (\\BZVCPFIL003\proj_ramires$\DB\AFM_HSA).
#
#  Como usar (PowerShell, na raiz do projeto):
#      git pull
#      powershell -ExecutionPolicy Bypass -File .\scripts\publicar-na-rede.ps1
# =====================================================================
param(
    [string]$Destino = '\\BZVCPFIL003\proj_ramires$\HSA_AFM',
    [int]$ManterVersoes = 5
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$versao = 'v' + (Get-Date -Format 'yyyy-MM-dd_HHmm')

# Tudo o que aparecer na tela tambem fica em scripts\publicar.log -
# se algo falhar, e so enviar esse arquivo.
try { Stop-Transcript | Out-Null } catch { }
Start-Transcript -Path "$PSScriptRoot\publicar.log" -Force | Out-Null

function Parar([string]$msg) {
    Stop-Transcript | Out-Null
    throw $msg
}

if (-not (Test-Path "$raiz\desktop\AfmHsa.Desktop.csproj")) {
    Parar "A pasta 'desktop' nao existe aqui - rode 'git pull' para baixar a versao mais nova do projeto e tente de novo."
}

Write-Host ""
Write-Host "== AFM HSA: publicando a versao $versao em $Destino ==" -ForegroundColor Cyan

# 1) Compila o programa (janela + servidor), auto-contido: os PCs nao precisam de .NET
Write-Host "`n[1/5] Compilando o programa..." -ForegroundColor Yellow
$pastaApp = "$raiz\out\app\$versao"
dotnet publish "$raiz\desktop\AfmHsa.Desktop.csproj" -c Release -r win-x64 `
    --self-contained true -p:PublishReadyToRun=true -o "$pastaApp"
if ($LASTEXITCODE -ne 0) { Parar 'Falha ao compilar o programa - os detalhes estao acima e em scripts\publicar.log.' }

# As telas, os modelos (Word e pricing), o mapa e a base de municipios: o
# wwwroot inteiro do projeto vai junto, e o appsettings.json (pasta de dados e
# login) tambem.
robocopy "$raiz\wwwroot" "$pastaApp\wwwroot" /E /R:1 /W:1 /NP /NFL /NDL | Out-Null
if ($LASTEXITCODE -ge 8) { Parar "Falha ao copiar o wwwroot (robocopy $LASTEXITCODE)." }
Copy-Item "$raiz\appsettings.json" "$pastaApp\appsettings.json" -Force
New-Item -ItemType Directory -Force -Path "$pastaApp\Data" | Out-Null
Copy-Item "$raiz\Data\seed.json" "$pastaApp\Data\seed.json" -Force

# So segue se tudo o que o programa precisa estiver na pasta
$faltando = @(
    'AfmHsaApp.exe',
    'appsettings.json',
    'appsettings.Desktop.json',
    'duckdb.dll',
    'wwwroot\app.css',
    'wwwroot\modelos\proposta-comercial.docx',
    'wwwroot\modelos\pricing.xlsm',
    'wwwroot\modelos\termos-hchl-hpu-es.txt',
    'wwwroot\dados\municipios.csv',
    'Data\seed.json'
) | Where-Object { -not (Test-Path (Join-Path $pastaApp $_)) }
if ($faltando.Count -gt 0) {
    Parar "A versao compilada ficou sem: $($faltando -join ', '). Nada foi copiado para a rede."
}

# 2) Compila o lancador (um .exe unico e pequeno)
Write-Host "`n[2/5] Compilando o lancador..." -ForegroundColor Yellow
dotnet publish "$raiz\launcher\AfmHsa.Launcher.csproj" -c Release -r win-x64 `
    --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true `
    -p:EnableCompressionInSingleFile=false -o "$raiz\out\launcher"
if ($LASTEXITCODE -ne 0) { Parar 'Falha ao compilar o lancador - os detalhes estao acima e em scripts\publicar.log.' }

# 3) Copia a versao nova para a rede (numa pasta nova: ninguem trava arquivo)
Write-Host "`n[3/5] Copiando a versao para a rede..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path "$Destino\app" | Out-Null
robocopy "$pastaApp" "$Destino\app\$versao" /E /R:2 /W:2 /NP /NFL /NDL | Out-Null
if ($LASTEXITCODE -ge 8) { Parar "Falha ao copiar os arquivos para a rede (robocopy $LASTEXITCODE)." }

# 4) Vira a chave: a versao so e anunciada DEPOIS da copia terminar
Write-Host "`n[4/5] Ativando a versao $versao..." -ForegroundColor Yellow
Set-Content -Path "$Destino\app\versao.txt" -Value $versao -Encoding ASCII

try {
    Copy-Item "$raiz\out\launcher\AFM_HSA.exe" "$Destino\AFM_HSA.exe" -Force
} catch {
    Write-Warning "Nao consegui atualizar o AFM_HSA.exe (alguem deve estar abrindo o programa agora). Rode de novo mais tarde se o lancador tiver mudado."
}

# Atalho com a logo da Howden na propria pasta de rede (o icone vem de dentro
# do AFM_HSA.exe). Quem abrir pela rede ganha tambem um atalho na area de
# trabalho - o lancador cria na primeira abertura.
try {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut("$Destino\AFM HSA - Aftermarket Intelligence.lnk")
    $lnk.TargetPath = "$Destino\AFM_HSA.exe"
    $lnk.WorkingDirectory = $Destino
    $lnk.IconLocation = "$Destino\AFM_HSA.exe,0"
    $lnk.Description = 'AFM HSA - Aftermarket Intelligence - Howden'
    $lnk.Save()
} catch {
    Write-Warning "Nao consegui criar o atalho na pasta de rede: $($_.Exception.Message)"
}

# 5) Limpa versoes antigas na rede (mantem as ultimas N)
Write-Host "`n[5/5] Limpando versoes antigas..." -ForegroundColor Yellow
Get-ChildItem "$Destino\app" -Directory -Filter 'v*' |
    Sort-Object Name -Descending |
    Select-Object -Skip $ManterVersoes |
    ForEach-Object {
        Write-Host "  removendo $($_.Name)"
        Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }

# Arquivos da publicacao antiga (tudo solto na raiz): avisa para apagar
$antigos = @(Get-ChildItem $Destino -File | Where-Object {
    $_.Name -notin @('AFM_HSA.exe', 'AFM HSA - Aftermarket Intelligence.lnk') })
if ($antigos.Count -gt 0) {
    Write-Warning "Ha $($antigos.Count) arquivo(s) da publicacao antiga soltos em $Destino (ex.: AfmHsa.exe, *.dll, Abrir AFM HSA.bat). Pode apagar: o programa agora fica em app\."
}

Write-Host "`n== Pronto! Versao $versao publicada. ==" -ForegroundColor Green
Write-Host "Os usuarios abrem por: $Destino\AFM_HSA.exe (ou o atalho com a logo, na mesma pasta)"
Write-Host 'Quem estiver com o programa aberto recebe a atualizacao na proxima vez que abrir.'
Stop-Transcript | Out-Null
