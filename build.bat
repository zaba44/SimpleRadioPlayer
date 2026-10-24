@echo off
chcp 65001 >nul
title Build - SimpleRadioPlayer
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 goto brak_dotnet

:menu
cls
echo ========================================
echo    BUILD - SimpleRadioPlayer
echo ========================================
echo.
echo    [1] Wersja lite - mala, wymaga .NET
echo    [2] Samodzielna x64
echo    [3] Samodzielna x86
echo    [4] Samodzielna arm64
echo    [5] Wszystkie samodzielne
echo    [6] Wszystko
echo    [0] Wyjscie
echo.
echo    Uwaga: obok exe zawsze musi lezec folder libvlc - kopiuj caly folder.
echo    Kazde budowanie usuwa CALY folder output - z listami, ustawieniami i logo.
echo.
set "wybor="
set /p wybor=Wybierz opcje:
if "%wybor%"=="1" goto lite
if "%wybor%"=="2" goto b_x64
if "%wybor%"=="3" goto b_x86
if "%wybor%"=="4" goto b_arm64
if "%wybor%"=="5" goto wszystkie
if "%wybor%"=="6" goto calosc
if "%wybor%"=="0" goto koniec
goto menu

:lite
call :czysc_output
if errorlevel 1 goto blad
call :czysc
echo.
echo [*] Buduje wersje lite...
dotnet publish -c Release --self-contained false -o output\lite
if errorlevel 1 goto blad
call :wtyczki output\lite
goto gotowe

:b_x64
call :czysc_output
if errorlevel 1 goto blad
call :czysc
call :buduj win-x64 x64
if errorlevel 1 goto blad
goto gotowe

:b_x86
call :czysc_output
if errorlevel 1 goto blad
call :czysc
call :buduj win-x86 x86
if errorlevel 1 goto blad
goto gotowe

:b_arm64
call :czysc_output
if errorlevel 1 goto blad
call :czysc
call :buduj win-arm64 arm64
if errorlevel 1 goto blad
goto gotowe

:wszystkie
call :czysc_output
if errorlevel 1 goto blad
call :czysc
call :buduj win-x64 x64
if errorlevel 1 goto blad
call :buduj win-x86 x86
if errorlevel 1 goto blad
call :buduj win-arm64 arm64
if errorlevel 1 goto blad
goto gotowe

:calosc
call :czysc_output
if errorlevel 1 goto blad
call :czysc
echo.
echo [*] Buduje wersje lite...
dotnet publish -c Release --self-contained false -o output\lite
if errorlevel 1 goto blad
call :wtyczki output\lite
call :buduj win-x64 x64
if errorlevel 1 goto blad
call :buduj win-x86 x86
if errorlevel 1 goto blad
call :buduj win-arm64 arm64
if errorlevel 1 goto blad
goto gotowe

:gotowe
call :czysc
echo.
echo [OK] Gotowe. Wyniki w folderze output
echo.
pause
goto menu

:blad
call :czysc
echo.
echo [BLAD] Kompilacja nie powiodla sie. Przewin w gore po szczegoly.
echo.
pause
goto menu

:brak_dotnet
echo.
echo [BLAD] Nie znaleziono .NET SDK.
echo Pobierz go ze strony https://dotnet.microsoft.com/download
echo.
pause
goto koniec

:buduj
echo.
echo [*] Buduje wersje samodzielna %~2...
call :czysc
dotnet publish -c Release -r %~1 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o output\standalone\%~2
if errorlevel 1 exit /b 1
if /i "%~2"=="arm64" if /i not "%PROCESSOR_ARCHITECTURE%"=="ARM64" exit /b 0
call :wtyczki output\standalone\%~2
exit /b 0

:wtyczki
rem plugins.dat sprawia, ze LibVLC nie laduje wszystkich ~300 wtyczek przy kazdym starcie
echo [*] Przygotowanie cache wtyczek LibVLC w %~1...
start "" /wait "%~1\SimpleRadioPlayer.exe" --przygotuj-wtyczki
exit /b 0

:czysc_output
rem Calkowite czyszczenie: poprzednie wersje, listy stacji, ustawienia i logotypy
if exist output rmdir /s /q output
if not exist output exit /b 0
echo.
echo [BLAD] Nie mozna usunac folderu output - zamknij program SimpleRadioPlayer i sprobuj ponownie.
exit /b 1

:czysc
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj
exit /b 0

:koniec
