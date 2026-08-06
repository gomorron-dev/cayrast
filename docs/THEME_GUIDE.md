# Writing a Cayrast theme

A theme overrides CSS custom properties. There is no code, no build step, and no
restart — which is deliberate: theming should be approachable to people who are not
developers.

---

## The shape of a theme

A `.cayrast-theme` file is JSON:

```json
{
  "name": "Midnight",
  "id": "yourname.midnight",
  "version": "1.0.0",
  "author": "Your Name",
  "base": "dark",
  "variables": {
    "--cy-accent": "#7aa2f7",
    "--cy-bg-panel": "rgba(26, 27, 38, 0.82)",
    "--cy-fg-primary": "rgba(192, 202, 245, 0.95)",
    "--cy-radius-panel": "8px"
  }
}
```

`base` is `light` or `dark` and supplies every token you do not override. Without it
you would have to redefine all of them to avoid unreadable combinations — and most
themes would get one wrong.

Override only what you mean to change. A theme that sets four tokens is usually better
than one that sets forty.

---

## Tokens

Names follow `--cy-<category>-<role>`. The authoritative list is
[`theme.css`](../ui/shell/src/styles/theme.css); the ones you will actually want:

### Colour

| Token | What it colours |
|---|---|
| `--cy-accent` | Highlights, matched characters, focus rings |
| `--cy-bg-panel` | The launcher surface |
| `--cy-bg-row-hover` | A hovered result |
| `--cy-bg-row-selected` | The keyboard-selected result |
| `--cy-fg-primary` | Result titles, query text |
| `--cy-fg-secondary` | Subtitles and descriptions |
| `--cy-fg-tertiary` | Category labels, placeholder text |
| `--cy-border-panel` | The hairline around the panel |
| `--cy-shadow-panel` | The drop shadow |

`--cy-accent-hover` and `--cy-accent-muted` are derived from `--cy-accent`
automatically, so setting the accent alone gets you a coherent set.

### Shape and space

`--cy-radius-panel`, `--cy-radius-row`, `--cy-radius-chip`,
`--cy-space-1` … `--cy-space-5`, `--cy-row-height`, `--cy-icon-size`

### Type

`--cy-font`, `--cy-font-mono`, `--cy-text-query`, `--cy-text-title`,
`--cy-text-subtitle`, `--cy-text-label`

### Motion

`--cy-duration-fast`, `--cy-duration-normal`, `--cy-ease`, `--cy-motion-scale`

`--cy-motion-scale` multiplies every duration. Do not set it to `0` in a theme —
that is the user's accessibility preference to make, not yours.

---

## Keep the panel translucent

The launcher sits over whatever the user is doing, and the native window carries a DWM
Acrylic backdrop that shows through. An opaque `--cy-bg-panel` hides it and makes the
window look pasted on top of the desktop rather than part of it.

Use an `rgba()` with an alpha somewhere around `0.7`–`0.85`.

---

## What gets rejected, and why

Theme values are injected into the stylesheet, and a theme file can come from anywhere.
That makes an unvalidated value a CSS injection vector, so values are checked against
an allow-list of shapes before they are applied.

**Accepted:** hex colours, `rgb()`, `rgba()`, `hsl()`, `hsla()`, `color-mix()`,
`cubic-bezier()`, `calc()`, `var()`, numbers with CSS units, and font-family lists.

**Rejected:**

| Value | Why |
|---|---|
| `red; } body { display: none }` | Closes the declaration and writes arbitrary rules |
| `url(http://example.test/x.png)` | Fetches a remote resource, leaking that the user runs Cayrast |
| `@import ...` | Same |
| Names outside `--cy-*` | Would let a theme override a module UI's own variables |

An invalid value is dropped and reported; the rest of your theme still loads. One bad
line costs you that token rather than the whole file.

---

## Installing and sharing

Drop the `.cayrast-theme` file into:

```
%APPDATA%\Cayrast\Themes\
```

Then pick it in Settings → Appearance. Export from the same place to share.

---

## A worked example

A restrained dark theme that changes only what it needs to:

```json
{
  "name": "Ember",
  "id": "example.ember",
  "version": "1.0.0",
  "author": "Example",
  "base": "dark",
  "variables": {
    "--cy-accent": "#d97757",
    "--cy-bg-panel": "rgba(28, 25, 23, 0.8)",
    "--cy-bg-row-selected": "rgba(217, 119, 87, 0.14)",
    "--cy-border-panel": "rgba(217, 119, 87, 0.2)",
    "--cy-radius-panel": "10px"
  }
}
```

Five tokens. The base supplies the rest, and the derived accent variants keep hover and
muted states consistent without being stated.
