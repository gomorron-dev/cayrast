import { bridge } from './bridge';
import { applyTheme } from './theme';
import type { CayrastSettings, SearchResponse, SearchResult } from './types';

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

  /**
   * Monotonic id of the most recently dispatched query.
   *
   * Responses can arrive out of order — a slow query for "d" can land after a fast
   * one for "disc". Without this check the stale response would overwrite fresher
   * results and the list would visibly flicker backwards as the user types.
   */
  #latestQueryId = 0;

  #debounceTimer: ReturnType<typeof setTimeout> | undefined;

  /** The currently selected result, if any. */
  get selected(): SearchResult | undefined {
    return this.results[this.selectedIndex];
  }

  /** Loads settings and applies them to the document. */
  async initialise(): Promise<void> {
    try {
      const settings = await bridge.request<CayrastSettings>('settings.get');
      this.settings = settings;
      applyTheme(settings);
    } catch (error) {
      // A failure here is not fatal: the built-in token defaults still render a
      // usable interface, so the launcher opens rather than showing nothing.
      console.error('[cayrast] Could not load settings.', error);
    }

    bridge.on<CayrastSettings>('settings.changed', (settings) => {
      this.settings = settings;
      applyTheme(settings);
    });

    bridge.on('app.shown', () => this.onShown());
  }

  /** Called when the host shows the window. */
  onShown(): void {
    if (this.settings?.behavior.clearQueryOnHide !== false) {
      this.setQuery('');
    }
  }

  /** Updates the query and schedules a debounced search. */
  setQuery(value: string): void {
    this.query = value;
    this.selectedIndex = 0;

    if (value.trim().length === 0) {
      this.results = [];
      this.searching = false;
      clearTimeout(this.#debounceTimer);
      return;
    }

    // Debounce so a burst of keystrokes mid-word produces one query rather than
    // one per character, each of which the next would immediately supersede.
    clearTimeout(this.#debounceTimer);
    const delay = this.settings?.search.debounceMilliseconds ?? 40;
    this.#debounceTimer = setTimeout(() => void this.#runSearch(value), delay);
  }

  async #runSearch(query: string): Promise<void> {
    const queryId = ++this.#latestQueryId;
    this.searching = true;

    try {
      const response = await bridge.request<SearchResponse>('search.query', { text: query });

      // Discard anything that is no longer the newest query.
      if (queryId !== this.#latestQueryId) {
        return;
      }

      this.results = response?.results ?? [];
      this.selectedIndex = 0;
      this.error = null;
    } catch (error) {
      if (queryId !== this.#latestQueryId) {
        return;
      }

      this.results = [];
      this.error = error instanceof Error ? error.message : 'Search failed.';
    } finally {
      if (queryId === this.#latestQueryId) {
        this.searching = false;
      }
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

  /** Asks the host to hide the launcher. */
  hide(): void {
    bridge.notify('app.hide');
  }
}

/** The shared application state. */
export const app = new AppState();
