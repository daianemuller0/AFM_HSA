# =====================================================================
#  AFM HSA - Publicacao na rede
# ---------------------------------------------------------------------
#  Compila o app em Release, embute o runtime do .NET 8 (self-contained,
#  para nao precisar instalar nada em cada PC) e copia tudo para a pasta
#  de rede. Cada usuario abre pelo "Abrir AFM HSA.bat" (roda em
#  localhost:5090 e le a base Parquet do caminho configurado).
#
#  Como usar (PowerShell, na raiz do projeto):
#      powershell -ExecutionPolicy Bypass -File .\scripts\publicar-na-rede.ps1
#
#  A base de dados (Parquet) NAO e tocada - fica no caminho separado
#  definido em appsettings.json (Data:Folder).
# =====================================================================

$ErrorActionPreference = "Stop"

# --- Configuracao -----------------------------------------------------
$Destino  = "\\BZVCPFIL003\proj_ramires$\HSA_AFM"   # pasta do APP na rede
$Projeto  = Split-Path -Parent $PSScriptRoot        # raiz do repositorio
$Runtime  = "win-x64"
$Publish  = Join-Path $Projeto "bin\publish"        # saida local temporaria

Write-Host ""
Write-Host "==> AFM HSA - publicacao na rede" -ForegroundColor Cyan
Write-Host "    Projeto : $Projeto"
Write-Host "    Destino : $Destino"
Write-Host ""

# --- 1) Compilar (self-contained) -------------------------------------
Write-Host "==> Compilando em Release (self-contained $Runtime)..." -ForegroundColor Cyan
if (Test-Path $Publish) { Remove-Item $Publish -Recurse -Force }

dotnet publish "$Projeto" -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o "$Publish"

if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (codigo $LASTEXITCODE)." }

# --- 2) Garantir que a pasta de rede existe ---------------------------
if (-not (Test-Path $Destino)) {
    Write-Host "==> Criando pasta de rede $Destino ..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $Destino -Force | Out-Null
}

# --- 3) Criar o atalho de abertura ------------------------------------
$LauncherPath  = Join-Path $Publish "Abrir AFM HSA.bat"
$LauncherLines = @(
    '@echo off',
    'title AFM HSA - Aftermarket Intelligence',
    'cd /d "%~dp0"',
    'start "" AfmHsa.exe'
)
Set-Content -Path $LauncherPath -Value $LauncherLines -Encoding ASCII

# --- 4) Copiar para a rede (espelhando) -------------------------------
Write-Host "==> Copiando para a rede (isso pode demorar)..." -ForegroundColor Cyan
robocopy "$Publish" "$Destino" /MIR /R:3 /W:5 /NP /NFL /NDL | Out-Null
$rc = $LASTEXITCODE
if ($rc -ge 8) { throw "robocopy falhou (codigo $rc)." }

Write-Host ""
Write-Host "==> Publicado com sucesso!" -ForegroundColor Green
Write-Host "    Os usuarios abrem por:  $Destino\Abrir AFM HSA.bat"
Write-Host "    (ou executando AfmHsa.exe direto na mesma pasta)"
Write-Host ""
