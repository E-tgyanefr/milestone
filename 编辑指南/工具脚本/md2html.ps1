# md2html.ps1 - Markdown(zi ji) to HTML, for Edge headless PDF
param([string]$MdPath, [string]$HtmlPath)
$content = [System.IO.File]::ReadAllText($MdPath, [System.Text.Encoding]::UTF8)
$lines = $content -split "`r?`n"
$sb = New-Object System.Text.StringBuilder
$style = "<html><head><meta charset='utf-8'><style>body{font-family:'Microsoft YaHei UI','Segoe UI',sans-serif;font-size:11pt;line-height:1.65;color:#1a1a1a;margin:2.2cm 2cm;}h1{font-size:22pt;border-bottom:2px solid #2C6CFF;padding-bottom:6px;margin-top:26px;}h2{font-size:16pt;color:#2C6CFF;margin-top:20px;border-left:4px solid #2C6CFF;padding-left:8px;}h3{font-size:13pt;margin-top:16px;}h4{font-size:12pt;margin-top:12px;}code{background:#f2f4f8;padding:1px 4px;border-radius:3px;font-family:Consolas,monospace;font-size:10pt;}pre{background:#f6f8fb;border:1px solid #dde3ee;border-radius:6px;padding:10px;overflow:auto;font-family:Consolas,monospace;font-size:9.5pt;}table{border-collapse:collapse;width:100%;margin:10px 0;}th{background:#e9eefb;border:1px solid #c6d2ea;padding:5px 8px;text-align:left;font-size:10pt;}td{border:1px solid #d8dfee;padding:4px 8px;font-size:9.5pt;vertical-align:top;}li{margin:2px 0;}blockquote{border-left:3px solid #2C6CFF;margin:8px 0;padding:2px 12px;background:#f6f8fb;color:#445;}</style></head><body>"
[void]$sb.Append($style)
$inCode = $false; $inTable = $false
$codeBuf = New-Object System.Text.StringBuilder
function Esc([string]$s){ return ($s -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;') }
function Inline([string]$s){
  $s = [System.Net.WebUtility]::HtmlEncode($s)
  $s = [regex]::Replace($s, '(\*\*)([^*]+)(\*\*)', '<b>$2</b>')
  $s = [regex]::Replace($s, '(`)([^`]+)(`)', '<code>$2</code>')
  return $s
}
foreach ($ln in $lines) {
  if ($inCode -and $ln -match '^\s*```') { [void]$sb.Append("<pre>" + $codeBuf.ToString() + "</pre>"); $inCode = $false; $codeBuf.Clear(); continue }
  if ($ln -match '^\s*```') { $inCode = $true; continue }
  if ($inCode) { [void]$codeBuf.Append((Esc $ln)); [void]$codeBuf.Append("`n"); continue }
  if ($ln.StartsWith('|')) {
    if (-not $inTable) { [void]$sb.Append('<table>'); $inTable = $true }
    $cells = $ln.Trim().Trim('|') -split '\|'
    $isSep = ($cells | Where-Object { $_ -match '^\s*-+\s*$' }).Count -gt 0
    if ($isSep) { continue }
    $rowHtml = ($cells | ForEach-Object { '<td>' + (Inline $_.Trim()) + '</td>' }) -join ''
    [void]$sb.Append('<tr>' + $rowHtml + '</tr>')
    continue
  } else {
    if ($inTable) { [void]$sb.Append('</table>'); $inTable = $false }
  }
  $t = $ln
  if ($t -match '^# ') { [void]$sb.Append('<h1>' + (Inline ($t.Substring(2))) + '</h1>') }
  elseif ($t -match '^## ') { [void]$sb.Append('<h2>' + (Inline ($t.Substring(3))) + '</h2>') }
  elseif ($t -match '^### ') { [void]$sb.Append('<h3>' + (Inline ($t.Substring(4))) + '</h3>') }
  elseif ($t -match '^#### ') { [void]$sb.Append('<h4>' + (Inline ($t.Substring(5))) + '</h4>') }
  elseif ($t -match '^\s*- ') { [void]$sb.Append('<li>' + (Inline ($t -replace '^\s*- ','')) + '</li>') }
  elseif ($t -match '^> ') { [void]$sb.Append('<blockquote>' + (Inline ($t.Substring(2))) + '</blockquote>') }
  elseif ($t.Trim() -eq '') { [void]$sb.Append('<p>&nbsp;</p>') }
  else { [void]$sb.Append('<p>' + (Inline $t) + '</p>') }
}
if ($inCode) { [void]$sb.Append('<pre>' + $codeBuf.ToString() + '</pre>') }
if ($inTable) { [void]$sb.Append('</table>') }
[void]$sb.Append('</body></html>')
[System.IO.File]::WriteAllText($HtmlPath, $sb.ToString(), [System.Text.Encoding]::UTF8)
Write-Output ('OK ' + $HtmlPath)