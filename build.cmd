@echo off
REM Baut NetPuls als einzelne NetPuls.exe (benoetigt auf dem Ziel-PC die .NET 8 Desktop Runtime).
REM Voraussetzung zum Bauen: .NET 8 SDK  (winget install Microsoft.DotNet.SDK.8)
cd /d "%~dp0src"
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ..\publish
echo.
echo Fertig: %~dp0publish\NetPuls.exe
pause
