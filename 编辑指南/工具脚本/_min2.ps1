$root = "D:\Users\etgya\Desktop\milestone"
$origTotal = 0; $newTotal = 0; $cnt = 0
$reps = @(
    @{k='ChartPlayer';v='P'},
    @{k='Chart';v='C'},@{k='ChartEditorPanel';v='CP'},
    @{k='Note';v='N'},@{k='Event';v='E'},@{k='ChartEvent';v='E'},
    @{k='Settings';v='S'},@{k='JudgeSettings';v='JS'},
    @{k='JudgeEngine';v='JE'},@{k='GameEngine';v='GE'},
    @{k='Panel';v='L'},@{k='GamePanel';v='GL'},@{k='EditorPanel';v='EL'},
    @{k='Manager';v='M'},@{k='MpManager';v='MM'},
    @{k='Canvas';v='V'},@{k='EditorCanvas';v='EV'},
    @{k='Player';v='P'},@{k='MpPlayerState';v='PS'},
    @{k='Game';v='G'},@{k='AudioPlayer';v='AP'},
    @{k='Timer';v='T'},@{k='Index';v='I'},@{k='EngineTime';v='ET'},
    @{k='Result';v='R'},@{k='Builder';v='B'},@{k='StringBuilder';v='SB'},
    @{k='Options';v='O'},@{k='JsonSerializerOptions';v='JO'},
    @{k='Context';v='X'},@{k='Config';v='CG'},
    @{k='Service';v='SV'},@{k='Collection';v='CL'},
    @{k='List';v='LT'},@{k='ArrayList';v='AL'},
    @{k='Array';v='A'},@{k='Dictionary';v='DK'},
    @{k='Parameters';v='PR'},@{k='Parameter';v='P'},
    @{k='Argument';v='AG'},@{k='Type';v='TP'},
    @{k='Value';v='V'},@{k='Values';v='VS'},
    @{k='Name';v='NM'},@{k='Key';v='K'},
    @{k='Data';v='D'},@{k='Error';v='ER'},
    @{k='Message';v='MG'},@{k='Path';v='PA'},
    @{k='Directory';v='DI'},@{k='File';v='FL'},
    @{k='Reader';v='R'},@{k='Writer';v='W'},
    @{k='Stream';v='SM'},@{k='Item';v='IT'},
    @{k='Items';v='ITS'},@{k='Element';v='EL'},
    @{k='Default';v='DF'},@{k='Current';v='CU'},
    @{k='Previous';v='PV'},@{k='Success';v='SC'},
    @{k='Failure';v='F'},@{k='EventArgs';v='EA'},
    @{k='EventHandler';v='EH'},@{k='Application';v='APP'},
    @{k='Form';v='FM'},@{k='Control';v='CTRL'},
    @{k='Graphics';v='GR'},@{k='Color';v='COL'},
    @{k='Font';v='FT'},@{k='Size';v='SZ'},
    @{k='Point';v='PT'},@{k='Rectangle';v='RC'},
    @{k='Brush';v='BR'},@{k='Pen';v='PN'},
    @{k='Image';v='IMG'},@{k='Object';v='OB'},
    @{k='Exception';v='EX'},@{k='Task';v='TS'},
    @{k='Thread';v='TR'},@{k='Mutex';v='MT'},
    @{k='Semaphore';v='SP'},@{k='Lock';v='LK'},
    @{k='Delegate';v='DL'},@{k='Action';v='AC'},
    @{k='Func';v='FC'},@{k='Predicate';v='PD'},
    @{k='Comparer';v='CM'},@{k='KeyValuePair';v='KP'},
    @{k='Tuple';v='TU'},@{k='TupleElementNames';v='TEN'},
    @{k='DescriptionAttribute';v='DESC'},@{k='Description';v='DESC'},
    @{k='ObsoleteAttribute';v='OBS'},@{k='Obsolete';v='OBS'},
    @{k='SerializableAttribute';v='SER'},@{k='Serializable';v='SER'},
    @{k='FlagsAttribute';v='FG'},@{k='Flags';v='FG'},
    @{k='DllImportAttribute';v='DLL'},@{k='DllImport';v='DLL'},
    @{k='STAThreadAttribute';v='STA'},@{k='STAThread';v='STA'}
)
Get-ChildItem -Recurse -File *.cs -Path $root | Where-Object { $_.FullName -notmatch '\\obj\\' } | ForEach-Object {
    $path = $_.FullName
    $orig = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $origTotal += $orig.Length; $cnt++
    $code = $orig
    foreach ($r in $reps) {
        $code = $code -replace '(?<![.\w])'+[regex]::Escape($r.k)+'(?!\w)', $r.v
    }
    $code = $code -replace '\r?\n', ''
    $code = $code -replace '\t', ''
    while ($code -match '  +') { $code = $code -replace '  +', ' ' }
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
