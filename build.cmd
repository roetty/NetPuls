@echo off
REM Baut NetPuls als einzelne, eigenstaendige NetPuls.exe (kein .NET noetig auf dem Ziel-PC).
REM Voraussetzung zum Bauen: .NET 8 SDK  (winget install Microsoft.DotNet.SDK.8)
cd /d "%~dp0src"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o ..\publish
echo.
echo Fertig: %~dp0publish\NetPuls.exe
pause
