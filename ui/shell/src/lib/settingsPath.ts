/**
 * Reads and writes settings by their descriptor id.
 *
 * Descriptor ids are dotted paths into the settings object — `appearance.accentColor`
 * addresses `settings.appearance.accentColor`. Navigating generically here means the
 * host needs no per-setting mapping: it hands over the whole settings object, the
 * interface edits one value by path, and sends the whole object back.
 *
 * The alternative — a switch statement in the host mapping every id to a `with`
 * expression — would have to be extended by hand for every new setting, and would be
 * the thing people forget when adding one.
 */

/** Reads the value at a dotted path, or `undefined` if any segment is missing. */
export function readSetting(root: unknown, path: string): unknown {
  let current = root;

  for (const segment of path.split('.')) {
    if (current === null || typeof current !== 'object') {
      return undefined;
    }

    current = (current as Record<string, unknown>)[segment];
  }

  return current;
}

/**
 * Returns a copy of `root` with the value at a dotted path replaced.
 *
 * Copies each level on the way down rather than mutating, so Svelte's reactivity sees
 * a new object and the caller's original stays untouched — which matters because the
 * original is what gets restored if the host rejects the change.
 */
export function writeSetting<T>(root: T, path: string, value: unknown): T {
  const segments = path.split('.');

  if (segments.length === 0 || root === null || typeof root !== 'object') {
    return root;
  }

  const clone = { ...(root as Record<string, unknown>) };
  let current = clone;

  for (let i = 0; i < segments.length - 1; i++) {
    const segment = segments[i]!;
    const child = current[segment];

    // A missing intermediate level is created rather than silently dropping the write,
    // which keeps a settings file written by an older version editable.
    current[segment] = child !== null && typeof child === 'object' ? { ...(child as Record<string, unknown>) } : {};
    current = current[segment] as Record<string, unknown>;
  }

  current[segments[segments.length - 1]!] = value;
  return clone as T;
}
