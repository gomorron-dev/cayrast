<script lang="ts">
  import { app } from '../lib/appState.svelte';
  import { readSetting } from '../lib/settingsPath';
  import SettingControl from './SettingControl.svelte';

  let filter = $state('');

  /**
   * Groups descriptors by category, applying the in-page filter.
   *
   * The filter matches labels, descriptions, and keywords — the same fields the search
   * provider matches — so finding a setting from the search box and finding it here
   * behave identically. They are the same data.
   */
  const groups = $derived.by(() => {
    const needle = filter.trim().toLowerCase();

    const matching = app.settingsSchema.filter((descriptor) => {
      if (needle.length === 0) {
        return true;
      }

      return (
        descriptor.label.toLowerCase().includes(needle) ||
        (descriptor.description ?? '').toLowerCase().includes(needle) ||
        descriptor.category.toLowerCase().includes(needle) ||
        descriptor.keywords.some((keyword) => keyword.toLowerCase().includes(needle))
      );
    });

    const byCategory = new Map<string, typeof matching>();
    for (const descriptor of matching) {
      const existing = byCategory.get(descriptor.category);
      if (existing) {
        existing.push(descriptor);
      } else {
        byCategory.set(descriptor.category, [descriptor]);
      }
    }

    return [...byCategory.entries()];
  });
</script>

<div class="settings">
  <header class="settings__header">
    <h2 class="settings__title">Settings</h2>

    <input
      class="settings__filter"
      type="text"
      placeholder="Filter settings…"
      aria-label="Filter settings"
      value={filter}
      oninput={(event) => (filter = event.currentTarget.value)}
    />

    <button class="settings__close" type="button" onclick={() => app.closeSettings()}>Done</button>
  </header>

  <div class="settings__body">
    {#if groups.length === 0}
      <p class="settings__empty">Nothing matches &ldquo;{filter}&rdquo;.</p>
    {/if}

    {#each groups as [category, descriptors] (category)}
      <section class="settings__group">
        <h3 class="settings__category">{category}</h3>

        {#each descriptors as descriptor (descriptor.id)}
          <SettingControl
            {descriptor}
            value={readSetting(app.settings, descriptor.id) ?? descriptor.defaultValue}
            onchange={(value) => void app.updateSetting(descriptor.id, value)}
          />
        {/each}
      </section>
    {/each}

    {#if app.modules.length > 0}
      <section class="settings__group">
        <h3 class="settings__category">Modules</h3>

        {#each app.modules as module (module.id)}
          <div class="module">
            <div class="module__text">
              <span class="module__name">{module.name}</span>
              <span class="module__meta">
                v{module.version} · {module.author} · {module.trustLevel}
              </span>
              {#if module.description}
                <p class="module__description">{module.description}</p>
              {/if}
              {#if module.failureReason}
                <p class="module__failure">{module.failureReason}</p>
              {/if}
            </div>

            <span class="module__state" data-state={module.state.toLowerCase()}>{module.state}</span>
          </div>
        {/each}
      </section>
    {/if}
  </div>
</div>

<style>
  .settings {
    display: flex;
    flex-direction: column;
    flex: 1;
    min-height: 0;
  }

  .settings__header {
    display: flex;
    align-items: center;
    gap: var(--cy-space-3);
    padding: var(--cy-space-3) var(--cy-space-5);
    border-bottom: 1px solid var(--cy-border-divider);
    flex-shrink: 0;
  }

  .settings__title {
    margin: 0;
    font-size: var(--cy-text-query);
    font-weight: 500;
  }

  .settings__filter {
    flex: 1;
    padding: 4px 10px;
    border: 1px solid var(--cy-border-panel);
    border-radius: var(--cy-radius-chip);
    background: var(--cy-bg-chip);
    color: var(--cy-fg-primary);
    font-family: inherit;
    font-size: var(--cy-text-subtitle);
    user-select: text;
  }

  .settings__close {
    padding: 4px 12px;
    border: 1px solid var(--cy-border-panel);
    border-radius: var(--cy-radius-chip);
    background: var(--cy-accent);
    color: var(--cy-accent-contrast);
    font-family: inherit;
    font-size: var(--cy-text-subtitle);
    cursor: pointer;
  }

  .settings__body {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
    padding: 0 var(--cy-space-5) var(--cy-space-5);
  }

  .settings__group {
    margin-top: var(--cy-space-4);
  }

  .settings__category {
    margin: 0 0 var(--cy-space-1);
    font-size: var(--cy-text-label);
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.06em;
    color: var(--cy-fg-tertiary);
  }

  .settings__empty {
    padding: var(--cy-space-5) 0;
    text-align: center;
    color: var(--cy-fg-secondary);
    font-size: var(--cy-text-title);
  }

  .module {
    display: flex;
    align-items: flex-start;
    gap: var(--cy-space-4);
    padding: var(--cy-space-3) 0;
    border-bottom: 1px solid var(--cy-border-divider);
  }

  .module__text {
    flex: 1;
    min-width: 0;
  }

  .module__name {
    font-size: var(--cy-text-title);
    color: var(--cy-fg-primary);
  }

  .module__meta {
    display: block;
    font-size: var(--cy-text-label);
    color: var(--cy-fg-tertiary);
  }

  .module__description {
    margin: 4px 0 0;
    font-size: var(--cy-text-subtitle);
    color: var(--cy-fg-secondary);
  }

  .module__failure {
    margin: 4px 0 0;
    font-size: var(--cy-text-subtitle);
    color: #ff6b6b;
  }

  .module__state {
    padding: 2px 8px;
    border-radius: var(--cy-radius-chip);
    background: var(--cy-bg-chip);
    font-size: var(--cy-text-label);
    color: var(--cy-fg-secondary);
    flex-shrink: 0;
  }

  .module__state[data-state='enabled'] {
    color: var(--cy-accent);
  }

  .module__state[data-state='failed'] {
    color: #ff6b6b;
  }
</style>
