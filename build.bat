@echo off
setlocal enabledelayedexpansion

echo.
echo === Discord Monitor - Build Script ===
echo.

dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] .NET SDK no encontrado.
    pause
    exit /b 1
)

echo [OK] .NET SDK detectado
echo.

echo [1/2] Compilando...
cd /d "%~dp0DiscordMonitor"

dotnet publish -c Release -o "..\publish" --nologo

if %errorlevel% neq 0 (
    echo [ERROR] La compilacion fallo.
    pause
    exit /b 1
)

echo.
echo [OK] Listo en: publish\DiscordMonitor.exe
echo.
echo Ejecuta publish\DiscordMonitor.exe para iniciar la app.
echo Para autostart: clic derecho en el tray, Iniciar con Windows.
echo.
pause
