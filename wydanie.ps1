# Buduje wszystkie wersje do wydania i pakuje je do ZIP z sumami SHA-256.
# Uzywany przez GitHub Actions (.github/workflows/wydanie.yml); mozna go tez uruchomic lokalnie:
#   powershell -ExecutionPolicy Bypass -File wydanie.ps1 -Wersja 1.2.0
# Wynik: output\wydanie\*.zip + SHA256SUMS.txt. Folder output jest najpierw czyszczony w calosci.
param(
    [Parameter(Mandatory = $true)][string]$Wersja
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$Wersja = $Wersja.Trim().TrimStart('v', 'V')
if ($Wersja -notmatch '^\d+\.\d+\.\d+$') { throw "Wersja musi miec postac 1.2.3 (podano: $Wersja)" }

$output = Join-Path $PSScriptRoot 'output'
$pakiety = Join-Path $output 'pakiety'
$wydanie = Join-Path $output 'wydanie'
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Force $pakiety, $wydanie | Out-Null

$wersjaPliku = "$Wersja.0"
$wspolne = @('-c', 'Release', '-v', 'q', '-nologo',
    "-p:Version=$Wersja", "-p:FileVersion=$wersjaPliku", "-p:AssemblyVersion=$wersjaPliku",
    '-p:IncludeSourceRevisionInInformationalVersion=false')
$samodzielna = @('--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true', '-p:DebugType=none')

# Nazwa pliku ZIP => argumenty publikacji
$warianty = [ordered]@{
    'win-x64'      = @('-r', 'win-x64') + $samodzielna
    'win-x86'      = @('-r', 'win-x86') + $samodzielna
    'win-arm64'    = @('-r', 'win-arm64') + $samodzielna
    'lite-win-x64' = @('--self-contained', 'false')
}

# Stala data plikow LibVLC: plugins.dat zapamietuje date i rozmiar kazdej wtyczki,
# a parzysta sekunda w UTC przetrwa zapis w ZIP (format ZIP zaokragla czas do 2 s).
$dataPlikow = [datetime]::SpecifyKind([datetime]'2026-01-01T00:00:00', 'Utc')

function Usun-Kompilacje { foreach ($d in 'bin', 'obj') { if (Test-Path $d) { Remove-Item $d -Recurse -Force } } }

foreach ($nazwa in $warianty.Keys) {
    Write-Host "[*] Buduje $nazwa..."
    $cel = Join-Path $pakiety "$nazwa\SimpleRadioPlayer"
    Usun-Kompilacje
    & dotnet publish @wspolne @($warianty[$nazwa]) -o $cel
    if ($LASTEXITCODE -ne 0) { throw "Kompilacja $nazwa nie powiodla sie" }
    Usun-Kompilacje

    Get-ChildItem (Join-Path $cel 'libvlc') -Recurse -File | ForEach-Object { $_.LastWriteTimeUtc = $dataPlikow }

    # Cache wtyczek da sie wygenerowac tylko exe, ktore ten komputer potrafi uruchomic
    $arm = $nazwa -like '*arm64*'
    if (-not $arm -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
        $exe = Join-Path $cel 'SimpleRadioPlayer.exe'
        Start-Process -FilePath $exe -ArgumentList '--przygotuj-wtyczki' -Wait -NoNewWindow
        $dat = Get-ChildItem (Join-Path $cel 'libvlc') -Recurse -Filter 'plugins.dat' | Select-Object -First 1
        if ($dat) { $dat.LastWriteTimeUtc = $dataPlikow } else { Write-Warning "Brak plugins.dat w $nazwa - program utworzy go przy pierwszym starcie" }
    }
    # Eksplorator rozpakowuje ZIP z datami w czasie lokalnym (przesuniecie o strefe) -
    # program odczytuje stad date docelowa i przywraca ja przed startem LibVLC
    $katalogVlc = Get-ChildItem (Join-Path $cel 'libvlc') -Directory | Select-Object -First 1
    [IO.File]::WriteAllText((Join-Path $katalogVlc.FullName 'daty-plikow.txt'), $dataPlikow.ToString('yyyy-MM-ddTHH:mm:ssZ'))

    $zip = Join-Path $wydanie "SimpleRadioPlayer-$Wersja-$nazwa.zip"
    $sevenZip = @((Get-Command 7z -ErrorAction SilentlyContinue).Source, "$env:ProgramFiles\7-Zip\7z.exe") |
        Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    Push-Location (Join-Path $pakiety $nazwa)
    try {
        if ($sevenZip) {
            # -mtc=on: dokladne daty plikow (NTFS) w archiwum, nie tylko 2-sekundowe
            & $sevenZip a -tzip -mx=9 -mtc=on -bso0 -bsp0 $zip 'SimpleRadioPlayer'
            if ($LASTEXITCODE -ne 0) { throw "Pakowanie $nazwa nie powiodlo sie" }
        }
        else {
            Compress-Archive -Path 'SimpleRadioPlayer' -DestinationPath $zip -CompressionLevel Optimal
        }
    }
    finally { Pop-Location }
    Write-Host ("    {0} ({1:N1} MB)" -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB))
}

$sumy = Get-ChildItem $wydanie -Filter '*.zip' | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
[IO.File]::WriteAllLines((Join-Path $wydanie 'SHA256SUMS.txt'), [string[]]$sumy)
Write-Host "[OK] Gotowe: $wydanie"
