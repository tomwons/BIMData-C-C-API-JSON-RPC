@echo off
setlocal

rem Ustaw zmienne
set BUILD_DIR=Build
set AC_VERSION=29
set AC_API_DEVKIT_DIR=D:\API.Development.Kit.WIN.29.3000\Support

rem Usuń istniejący katalog Build, jeśli jest
if exist %BUILD_DIR% (
    rmdir /s /q %BUILD_DIR%
)

rem Utwórz katalog Build
mkdir %BUILD_DIR%
cd %BUILD_DIR%

rem Konfiguracja CMake
cmake -G "Visual Studio 18 2026" -A "x64" -T "v143" ^
  -DAC_VERSION=%AC_VERSION% ^
  -DAC_API_DEVKIT_DIR="%AC_API_DEVKIT_DIR%" ^
  ..

rem Budowanie projektu w trybie Debug
cmake --build . --config Debug

echo.
echo Gotowe! Projekt został pomyślnie zbudowany.

pause
endlocal