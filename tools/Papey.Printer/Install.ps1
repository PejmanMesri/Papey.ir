param(
    [Parameter(Mandatory)][string]$PublishedFolder,
    [Parameter(Mandatory)][string]$PasswordFile,
    [string]$PrinterName = 'BIXOLON SRP-330II'
)
$ErrorActionPreference = 'Stop'
$installFolder = Join-Path $env:LOCALAPPDATA 'Programs\PapeyPrinter'
$dataFolder = Join-Path $env:LOCALAPPDATA 'Papey\Printer'
$printerExe = Join-Path $installFolder 'Papey.Printer.exe'
if (-not (Test-Path -LiteralPath (Join-Path $PublishedFolder 'Papey.Printer.exe'))) { throw 'Publish the Windows app first.' }
if (-not (Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue)) { throw 'Install the printer driver first.' }
Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $printerExe } | ForEach-Object {
    # Updates must be done while the queue is idle; never interrupt an in-flight receipt.
    $pending = Join-Path $dataFolder 'pending.json'
    if ((Test-Path -LiteralPath $pending) -and (Get-Content -LiteralPath $pending -Raw).Trim() -ne 'null') { throw 'Resolve the pending print before updating.' }
    Stop-Process -Id $_.ProcessId
}
New-Item -ItemType Directory -Force -Path $installFolder,$dataFolder | Out-Null
Copy-Item -Path (Join-Path $PublishedFolder '*') -Destination $installFolder -Recurse -Force
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$acl = Get-Acl -LiteralPath $dataFolder
$acl.SetAccessRuleProtection($true, $false)
$acl.SetAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
$acl.SetAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new('SYSTEM','FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $dataFolder -AclObject $acl
$setup = Start-Process -FilePath $printerExe -ArgumentList @('--configure', ('"' + $PasswordFile + '"'), ('"' + $PrinterName + '"')) -WindowStyle Hidden -Wait -PassThru
if ($setup.ExitCode -ne 0) { throw 'Printer configuration failed. See the local agent log.' }
$check = Start-Process -FilePath $printerExe -ArgumentList '--check' -WindowStyle Hidden -Wait -PassThru
if ($check.ExitCode -ne 0) { throw 'Cannot authenticate with papey.ir. See the local agent log.' }
$action = New-ScheduledTaskAction -Execute $printerExe -WorkingDirectory $installFolder
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity
$principal = New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName 'Papey Order Printer' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Print paid papey.ir orders on the local BIXOLON receipt printer.' -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Papey Order Printer.lnk'))
$shortcut.TargetPath = $printerExe; $shortcut.WorkingDirectory = $installFolder; $shortcut.Save()
Start-ScheduledTask -TaskName 'Papey Order Printer'
Write-Output "Installed: $printerExe"
Write-Output 'Starts automatically when this Windows user signs in.'
