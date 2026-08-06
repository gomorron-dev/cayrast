import { bridge } from './bridge';
import { applyTheme } from './theme';
import { writeSetting } from './settingsPath';
import type { CayrastSettings, ModuleInfo, SearchResult, SettingDescriptor } from './types';

interface SearchResponse {
  query: string;
  results: SearchResult[];
}

interface ActivateResponse {
  ok: boolean;
  close?: boolean;
  copyText?: string | null;
  message?: string | null;
  navigate?: string | null;
}

/**
 * The launcher's shared reactive state.
 *
 * Uses Svelte 5 runes, so components read these fields directly and re-render only
 * where a value they touched actually changed.
 */
class AppState {
  /** Current query text. */
  query = $state('');

  /** Results for the current query, in ranked order. */
  results = $state<SearchResult[]>([]);

  /** Index of the keyboard-selected row. */
  selectedIndex = $state(0);

  /** Whether a query is in flight. */
  searching = $state(false);

  /** Host settings, once loaded. */
  settings = $state<CayrastSettings | null>(null);

  /** Last error worth showing the user, if any. */
  error = $state<string | null>(null);

  /** Output from a command that asked to stay on screen, such as `calc` or `help`. */
  output = $state<string | null>(null);

  /** Which view is showing. */
  view = $state<'search' | 'settings'>('search');

  /** Setting descriptors, used to generate the settings screen. */
  settingsSchema = $state<SettingDescriptor[]>([]);

  /** Installed modules, shown in settings. */
  modules = $state<ModuleInfo[]>([]);

  /**
   * The query whose results are currently displayed.
   *
   * Partial results arrive as unsolicited events while the user keeps typing, so each
   * batch has to be checked against what is on screen now. Without this the list would
   * visibly flicker backwards as a slower earlier query reported in.
   */
  #displayedQuery = '';

  #debounceTimer: ReturnType<typeof setTimeout> | undefined;

  /** The currently selected result, if any. */
  get selected(): SearchResult | undefined {
    return this.results[this.selectedIndex];
  }

  /** Loads settings, applies the theme, and subscribes to host events. */
  async initialise(): Promise<void> {
    try {
      const settings = await bridge.request<CayrastSettings>('settings.get');
      this.settings = settings;
      applyTheme(settings);
    } catch (error) {
      // Not fatal: the stylesheet's own token defaults still render a usable
      // interface, so the launcher opens rather than showing nothing.
      console.error('[cayrast] Could not load settings.', error);
    }

    bridge.on<CayrastSettings>('settings.changed', (settings) => {
      this.settings = settings;
      applyTheme(settings);
    });

    // Progressive results. The host emits a ranked snapshot each time a provider
    // reports in, so fast providers paint while slow ones are still working.
    bridge.on<SearchResponse>('search.partial', (payload) => {
      if (!payload || payload.query !== this.query.trim()) {
        return;
      }

      this.#applyResults(payload.query, payload.results);
    });

    bridge.on('app.shown', () => this.onShown());

    bridge.on<{ view: string }>('navigate', (payload) => {
      if (payload?.view === 'settings') {
        void this.openSettings();
      }
    });
  }

  /** Switches to the settings screen, loading its schema on first use. */
  async openSettings(): Promise<void> {
    this.view = 'settings';
    this.output = null;

    // Loaded lazily. The schema is only needed once someone opens settings, and
    // fetching it at startup would add work to the path that delays sign-in.
    if (this.settingsSchema.length === 0) {
      try {
        const response = await bridge.request<{ settings: SettingDescriptor[] }>('settings.schema');
        this.settingsSchema = response?.settings ?? [];
      } catch (error) {
        console.error('[cayrast] Could not load the settings schema.', error);
        this.error = 'Could not load settings.';
      }
    }

    try {
      const response = await bridge.request<{ modules: ModuleInfo[] }>('modules.list');
      this.modules = response?.modules ?? [];
    } catch {
      // Modules are supplementary; failing to list them must not stop settings opening.
      this.modules = [];
    }
  }

  /** Returns to the search screen. */
  closeSettings(): void {
    this.view = 'search';
    this.setQuery('');
  }

  /**
   * Changes one setting by its descriptor id.
   *
   * Ids are dotted paths into the settings object, so the whole object is edited by
   * path and sent back. That means the host needs no per-setting mapping — and no
   * switch statement anyone has to remember to extend when adding a setting.
   */
  async updateSetting(id: string, value: unknown): Promise<void> {
    if (!this.settings) {
      return;
    }

    const previous = this.settings;
    const updated = writeSetting(previous, id, value);

    // Applied locally first so sliders track the pointer rather than lagging a
    // round-trip behind it.
    this.settings = updated;

    try {
      // The host normalises and clamps, then returns what it actually stored — which
      // may differ from what was sent. Adopting the response keeps the interface
      // showing the truth rather than an optimistic value that was rejected.
      const stored = await bridge.request<CayrastSettings>('settings.set', updated);
      if (stored) {
        this.settings = stored;
        applyTheme(stored);
      }
    } catch (error) {
      console.error('[cayrast] Could not save the setting.', error);
      this.settings = previous;
      this.error = 'Could not save that setting.';
    }
  }

  /** Called when the host shows the window. */
  onShown(): void {
    // The window is warm and reused, so it keeps whatever state it had when hidden.
    // Reopening into a settings screen someone left twenty minutes ago is not what
    // pressing the launcher hotkey means.
    this.view = 'search';

    if (this.settings?.behavior.clearQueryOnHide !== false) {
      this.setQuery('');
    }

    this.output = null;
    this.error = null;
  }

  /** Updates the query and schedules a debounced search. */
  setQuery(value: string): void {
    this.query = value;
    this.selectedIndex = 0;
    this.output = null;

    if (value.trim().length === 0) {
      this.results = [];
      this.#displayedQuery = '';
      this.searching = false;
      clearTimeout(this.#debounceTimer);
      return;
    }

    // Debounce so a burst of keystrokes mid-word produces one query rather than one
    // per character, each of which the next would immediately supersede.
    clearTimeout(this.#debounceTimer);
    const delay = this.settings?.search.debounceMilliseconds ?? 40;
    this.#debounceTimer = setTimeout(() => void this.#runSearch(value.trim()), delay);
  }

  async #runSearch(query: string): Promise<void> {
    this.searching = true;

    try {
      const response = await bridge.request<SearchResponse>('search.query', { text: query });

      // The response can land after the user has moved on; partial events have the
      // same hazard and the same guard.
      if (response && response.query === this.query.trim()) {
        this.#applyResults(response.query, response.results);
        this.error = null;
      }
    } catch (error) {
      if (query === this.query.trim()) {
        this.results = [];
        this.error = error instanceof Error ? error.message : 'Search failed.';
      }
    } finally {
      if (query === this.query.trim()) {
        this.searching = false;
      }
    }
  }

  #applyResults(query: string, results: SearchResult[]): void {
    const previousId = this.selected?.id;
    this.#displayedQuery = query;
    this.results = results ?? [];

    // Keep the selection on whatever the user had highlighted if it survived the
    // re-rank. Resetting to the top on every partial would move the selection out from
    // under someone who had already arrowed down to what they wanted.
    if (previousId) {
      const index = this.results.findIndex((result) => result.id === previousId);
      this.selectedIndex = index >= 0 ? index : 0;
    } else {
      this.selectedIndex = 0;
    }
  }

  /** Moves the selection, wrapping at both ends. */
  moveSelection(delta: number): void {
    if (this.results.length === 0) {
      return;
    }

    // Wrapping means Up from the first row reaches the last, which is faster than
    // holding Down through a long list.
    const count = this.results.length;
    this.selectedIndex = (this.selectedIndex + delta + count) % count;
  }

  /** Runs an action on the selected result. */
  async activate(actionId = 'default'): Promise<void> {
    const result = this.selected;
    if (!result) {
      return;
    }

    try {
      const response = await bridge.request<ActivateResponse>('result.activate', {
        resultId: result.id,
        actionId,
      });

      if (response?.navigate === 'settings') {
        await this.openSettings();
        return;
      }

      if (response?.copyText) {
        await this.#copyToClipboard(response.copyText);
      }

      if (response?.message) {
        // Commands such as `calc` and `help` return output meant to be read, so the
        // launcher stays open showing it rather than dismissing.
        this.output = response.message;
      }

      if (!response?.ok) {
        this.error = response?.message ?? 'That action failed.';
        return;
      }

      this.error = null;

      if (response.close !== false && !response.message) {
        this.hide();
      }
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'That action failed.';
    }
  }

  /** Copies command output, so a result can be used rather than only read. */
  async copyOutput(): Promise<void> {
    if (this.output) {
      await this.#copyToClipboard(this.output);
    }
  }

  async #copyToClipboard(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
    } catch (error) {
      // The clipboard API needs a focused document and can refuse; failing silently
      // would leave the user believing the copy worked.
      console.error('[cayrast] Clipboard write failed.', error);
      this.error = 'Could not copy to the clipboard.';
    }
  }

  /** Asks the host to hide the launcher. */
  hide(): void {
    bridge.notify('app.hide');
  }
}

/** The shared application state. */
export const app = new AppState();
