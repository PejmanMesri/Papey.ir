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
$acl = [System.Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true, $false)
$acl.SetAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
$acl.SetAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new('SYSTEM','FullControl','ContainerInherit,ObjectInherit','None','Allow'))
if ($PSVersionTable.PSEdition -eq 'Desktop') {
    [IO.DirectoryInfo]::new($dataFolder).SetAccessControl($acl)
} else {
    [IO.FileSystemAclExtensions]::SetAccessControl([IO.DirectoryInfo]::new($dataFolder), $acl)
}
$setup = Start-Process -FilePath $printerExe -ArgumentList @('--configure', ('"' + $PasswordFile + '"'), ('"' + $PrinterName + '"')) -WindowStyle Hidden -Wait -PassThru
if ($setup.ExitCode -ne 0) { throw 'Printer configuration failed. See the local agent log.' }
$check = Start-Process -FilePath $printerExe -ArgumentList '--check' -WindowStyle Hidden -Wait -PassThru
if ($check.ExitCode -ne 0) { throw 'Cannot authenticate with papey.ir. See the local agent log.' }
$action = New-ScheduledTaskAction -Execute $printerExe -WorkingDirectory $installFolder
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity
$principal = New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName 'Papey Order Printer' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Print paid papey.ir orders on the local BIXOLON receipt printer.' -Force | Out-Null
# Provision the encrypted configuration in the same interactive context as startup.
# Some managed desktops expose different profile views to installer child processes.
$configBlob = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $dataFolder 'settings.json')))
$escapedDataFolder = $dataFolder.Replace("'", "''")
$bootstrap = @"
`$ErrorActionPreference = 'Stop'
`$folder = '$escapedDataFolder'
[IO.Directory]::CreateDirectory(`$folder) | Out-Null
[IO.File]::WriteAllBytes((Join-Path `$folder 'settings.json'), [Convert]::FromBase64String('$configBlob'))
`$acl = [Security.AccessControl.DirectorySecurity]::new()
`$acl.SetAccessRuleProtection(`$true, `$false)
`$acl.SetAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.WindowsIdentity]::GetCurrent().Name,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
`$acl.SetAccessRule([Security.AccessControl.FileSystemAccessRule]::new('SYSTEM','FullControl','ContainerInherit,ObjectInherit','None','Allow'))
[IO.DirectoryInfo]::new(`$folder).SetAccessControl(`$acl)
"@
$encodedBootstrap = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($bootstrap))
$bootstrapAction = New-ScheduledTaskAction -Execute "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -Argument "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand $encodedBootstrap"
try {
    Set-ScheduledTask -TaskName 'Papey Order Printer' -Action $bootstrapAction | Out-Null
    Start-ScheduledTask -TaskName 'Papey Order Printer'
    Start-Sleep -Milliseconds 500
    for ($attempt = 0; $attempt -lt 40 -and (Get-ScheduledTask -TaskName 'Papey Order Printer').State -eq 'Running'; $attempt++) { Start-Sleep -Milliseconds 250 }
    if ((Get-ScheduledTaskInfo -TaskName 'Papey Order Printer').LastTaskResult -ne 0) { throw 'Startup profile provisioning failed.' }
} finally {
    Set-ScheduledTask -TaskName 'Papey Order Printer' -Action $action | Out-Null
}
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Papey Order Printer.lnk'))
$shortcut.TargetPath = $printerExe; $shortcut.WorkingDirectory = $installFolder; $shortcut.Save()
Start-ScheduledTask -TaskName 'Papey Order Printer'
Write-Output "Installed: $printerExe"
Write-Output 'Starts automatically when this Windows user signs in.'
