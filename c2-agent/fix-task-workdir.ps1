# fix-task-workdir.ps1
# Corrige o WorkingDirectory da PaneasMonitorTask

$ErrorActionPreference = "Stop"

Write-Host "Corrigindo WorkingDirectory da PaneasMonitorTask..." -ForegroundColor Yellow

try {
    # Get task
    $task = Get-ScheduledTask -TaskName "PaneasMonitorTask" -ErrorAction Stop

    if ($task) {
        Write-Host "Task encontrada: $($task.TaskName)" -ForegroundColor Green

        # Get current action
        $action = $task.Actions[0]
        Write-Host "Executável atual: $($action.Execute)" -ForegroundColor Gray
        Write-Host "WorkingDirectory atual: '$($action.WorkingDirectory)'" -ForegroundColor Gray

        # Get directory from executable path
        $exePath = $action.Execute
        $workDir = Split-Path -Parent $exePath

        Write-Host "Novo WorkingDirectory: $workDir" -ForegroundColor Cyan

        # Create new action with WorkingDirectory
        $newAction = New-ScheduledTaskAction -Execute $exePath -WorkingDirectory $workDir

        # Update task
        Set-ScheduledTask -TaskName "PaneasMonitorTask" -Action $newAction -ErrorAction Stop

        Write-Host "✓ Task atualizada com sucesso!" -ForegroundColor Green
        Write-Host ""
        Write-Host "Próximo passo: Reinicie o Agent ou faça logout/login" -ForegroundColor White
    } else {
        Write-Host "Task PaneasMonitorTask não encontrada!" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "ERRO: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
