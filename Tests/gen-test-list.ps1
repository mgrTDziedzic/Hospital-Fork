<#
.SYNOPSIS
  Generates Tests/TESTS.md (overview of the in-game tests) from the [HospitalTest] attributes
  and the "// Given: / When: / Then:" comments above each test method.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tests\gen-test-list.ps1
#>
$ErrorActionPreference = 'Stop'
$testsDir = Join-Path $PSScriptRoot 'HospitalTests\Source\Tests'
$outFile = Join-Path $PSScriptRoot 'TESTS.md'

function Esc([string]$s) {
    if ($null -eq $s) { return '' }
    return ($s -replace '\|', '\|')
}

function Get-Part([string]$text, [string]$name) {
    $m = [regex]::Match($text, '(?s)(?<![A-Za-z])' + $name + ':\s*(.*?)\s*(?=(?<![A-Za-z])(?:Given|When|Then):|$)', 'None')
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    return ''
}

$sections = New-Object System.Collections.Generic.List[string]
$total = 0
$warnings = 0

foreach ($file in (Get-ChildItem -Path $testsDir -Filter '*.cs' | Sort-Object Name)) {
    $lines = [System.IO.File]::ReadAllLines($file.FullName, [System.Text.Encoding]::UTF8)
    # const aliases of SupportedMods keys, e.g. "private const string MdId = ...SupportedMods.MultiDoctor;"
    $alias = @{}
    foreach ($l in $lines) {
        $am = [regex]::Match($l, 'const\s+string\s+(\w+)\s*=\s*[\w\.:]*SupportedMods\.(\w+)\s*;')
        if ($am.Success) { $alias[$am.Groups[1].Value] = $am.Groups[2].Value }
    }
    $rows = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch '^\s*\[HospitalTest\(') { continue }

        # attribute text (may span lines)
        $attr = $lines[$i]
        $j = $i
        while ($attr -notmatch '\)\]\s*$' -and $j + 1 -lt $lines.Count) { $j++; $attr += ' ' + $lines[$j].Trim() }

        $desc = ''
        $bs = [string][char]92
        $dm = [regex]::Match($attr, '\(\s*"((?:[^"' + $bs + $bs + ']|' + $bs + $bs + '.)*)"')
        if ($dm.Success) { $desc = $dm.Groups[1].Value.Replace($bs + '"', '"').Replace($bs + $bs, $bs) }
        $reqs = @()
        foreach ($rm in [regex]::Matches($attr.Substring($dm.Index + $dm.Length), '(Requires\w+)\s*=\s*([^,\)]+)')) {
            $val = $rm.Groups[2].Value.Trim().Trim('"') -replace '^(\w+\.)*SupportedMods\.', ''
            if ($alias.ContainsKey($val)) { $val = $alias[$val] }
            $reqs += ($rm.Groups[1].Value -replace '^Requires', '').ToLower() + ' ' + $val
        }

        # method name
        $method = ''
        for ($k = $j + 1; $k -lt [Math]::Min($j + 6, $lines.Count); $k++) {
            $mm = [regex]::Match($lines[$k], '\bstatic\s+\S+\s+(\w+)\s*\(')
            if ($mm.Success) { $method = $mm.Groups[1].Value; break }
        }

        # contiguous // comment block directly above the attribute
        $c = New-Object System.Collections.Generic.List[string]
        for ($k = $i - 1; $k -ge 0 -and $lines[$k] -match '^\s*//'; $k--) {
            $c.Insert(0, ($lines[$k] -replace '^\s*//+\s*', '').Trim())
        }
        $text = ($c -join ' ')
        $given = Get-Part $text 'Given'
        $when = Get-Part $text 'When'
        $then = Get-Part $text 'Then'
        if ($given -eq '' -and $when -eq '' -and $then -eq '') {
            Write-Host ("WARNING: no Given/When/Then comment: {0} {1}" -f $file.Name, $method)
            $warnings++
        } elseif ($then -eq '' -or ($given -eq '' -and $when -eq '')) {
            Write-Host ("WARNING: incomplete Given/When/Then comment: {0} {1}" -f $file.Name, $method)
            $warnings++
        }

        $id = $desc
        $ci = $desc.IndexOf(':')
        if ($ci -gt 0) { $id = $desc.Substring(0, $ci) }
        $descRest = $desc
        if ($ci -gt 0) { $descRest = $desc.Substring($ci + 1).Trim() }

        $rows.Add(('| {0} | `{1}` | {2} | {3} | {4} | {5} | {6} |' -f (Esc $id), $method, (Esc $descRest), (Esc $given), (Esc $when), (Esc $then), (Esc ($reqs -join ', '))))
        $total++
    }
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('## ' + $file.Name + "`n`n")
    [void]$sb.Append("| ID | Method | Description | Given | When | Then | Requires |`n")
    [void]$sb.Append("|---|---|---|---|---|---|---|`n")
    foreach ($r in $rows) { [void]$sb.Append($r + "`n") }
    $sections.Add($sb.ToString())
}

$out = New-Object System.Text.StringBuilder
[void]$out.Append("# Hospital in-game tests`n`n")
[void]$out.Append("Generated file, do not edit by hand. Regenerate with:`n`n")
[void]$out.Append('`powershell -ExecutionPolicy Bypass -File Tests\gen-test-list.ps1`' + "`n`n")
[void]$out.Append("Source: ``[HospitalTest]`` attributes and the ``// Given: / When: / Then:`` comments above each test method in ``Tests/HospitalTests/Source/Tests/``.`n`n")
[void]$out.Append("Total tests: $total`n`n")
[void]$out.Append(($sections -join "`n"))

[System.IO.File]::WriteAllText($outFile, $out.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Wrote $outFile ($total tests, $warnings warnings)"
