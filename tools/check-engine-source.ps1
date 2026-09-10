# Milestone engine-source isolation guard.
# Allowed: engine-built binaries/packages (.dll/.exe/.lib/.a/.nupkg/.pdb) and data assets.
# Forbidden: engine source/header/project files or an engine source tree anywhere under scan root.
# Usage:
#   pwsh -NoProfile -File tools\check-engine-source.ps1 -FailOnFind
#   pwsh -NoProfile -File tools\check-engine-source.ps1 -Path ..\HybridProjects\MILESTONE -FailOnFind
#   pwsh -NoProfile -File tools\check-engine-source.ps1 -Quarantine
[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string[]]$Path = @(),
    [switch]$FailOnFind,
    [switch]$Quarantine,
    [string]$QuarantineRoot,
    [switch]$IncludeHistory
)
$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = (Get-Location).Path }
$Root = (Resolve-Path -LiteralPath $Root).Path
if (-not $Path -or $Path.Count -eq 0) { $Path = @($Root) }
$scanRoots = @()
foreach ($p in $Path) {
    if ([string]::IsNullOrWhiteSpace($p)) { continue }
    if (Test-Path -LiteralPath $p) { $scanRoots += (Resolve-Path -LiteralPath $p).Path }
    else { Write-Warning "scan path not found: $p" }
}
if ($scanRoots.Count -eq 0) { throw 'no scan path' }
if ([string]::IsNullOrWhiteSpace($QuarantineRoot)) {
    $QuarantineRoot = Join-Path (Split-Path -Parent $Root) '_engine-source-quarantine'
}
$QuarantineRoot = [System.IO.Path]::GetFullPath($QuarantineRoot)
function Get-RelPath([string]$Base, [string]$FullName) {
    $b = [System.IO.Path]::GetFullPath($Base).TrimEnd('\', '/')
    $f = [System.IO.Path]::GetFullPath($FullName)
    if ($f.StartsWith($b, [System.StringComparison]::OrdinalIgnoreCase)) { return $f.Substring($b.Length).TrimStart('\', '/') }
    return $f
}
function Test-IsUnder([string]$Child, [string]$Parent) {
    $c = [System.IO.Path]::GetFullPath($Child).TrimEnd('\', '/') + '\'
    $p = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + '\'
    return $c.StartsWith($p, [System.StringComparison]::OrdinalIgnoreCase)
}
function Read-HeadText([string]$File) {
    try {
        $fs = [System.IO.File]::Open($File, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $len = [Math]::Min(131072, $fs.Length)
            $buf = New-Object byte[] $len
            [void]$fs.Read($buf, 0, $len)
            return [System.Text.Encoding]::UTF8.GetString($buf)
        } finally { $fs.Dispose() }
    } catch { return '' }
}$strongEngineDirs = @('引擎源码', '引擎', '引擎v3', 'engine-v3', 'engine-tree', 'rhygemaker', 'hybridengine')
$engineDirContext = @('build', 'obj', 'scratch', 'backup', 'pre-migration', '迁移', 't56', 't104', '输出产物', '构建产物')
$nativeExt = @('.h', '.hpp', '.hh', '.hxx', '.c', '.cc', '.cpp', '.cxx', '.inl', '.ipp', '.ixx', '.cppm', '.m', '.mm')
$projectExt = @('.csproj', '.sln', '.vcxproj', '.cmake', '.mk')
$binaryOrDataExt = @(
    '.dll', '.exe', '.lib', '.a', '.obj', '.o', '.pdb', '.so', '.dylib', '.nupkg', '.snupkg', '.zip', '.7z', '.tar', '.gz',
    '.mil', '.osu', '.mc', '.sm', '.ssc', '.qua', '.aff', '.adofai', '.msprefab', '.mscene', '.msmeta',
    '.wav', '.mp3', '.ogg', '.bmp', '.png', '.jpg', '.jpeg', '.gif', '.ttf', '.otf', '.ini', '.ico', '.lnk'
)
$engineNativeMarkers = @(
    'HYBRIDENGINE_REFLECT', 'HYBRIDENGINE_API', 'namespace HybridEngine',
    '#include <hybridengine', '#include "hybridengine', '#include <rhygemaker', '#include "rhygemaker',
    'ms_bind.h', 'ms_engine_', 'ms_rhythm_', 'MS_RHYTHM_', 'MS_API', 'namespace rhygemaker'
)
$engineManagedMarkers = @(
    'namespace HybridEngine.Engine.Internal', 'private const string Dll = "hybridengine"', 'DllImport(Dll'
)
$engineProjectMarkers = @(
    'AssemblyName>MilestoneEngine', 'AssemblyName>HybridEngine'
)
function Test-AllowedGenerated([string]$RelPath, [string]$Name) {
    if ($RelPath -match '(?i)CMakeFiles[\\/].*CompilerId[^\\/]*[\\/].*\.(c|cpp)$') { return $true }
    if ($Name -match '(?i)^CMake(C|CXX)CompilerId\.') { return $true }
    return $false
}
function Test-ProjectReferencesInRepoEngine([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    if ($Text -match '(?i)(ProjectReference|Compile)[^>]*Include\s*=\s*"[^"]*(引擎源码|引擎[\\/]engine)' ) { return $true }
    if ($Text -match '(?i)(ProjectReference|Compile)[^>]*Include\s*=\s*"\.\.[\\/][^"]*[\\/]engine[\\/]') { return $true }
    return $false
}
function Test-EngineSourceFile([System.IO.FileInfo]$File, [string]$ScanRoot, [ref]$Reason) {
    $rel = Get-RelPath $ScanRoot $File.FullName
    $ext = $File.Extension.ToLowerInvariant()
    if (Test-AllowedGenerated -RelPath $rel -Name $File.Name) { return $false }
    if ($binaryOrDataExt -contains $ext) { return $false }
    $isSource = ($nativeExt -contains $ext) -or ($ext -eq '.cs') -or ($ext -eq '.py') -or ($projectExt -contains $ext)
    if (-not $isSource) { return $false }
    $parts = $rel -split '[\\/]' | Where-Object { $_ -ne '' }
    $underEngineDir = $false
    $rootLeaf = Split-Path -Leaf (Resolve-Path -LiteralPath $ScanRoot)
    if ($rootLeaf -and ($strongEngineDirs -contains $rootLeaf)) { $underEngineDir = $true }
    if (-not $underEngineDir -and $rootLeaf -ieq 'engine' -and (Test-Path -LiteralPath (Join-Path $ScanRoot 'MilestoneEngine.csproj'))) { $underEngineDir = $true }
    foreach ($seg in $parts) { if ($strongEngineDirs -contains $seg) { $underEngineDir = $true; break } }
    if (-not $underEngineDir) {
        for ($i = 0; $i -lt $parts.Count; $i++) {
            if ($parts[$i] -ieq 'engine') {
                $context = ($parts[0..$i] -join '\').ToLowerInvariant()
                foreach ($tok in $engineDirContext) { if ($context.Contains($tok.ToLowerInvariant())) { $underEngineDir = $true; break } }
                if ($underEngineDir) { break }
            }
        }
    }
    $text = ''
    $marker = ''
    $markerSet = @()
    if ($nativeExt -contains $ext -or $ext -eq '.cs' -or $ext -eq '.py' -or $projectExt -contains $ext) {
        $text = Read-HeadText $File.FullName
    }
    if ($nativeExt -contains $ext) { $markerSet = $engineNativeMarkers }
    elseif ($ext -eq '.cs') { $markerSet = $engineManagedMarkers }
    elseif ($projectExt -contains $ext) { $markerSet = $engineProjectMarkers }
    foreach ($m in $markerSet) {
        if ($text.IndexOf($m, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { $marker = $m; break }
    }
    if ($nativeExt -contains $ext) {
        if ($underEngineDir) { $Reason.Value = 'native source/header under engine dir'; return $true }
        if ($marker) { $Reason.Value = "native source marker: $marker"; return $true }
        return $false
    }
    if ($projectExt -contains $ext) {
        if ($underEngineDir -or $marker -or (Test-ProjectReferencesInRepoEngine $text)) { $Reason.Value = 'engine project/build file or in-repo engine-source reference'; return $true }
        return $false
    }
    if ($ext -eq '.cs') {
        if ($underEngineDir -or $marker) { $Reason.Value = 'engine C# source'; return $true }
        return $false
    }
    if ($ext -eq '.py') {
        if ($underEngineDir -or $rel -match '(?i)(hybridengine|rhygemaker)') { $Reason.Value = 'engine Python source/binding'; return $true }
        if ($marker) { $Reason.Value = "Python binding marker: $marker"; return $true }
        return $false
    }
    return $false
}function Get-StrongEngineRoot([string]$FullPath, [string]$ScanRoot) {
    $rel = Get-RelPath $ScanRoot $FullPath
    $parts = $rel -split '[\\/]' | Where-Object { $_ -ne '' }
    $current = $ScanRoot
    for ($i = 0; $i -lt $parts.Count; $i++) {
        $current = Join-Path $current $parts[$i]
        if (-not (Test-Path -LiteralPath $current -PathType Container)) { break }
        $seg = $parts[$i]
        if ($strongEngineDirs -contains $seg) { return $current }
        if ($seg -ieq 'engine') {
            $parent = Split-Path -Parent $current
            $hasSiblingProject = Test-Path -LiteralPath (Join-Path $parent 'MilestoneEngine.csproj')
            $context = $rel.ToLowerInvariant()
            $hasContext = $hasSiblingProject
            if (-not $hasContext) { foreach ($tok in $engineDirContext) { if ($context.Contains($tok.ToLowerInvariant())) { $hasContext = $true; break } } }
            if ($hasContext) { return $current }
        }
    }
    return $null
}
$hits = New-Object System.Collections.Generic.List[object]
foreach ($root in $scanRoots) {
    Write-Host "[scan] $root"
    Get-ChildItem -LiteralPath $root -Recurse -Force -File -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.FullName -match '[\\/]\.git[\\/]') { return }
        $reason = ''
        if (Test-EngineSourceFile -File $_ -ScanRoot $root -Reason ([ref]$reason)) {
            $hits.Add([pscustomobject]@{
                Path = $_.FullName
                Relative = (Get-RelPath $root $_.FullName)
                ScanRoot = $root
                Size = $_.Length
                Reason = $reason
                StrongRoot = (Get-StrongEngineRoot -FullPath $_.FullName -ScanRoot $root)
            }) | Out-Null
        }
    }
}
if ($IncludeHistory) {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($git) {
        Push-Location $Root
        try {
            $objects = & git -c core.quotepath=false rev-list --objects --all 2>$null
            foreach ($line in $objects) {
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                $sp = $line.IndexOf(' ')
                if ($sp -lt 0) { continue }
                $hash = $line.Substring(0, $sp)
                $rel = $line.Substring($sp + 1)
                if ([string]::IsNullOrWhiteSpace($rel)) { continue }
                $ext = [System.IO.Path]::GetExtension($rel).ToLowerInvariant()
                if ($binaryOrDataExt -contains $ext) { continue }
                $parts = $rel -split '[\\/]'
                $enginePath = $false
                foreach ($seg in $parts) { if ($strongEngineDirs -contains $seg) { $enginePath = $true; break } }
                $engineProjectName = ($rel -match '(?i)(MilestoneEngine|HybridEngine\.Bind|RhygeMaker\.Bind|CMakeLists\.txt)')
                $sourcePath = ($nativeExt -contains $ext) -or (($projectExt -contains $ext) -and ($enginePath -or $engineProjectName))
                if ($enginePath -or $sourcePath) {
                    $hits.Add([pscustomobject]@{
                        Path = "[history] $hash $rel"
                        Relative = $rel
                        ScanRoot = $Root
                        Size = 0
                        Reason = 'engine source/header path in Git history'
                        StrongRoot = $null
                    }) | Out-Null
                }
            }
        } finally { Pop-Location }
    } else { Write-Warning 'git not found; history scan skipped' }
}
$seen = @{}
$unique = New-Object System.Collections.Generic.List[object]
foreach ($h in $hits) { if (-not $seen.ContainsKey($h.Path)) { $seen[$h.Path] = $true; $unique.Add($h) | Out-Null } }
Write-Host "[result] engine-source leak candidates: $($unique.Count)"
foreach ($h in $unique) { Write-Host ('  - ' + $h.Relative + '  <- ' + $h.Reason) }
if ($Quarantine -and $unique.Count -gt 0) {
    foreach ($r in $scanRoots) { if (Test-IsUnder -Child $QuarantineRoot -Parent $r) { throw "quarantine root must be outside scan root: $QuarantineRoot" } }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $moveRoots = @{}
    foreach ($h in $unique) {
        if ($h.Path -like '[history]*') { continue }
        $rootToMove = $h.StrongRoot
        if ([string]::IsNullOrWhiteSpace($rootToMove)) { $rootToMove = $h.Path }
        if (-not $moveRoots.ContainsKey($rootToMove)) { $moveRoots[$rootToMove] = $h.ScanRoot }
    }
    $moved = 0
    foreach ($item in $moveRoots.GetEnumerator()) {
        $rootToMove = $item.Key
        $scanRoot = $item.Value
        if (-not (Test-Path -LiteralPath $rootToMove)) { continue }
        $rel = Get-RelPath $scanRoot $rootToMove
        $dest = Join-Path (Join-Path $QuarantineRoot ($stamp + '\' + (Split-Path -Leaf $scanRoot))) $rel
        $destDir = Split-Path -Parent $dest
        if ($destDir) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
        Move-Item -LiteralPath $rootToMove -Destination $dest -Force
        Write-Host "[quarantine] $rootToMove -> $dest"
        $moved++
    }
    Write-Host "[quarantine] moved $moved root(s)/file(s) to $QuarantineRoot"
}
if ($FailOnFind -and $unique.Count -gt 0) { exit 1 }
exit 0