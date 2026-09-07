# Script rapide pour generer uniquement l'installeur Windows a partir du binaire existant
Write-Host "Generation de l'installeur Windows..." -ForegroundColor Cyan

$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host "[ERREUR] Compilateur Inno Setup non trouve (ISCC.exe)." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path "publish\standalone\DesktopOrganizeMaxxing.exe")) {
    Write-Host "[INFO] Le binaire autonome n'existe pas encore. Lancement de build_release.ps1..." -ForegroundColor Yellow
    & .\build_release.ps1
    exit $LASTEXITCODE
}

& $iscc "installer.iss"

if ($LASTEXITCODE -eq 0 -and (Test-Path "publish\installer\DesktopOrganizeMaxxing_Setup.exe")) {
    Write-Host "`n[SUCCES] L'installeur Windows est pret :" -ForegroundColor Green
    Write-Host "Emplacement : $(Resolve-Path 'publish\installer\DesktopOrganizeMaxxing_Setup.exe')" -ForegroundColor Yellow
    Write-Host "Taille : $([math]::Round((Get-Item 'publish\installer\DesktopOrganizeMaxxing_Setup.exe').Length / 1MB, 2)) Mo" -ForegroundColor Cyan
} else {
    Write-Host "`n[ERREUR] Echec de la generation de l'installeur." -ForegroundColor Red
}
