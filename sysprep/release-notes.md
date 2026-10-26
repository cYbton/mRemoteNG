sysprep.io build of mRemoteNG for the sysprep.io Hyper-V training labs, based on upstream 1.78.2-dev.

Portable, self-contained x64 build - unzip and start `mRemoteNG.exe`, nothing to install.

New in sysprep.io.9: connected sessions show a green dot on their icon. The external credential providers (Delinea Secret Server, Passwordstate, 1Password, Vault/OpenBao) are not part of this build. No more confCons.xml backup copies by default (Options > Backup). `mRemoteNG.exe --warmup` loads all program files once and ends (exit code = files blocked by Windows) - Hyper-Lab runs it after unpacking, so Windows checks the new files during the deploy and not on the first start. The unused Windows 10 SDK projection (56 MB) and the external connector libraries (AWS SDK, VaultSharp) are gone.
sysprep.io.8: the splash screen shows right at the start, also on the slower first start of a new version.
sysprep.io.7: the splash screen opens on the same screen as the main window.

Icons: mRemoteNG's own in `Icons/mRemoteNG`, community icons by bearlikelion, Azure/Citrix/network icons of the sysprep.io lab diagrams - see `Icons/NOTICE.md`.

Built-in defaults: sysprep.io dark theme, larger fonts and tree icons, English UI, automatic reconnect after a server restart, quiet notifications, Consolas 12 for SSH (bundled PuTTY).

Source: https://github.com/cYbton/mRemoteNG/tree/sysprep (GPL-2.0, like upstream mRemoteNG).
