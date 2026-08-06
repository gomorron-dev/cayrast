<script lang="ts">
  import { app } from '../lib/appState.svelte';
  import ResultRow from './ResultRow.svelte';

  interface Props {
    onactivate: (index: number) => void;
  }

  const { onactivate }: Props = $props();

  let container = $state<HTMLDivElement | null>(null);

  /**
   * Keeps the keyboard selection visible.
   *
   * Arrow keys can move the selection past the visible window; without this the
   * highlight scrolls out of view and the user is navigating blind. `nearest` scrolls
   * the minimum distance needed, which avoids the jarring recentring that `center`
   * produces on every single keypress.
   */
  $effect(() => {
    const index = app.selectedIndex;
    if (!container || app.results.length === 0) {
      return;
    }

    const row = container.querySelector<HTMLElement>(`#cy-result-${index}`);
    row?.scrollIntoView({ block: 'nearest' });
  });
</script>

<div id="cy-results" bind:this={container} class="results" role="listbox" aria-label="Search results" tabindex="-1">
  {#each app.results as result, index (result.id)}
    <ResultRow
      {result}
      {index}
      selected={index === app.selectedIndex}
      onactivate={() => onactivate(index)}
      onhover={() => (app.selectedIndex = index)}
    />
  {/each}
</div>

<style>
  .results {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
    padding-bottom: var(--cy-space-3);

    /* Contain layout and paint so scrolling a long list does not invalidate the
       rest of the panel. */
    contain: layout paint;
  }
</style>
