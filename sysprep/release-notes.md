sysprep.io build of mRemoteNG for the sysprep.io Hyper-V training labs, based on upstream 1.78.2-dev.

Portable, self-contained x64 build - unzip and start `mRemoteNG.exe`, nothing to install.

New in sysprep.io.10: the window starts maximized, the splash screen is a lightweight WinForms window. Slimmer: no WPF, no SQL Server/MySQL connection storage (connections live in confCons.xml), no VNC/ARD. No built-in update check (it offered upstream mRemoteNG) - Hyper-Lab's deploy and the LabKit bring the latest sysprep.io release.
sysprep.io.9: connected sessions show a green dot on their icon. No external credential providers (Delinea Secret Server, Passwordstate, 1Password, Vault/OpenBao). No confCons.xml backup copies by default (Options > Backup). The NuGet libraries ship as their signed originals (Smart App Control). `mRemoteNG.exe --warmup` loads all program files once and ends (exit code = files blocked by Windows) - Hyper-Lab runs it after unpacking, so Windows checks the new files during the deploy and not on the first start.

Icons: mRemoteNG's own in `Icons/mRemoteNG`, community icons by bearlikelion, Azure/Citrix/network icons of the sysprep.io lab diagrams - see `Icons/NOTICE.md`.

Built-in defaults: sysprep.io dark theme, larger fonts and tree icons, English UI, automatic reconnect after a server restart, quiet notifications, Consolas 12 for SSH (bundled PuTTY).

Source: https://github.com/cYbton/mRemoteNG/tree/sysprep (GPL-2.0, like upstream mRemoteNG).