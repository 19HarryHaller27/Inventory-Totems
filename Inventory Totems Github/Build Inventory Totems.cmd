@echo off
cd /d "%~dp0"

echo Building Inventory Totems (Release)...
echo Uses Directory.Build.props for VintageStoryPath if set.
echo.

dotnet build "InventoryTotems.csproj" -c Release

if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%

echo.
echo OK: InventoryTotems.dll + modinfo + assets staged per InventoryTotems.csproj targets.
echo   - Project folder (next to this script): DLL + PDB + deploy copies as configured.
echo   - out\ForMods\ : full portable mod folder (DLL + modinfo + assets).
echo   - On Windows: %%APPDATA%%\VintagestoryData\Mods\InventoryTotems if deploy ran (see csproj; set InventoryTotemsNoDeploy=true to skip copy to AppData).
echo.
pause
