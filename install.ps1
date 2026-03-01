# install.ps1 - bootstrap do ralph no Windows (baixa release + executa "ralph install")
# Uso:
#   irm https://raw.githubusercontent.com/rodrigojager/ralph/main/install.ps1 | iex
#   $env:RALPH_REPO="owner/repo"; irm https://raw.githubusercontent.com/rodrigojager/ralph/main/install.ps1 | iex

param(
    [string]$Repo = $env:RALPH_REPO,
    [string]$Version = "latest",
    [string]$BinDir = "$env:LOCALAPPDATA\Ralph\bin"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Repo)) { $Repo = "rodrigojager/ralph" }

$env:RALPH_REPO = $Repo
$userAgent = @{ "User-Agent" = "ralph-installer" }

if ($Version -eq "latest") {
    $apiUrl = "https://api.github.com/repos/$Repo/releases/latest"
}
else {
    $apiUrl = "https://api.github.com/repos/$Repo/releases/tags/$Version"
}

Write-Host "Consultando release em $apiUrl ..."
$release = Invoke-RestMethod -Uri $apiUrl -Headers $userAgent

$binaryAsset = $release.assets | Where-Object { $_.name -eq "ralph-win-x64.exe" } | Select-Object -First 1
if (-not $binaryAsset) {
    Write-Error "Asset ralph-win-x64.exe não encontrado na release. Repositório: $Repo"
    exit 1
}

$langAsset = $release.assets | Where-Object { $_.name -eq "ralph-lang.zip" } | Select-Object -First 1

$tempRoot = Join-Path $env:TEMP ("ralph-bootstrap-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

try {
    $bootstrapExe = Join-Path $tempRoot "ralph.exe"
    Write-Host "Baixando $($binaryAsset.name) ..."
    Invoke-WebRequest -Uri $binaryAsset.browser_download_url -OutFile $bootstrapExe -Headers $userAgent

    if ($langAsset) {
        $assetsDir = Join-Path $tempRoot "assets"
        New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
        $langZip = Join-Path $assetsDir "ralph-lang.zip"
        Write-Host "Baixando pacote de idiomas ..."
        Invoke-WebRequest -Uri $langAsset.browser_download_url -OutFile $langZip -Headers $userAgent
        Expand-Archive -Path $langZip -DestinationPath (Join-Path $tempRoot "lang") -Force
    }

    Write-Host "Executando instalador interativo: ralph install $BinDir"
    & $bootstrapExe install $BinDir
    if ($LASTEXITCODE -ne 0) {
        throw "ralph install falhou com exit code $LASTEXITCODE"
    }

    Write-Host ""
    Write-Host "Instalação concluída. Verifique em um novo terminal: ralph --help"
}
finally {
    Remove-Item -Path $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
