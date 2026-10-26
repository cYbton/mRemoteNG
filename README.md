# mRemoteNG – sysprep.io build

A fork of [mRemoteNG](https://github.com/mRemoteNG/mRemoteNG), the open source, multi-protocol, tabbed remote
connections manager, for the Hyper-V training labs of [sysprep.io](https://sysprep.io). It is based on upstream
1.78.2-dev and ships as a portable, self-contained x64 build with the training defaults built in.

The lab automation (Hyper-Lab) installs the latest release of this fork and generates the lab's connection file
for it. The build works on its own as well: unzip it and start `mRemoteNG.exe` – nothing to install, no .NET
runtime needed.

**Download:** [latest release](https://github.com/cYbton/mRemoteNG/releases/latest)

## What is different from upstream

**Built-in defaults** – dark theme (`sysprep.io_dark`), larger fonts and tree icons, English UI, the window starts
maximized, only the connection tree on the left, quiet notifications, no tray icon, no close confirmation, no
connection file backups. RDP connections redirect the clipboard and Windows key combinations and use the newest RDP
client on the PC. SSH runs in the bundled PuTTY with Consolas 12.

**RDP**

- Automatic reconnect only after an unexpected disconnect, with a themed reconnect window; after a server restart
  it waits for the RDP port; no reconnect after a sign-out or when the session was taken over elsewhere.
- No white flash while connecting, the session is resized smoothly when panels change.
- The first connection after the start doesn't hang (the RDP client is loaded in advance).

**Web interfaces (HTTP/HTTPS connections)**

- A browser-like bar above the page: back, forward, reload, **Log in** and an address bar.
- Login forms (OPNsense, Citrix Web Studio, StoreFront, …) are filled in with the connection's username and
  password and sent automatically when the tab opens; **Log in** does the same by hand. HTTP login prompts
  (Basic/NTLM, e.g. Windows Admin Center) are answered too.
- The self-signed certificates of the lab's web interfaces are accepted without a warning page.

**Connection tree** – a green dot marks connected sessions; Azure, Citrix and network icons of the lab diagrams in
`Icons/` (see [`Icons/NOTICE.md`](mRemoteNG/Icons/NOTICE.md)).

**Removed** – to keep the download small and the number of unsigned files low: external credential providers
(Delinea, Passwordstate, 1Password, Vault/OpenBao), SQL Server/MySQL connection storage, VNC/ARD, WPF, the
translations and language selection, and the built-in update check (it would offer upstream mRemoteNG; updates
come with Hyper-Lab).

## Smart App Control

The build is not code-signed. On a Windows PC with Smart App Control turned on, Windows judges every new, unsigned
file in the cloud and can block it. The build keeps that to a minimum:

- the NuGet libraries ship as their signed originals (not recompiled),
- the RDP interop assemblies, ObjectListView and the plugin contracts are fixed binaries
  (`mRemoteNG/libs/sysprep`) that keep their hash from release to release,
- so per release only `mRemoteNG.dll` is new.

`mRemoteNG.exe --warmup` loads every program file once without running it and ends; the exit code is the number of
files Windows blocked, their paths come on stdout. Hyper-Lab runs it right after unpacking, so Windows checks the
new files during the deploy and not on the user's first start.

## Building and releasing

Builds run on GitHub Actions ([`.github/workflows/sysprep-build.yml`](.github/workflows/sysprep-build.yml)) – a
local build on a PC with Smart App Control on gets its freshly built DLLs blocked.

1. Count up `sysprepRelease` in `mRemoteNG/Properties/AssemblyInfo.tt` **and** `AssemblyInfo.cs`, and describe the
   release in [`sysprep/release-notes.md`](sysprep/release-notes.md).
2. Push to the `sysprep` branch: the workflow builds the zip and attaches it to the run.
3. Test that zip – including `mRemoteNG.exe --warmup` on a PC with Smart App Control on.
4. Push the tag `v1.78.2-sysprep.io.<n>`: the workflow publishes the zip of the tested build of that commit, byte
   for byte, as the latest release. It doesn't build again.

Changed defaults belong in three places: `Properties/*.settings`, the matching `*.Designer.cs` and – for the main
`Settings` class – `App.config`. Code changes of the fork are marked with `sysprep fork:` comments.

## License and credits

GPL-2.0, like upstream mRemoteNG – see [`COPYING.txt`](COPYING.txt). All credit for mRemoteNG goes to its
developers and contributors ([`CREDITS.md`](CREDITS.md)); if mRemoteNG is useful to you, consider
[supporting the upstream project](https://mremoteng.org/contribute). Documentation for the features this fork
didn't change: [mremoteng.readthedocs.io](https://mremoteng.readthedocs.io/en/latest/).
