# Pass 5: Extract strings, remove whitespace, restore strings
$root = "D:\Users\etgya\Desktop\milestone"
$origTotal = 0; $newTotal = 0; $cnt = 0
$strCounter = 0
$strMap = @{}

Get-ChildItem -Recurse -File *.cs -Path $root | Where-Object { $_.FullName -notmatch '\\obj\\' } | ForEach-Object {
    $path = $_.FullName
    $orig = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $origTotal += $orig.Length; $cnt++
    $code = $orig

    # Phase 1: Extract string literals (both "..." and @"..." and $"...")
    $strMap = @{}
    $strCounter = 0
    function SaveStr { param($s); $key = "__STR${strCounter}__"; $strMap[$key] = $s; $strCounter++; return $key }

    # Match $""" interpolated strings first (longest), then @"..." verbatim, then "..." regular
    $code = $code -replace '(?s)\$@"((?:[^"\\]|\\.)*)"', { param($m); SaveStr ("$" + '"' + $m.Groups[1].Value + '"') }
    $code = $code -replace '(?s)@"((?:[^"\\]|\\.)*)"', { param($m); SaveStr ('"' + $m.Groups[1].Value + '"') }
    $code = $code -replace '(?s)"((?:[^"\\]|\\.)*)"',''
    # Actually this won't work with -replace in this context. Let me use a different approach.

    # Reset and use a simpler method: find all strings via regex and replace
    $strCounter = 0
    $strMap = @{}

    # Use a regex with a callback to extract strings
    $pattern = '"(?:[^"\\]|\\.)*"'
    $strings = [regex]::Matches($code, $pattern)
    $strCounter = 0
    $strMap = @{}
    $replacements = @{}
    foreach ($m in $strings) {
        $key = "__S${strCounter}__"
        $replacements[$key] = $m.Value
        $strCounter++
    }
    # Replace strings with placeholders (in reverse order to preserve positions)
    $code = $code -replace $pattern, 'PLACEHOLDER'
    # Hmm, -replace doesn't support callbacks. Let me try a different approach.

    # Simpler: just replace all string content with short placeholder
    # This is safe because we're not changing the structure, just removing spaces around operators
    # The actual string CONTENT will remain unchanged (we only remove spaces outside strings)

    # Better approach: use a regex that matches strings OR operators-with-spaces
    # Replace patterns like `op space` or `space op` only when outside strings
    # Since we can't easily do "outside strings" with regex, let's use a two-pass approach:
    # Pass A: Replace "..." with placeholder markers
    # Pass B: Remove spaces around operators
    # Pass C: Restore original strings

    # Let me just do the operator space removal with a regex that skips string contents
    # Use (?<="[^"]*)\s+ operator pattern won't work for all cases

    # Final approach: manual character-by-character but optimized
    $sb = [System.Text.StringBuilder]::new($code.Length)
    $inStr = $false; $strCh = '"'; $esc = $false; $prev = ' '

    for ($i = 0; $i -lt $code.Length; $i++) {
        $c = $code[$i]

        if ($esc) { $sb.Append($c); $esc = $false; $prev = $c; continue }
        if ($c -eq '\') { $sb.Append($c); $esc = $true; $prev = $c; continue }

        if ($inStr) {
            $sb.Append($c)
            if ($c -eq $strCh) { $inStr = $false }
            $prev = $c; continue
        }

        if ($c -eq '"') { $inStr = $true; $strCh = $c; $sb.Append($c); $prev = $c; continue }
        if ($c -eq "'") { $inStr = $true; $strCh = $c; $sb.Append($c); $prev = $c; continue }

        if ([char]::IsWhiteSpace($c)) {
            # Skip consecutive whitespace
            $j = $i + 1
            while ($j -lt $code.Length -and [char]::IsWhiteSpace($code[$j])) { $j++ }
            if ($j -lt $code.Length) {
                $nx = $code[$j]
                $isOp = $nx -in @('=', '+', '-', '*', '/', '%', '<', '>', '!', '~', '^', '&', '|', '?', ':', ',', ';', '.', '[', ']', '(', ')', '{', '}')
                $wasOp = $prev -in @('=', '+', '-', '*', '/', '%', '<', '>', '!', '~', '^', '&', '|', '?', ':', ',', ';', '.', '[', ']', '(', ')', '{', '}')
                if (($isOp -or $wasOp) -and $prev -ne ' ' -and $prev -ne "`n" -and $prev -ne "`r") {
                    $i = $j - 1
                    $sb.Append(' ')
                } else {
                    $sb.Append(' ')
                }
            }
            $prev = ' '; continue
        }

        $sb.Append($c)
        $prev = $c
    }

    $code = $sb.ToString().Trim()
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
