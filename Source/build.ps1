param(
    [string]$GameRoot = 'D:\SteamLibrary\steamapps\common\RimWorld',
    [string]$HarmonyDll = '',
    [string]$RimWorldVersion = '1.6'
)

# ResearchLoops build script.
# Precedent: Mods\NoForcedSlowdown\Source\build.ps1 (only deviation: source input is
# every *.cs under Source\, plus an explicit System.Xml reference for the def generator).

$ErrorActionPreference = 'Stop'
$modRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not (Test-Path $GameRoot)) { throw "GameRoot not found: $GameRoot" }
if (-not $HarmonyDll) {
    # Harmony used to live under 'Current\Assemblies', but the workshop item now ships one folder
    # per game version, so probing only 'Current' made this script fail on a normal install.
    # Candidates are tried in order; 'Current' is kept first for older layouts.
    $harmonyRoot = Join-Path (Split-Path $GameRoot -Parent) '..\workshop\content\294100\2009463077'
    $candidates = @(
        (Join-Path $harmonyRoot 'Current\Assemblies\0Harmony.dll'),
        (Join-Path $harmonyRoot "$RimWorldVersion\Assemblies\0Harmony.dll")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { $HarmonyDll = $candidate; break }
    }
    if (-not $HarmonyDll) {
        throw "0Harmony.dll not found. Tried:`n  $($candidates -join "`n  ")`nPass -HarmonyDll <path> to override."
    }
}
if (-not (Test-Path $HarmonyDll)) { throw "0Harmony.dll not found: $HarmonyDll" }

$managed = Join-Path $GameRoot 'RimWorldWin64_Data\Managed'
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
    "/out:$outputDirectory\ResearchLoops.dll",
    "/reference:$managed\mscorlib.dll",
    "/reference:$managed\System.dll",
    "/reference:$managed\System.Core.dll",
    "/reference:$managed\System.Xml.dll",
    "/reference:$managed\netstandard.dll",
    "/reference:$managed\Assembly-CSharp.dll",
    # IMGUIModule / TextRenderingModule are needed to resolve the Widgets.Label / Widgets.ButtonText
    # overload families (GUIContent, TextAnchor) that the settings + preset windows call. Without
    # them overload resolution fails with CS0012 even though those exact overloads are never taken.
    "/reference:$managed\UnityEngine.IMGUIModule.dll",
    "/reference:$managed\UnityEngine.TextRenderingModule.dll",
    "/reference:$managed\UnityEngine.CoreModule.dll",
    "/reference:$managed\UnityEngine.dll",
    "/reference:$HarmonyDll"
) + $sources

& $dotnet $compiler @compilerArgs 2>&1 | ForEach-Object { $_.ToString() } | Out-File -FilePath (Join-Path $modRoot 'build_errors.log') -Encoding UTF8
if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $LASTEXITCODE" }

Write-Output "Built $outputDirectory\ResearchLoops.dll ($($sources.Count) source files)"
