<script lang="ts">
  import { app } from './lib/appState.svelte';
  import { applyTheme, watchSystemTheme } from './lib/theme';
  import SearchInput from './components/SearchInput.svelte';
  import ResultList from './components/ResultList.svelte';
  import OutputPanel from './components/OutputPanel.svelte';

  let searchInput = $state<ReturnType<typeof SearchInput> | null>(null);

  $effect(() => {
    void app.initialise();

    // Windows can switch between light and dark while Cayrast is resident for days at
    // a time. Following that live is the difference between feeling like part of the
    // system and feeling like a web page someone left open.
    return watchSystemTheme(() => {
      if (app.settings) {
        applyTheme(app.settings);
      }
    });
  });

  /**
   * Returns focus to the query field whenever the host shows the window.
   *
   * The window is warm and reused, so it keeps whatever focus state it had when it was
   * hidden. Without this the launcher opens with stale focus and silently swallows the
   * first thing typed — which reads as the application being broken.
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
    void app.activate();
  }

  const showResults = $derived(app.results.length > 0);
  const showEmptyState = $derived(
    app.query.trim().length > 0 && !app.searching && app.results.length === 0 && !app.output,
  );
</script>

<main class="panel">
  <SearchInput bind:this={searchInput} onsubmit={() => void app.activate()} />

  {#if app.output}
    <div class="panel__divider"></div>
    <OutputPanel text={app.output} oncopy={() => void app.copyOutput()} />
  {:else if showResults}
    <div class="panel__divider"></div>
    <ResultList onactivate={activate} />
  {:else if showEmptyState}
    <div class="panel__divider"></div>
    <div class="empty">
      <p class="empty__title">No results for &ldquo;{app.query}&rdquo;</p>
      <p class="empty__hint">Try a different search, or type <kbd>help</kbd> to list commands.</p>
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
     * The native window already carries a DWM Acrylic backdrop and rounded corners.
     * This layer adds only the tint and hairline border on top — painting an opaque
     * background here would hide the compositor effect entirely.
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

  .empty__hint kbd {
    padding: 1px 5px;
    border-radius: var(--cy-radius-chip);
    background: var(--cy-bg-chip);
    font-family: var(--cy-font-mono);
    font-size: 0.9em;
  }

  .error {
    padding: var(--cy-space-3) var(--cy-space-5);
    border-top: 1px solid var(--cy-border-divider);
    font-size: var(--cy-text-subtitle);

    /* Not a theme token: this must stay legible even under a theme that overrides
       every colour, because it is how failures reach the user. */
    color: #ff6b6b;
  }
</style>
