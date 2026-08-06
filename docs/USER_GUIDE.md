# Using Cayrast

---

## Opening it

Press **Alt+Space**. That is the whole interaction model.

- **Esc** clears the query; press it again to dismiss
- **↑ ↓** move through results, wrapping at both ends
- **Enter** runs the selected result
- Clicking away dismisses the launcher

Cayrast lives in the system tray and stays running. Closing the window only hides it —
quit properly from the tray menu.

If Alt+Space does nothing, another application probably owns it. Cayrast logs this at
startup and still works from the tray; pick a different combination in
Settings → Behaviour → Hotkey.

---

## Searching

Type anything. Results come from applications, commands, and settings, ranked together.

Matching is fuzzy and matched characters are highlighted, so you can see *why*
something matched:

| You type | You get |
|---|---|
| `vsc` | **V**isual **S**tudio **C**ode |
| `chr` | **Chr**ome |
| `term` | **Term**inal |

Two things shape the ranking beyond the text itself:

- **Where the match starts.** A match at the beginning outranks one buried in the
  middle, and word initials outrank scattered letters.
- **What you actually use.** Cayrast remembers what you pick and how recently, so your
  daily tools rise to the top. This is stored locally and never leaves the machine.

---

## Commands

Type a verb and its arguments. Commands that can preview do so live, before you press
Enter.

| Command | Example | Notes |
|---|---|---|
| `calc` | `calc 20*50` | Also `=`. Supports `sqrt`, `pi`, `^`, brackets |
| `uuid` | `uuid 5` | Random UUIDs |
| `base64` | `base64 hello` | `unbase64` decodes |
| `urlencode` | `urlencode a b` | `urldecode` reverses it |
| `sha256` | `sha256 hello` | Also `md5`, `sha1`, `sha512` |
| `timestamp` | `timestamp 1700000000` | No argument gives the current time |
| `json` | `json {"a":1}` | Formats and validates |
| `help` | `help calc` | Lists everything, including module commands |

Command output stays on screen with a **Copy** button rather than dismissing, because
the answer is usually the point.

`help` is generated from the live command registry, so a module's commands appear the
moment it loads.

---

## Settings

Open from the tray menu, or just search for what you want to change — settings are
indexed like everything else, by concept as well as by name. Searching `glass`,
`acrylic`, or `see-through` all find the transparency slider.

| Category | Covers |
|---|---|
| **Appearance** | Theme, accent, opacity, corner radius, position, scale, animation |
| **Behaviour** | Hotkey, startup, focus behaviour, monitor choice, tray icon |
| **Search** | Result limit, typing delay |
| **Privacy** | Browser history, clipboard history and encryption |
| **Updates** | Update checking and prerelease opt-in |

Changes apply immediately. Nothing here needs a restart.

### Privacy defaults

Every privacy setting defaults to the conservative choice, so a user who never opens
settings gets the most private configuration rather than the most featureful one:

- Browser history search is **off**. It is the most sensitive thing Cayrast could
  reach, and a launcher opens over whatever you happen to be sharing.
- Clipboard encryption is **on**.
- Content that password managers mark as sensitive is **skipped**.
- Automatic update installation is **off**. Cayrast will not replace itself without
  asking.

There is no telemetry, no analytics, and no account. Nothing is sent anywhere unless a
feature you enabled inherently requires it.

---

## Hidden mode

Turn off Settings → Behaviour → *Show the tray icon*. Cayrast keeps running and keeps
answering its hotkey with no visible presence.

Remember your hotkey before doing this — with no tray icon, it is the only way back in.

---

## Multiple monitors

By default the launcher opens on whichever monitor has your cursor, which is the best
available guess at where you are looking. Turn it off in Settings → Behaviour to pin it
to the primary display instead.

Mixed DPI is handled: the panel is sized in physical pixels per monitor, so it looks the
same on a 150% laptop screen and a 100% external one.

---

## Where your data lives

```
%APPDATA%\Cayrast\
  Settings\      settings.json, hand-editable
  Plugins\       installed modules
  Themes\        installed themes
  Database\      frecency ranking data

%LOCALAPPDATA%\Cayrast\
  Logs\          rolling logs, 7 days
  Cache\         regenerable; safe to delete
  WebView2\      browser data
```

Nothing is written to the install directory, so uninstalling never touches your
configuration and reinstalling picks it back up.

A corrupt `settings.json` will not stop Cayrast starting — it is moved aside as
`settings.json.corrupt-<timestamp>` and defaults are used. Your original is preserved.

---

## Modules

Almost every feature is a module, including the built-in ones. Installed modules are
listed in Settings → Modules with their version, trust level, and state.

> **⚠️ Module sandboxing is not built yet.** Modules currently run inside Cayrast with
> full access to whatever you can access, and the permissions they declare are not
> enforced. **Treat installing a module exactly like running any other program you
> downloaded** — install only ones you trust. Cayrast shows each module's trust level
> as "InProcess" rather than pretending otherwise.

Once sandboxing lands, third-party modules will run in a separate low-integrity process
where their declared permissions are enforced by the operating system rather than by
good behaviour.

To write one, see the [plugin guide](PLUGIN_GUIDE.md).

---

## Themes

Drop a `.cayrast-theme` file into `%APPDATA%\Cayrast\Themes\` and select it in
Settings → Appearance. See the [theme guide](THEME_GUIDE.md) to write one.

---

## When something goes wrong

Logs are in `%LOCALAPPDATA%\Cayrast\Logs`, one file per day, kept for a week. They record
startup, module loading, and errors — but skim before sharing one, since they can contain
file paths.

If Cayrast fails to start it shows the reason and the log location rather than
disappearing silently.

Bug reports go to [GitHub issues](https://github.com/gomorron-dev/cayrast/issues). Security
issues should go through [private reporting](../SECURITY.md) instead.
