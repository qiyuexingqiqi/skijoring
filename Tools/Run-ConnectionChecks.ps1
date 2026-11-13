param([string]$Executable)
$ErrorActionPreference = 'Stop'
$raceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Executable) { $Executable = Join-Path $raceRoot 'Build\Windows\Skijoring.exe' }
$raceOutput = Join-Path $raceRoot ('Logs\ConnectionChecks-{0}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $raceOutput -Force | Out-Null
foreach ($raceCase in @('lobby', 'disconnect')) {
    $raceRoom = Join-Path $raceOutput "$raceCase-room.txt"
    $raceRoles = if ($raceCase -eq 'lobby') { @('lobbyhost', 'idle', 'idle', 'idle', 'wrongcode', 'full') } else { @('disconnecthost', 'disconnect', 'disconnect', 'disconnect') }
    $raceProcesses = @()
    try {
        for ($raceIndex = 0; $raceIndex -lt $raceRoles.Length; $raceIndex++) {
            $raceReport = Join-Path $raceOutput "$raceCase-$raceIndex.json"
            $raceLog = Join-Path $raceOutput "$raceCase-$raceIndex.log"
            $raceArgs = @('-batchmode', '-raceSmoke', $raceRoles[$raceIndex], '-racePlayers', '4', '-raceName', "QA_$raceIndex",
                '-raceRoom', "`"$raceRoom`"", '-raceReport', "`"$raceReport`"", '-logFile', "`"$raceLog`"")
            if ($raceIndex -eq 0 -and $raceCase -eq 'lobby') { $raceArgs += @('-raceCapture', '1', '-screen-width', '1280', '-screen-height', '800', '-screen-fullscreen', '0') }
            else { $raceArgs += '-nographics' }
            $raceProcesses += Start-Process -FilePath $Executable -ArgumentList $raceArgs -WindowStyle Hidden -PassThru
        }
        Write-Output "Connection check started: $raceCase"
        $raceDeadline = (Get-Date).AddSeconds(60)
        do {
            Start-Sleep -Seconds 1
            $raceReports = @(Get-ChildItem -LiteralPath $raceOutput -Filter "$raceCase-*.json")
            if ($raceReports.Count -eq $raceRoles.Length) { break }
        } while ((Get-Date) -lt $raceDeadline)
        if ($raceReports.Count -ne $raceRoles.Length) { throw "Missing $raceCase reports. See $raceOutput" }
        $raceResults = @($raceReports | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json })
        $raceResults | Format-Table role,passed,detail
        if (@($raceResults | Where-Object { !$_.passed }).Count) { throw "Connection check failed. See $raceOutput" }
    } finally {
        foreach ($raceProcess in $raceProcesses) {
            if (!$raceProcess.WaitForExit(3000)) { Stop-Process -Id $raceProcess.Id -ErrorAction SilentlyContinue }
        }
    }
}
Write-Output "CONNECTION_CHECKS_PASS $raceOutput"
