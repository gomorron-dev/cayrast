<script lang="ts">
  import { app } from '../lib/appState.svelte';

  interface Props {
    /** Invoked when the user commits the selected result. */
    onsubmit: () => void;
  }

  const { onsubmit }: Props = $props();

  let input = $state<HTMLInputElement | null>(null);

  /**
   * Keeps focus in the query field.
   *
   * The launcher exists to be typed into. If focus is anywhere else when it opens,
   * the first keystrokes are silently lost — which reads to the user as the
   * application being broken rather than merely unfocused.
   */
  export function focus(): void {
    input?.focus();
    input?.select();
  }

  function onKeyDown(event: KeyboardEvent): void {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        app.moveSelection(1);
        break;

      case 'ArrowUp':
        event.preventDefault();
        app.moveSelection(-1);
        break;

      case 'Enter':
        event.preventDefault();
        onsubmit();
        break;

      case 'Escape':
        event.preventDefault();

        // Escape clears a non-empty query first and only dismisses on a second
        // press. Closing outright would throw away typing the user may have meant
        // only to correct.
        if (app.query.length > 0) {
          app.setQuery('');
        } else {
          app.hide();
        }
        break;

      case 'Tab':
        // Nothing else in the panel is tab-focusable, so the default behaviour
        // would move focus out of the input and strand the keyboard user.
        event.preventDefault();
        break;
    }
  }
</script>

<div class="search">
  <svg class="search__icon" viewBox="0 0 20 20" aria-hidden="true">
    <circle cx="8.5" cy="8.5" r="5.5" fill="none" stroke="currentColor" stroke-width="1.6" />
    <path d="M12.8 12.8 L17 17" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" />
  </svg>

  <input
    bind:this={input}
    class="search__input"
    type="text"
    spellcheck="false"
    autocomplete="off"
    autocorrect="off"
    autocapitalize="off"
    placeholder="Search applications, files, and commands…"
    aria-label="Search"
    role="combobox"
    aria-expanded={app.results.length > 0}
    aria-controls="cy-results"
    aria-activedescendant={app.selected ? `cy-result-${app.selectedIndex}` : undefined}
    value={app.query}
    oninput={(event) => app.setQuery(event.currentTarget.value)}
    onkeydown={onKeyDown}
  />

  {#if app.searching}
    <span class="search__spinner" aria-label="Searching"></span>
  {/if}
</div>

<style>
  .search {
    display: flex;
    align-items: center;
    gap: var(--cy-space-3);
    padding: var(--cy-space-4) var(--cy-space-5);
    flex-shrink: 0;
  }

  .search__icon {
    width: 20px;
    height: 20px;
    flex-shrink: 0;
    color: var(--cy-fg-tertiary);
  }

  .search__input {
    flex: 1;
    min-width: 0;
    border: none;
    background: var(--cy-bg-input);
    color: var(--cy-fg-primary);
    font-family: inherit;
    font-size: var(--cy-text-query);
    font-weight: 400;
    line-height: 1.4;

    /* The panel is the input as far as the user is concerned; a visible field
       border inside it would read as a control within a control. */
    outline: none;

    /* Typing is the whole point of this element, so it opts out of the
       application-wide selection lock. */
    user-select: text;
    cursor: text;
  }

  .search__input::placeholder {
    color: var(--cy-fg-tertiary);
  }

  .search__spinner {
    width: 14px;
    height: 14px;
    flex-shrink: 0;
    border: 2px solid var(--cy-fg-tertiary);
    border-top-color: transparent;
    border-radius: 50%;
    animation: spin calc(700ms * max(var(--cy-motion-scale), 0.001)) linear infinite;
  }

  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }

  /* A spinner is the one thing that must not animate under reduced motion; it
     becomes a static ring rather than disappearing, so the state is still legible. */
  @media (prefers-reduced-motion: reduce) {
    .search__spinner {
      animation: none;
      border-top-color: var(--cy-fg-tertiary);
      opacity: 0.5;
    }
  }
</style>
