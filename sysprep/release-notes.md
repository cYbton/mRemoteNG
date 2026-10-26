sysprep.io build of mRemoteNG for the sysprep.io Hyper-V training labs, based on upstream 1.78.2-dev.

Portable, self-contained x64 build - unzip and start `mRemoteNG.exe`, nothing to install.

New in sysprep.io.9: single-file build - the program and its libraries are one `mRemoteNG.exe` instead of about 90 separate unsigned DLLs, so Smart App Control has far fewer files to judge and the first start is faster. Connected sessions show a green dot on their icon. The external credential providers (Delinea Secret Server, Passwordstate, 1Password, Vault/OpenBao) are not part of this build. No more confCons.xml backup copies by default (Options > Backup).
sysprep.io.8: the splash screen shows right at the start, also on the slower first start of a new version.
sysprep.io.7: the splash screen opens on the same screen as the main window.

Icons: mRemoteNG's own in `Icons/mRemoteNG`, community icons by bearlikelion, Azure/Citrix/network icons of the sysprep.io lab diagrams - see `Icons/NOTICE.md`.

Built-in defaults: sysprep.io dark theme, larger fonts and tree icons, English UI, automatic reconnect after a server restart, quiet notifications, Consolas 12 for SSH (bundled PuTTY).

Source: https://github.com/cYbton/mRemoteNG/tree/sysprep (GPL-2.0, like upstream mRemoteNG).
