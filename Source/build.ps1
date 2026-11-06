param(
    [string]$GameRoot = 'D:\SteamLibrary\steamapps\common\RimWorld',
    [string]$HarmonyDll = '',
    [string]$RimWorldVersion = ''
)

# ResearchLoops build script.
#
# 2026-11-06: RimWorld 1.7 (Waylith engine, net10.0) removed the
# RimWorldWin64_Data\Managed folder. Game assemblies now sit directly in the game root,
# and RimWorld.runtimeconfig.json redirects every UnityEngine* reference to
# UnityStubs.dll (WaylithClr.AssemblyNameRedirects). This script detects both layouts,
# so the same command builds against 1.6 and 1.7 without hand editing.

$ErrorActionPreference = 'Stop'
$modRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not (Test-Path $GameRoot)) { throw "GameRoot not found: $GameRoot" }

# ---- Layout detection ---------------------------------------------------------
$managed16 = Join-Path $GameRoot 'RimWorldWin64_Data\Managed'
$is17 = -not (Test-Path $managed16)
$managed = if ($is17) { $GameRoot } else { $managed16 }
if ([string]::IsNullOrEmpty($RimWorldVersion)) {
    $RimWorldVersion = if ($is17) { '1.7' } else { '1.6' }
}
Write-Output "RimWorld $RimWorldVersion layout: $managed"

# ---- Harmony ------------------------------------------------------------------
if (-not $HarmonyDll) {
    $harmonyRoot = Join-Path (Split-Path $GameRoot -Parent) '..\workshop\content\294100\2009463077'
    # 1.7 ships one Harmony folder per game version; prefer the matching one over Current.
    $candidates = @(
        (Join-Path $harmonyRoot "$RimWorldVersion\Assemblies\0Harmony.dll"),
        (Join-Path $harmonyRoot 'Current\Assemblies\0Harmony.dll'),
        (Join-Path $harmonyRoot '1.6\Assemblies\0Harmony.dll'),
        (Join-Path $harmonyRoot '1.7\Assemblies\0Harmony.dll')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { $HarmonyDll = $candidate; break }
    }
    if (-not $HarmonyDll) {
        throw "0Harmony.dll not found. Tried:`n  $($candidates -join "`n  ")`nPass -HarmonyDll <path> to override."
    }
}
if (-not (Test-Path $HarmonyDll)) { throw "0Harmony.dll not found: $HarmonyDll" }

# ---- References ---------------------------------------------------------------
# 1.6 (Mono/.NET Framework): mscorlib.dll is a real assembly, so a short explicit
# reference list is enough.
# 1.7 (Waylith, .NET 10): mscorlib.dll / netstandard.dll / System.* are thin
# forwarding facades (mscorlib is only 49 KB); the real framework types live in
# System.Private.CoreLib.dll (16 MB). With /nostdlib+ csc needs BOTH the facades
# and their forward targets, otherwise it fails with CS0518 (predefined type
# System.Object is not defined) and CS0012 for every LINQ / XML / collection /
# UnityEngine type that appears in the game assembly's signatures. The game root
# holds the exact runtime set, so reference every managed DLL there and skip
# native ones (AssemblyName.GetAssemblyName throws on those).
$core16Refs = @(
    'mscorlib.dll',
    'System.dll',
    'System.Core.dll',
    'System.Xml.dll',
    'netstandard.dll',
    'Assembly-CSharp.dll',
    # IMGUIModule / TextRenderingModule resolve the Widgets.Label / Widgets.ButtonText
    # overload families (GUIContent, TextAnchor) used by the settings + preset windows.
    'UnityEngine.IMGUIModule.dll',
    'UnityEngine.TextRenderingModule.dll',
    'UnityEngine.CoreModule.dll',
    'UnityEngine.dll'
)
$core17Refs = @(
    'mscorlib.dll', 'netstandard.dll', 'System.dll', 'System.Core.dll',
    'System.Xml.dll', 'System.Runtime.dll', 'System.Private.CoreLib.dll',
    'Assembly-CSharp.dll', 'UnityStubs.dll'
)
if ($is17) {
    $managedDlls = @(Get-ChildItem -Path $managed -Filter '*.dll' -File | Where-Object {
        try { [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null; $true }
        catch { $false }
    } | ForEach-Object { $_.Name })
    $referenceNames = @($core17Refs + $managedDlls | Select-Object -Unique)
} else {
    $referenceNames = $core16Refs
}
Write-Output "Referencing $($referenceNames.Count) assemblies from $managed"
foreach ($name in $referenceNames) {
    $path = Join-Path $managed $name
    if (-not (Test-Path $path)) {
        throw "Missing game reference: $path`nIf this is a game-version mismatch, pass -GameRoot to the right install."
    }
}

# ---- Compiler -----------------------------------------------------------------
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$sdkVersion = (& $dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot locate .NET SDK.' }
$compiler = Join-Path (Split-Path $dotnet) "sdk\$sdkVersion\Roslyn\bincore\csc.dll"

$outputDirectory = Join-Path $modRoot 'Assemblies'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$sources = @(Get-ChildItem -Path $PSScriptRoot -Filter '*.cs' -Recurse -File | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw "No .cs sources found under $PSScriptRoot" }

$compilerArgs = @(
    '/nologo', '/target:library', '/optimize+', '/nostdlib+', '/langversion:7.3',
    "/out:$outputDirectory\ResearchLoops.dll"
)
foreach ($name in $referenceNames) {
    $compilerArgs += "/reference:$(Join-Path $managed $name)"
}
$compilerArgs += "/reference:$HarmonyDll"
$compilerArgs += $sources

& $dotnet $compiler @compilerArgs 2>&1 | ForEach-Object { $_.ToString() } | Out-File -FilePath (Join-Path $modRoot 'build_errors.log') -Encoding UTF8
if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $LASTEXITCODE — see $(Join-Path $modRoot 'build_errors.log')" }

Write-Output "Built $outputDirectory\ResearchLoops.dll ($($sources.Count) source files)"
