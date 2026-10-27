# mRemoteNG – sysprep.io build

A fork of [mRemoteNG](https://github.com/mRemoteNG/mRemoteNG), the open source, multi-protocol, tabbed remote
connections manager, for the Hyper-V training labs of [sysprep.io](https://sysprep.io). It is based on upstream
1.78.2-dev and ships as a portable, self-contained x64 build with the training defaults built in.

The lab automation (Hyper-Lab) installs the latest release of this fork and generates the lab's connection file
for it. The build works on its own as well: unzip it and start `mRemoteNG.exe` – nothing to install, no .NET
runtime needed.

**Download:** [latest release](https://github.com/cYbton/mRemoteNG/releases/latest)

## The lab's connections (Hyper-Lab)

Hyper-Lab's generator writes `confCons.xml` from the lab configs; the file is all a training PC needs besides
this build.

- **One folder per lab** (e.g. `sh000`, `cx000`) holding the lab's username and password (Administrator of the lab
  domain), with a subfolder per kind of connection that inherits them:
  - **RDP** – every server and client of the lab, plus extra entries for the lab's test users where a course needs
    them (e.g. `sh000-cl01 (cx000-user01)`).
  - **SSH** – the appliances: the OPNsense firewall (root) and the Citrix NetScaler (nsroot).
  - **Websites** – the lab's web interfaces as browser tabs with automatic login: OPNsense, Citrix Web Studio,
    Director, StoreFront (also once per test user), Licensing Manager, WEM Web Console, NetScaler, Windows Admin
    Center. A tab is named after the interface; the server behind it is in its description, and in its name only
    where two servers of a lab offer the same interface. Interfaces that share a server keep their own icon
    (StoreFront, Director and Licensing Manager next to the Delivery Controller's Web Studio).
- **No host key question:** each SSH connection carries the appliance's host key fingerprint
  (`-hostkey SHA256:…` in *SSH Options*), read from the running appliance when the file is generated. The exported
  lab VMs keep their keys, so PuTTY connects without asking on every PC the lab and the file are copied to.

## What is different from upstream

### RDP

- **The session follows the window.** With *Fit to window* and *automatic resize* (as in mRemoteNG 1.76) the
  picture scales while you drag the window, splitter or a panel, and when you let go the session takes the new
  size in place – no reconnect. Sizes are kept within what RDP accepts, nothing is sent while the tab is hidden.
- **Reconnect only when it makes sense.** After an unexpected disconnect a themed reconnect window stays until the
  session is back (at most 30 attempts, with a check of the RDP port). After a server restart it waits for the
  server; after a sign-out or a shutdown it waits until the server was down, else the tab closes after a minute.
  No reconnect after a failed first connect, after *Disconnect* or when the session was taken over elsewhere.
- **Fast and calm connecting.** No white flash before the session shows (the connection area keeps the theme
  background), the session is repainted once it is connected, and the first connection after the start doesn't
  hang (the RDP client is loaded in advance).
- **Sharp picture:** the client reports a LAN connection instead of upstream's fixed "modem" – with the H.264
  graphics pipeline of the newest RDP client, "modem" made the server compress hard and blurred desktop text.
- **Defaults:** clipboard and Windows key combinations (Alt+Tab, Win+…) redirected, full picture quality (32-bit colour, font smoothing, desktop composition, themes, wallpaper), the newest RDP
  client on the PC (*Highest*) – also for connection files of older formats.

### Web interfaces (HTTP/HTTPS connections)

- A browser-like bar above the page: back, forward, reload, **Log in** and an address bar.
- Login forms (OPNsense, Citrix Web Studio, StoreFront, …) are filled in with the connection's username and
  password and sent automatically when the tab opens; **Log in** does the same by hand. The input goes in as real
  keystrokes, so it works with any web framework; a login page is never sent twice (no account lockout).
- HTTP login prompts (Basic/NTLM, e.g. Windows Admin Center) are answered with the connection's credentials, and
  the self-signed certificates of the lab's web interfaces are accepted without a warning page.

### SSH

- The bundled PuTTY gets Consolas 12 as its default font unless one is set.
- *SSH Options* are read from connection files of format 2.6 too (Hyper-Lab's generator): it puts
  `-hostkey <fingerprint>` there for the lab appliances, so PuTTY does not ask for their host key on the PCs the
  file is copied to.

### Look and feel

- **sysprep.io dark theme** with the details fixed: triangle expand glyphs in the tree, the Visual Studio icons
  (tree, options list, toolbars) without their white fills, a thin frame around the connection tabs, no white
  areas while connecting, a themed reconnect window, the Japanese color names only for a Japanese UI.
- **Font sizes** in *Options > Appearance*: connection tree, program (tabs, panels, dialogs, option pages – fixed
  layouts grow with the font) and the icon size of the tree; the Config panel uses the tree's font size.
- **Connection tree:** a green dot marks connected sessions; no selection jumps around while you work in a tab; no
  grey line below the last node.
- **Icons:** the Azure, Citrix and network icon sets of the sysprep.io lab diagrams, mRemoteNG's own icons and the
  bearlikelion community set in `Icons/` (see [`Icons/NOTICE.md`](mRemoteNG/Icons/NOTICE.md)). The icon menu of
  a connection has a submenu per set and category and loads the pictures when opened; the scripts that render the
  lab icons from the sysprep.io draw.io stencils are in [`sysprep/IconBuilder`](sysprep/IconBuilder).
- **Layout:** the window starts maximized on the main screen; on the left (250 px) the connection tree and the
  Config panel share their place, and every start shows the connection tree in front; Notifications are hidden
  and back via the *View* menu, which got entries for Connections and Config; the panel tab strip shows only
  when *Always show panel tabs* is set; no minimize arrow on the tabs.
- **Start:** the splash screen shows right away, on the screen the window opens on; the log records how long the
  start took.

### Defaults and settings

- **Built-in training defaults:** English UI, no tray icon, no quick connect toolbar, no close confirmation, quiet
  notifications (no pop-ups for errors), automatic reconnect, PuTTY sessions hidden in the tree, no connection
  file backups, no password prompt for user profiles on start.
- **Settings carry over:** all option pages are taken over when a new version starts.
- **Version:** `1.78.2 (sysprep.io.N)` – in the About dialog, the splash screen and the file version.

### Removed

To keep the download small and the number of unsigned files low (see below): the external credential providers
(Delinea, Passwordstate, 1Password, Vault/OpenBao), storing the connections in SQL Server/MySQL, VNC/ARD, WPF, the
translations and the language selection, the Backup, SQL Server and Updates option pages, and the built-in update
check (it would offer upstream mRemoteNG – updates come with Hyper-Lab). At the start only plugin DLLs (`mRp.*`,
`*Plugin*`) are looked at in the program folder – checking every DLL froze the start for up to a minute in the
self-contained build.
## Smart App Control

The build is not code-signed. On a Windows PC with Smart App Control turned on, Windows judges every new, unsigned
file in the cloud and can block it. The build keeps that to a minimum:

- the NuGet libraries ship as their signed originals (not recompiled),
- the RDP interop assemblies, ObjectListView and the plugin contracts are fixed binaries
  (`mRemoteNG/libs/sysprep`) that keep their hash from release to release – built from
  [`ObjectListView`](ObjectListView) and [`mRemoteNG.PluginContracts`](mRemoteNG.PluginContracts) in this repository
  and from the RDP client's type library (MSTSCLib),
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
