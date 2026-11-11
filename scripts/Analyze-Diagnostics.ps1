<#
.SYNOPSIS
    診断ログ（JSON Lines）を読み、不具合を調べるための要約を表示します。

.DESCRIPTION
    入力は、不具合レポートの zip、diagnostics フォルダー、または .jsonl ファイルです。
    外部ツールは使わず、Windows PowerShell 5.1 と PowerShell 7 の両方で動きます。
    ログの形と分類は docs/LOGGING_STRATEGY.md の 3 と 7 を参照してください。

    表示するもの：
      1. 環境（各実行の app.start の値）
      2. 実行ごとの件数・Error / Fatal の数・終了の有無（app.exit がなければ異常終了の疑い）
      3. Error / Fatal の一覧（evt ごとの件数）
      -Report 7F3A-1842 を付けると、その行と同じ実行の直前 -Before 行（既定 200）
      -Event obd.connect.failed を付けると、その evt の行（* を使うと前方一致の代わりにワイルドカードで絞る）

    壊れた行（途中で切れた JSON など）は飛ばし、件数を表示します。
    同じ実行 ID と seq の行が 2 回出ても（リングバッファとの重なり）1 件として数えます。

.PARAMETER Path
    不具合レポートの zip、diagnostics フォルダー、または .jsonl ファイル。

.PARAMETER Report
    報告用の番号（実行 ID の先頭 4 桁 + "-" + seq。例：7F3A-1842）。

.PARAMETER Event
    表示を絞る evt（例：obd.connect.failed）。* を含むときはワイルドカードとして扱う。

.PARAMETER Before
    -Report のとき、直前に表示する行数（既定 200）。

.EXAMPLE
    .\scripts\Analyze-Diagnostics.ps1 -Path .\bug-report.zip

.EXAMPLE
    .\scripts\Analyze-Diagnostics.ps1 -Path .\bug-report.zip -Report 7F3A-1842

.EXAMPLE
    .\scripts\Analyze-Diagnostics.ps1 -Path .\diagnostics -Event 'obd.connect.*'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Path,

    [string]$Report,

    [string]$Event,

    [ValidateRange(1, 100000)]
    [int]$Before = 200
)

$ErrorActionPreference = 'Stop'
$work = $null

function Get-RunCode {
    param([string]$Run)
    return $Run.Substring(0, [Math]::Min(4, $Run.Length)).ToUpperInvariant()
}

try {
    # --- 入力のファイルを集める ---
    $item = Get-Item -LiteralPath $Path
    if ($item.PSIsContainer) {
        $jsonlFiles = @(Get-ChildItem -LiteralPath $item.FullName -Filter '*.jsonl' -Recurse -File)
    }
    elseif ($item.Extension -eq '.zip') {
        $work = Join-Path ([IO.Path]::GetTempPath()) ('analyze-diag-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $work | Out-Null
        Expand-Archive -LiteralPath $item.FullName -DestinationPath $work -Force
        $jsonlFiles = @(Get-ChildItem -LiteralPath $work -Filter '*.jsonl' -Recurse -File)
    }
    elseif ($item.Extension -eq '.jsonl') {
        $jsonlFiles = @($item)
    }
    else {
        throw "zip、.jsonl、または diagnostics フォルダーを指定してください：$Path"
    }
    if ($jsonlFiles.Count -eq 0) { throw '.jsonl が見つかりません。' }

    # --- 読む（壊れた行は飛ばして数える。同じ実行 ID と seq は 1 件にする） ---
    $records = New-Object 'System.Collections.Generic.List[object]'
    $seen = @{}
    $broken = 0
    $duplicates = 0
    $blank = 0

    foreach ($file in ($jsonlFiles | Sort-Object FullName)) {
        # FileShare.ReadWrite：アプリがログを書いている最中でも読めるようにする。
        $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $reader = New-Object IO.StreamReader($stream, [Text.Encoding]::UTF8)
        try {
            while ($true) {
                $line = $reader.ReadLine()
                if ($null -eq $line) { break }
                if ([string]::IsNullOrWhiteSpace($line)) { $blank++; continue }

                $o = $null
                try { $o = ConvertFrom-Json -InputObject $line } catch { $o = $null }
                if ($null -eq $o -or $null -eq $o.run -or $null -eq $o.seq -or $null -eq $o.evt) {
                    $broken++
                    continue
                }

                $key = '{0}|{1}' -f $o.run, $o.seq
                if ($seen.ContainsKey($key)) { $duplicates++; continue }
                $seen[$key] = $true

                $props = ''
                if ($null -ne $o.props) { $props = ConvertTo-Json -InputObject $o.props -Compress -Depth 6 }
                $exType = ''
                if ($null -ne $o.ex -and $null -ne $o.ex.type) { $exType = [string]$o.ex.type }

                $records.Add([pscustomobject]@{
                    Run   = [string]$o.run
                    Seq   = [long]$o.seq
                    Ts    = [string]$o.ts
                    Lvl   = [string]$o.lvl
                    Evt   = [string]$o.evt
                    Msg   = [string]$o.msg
                    Ring  = ($o.ring -eq $true)
                    Props = $props
                    Ex    = $exType
                })
            }
        }
        finally {
            $reader.Dispose()
            $stream.Dispose()
        }
    }

    if ($records.Count -eq 0) { throw '読める行がありませんでした。' }

    # --- 実行ごとにまとめる ---
    $runFirst = @{}
    foreach ($r in $records) {
        if (-not $runFirst.ContainsKey($r.Run) -or [string]::CompareOrdinal($r.Ts, $runFirst[$r.Run]) -lt 0) {
            $runFirst[$r.Run] = $r.Ts
        }
    }
    $runOrder = @($runFirst.Keys | Sort-Object { $runFirst[$_] })
    $byRun = $records | Group-Object -Property Run -AsHashTable -AsString

    Write-Host ''
    Write-Host "入力：$($item.Name)（.jsonl $($jsonlFiles.Count) 個）"
    Write-Host "読めた行 $($records.Count) 件、壊れた行 $broken 件（飛ばした）、重複 $duplicates 件（除いた）、空行 $blank 件"

    # 1. 環境
    Write-Host ''
    Write-Host '== 環境（各実行の app.start）=='
    foreach ($run in $runOrder) {
        $start = @($byRun[$run] | Where-Object { $_.Evt -eq 'app.start' } | Sort-Object Seq | Select-Object -First 1)
        if ($start.Count -gt 0) {
            Write-Host ('  {0}  {1}  {2}' -f (Get-RunCode $run), $start[0].Ts, $start[0].Props)
        }
        else {
            Write-Host ('  {0}  app.start なし（ログの先頭が切れている）' -f (Get-RunCode $run))
        }
    }

    # 2. 実行ごとの件数
    Write-Host ''
    Write-Host '== 実行ごと =='
    $runRows = foreach ($run in $runOrder) {
        $g = @($byRun[$run])
        $errCount = @($g | Where-Object { $_.Lvl -eq 'error' -or $_.Lvl -eq 'fatal' }).Count
        $ringCount = @($g | Where-Object { $_.Ring }).Count
        $hasExit = @($g | Where-Object { $_.Evt -eq 'app.exit' }).Count -gt 0
        $exitText = '異常終了の疑い（app.exit なし）'
        if ($hasExit) { $exitText = '正常' }
        [pscustomobject]@{
            実行       = Get-RunCode $run
            開始       = $runFirst[$run]
            件数       = $g.Count
            エラー     = $errCount
            リング     = $ringCount
            終了       = $exitText
        }
    }
    $runRows | Format-Table -AutoSize | Out-Host
    Write-Host '（app.exit がないのは、異常終了か、ログを書き出した時点でアプリが起動中だった場合です）'

    # 3. Error / Fatal の一覧
    Write-Host ''
    Write-Host '== Error / Fatal（evt ごと、件数の多い順）=='
    $errRecords = @($records | Where-Object { $_.Lvl -eq 'error' -or $_.Lvl -eq 'fatal' })
    if ($errRecords.Count -eq 0) {
        Write-Host '  なし'
    }
    else {
        $errRows = $errRecords | Group-Object -Property Evt | ForEach-Object {
            $sorted = @($_.Group | Sort-Object Ts)
            $level = 'error'
            if (@($_.Group | Where-Object { $_.Lvl -eq 'fatal' }).Count -gt 0) { $level = 'fatal' }
            [pscustomobject]@{
                evt    = $_.Name
                レベル = $level
                件数   = $_.Count
                実行数 = @($_.Group | Select-Object -ExpandProperty Run -Unique).Count
                最初   = $sorted[0].Ts
                最後   = $sorted[$sorted.Count - 1].Ts
                例     = $sorted[0].Msg
            }
        }
        $errRows | Sort-Object 件数 -Descending | Format-Table -AutoSize -Wrap | Out-Host
    }

    # 4. 報告番号の前後
    if ($Report) {
        $m = [regex]::Match($Report, '^([0-9A-Fa-f]{4})-(\d+)$')
        if (-not $m.Success) { throw "報告の番号は 7F3A-1842 の形で指定してください：$Report" }
        $prefix = $m.Groups[1].Value.ToLowerInvariant()
        $target = [long]$m.Groups[2].Value

        $candidates = @($runOrder | Where-Object { $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
        if ($candidates.Count -ne 1) {
            throw "実行 ID の先頭 '$prefix' に当たる実行が $($candidates.Count) 件あります（1 件に決まりません）。"
        }
        $run = $candidates[0]
        $g = @($byRun[$run] | Sort-Object Seq)
        $hit = @($g | Where-Object { $_.Seq -eq $target })
        if ($hit.Count -eq 0) { throw "番号 $Report の行がありません（その行が壊れていた可能性があります）。" }

        # 同じ実行の、番号以前の行を順に並べ、後ろから -Before + 1 行（番号の行を含む）
        $ctx = @($g | Where-Object { $_.Seq -le $target } | Select-Object -Last ($Before + 1))
        Write-Host ''
        Write-Host "== 報告番号 $Report の行と直前の流れ（$($ctx.Count) 行）=="
        $ctx | ForEach-Object {
            $mark = ''
            if ($_.Seq -eq $target) { $mark = '>>' }
            [pscustomobject]@{
                ' '    = $mark
                番号   = '{0}-{1}' -f (Get-RunCode $_.Run), $_.Seq
                時刻   = $_.Ts
                レベル = $_.Lvl
                evt    = $_.Evt
                内容   = $_.Msg
                props  = $_.Props
            }
        } | Format-Table -AutoSize -Wrap | Out-Host
        if ($hit[0].Ex) { Write-Host "  例外：$($hit[0].Ex)" }
    }

    # 5. evt で絞り込み
    if ($Event) {
        $hits = @($records | Where-Object {
            if ($Event.Contains('*')) { $_.Evt -like $Event } else { $_.Evt -eq $Event }
        })
        Write-Host ''
        Write-Host "== evt：$Event（$($hits.Count) 件）=="
        if ($hits.Count -eq 0) {
            Write-Host '  該当なし'
        }
        else {
            $hits | Sort-Object @{ Expression = { $runFirst[$_.Run] } }, Seq | ForEach-Object {
                [pscustomobject]@{
                    時刻   = $_.Ts
                    番号   = '{0}-{1}' -f (Get-RunCode $_.Run), $_.Seq
                    レベル = $_.Lvl
                    evt    = $_.Evt
                    内容   = $_.Msg
                    props  = $_.Props
                }
            } | Format-Table -AutoSize -Wrap | Out-Host
        }
    }
}
finally {
    if ($work -and (Test-Path -LiteralPath $work)) {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
