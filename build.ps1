# Clean release build of Hyprism. Produces one file: dist\Hyprism-Setup-<version>-<arch>.exe
#   .\build.ps1                 # x64
#   .\build.ps1 -Arch arm64
param([ValidateSet('x64', 'arm64')] [string]$Arch = 'x64')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$app = "$root\src\Hyprism.App\Hyprism.App.csproj"
$folder = "$root\publish\$Arch"

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 is required: winget install JRSoftware.InnoSetup --scope user' }

# A running Hyprism locks its files.
Get-Process Hyprism -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# Clean: stale outputs would otherwise leak old files into the installer.
Remove-Item "$root\publish", "$root\dist", "$root\installer\Output" -Recurse -Force -ErrorAction SilentlyContinue
dotnet clean $app -c Release -p:Platform=$Arch -v q | Out-Null

dotnet run --project "$root\tests\Hyprism.Checks" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Self-checks failed.' }

dotnet publish $app -c Release -r "win-$Arch" -p:Platform=$Arch --self-contained true -o $folder
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

& $iscc /Q "/DArch=$Arch" "$root\installer\Hyprism.iss"
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

New-Item -ItemType Directory -Force "$root\dist" | Out-Null
Move-Item "$root\installer\Output\Hyprism-Setup-*-$Arch.exe" "$root\dist\" -Force
Remove-Item "$root\installer\Output", "$root\publish" -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem "$root\dist" | Format-Table Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } -AutoSize
