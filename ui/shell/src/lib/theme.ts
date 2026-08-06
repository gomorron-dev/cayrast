import type { CayrastSettings } from './types';

/**
 * Projects host settings onto the document's CSS custom properties.
 *
 * This function is the entire link between "the user changed a setting" and "the
 * interface looks different". Because every visual value in the stylesheet reads
 * from a custom property, changing one here updates the UI with no re-render, no
 * component knowing a setting exists, and no restart.
 */
export function applyTheme(settings: CayrastSettings): void {
  const root = document.documentElement;
  const { appearance } = settings;

  root.dataset.theme = resolveTheme(appearance.theme);

  root.style.setProperty('--cy-accent', appearance.accentColor);
  root.style.setProperty('--cy-radius-panel', `${appearance.borderRadius}px`);

  // Reduced motion wins over the user's animation-speed preference. Someone who
  // told Windows they are motion-sensitive should not have to also find this
  // application's own setting, and should never be overridden by a theme.
  const reduceMotion =
    appearance.respectReducedMotion && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  root.style.setProperty('--cy-motion-scale', reduceMotion ? '0' : String(appearance.animationSpeed));

  root.style.setProperty('--cy-panel-opacity', String(appearance.transparency));
  root.style.setProperty('--cy-shadow-opacity', String(appearance.shadowIntensity));

  if (appearance.fontFamily) {
    root.style.setProperty('--cy-font', appearance.fontFamily);
  } else {
    root.style.removeProperty('--cy-font');
  }

  // uiScale multiplies the root font size, so everything expressed in rem scales
  // together. The host separately scales the window itself, so the two stay in step.
  root.style.fontSize = `${16 * appearance.uiScale}px`;
}

function resolveTheme(mode: CayrastSettings['appearance']['theme']): 'light' | 'dark' {
  if (mode === 'Light') {
    return 'light';
  }

  if (mode === 'Dark') {
    return 'dark';
  }

  // System and Custom both follow the OS for now. WebView2 reports the Windows app
  // theme through this media query, so it stays correct when the user switches
  // Windows between light and dark while Cayrast is running.
  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
}

/**
 * Re-applies the theme when Windows switches between light and dark.
 *
 * Returns an unsubscribe function.
 */
export function watchSystemTheme(onChange: () => void): () => void {
  const query = window.matchMedia('(prefers-color-scheme: light)');
  query.addEventListener('change', onChange);
  return () => query.removeEventListener('change', onChange);
}
