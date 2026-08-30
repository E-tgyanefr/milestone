# Aggressive whitespace removal pass on single-line .cs files
$root = "D:\Users\etgya\Desktop\milestone"
$origTotal = 0; $newTotal = 0; $cnt = 0

Get-ChildItem -Recurse -File *.cs -Path $root | Where-Object { $_.FullName -notmatch '\\obj\\' } | ForEach-Object {
    $path = $_.FullName
    $orig = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $origTotal += $orig.Length; $cnt++
    $code = $orig

    # Remove spaces after opening braces/parens/before closing
    $code = $code -replace '\{\s+', '{}'
    $code = $code -replace '\s+\}', '}'
    $code = $code -replace '\(\s+', '('
    $code = $code -replace '\s+\)', ')'

    # Remove spaces around operators (use word-boundary-safe patterns)
    $code = $code -replace '(?<=\S)\s+(?==|&|\||<|>|!|~|\^|\+|-|\*|/|%|\.)', ''
    # Handle => specifically
    $code = $code -replace '(\S)\s*=>\s*(\S)', '$1=>$2'
    # Handle := in lambda
    $code = $code -replace '(\S)\s*:\s*(\S)', '$1:$2'
    # Handle == != separately (don't touch)
    # Handle , and ;
    $code = $code -replace '(\S)\s*,\s*(\S)', '$1,$2'
    $code = $code -replace '(\S)\s*;\s*(\S)', '$1;$2'
    # Handle [ and ]
    $code = $code -replace '\[\s+', '['
    $code = $code -replace '\s+\]', ']'
    # Handle = (not == or !=)
    $code = $code -replace '([a-zA-Z0-9_])\s*=\s*([a-zA-Z0-9_(])', '$1=$2'

    # Squeeze remaining multiple spaces
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
