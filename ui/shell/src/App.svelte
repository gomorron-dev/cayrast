<script lang="ts">
  import { app } from './lib/appState.svelte';
  import { applyTheme, watchSystemTheme } from './lib/theme';
  import SearchInput from './components/SearchInput.svelte';
  import ResultList from './components/ResultList.svelte';

  let searchInput = $state<ReturnType<typeof SearchInput> | null>(null);

  $effect(() => {
    void app.initialise();

    // Windows can switch between light and dark while Cayrast is resident for days
    // at a time. Following that live is the difference between feeling like part of
    // the system and feeling like a web page someone left open.
    return watchSystemTheme(() => {
      if (app.settings) {
        applyTheme(app.settings);
      }
    });
  });

  /**
   * Returns focus to the query field whenever the host shows the window.
   *
   * The window is warm and reused, so it keeps whatever focus state it had when it
   * was hidden. Without this the launcher would open with focus somewhere stale and
   * silently swallow the first thing typed.
   */
  $effect(() => {
    const focusQuery = () => searchInput?.focus();
    focusQuery();

    document.addEventListener('visibilitychange', focusQuery);
    window.addEventListener('focus', focusQuery);

    return () => {
      document.removeEventListener('visibilitychange', focusQuery);
      window.removeEventListener('focus', focusQuery);
    };
  });

  function activate(index: number): void {
    app.selectedIndex = index;
    submit();
  }

  function submit(): void {
    const result = app.selected;
    if (!result) {
      return;
    }

    // Wired to the real action dispatcher in Phase 2, once the command engine and
    // search providers exist to act on.
    console.info('[cayrast] Activate', result.id);
  }

  const showResults = $derived(app.results.length > 0);
  const showEmptyState = $derived(app.query.trim().length > 0 && !app.searching && app.results.length === 0);
</script>

<main class="panel" class:panel--expanded={showResults || showEmptyState}>
  <SearchInput bind:this={searchInput} onsubmit={submit} />

  {#if showResults}
    <div class="panel__divider"></div>
    <ResultList onactivate={activate} />
  {:else if showEmptyState}
    <div class="panel__divider"></div>
    <div class="empty">
      <p class="empty__title">No results for "{app.query}"</p>
      <p class="empty__hint">Search providers arrive in Phase&nbsp;2.</p>
    </div>
  {/if}

  {#if app.error}
    <div class="error" role="alert">{app.error}</div>
  {/if}
</main>

<style>
  .panel {
    display: flex;
    flex-direction: column;
    height: 100%;
    overflow: hidden;

    /*
     * The native window already has a DWM Acrylic backdrop and rounded corners.
     * This layer adds only the tint and hairline border on top of it — painting an
     * opaque background here would hide the compositor effect entirely.
     */
    background: var(--cy-bg-panel);
    border: 1px solid var(--cy-border-panel);
    border-radius: var(--cy-radius-panel);
    box-shadow: var(--cy-shadow-panel);

    /* Clip children to the rounded corner so the results list cannot square it off. */
    isolation: isolate;
  }

  .panel__divider {
    height: 1px;
    background: var(--cy-border-divider);
    flex-shrink: 0;
  }

  .empty {
    padding: var(--cy-space-5);
    text-align: center;
  }

  .empty__title {
    margin: 0 0 var(--cy-space-1);
    font-size: var(--cy-text-title);
    color: var(--cy-fg-secondary);
  }

  .empty__hint {
    margin: 0;
    font-size: var(--cy-text-subtitle);
    color: var(--cy-fg-tertiary);
  }

  .error {
    padding: var(--cy-space-3) var(--cy-space-5);
    border-top: 1px solid var(--cy-border-divider);
    font-size: var(--cy-text-subtitle);
    color: #ff6b6b;
  }
</style>
