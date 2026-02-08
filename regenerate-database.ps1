# Script PowerShell pour régénérer la base de données avec les nouvelles données

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Régénération de la base de données MMV avec données complètes" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Arrêter l'application si elle tourne
Write-Host "[1/4] Arrêt de l'application en cours..." -ForegroundColor Yellow
taskkill /IM MMV.App.exe /F 2>$null
Start-Sleep -Seconds 1

# 2. Supprimer les anciennes bases de données
Write-Host "[2/4] Suppression des anciennes bases de données..." -ForegroundColor Yellow

# Base de données dans le répertoire de l'app
$appDbPath = ".\src\MMV.App\bin\Debug\net8.0\mmv-optic.db"
if (Test-Path $appDbPath) {
    Remove-Item $appDbPath -Force
    Write-Host "  ✓ Supprimé: $appDbPath" -ForegroundColor Green
}

# Base de données dans LOCALAPPDATA
$localAppDataPath = "$env:LOCALAPPDATA\ManageMyVision\mmv.db"
if (Test-Path $localAppDataPath) {
    Remove-Item $localAppDataPath -Force
    Write-Host "  ✓ Supprimé: $localAppDataPath" -ForegroundColor Green
}

# Supprimer aussi les fichiers -shm et -wal s'ils existent
$dbFiles = @(
    ".\src\MMV.App\bin\Debug\net8.0\mmv-optic.db-shm",
    ".\src\MMV.App\bin\Debug\net8.0\mmv-optic.db-wal",
    "$env:LOCALAPPDATA\ManageMyVision\mmv.db-shm",
    "$env:LOCALAPPDATA\ManageMyVision\mmv.db-wal"
)

foreach ($file in $dbFiles) {
    if (Test-Path $file) {
        Remove-Item $file -Force
        Write-Host "  ✓ Supprimé: $file" -ForegroundColor Green
    }
}

Write-Host ""

# 3. Compiler et lancer l'application
Write-Host "[3/4] Compilation et lancement de l'application..." -ForegroundColor Yellow
Push-Location ".\src\MMV.App"

dotnet build --configuration Debug

if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Compilation réussie" -ForegroundColor Green
    Write-Host ""
    Write-Host "[4/4] Lancement de l'application avec nouvelle base de données..." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host "  Nouvelle base de données créée avec:" -ForegroundColor Green
    Write-Host "  • 50 Clients" -ForegroundColor White
    Write-Host "  • 25 Fournisseurs" -ForegroundColor White
    Write-Host "  • 100 Produits (montures, verres, lentilles, accessoires)" -ForegroundColor White
    Write-Host "  • ~70 Ordonnances" -ForegroundColor White
    Write-Host "  • 40 Commandes" -ForegroundColor White
    Write-Host "  • 60 Ventes" -ForegroundColor White
    Write-Host "  • Mouvements de stock et notifications" -ForegroundColor White
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Identifiants de connexion:" -ForegroundColor Yellow
    Write-Host "  Username: admin" -ForegroundColor White
    Write-Host "  Password: admin" -ForegroundColor White
    Write-Host ""
    
    Start-Process dotnet -ArgumentList "run" -NoNewWindow
} else {
    Write-Host "  ✗ Erreur de compilation" -ForegroundColor Red
}

Pop-Location

Write-Host ""
Write-Host "Appuyez sur une touche pour fermer..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
