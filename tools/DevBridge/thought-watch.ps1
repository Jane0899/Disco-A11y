# Read-only capture of the thought-cabinet signals, for J11.
#
# Why this exists: a thought that finished cooking but was never confirmed blocks EVERY
# interaction in the game, and the state disappears the moment the player presses the
# cabinet key and confirms it. That is a few seconds to catch the one measurement that
# says WHICH signal marks the state - so it gets sampled continuously instead.
#
# Read-only on purpose: it sends nothing but `thought list`, needs no window focus, and
# never touches the save, so it can run while the game is being played normally.
#
#   .\thought-watch.ps1 -GamePath "D:\SteamLibrary\steamapps\common\Disco Elysium"
#
# Output goes to a timestamped log next to this script unless -Out says otherwise.
param(
    # No default that guesses: the Steam default path does not exist on every machine
    # (it does not on the one this was written for), and a silently wrong path here just
    # looks like "the game is not running".
    [Parameter(Mandatory = $true)][string]$GamePath,
    [string]$Out,
    # Safety cap, so a forgotten run cannot linger for days.
    [double]$MaxHours = 5,
    # How long to keep sampling after the last thought stops cooking - the interesting
    # part is what the signals do while the block is live, and when it is confirmed.
    [double]$TailMinutes = 15,
    [int]$IntervalSeconds = 10
)

$client = Join-Path $PSScriptRoot "bridge-client.ps1"
if (-not $Out) { $Out = Join-Path $PSScriptRoot ("thought-watch-{0}.log" -f (Get-Date -Format "yyyy-MM-dd_HH-mm-ss")) }

$deadline   = (Get-Date).AddHours($MaxHours)
$finishedAt = $null
$missCount  = 0

Add-Content $Out "=== Start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - every $IntervalSeconds s - game: $GamePath"

while ((Get-Date) -lt $deadline) {
    $ts = Get-Date -Format "HH:mm:ss"
    $r  = $null
    try { $r = & $client -GamePath $GamePath thought list 2>$null } catch { $r = $null }

    if ($null -eq $r -or $r.Count -eq 0) {
        # Game closed, or the bridge is gone. Tolerate a few misses - a loading screen can
        # swallow one - then stop rather than spin for hours against nothing.
        $missCount++
        Add-Content $Out "$ts  (no answer $missCount)"
        if ($missCount -ge 12) { Add-Content $Out "$ts  === stopping: bridge unreachable"; break }
    } else {
        $missCount = 0
        # Only the informative lines: ~48 UNKNOWN thoughts every interval would bury the
        # single transition this exists to catch.
        foreach ($line in ($r | Where-Object { $_ -notmatch '^UNKNOWN' })) { Add-Content $Out "$ts  $line" }

        if ((($r -join ' ') -notmatch 'COOKING') -and ($null -eq $finishedAt)) {
            $finishedAt = Get-Date
            Add-Content $Out "$ts  *** nothing is COOKING any more - a thought just finished ***"
        }
    }

    if (($null -ne $finishedAt) -and (((Get-Date) - $finishedAt).TotalMinutes -ge $TailMinutes)) {
        Add-Content $Out "$ts  === stopping: $TailMinutes minutes after completion"
        break
    }
    Start-Sleep -Seconds $IntervalSeconds
}
Add-Content $Out "=== End $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Output "Log: $Out"
