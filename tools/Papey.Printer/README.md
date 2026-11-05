# Papey Order Printer

For a new shop PC, extract the customer Windows ZIP and run `Setup.cmd`. The
setup screen asks for the shop administrator's username/password and local
printer. It verifies HTTPS login before saving credentials with Windows DPAPI
and enabling startup at Windows sign-in. The package contains no credentials,
cookies, print history, or customer data. This desktop setup uses the current
user's Run startup entry; the maintenance PowerShell installer below uses a
scheduled task instead. Neither needs an inbound network port.

Windows tray app for the shop's USB BIXOLON SRP-330II. No paid cloud-printing
subscription or inbound ports are needed. It connects outbound to papey.ir over
HTTPS and polls the authenticated durable queue every five seconds. Only orders
with verified payment enter the existing server queue; failed/pending payments
and historical orders are not automatically printed.

## Install

Requires Windows 10/11, the installed BIXOLON Windows driver, and Microsoft Edge
WebView2 Runtime. The installer must run as the Windows user who owns the printer.
The app uses that user's interactive Windows spooler and starts at sign-in; this
PC, the printer, and the Internet connection must remain available. It cannot print
while the computer is asleep, shut down, or signed out. Queued orders wait safely
on the server until the app returns.

```powershell
dotnet publish tools/Papey.Printer/Papey.Printer.csproj -c Release -r win-x64 --self-contained true -o "$env:TEMP/papey-printer-publish"
& tools/Papey.Printer/Install.ps1 -PublishedFolder "$env:TEMP/papey-printer-publish" -PasswordFile "$env:USERPROFILE/.ssh/papey-admin-password.txt"
```

Files install in `%LOCALAPPDATA%/Programs/PapeyPrinter`; the Desktop shortcut is
**Papey Order Printer**. The scheduled task **Papey Order Printer** starts it at
sign-in. A tray icon provides status, pause/resume, recovery, logs, and exit.
The installed app needs no .NET SDK/runtime installation (self-contained build).

`%LOCALAPPDATA%/Papey/Printer` contains Windows-DPAPI-protected credentials,
WebView session data, status, rotating diagnostic logs, and `pending.json`.
Credentials are encrypted for the current Windows user and the folder ACL is
restricted to that user and SYSTEM. Never commit or share this folder. To change
credentials, pause the app, wait for the queue to be idle, then rerun setup using
the updated existing password file. Changing the website admin password requires
updating the app's configuration too.

## Printing and recovery

The app opens the existing authenticated 80 mm order receipt with automatic
browser printing disabled. It validates the order ID, waits for fonts/images,
renews the job lease, and captures only the receipt rectangle. It crops unused
top/bottom space and sends native ESC/POS raster rows through the Windows RAW
spooler, followed by a minimum-feed partial cut. Paper length follows the invoice
content, with no 100 mm minimum, A4 page, or driver page-length padding. The
SRP-330II uses its 512-dot / 180 dpi print head; the SRP-332II uses 576 dots / 203 dpi.
Persian shaping is handled by WebView2. It never changes the default
Windows printer and never falls back to a different device.

The journal is flushed before calling the printer and after successful spooler
submission. Lost acknowledgements retry without printing another copy. A restart
while preparing can safely retry. A restart during submission pauses the app and
requires checking the paper and Windows spooler before choosing **Resolve
uncertain print**. Printer-unavailable results retry safely. Other ambiguous
results pause rather than blindly duplicating a receipt. Printer acceptance means
submission to the Windows printer system, not proof paper exited the device.
Offline/error spooler jobs prevent claiming new orders; an acknowledged print
submission with a subsequent queue error retains its journal until the queue is
healthy, without submitting another copy.
Use one active shop installation; local mutex prevents duplicate local instances.

Avoid interrupting an in-flight receipt when updating. Pause and wait until
`pending.json` is null, then run setup. Do not remove this journal to bypass a
recovery prompt. If another computer reclaimed the lease, resolve it on the server.

## Checks

```powershell
dotnet run --project tests/PrinterAgentChecks -c Release
```

Installed executable switches (only while the tray app is stopped):

- `--check`: authenticate and verify configuration; does not claim jobs or print.
- `--preview`: generate an exact cropped `test-receipt.png`, its dimensions, and
  a secondary PDF preview locally, without paper or a queue job.
- `--test`: send one clearly marked synthetic receipt to the configured printer.

Server API and isolated queue tests are documented in `PRINTING_API.md`.
Microsoft documents the
[Windows RAW spooler API](https://learn.microsoft.com/en-us/windows/win32/printdocs/sending-data-directly-to-a-printer).

## Version 1.2: blank-paper compatibility mode

The tray menu now includes **Print method** and **Print a test receipt**. If RAW
ESC/POS feeds blank paper, switch to **Windows driver** (click Print method).
Switching pauses automatic orders: run a synthetic test, then resume only after
checking the result. No automatic fallback reprints an uncertain order.
Driver mode draws the validated receipt bitmap through the installed Windows
driver, requests 80 mm paper with content-sized height, respects hardware margins,
and continues on another page if the driver caps roll length. RAW mode remains
available. Existing installations keep their current mode; new setup recommends
the Windows driver. Spooler acceptance cannot prove physical printing.

Before updating, exit the tray app after the queue is idle and any uncertain
job has been resolved. Extract the updated package, then run Setup.cmd. The
updater refuses a running instance or unresolved journal and preserves existing
credentials. To change credentials later, run the installed app with --setup.

If the printer's own self-test is also blank, website page dimensions cannot
explain that result: check the thermal paper and device with the customer. If
the self-test prints but the app's test does not, try the other print method and
check the driver/queue name. Do not mark a blank order as successfully printed.
