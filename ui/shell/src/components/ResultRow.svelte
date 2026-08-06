<script lang="ts">
  import type { SearchResult } from '../lib/types';

  interface Props {
    result: SearchResult;
    index: number;
    selected: boolean;
    onactivate: () => void;
    onhover: () => void;
  }

  const { result, index, selected, onactivate, onhover }: Props = $props();

  /**
   * Splits the title into matched and unmatched runs for highlighting.
   *
   * Adjacent matched indices are merged into one span rather than emitted per
   * character: with fuzzy matching a long run is common, and one element per letter
   * would both bloat the DOM and break text shaping such as ligatures and kerning.
   */
  const segments = $derived.by(() => {
    const indices = new Set(result.titleMatchIndices);
    const parts: Array<{ text: string; matched: boolean }> = [];

    let current = '';
    let currentMatched = false;

    for (let i = 0; i < result.title.length; i++) {
      const matched = indices.has(i);

      if (current.length > 0 && matched !== currentMatched) {
        parts.push({ text: current, matched: currentMatched });
        current = '';
      }

      current += result.title[i];
      currentMatched = matched;
    }

    if (current.length > 0) {
      parts.push({ text: current, matched: currentMatched });
    }

    return parts;
  });

  const primaryAction = $derived(result.actions[0]);
</script>

<div
  id="cy-result-{index}"
  class="row"
  class:row--selected={selected}
  role="option"
  aria-selected={selected}
  tabindex="-1"
  onclick={onactivate}
  onmousemove={onhover}
  onkeydown={(event) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      onactivate();
    }
  }}
>
  <div class="row__icon" aria-hidden="true">
    {#if result.icon.kind === 'DataUri' && result.icon.value}
      <img src={result.icon.value} alt="" />
    {:else}
      <!-- Placeholder until the icon resolver lands; keeps rows aligned so the
           list does not reflow when real icons arrive. -->
      <span class="row__icon-fallback">{result.title.charAt(0).toUpperCase()}</span>
    {/if}
  </div>

  <div class="row__text">
    <div class="row__title">
      {#each segments as segment}
        {#if segment.matched}<mark>{segment.text}</mark>{:else}{segment.text}{/if}
      {/each}
    </div>

    {#if result.subtitle}
      <div class="row__subtitle">{result.subtitle}</div>
    {/if}
  </div>

  <div class="row__meta">
    <span class="row__category">{result.category.displayName}</span>

    {#if selected && primaryAction?.shortcut}
      <kbd class="row__shortcut">{primaryAction.shortcut}</kbd>
    {/if}
  </div>
</div>

<style>
  .row {
    display: flex;
    align-items: center;
    gap: var(--cy-space-3);
    height: var(--cy-row-height);
    padding: 0 var(--cy-space-3);
    margin: 0 var(--cy-space-3);
    border-radius: var(--cy-radius-row);
    cursor: pointer;

    /* Only the background transitions. Animating layout properties here would
       cost a reflow on every arrow-key press through a long list. */
    transition: background-color var(--cy-duration-fast) var(--cy-ease);
  }

  .row:hover {
    background: var(--cy-bg-row-hover);
  }

  .row--selected {
    background: var(--cy-bg-row-selected);
  }

  .row__icon {
    display: grid;
    place-items: center;
    width: var(--cy-icon-size);
    height: var(--cy-icon-size);
    flex-shrink: 0;
  }

  .row__icon img {
    width: 100%;
    height: 100%;
    object-fit: contain;
  }

  .row__icon-fallback {
    display: grid;
    place-items: center;
    width: 100%;
    height: 100%;
    border-radius: var(--cy-radius-chip);
    background: var(--cy-bg-chip);
    color: var(--cy-fg-secondary);
    font-size: var(--cy-text-title);
    font-weight: 600;
  }

  .row__text {
    flex: 1;
    min-width: 0;
  }

  .row__title {
    font-size: var(--cy-text-title);
    font-weight: 500;
    color: var(--cy-fg-primary);

    /* Titles can be arbitrarily long — a deeply nested file path, say — and must
       never push the category and shortcut out of the row. */
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .row__title mark {
    background: none;
    color: var(--cy-accent);
    font-weight: 700;
  }

  .row__subtitle {
    font-size: var(--cy-text-subtitle);
    color: var(--cy-fg-secondary);
    white-space: nowrap;
    overflow: hidden;

    /* Paths are more recognisable by their tail than their head, so the ellipsis
       goes at the start via direction tricks handled by the host when needed. */
    text-overflow: ellipsis;
  }

  .row__meta {
    display: flex;
    align-items: center;
    gap: var(--cy-space-2);
    flex-shrink: 0;
  }

  .row__category {
    font-size: var(--cy-text-label);
    color: var(--cy-fg-tertiary);
    text-transform: uppercase;
    letter-spacing: 0.04em;
  }

  .row__shortcut {
    padding: 2px 6px;
    border-radius: var(--cy-radius-chip);
    background: var(--cy-bg-chip);
    color: var(--cy-fg-secondary);
    font-family: var(--cy-font);
    font-size: var(--cy-text-label);
  }
</style>
