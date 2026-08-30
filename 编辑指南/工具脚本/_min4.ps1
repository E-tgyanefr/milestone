# Pass 4: Remove unnecessary attributes, shorten remaining patterns
$root = "D:\Users\etgya\Desktop\milestone"
$origTotal = 0; $newTotal = 0; $cnt = 0

Get-ChildItem -Recurse -File *.cs -Path $root | Where-Object { $_.FullName -notmatch '\\obj\\' } | ForEach-Object {
    $path = $_.FullName
    $orig = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $origTotal += $orig.Length; $cnt++
    $code = $orig

    # Remove [Obsolete] attributes (harmless for compilation)
    $code = $code -replace '\[Obsolete\([^\]]*\)\]\s*', ''
    $code = $code -replace '\[Obsolete\]\s*', ''

    # Remove [Description] attributes (harmless for compilation)
    $code = $code -replace '\[Description\([^\]]*\)\]\s*', ''
    $code = $code -replace '\[Description\]\s*', ''

    # Remove [Flags] - not needed for most enums
    $code = $code -replace '\[Flags\]\s*', ''

    # Remove [Serializable]
    $code = $code -replace '\[Serializable\]\s*', ''

    # Remove [DefaultMember]
    $code = $code -replace '\[DefaultMember\([^\]]*\)\]\s*', ''

    # Remove [DebuggerStepThrough]
    $code = $code -replace '\[DebuggerStepThrough\]\s*', ''

    # Remove [DebuggerNonUserCode]
    $code = $code -replace '\[DebuggerNonUserCode\]\s*', ''

    # Remove [GeneratedCode]
    $code = $code -replace '\[GeneratedCode\([^\]]*\)\]\s*', ''

    # Remove [EditorRequired]
    $code = $code -replace '\[EditorRequired\]\s*', ''

    # Remove [DefaultValue]
    $code = $code -replace '\[DefaultValue\([^\]]*\)\]\s*', ''

    # Remove [Browsable]
    $code = $code -replace '\[Browsable\([^\]]*\)\]\s*', ''

    # Remove [Category]
    $code = $code -replace '\[Category\([^\]]*\)\]\s*', ''

    # Remove [DesignerSerializationVisibility]
    $code = $code -replace '\[DesignerSerializationVisibility\([^\]]*\)\]\s*', ''

    # Remove [SuppressUnmanagedCodeSecurity]
    $code = $code -replace '\[SuppressUnmanagedCodeSecurity\]\s*', ''

    # Remove [SecurityCritical]
    $code = $code -replace '\[SecurityCritical\]\s*', ''

    # Remove [SecuritySafeCritical]
    $code = $code -replace '\[SecuritySafeCritical\]\s*', ''

    # Remove [MethodImpl]
    $code = $code -replace '\[MethodImpl\([^\]]*\)\]\s*', ''

    # Remove [ThreadStatic]
    $code = $code -replace '\[ThreadStatic\]\s*', ''

    # Remove [field: NonSerialized]
    $code = $code -replace '\[field: NonSerialized\]\s*', ''

    # Remove [field: Nullable]
    $code = $code -replace '\[field: Nullable\]\s*', ''

    # Remove [param: ...] attributes
    $code = $code -replace '\[param:\s*[^\]]*\]\s*', ''

    # Remove [return: ...] attributes
    $code = $code -replace '\[return:\s*[^\]]*\]\s*', ''

    # Remove [field: ...] attributes (general)
    $code = $code -replace '\[field:\s*[^\]]*\]\s*', ''

    # Squeeze multiple spaces
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
