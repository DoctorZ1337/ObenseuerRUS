# Полная сборка: DLL фреймворка (если нет) -> dotnet build -> тесты -> проверка переводов -> dist\ObenseuerRUS-<версия>.zip.
# Запуск из корня проекта: powershell -ExecutionPolicy Bypass -File tools\build.ps1
# Папка игры ищется как в Directory.Build.props (OBENSEUER_GAME_DIR, затем библиотеки Steam).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Invoke-Step([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe $($arguments -join ' ') завершился с кодом $LASTEXITCODE" }
}

$csproj = 'src\ObenseuerRUS\ObenseuerRUS.csproj'
$version = ([xml](Get-Content $csproj -Raw -Encoding UTF8)).Project.PropertyGroup.Version | Select-Object -First 1
$modSource = Get-Content 'src\ObenseuerRUS\RusMod.cs' -Raw -Encoding UTF8
if ($modSource -notmatch "ModInfo\([^)]*`"$([regex]::Escape($version))`"\)") { throw "Версия в RusMod.cs (ModInfo) не совпадает с $version из $csproj" }

if (-not (Test-Path 'deps\ObenseuerCore\ObenseuerCore.dll')) { & (Join-Path $PSScriptRoot 'fetch-deps.ps1') }

Invoke-Step 'dotnet' @('build', $csproj, '-c', 'Release', '-nologo')
Invoke-Step 'dotnet' @('test', 'tests\ObenseuerRUS.Tests', '-c', 'Release', '-nologo')
Invoke-Step 'python' @('-I', 'tools\check.py')

$stage = Join-Path $root "build\package-$version"
$plugin = Join-Path $stage 'BepInEx\plugins\ObenseuerRUS'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force (Join-Path $plugin 'translations') | Out-Null
Copy-Item 'src\ObenseuerRUS\bin\Release\net472\ObenseuerRUS.dll' $plugin
Copy-Item 'translations\*.json' (Join-Path $plugin 'translations')
Copy-Item 'README.md' $plugin
# Шрифт под GPL идёт вместе со своим исходником — скриптом, которым он собран.
New-Item -ItemType Directory -Force (Join-Path $plugin 'fonts') | Out-Null
Copy-Item 'fonts\*', 'tools\font\make_cyrillic_font.py' (Join-Path $plugin 'fonts')

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "ObenseuerRUS-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
# Не CreateFromDirectory: в Windows PowerShell 5.1 он пишет пути с '\', и такие архивы неверно распаковываются вне Windows.
$archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    Get-ChildItem $stage -Recurse -File | ForEach-Object {
        $entry = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry) | Out-Null
    }
} finally { $archive.Dispose() }

# В архиве должны быть только файлы мода: свой DLL, переводы, README и свой шрифт с лицензией — без фреймворка и файлов игры.
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName })
} finally { $archive.Dispose() }
$prefix = 'BepInEx/plugins/ObenseuerRUS/'
$foreign = @($names | Where-Object { $_ -ne "${prefix}ObenseuerRUS.dll" -and $_ -ne "${prefix}README.md" -and $_ -notmatch "^$([regex]::Escape($prefix))translations/[^/]+\.json$" -and $_ -notmatch "^$([regex]::Escape($prefix))fonts/(ObenseuerTypewriterCyrillic\.ttf|COPYING-FreeFont\.txt|FONT-NOTICE\.txt|make_cyrillic_font\.py)$" })
if ($foreign.Count -gt 0) { throw "Лишние файлы в архиве: $($foreign -join ', ')" }
foreach ($required in 'ObenseuerRUS.dll', 'fonts/ObenseuerTypewriterCyrillic.ttf', 'fonts/COPYING-FreeFont.txt') {
    if ($names -notcontains "$prefix$required") { throw "В архиве нет $required" }
}
$jsonCount = @($names | Where-Object { $_ -like "${prefix}translations/*.json" }).Count
Write-Host "Готово: $zip ($($names.Count) файлов, переводов: $jsonCount)"
