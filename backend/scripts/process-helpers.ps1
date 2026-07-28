function Stop-ProcessTree([System.Diagnostics.Process]$Process) {
    if ($RunningOnWindows) {
        $killer = Start-Process -FilePath 'taskkill.exe' -ArgumentList @('/PID', $Process.Id, '/T', '/F') -PassThru -NoNewWindow -Wait
        if ($killer.ExitCode -ne 0 -and -not $Process.HasExited) { throw 'process_tree_kill_failed' }
    } else {
        & pkill -TERM -P $Process.Id 2>$null
        try { if (-not $Process.HasExited) { $Process.Kill() } } catch { }
    }
    if (-not $Process.WaitForExit(5000)) { throw 'process_tree_kill_timeout' }
}

function Invoke-BoundedSilent([string]$File, [string[]]$Arguments, [int]$TimeoutSeconds) {
    $quoted = @($Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } })
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo.FileName = $File
    $process.StartInfo.Arguments = ($quoted -join ' ')
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    if (-not $process.Start()) { throw 'command_start_failed' }
    $process.BeginOutputReadLine()
    $process.BeginErrorReadLine()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-ProcessTree $process
        throw 'command_timeout'
    }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'command_failed' }
}
