@echo off
setlocal

rem Domyslnie uruchom Pi w folderze, w ktorym znajduje sie ten plik.
set "PROJECT_DIR=%~dp0"

rem Opcjonalnie: przeciagnij folder na skrypt lub przekaz sciezke jako argument.
if not "%~1"=="" set "PROJECT_DIR=%~1"

if not exist "%PROJECT_DIR%\" (
    echo BLAD: Folder nie istnieje: "%PROJECT_DIR%"
    goto :end
)

pushd "%PROJECT_DIR%" || (
    echo BLAD: Nie moge otworzyc folderu: "%PROJECT_DIR%"
    goto :end
)

echo Folder projektu: %CD%
echo Uruchamiam Pi...
echo.

where pi.cmd >nul 2>&1
if not errorlevel 1 (
    call pi.cmd
    goto :after_pi
)

where pi.exe >nul 2>&1
if not errorlevel 1 (
    pi.exe
    goto :after_pi
)

where pi.bat >nul 2>&1
if not errorlevel 1 (
    call pi.bat
    goto :after_pi
)

echo BLAD: Nie znaleziono polecenia Pi w systemie.
echo Sprawdz w konsoli: pi --version
echo Jesli Pi nie jest zainstalowane, zobacz https://pi.dev/docs/latest/quickstart

:after_pi
popd

:end
echo.
echo Nacisnij dowolny klawisz, aby zamknac okno.
pause >nul
endlocal
