# Ultra-minify all .cs files
$root = "D:\Users\etgya\Desktop\milestone"
$origTotal = 0; $newTotal = 0; $cnt = 0

Get-ChildItem -Recurse -File *.cs -Path $root | Where-Object { $_.FullName -notmatch '\\obj\\' } | ForEach-Object {
    $path = $_.FullName
    $orig = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $origTotal += $orig.Length; $cnt++

    $code = $orig
    # Remove XML doc comments
    $code = $code -replace '(?m)^\s*///.*$', ''
    # Remove multi-line comments
    $code = $code -replace '(?s)/\*.*?\*/', ''
    # Remove single-line comments (simplified - remove lines starting with //)
    $code = $code -replace '(?m)^[ \t]*//[^\n]*\n', ''
    $code = $code -replace '(?m)[ \t]*//[^\n]*$', ''
    # Remove blank lines
    $code = $code -replace '[ \t]+\n', ''
    $code = $code -replace '\n{2,}', "`n"
    # Trim each line
    $code = $code -replace '(?m)[ \t]+$',''
    # Remove all newlines - one line per file
    $code = $code -replace '\r?\n',' '
    # Squeeze spaces
    while ($code -match '  +') { $code = $code -replace '  +', ' ' }
    # Trim
    $code = $code.Trim()
    if ($code.Length -gt 0) {
        [System.IO.File]::WriteAllText($path, $code, (New-Object System.Text.UTF8Encoding $false))
        $newTotal += $code.Length
    }
}

$saved = $origTotal - $newTotal
Write-Host "Files: $cnt"
Write-Host "Original: $([math]::Round($origTotal/1MB,3)) MB"
Write-Host "Minified: $([math]::Round($newTotal/1MB,3)) MB"
Write-Host "Saved: $([math]::Round($saved/1KB,1)) KB ($([math]::Round($saved/$origTotal*100,1))%)"
