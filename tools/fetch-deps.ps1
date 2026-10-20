# Скачивает официальный релиз Obenseuer Modding Framework v0.12.0-beta и кладёт ObenseuerCore.dll
# в deps\ObenseuerCore\ — только для сборки. В архив мода фреймворк не попадает.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$url = 'https://github.com/NomadsReach/Obenseuer-Modding-Framework/releases/download/v0.12.0-beta/ObenseuerModdingFramework-0.12.0-beta.zip'
$sha256 = 'E100A251EFAAF245AB5EAB1685E263C9A8E82B47914D24366825EBA243BB7834'
$deps = Join-Path $root 'deps'
$zip = Join-Path $deps 'ObenseuerModdingFramework-0.12.0-beta.zip'
$target = Join-Path $deps 'ObenseuerCore'

New-Item -ItemType Directory -Force $deps | Out-Null
if (-not (Test-Path $zip)) {
    Write-Host "Скачиваю $url"
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
}
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
if ($hash -ne $sha256) {
    Remove-Item $zip
    throw "SHA-256 не совпадает: $hash (ожидалось $sha256)"
}
$tmp = Join-Path $deps 'extract'
if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
Expand-Archive -LiteralPath $zip -DestinationPath $tmp
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $tmp 'BepInEx\plugins\ObenseuerCore\ObenseuerCore.dll') $target -Force
Copy-Item (Join-Path $tmp 'BepInEx\plugins\ObenseuerCore\LICENSE') (Join-Path $target 'LICENSE') -Force
Remove-Item -Recurse -Force $tmp
Write-Host "Готово: $target\ObenseuerCore.dll (SHA-256 архива проверен)"
