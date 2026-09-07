# Script pour generer l'executable autonome et l'installeur Windows
Write-Host "Compilation et packaging de DesktopOrganizeMaxxing..." -ForegroundColor Cyan

$outputDir = "publish\standalone"
if (Test-Path $outputDir) {
    Remove-Item -Recurse -Force $outputDir
}

dotnet publish src\DesktopOrganizeMaxxing\DesktopOrganizeMaxxing.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $outputDir

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[SUCCES] L'executable autonome est pret :" -ForegroundColor Green
    Write-Host "Emplacement : $(Resolve-Path $outputDir)\DesktopOrganizeMaxxing.exe" -ForegroundColor Yellow

    # Compilation de l'installeur Inno Setup
    $isccCandidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($iscc -and (Test-Path "installer.iss")) {
        Write-Host "`nGeneration de l'installeur Windows (DesktopOrganizeMaxxing_Setup.exe)..." -ForegroundColor Cyan
        & $iscc "installer.iss"
        if ($LASTEXITCODE -eq 0 -and (Test-Path "publish\installer\DesktopOrganizeMaxxing_Setup.exe")) {
            Write-Host "`n[SUCCES] L'installeur complet est pret :" -ForegroundColor Green
            Write-Host "Emplacement : $(Resolve-Path 'publish\installer\DesktopOrganizeMaxxing_Setup.exe')" -ForegroundColor Yellow
            Write-Host "Installeur pret a distribuer avec options de raccourci bureau et lancement au demarrage de Windows !" -ForegroundColor Cyan
        }
    }
} else {
    Write-Host "`n[ERREUR] Erreur lors de la compilation." -ForegroundColor Red
}
