param(
    [ValidateSet(4, 8)][int]$Players = 4,
    [string]$Executable,
    [switch]$Capture
)
$ErrorActionPreference = 'Stop'
$raceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Executable) { $Executable = Join-Path $raceRoot 'Build\Windows\Skijoring.exe' }
if (!(Test-Path -LiteralPath $Executable)) { throw "Build the Windows Development player first: $Executable" }
$raceOutput = Join-Path $raceRoot ('Logs\NetworkSmoke-{0}-{1}' -f $Players, (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $raceOutput -Force | Out-Null
$raceRoom = Join-Path $raceOutput 'room.txt'
$raceProcesses = @()
try {
    for ($raceIndex = 0; $raceIndex -lt $Players; $raceIndex++) {
        $raceRole = if ($raceIndex -eq 0) { 'host' } else { 'client' }
        $raceName = if ($raceIndex -eq 0) { 'QA_Host' } else { "QA_Client$raceIndex" }
        $raceGear = if ($raceIndex % 2 -eq 0) { 'single' } else { 'double' }
        $raceReport = Join-Path $raceOutput "$raceName.json"
        $raceLog = Join-Path $raceOutput "$raceName.log"
        $raceArgs = @('-batchmode', '-raceSmoke', $raceRole, '-racePlayers', $Players,
            '-raceName', $raceName, '-raceEquipment', $raceGear, '-raceRoom', "`"$raceRoom`"",
            '-raceReport', "`"$raceReport`"", '-logFile', "`"$raceLog`"")
        if ($raceIndex -eq 1) { $raceArgs += @('-raceDiscovery', '1') }
        if ($raceIndex -ne 0 -or !$Capture) { $raceArgs += '-nographics' }
        if ($raceIndex -eq 0 -and $Capture) { $raceArgs += @('-raceCapture', '1', '-screen-width', '1280', '-screen-height', '800', '-screen-fullscreen', '0') }
        $raceProcesses += Start-Process -FilePath $Executable -ArgumentList $raceArgs -WindowStyle Hidden -PassThru
    }
    Write-Output "Started $Players independent processes. Reports: $raceOutput"
    $raceDeadline = (Get-Date).AddSeconds(185)
    do {
        Start-Sleep -Seconds 1
        $raceReports = @(Get-ChildItem -LiteralPath $raceOutput -Filter '*.json')
        if ($raceReports.Count -eq $Players) { break }
    } while ((Get-Date) -lt $raceDeadline)
    if ($raceReports.Count -ne $Players) { throw "Missing results: $($raceReports.Count)/$Players. See $raceOutput" }
    $raceResults = @($raceReports | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json })
    $raceResults | Format-Table role,passed,participants,maxRemotes,maxRows,resetObserved,finishedObserved,rematchStarted
    if (@($raceResults | Where-Object { !$_.passed }).Count) { throw "Network validation failed. See $raceOutput" }
    Write-Output "NETWORK_SMOKE_${Players}_PASS $raceOutput"
} finally {
    foreach ($raceProcess in $raceProcesses) {
        if (!$raceProcess.WaitForExit(5000)) { Stop-Process -Id $raceProcess.Id -ErrorAction SilentlyContinue }
    }
}
