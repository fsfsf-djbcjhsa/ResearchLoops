param(
    [string]$GameRoot = 'D:\SteamLibrary\steamapps\common\RimWorld'
)

# Deploys the built mod into RimWorld's Mods folder.
#
# The project lives on the Desktop (per DESIGN §"项目路径"), but RimWorld only scans <game>/Mods,
# so this mirrors the runtime file set there. Build first: Source\build.ps1.
# Only the runtime folders are copied: docs, Source and Tests stay in the project.

$ErrorActionPreference = 'Stop'
$modRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$target = Join-Path $GameRoot 'Mods\ResearchLoops'

if (-not (Test-Path (Join-Path $modRoot 'About\About.xml'))) { throw "About\About.xml missing in $modRoot" }
if (-not (Test-Path (Join-Path $modRoot 'Assemblies\ResearchLoops.dll'))) {
    throw "Assemblies\ResearchLoops.dll missing — run Source\build.ps1 first."
}

$runtimes = @('About', 'Assemblies', 'Defs', 'Patches', 'Data', 'Languages', 'Sounds')

if (Test-Path $target) { Remove-Item -Recurse -Force $target }
New-Item -ItemType Directory -Force -Path $target | Out-Null

foreach ($folder in $runtimes) {
    $source = Join-Path $modRoot $folder
    if (-not (Test-Path $source)) { throw "expected runtime folder missing: $source" }
    $destination = Join-Path $target $folder
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
}

$drafts = Join-Path $target 'Data\LoopDrafts'
if (Test-Path $drafts) { Remove-Item -Recurse -Force $drafts }   # unmerged authoring notes, never runtime

Write-Output "Deployed to $target"
Get-ChildItem $target -Recurse -File | ForEach-Object {
    "  {0,-58} {1,7} bytes" -f $_.FullName.Substring($target.Length + 1), $_.Length
}
