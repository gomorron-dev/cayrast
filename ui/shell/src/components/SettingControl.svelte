<script lang="ts">
  import type { SettingDescriptor } from '../lib/types';

  interface Props {
    descriptor: SettingDescriptor;
    value: unknown;
    onchange: (value: unknown) => void;
  }

  const { descriptor, value, onchange }: Props = $props();

  const inputId = $derived(`setting-${descriptor.id.replace(/\./g, '-')}`);

  // Slider steps are derived from the range so a 0-1 opacity slider moves in
  // hundredths while a 0-2000 pixel slider moves in whole numbers. A single fixed step
  // would make one of the two useless.
  const sliderStep = $derived.by(() => {
    const min = descriptor.minimum ?? 0;
    const max = descriptor.maximum ?? 1;
    return max - min <= 5 ? 0.05 : 1;
  });
</script>

<div class="setting">
  <div class="setting__text">
    <label class="setting__label" for={inputId}>{descriptor.label}</label>
    {#if descriptor.description}
      <p class="setting__description">{descriptor.description}</p>
    {/if}
    {#if descriptor.requiresRestart}
      <p class="setting__restart">Takes effect after a restart</p>
    {/if}
  </div>

  <div class="setting__control">
    {#if descriptor.kind === 'Boolean'}
      <input
        id={inputId}
        type="checkbox"
        class="setting__switch"
        checked={value === true}
        onchange={(event) => onchange(event.currentTarget.checked)}
      />
    {:else if descriptor.kind === 'Choice'}
      <select
        id={inputId}
        class="setting__select"
        value={String(value ?? '')}
        onchange={(event) => onchange(event.currentTarget.value)}
      >
        {#each descriptor.choices as choice}
          <option value={choice.value}>{choice.label}</option>
        {/each}
      </select>
    {:else if descriptor.kind === 'Slider'}
      <input
        id={inputId}
        type="range"
        class="setting__slider"
        min={descriptor.minimum ?? 0}
        max={descriptor.maximum ?? 1}
        step={sliderStep}
        value={Number(value ?? 0)}
        oninput={(event) => onchange(Number(event.currentTarget.value))}
      />
      <span class="setting__value">{Number(value ?? 0).toFixed(sliderStep < 1 ? 2 : 0)}</span>
    {:else if descriptor.kind === 'Integer'}
      <input
        id={inputId}
        type="number"
        class="setting__number"
        min={descriptor.minimum ?? undefined}
        max={descriptor.maximum ?? undefined}
        value={Number(value ?? 0)}
        onchange={(event) => onchange(Number(event.currentTarget.value))}
      />
    {:else if descriptor.kind === 'Color'}
      <input
        id={inputId}
        type="color"
        class="setting__color"
        value={String(value ?? '#000000')}
        oninput={(event) => onchange(event.currentTarget.value)}
      />
      <span class="setting__value setting__value--mono">{String(value ?? '')}</span>
    {:else}
      <!-- Text, Hotkey, and Path all render as text for now. Hotkey capture and a
           folder picker need host round-trips and arrive with their own settings pages. -->
      <input
        id={inputId}
        type="text"
        class="setting__text-input"
        value={String(value ?? '')}
        onchange={(event) => onchange(event.currentTarget.value)}
      />
    {/if}
  </div>
</div>

<style>
  .setting {
    display: flex;
    align-items: center;
    gap: var(--cy-space-4);
    padding: var(--cy-space-3) 0;
    border-bottom: 1px solid var(--cy-border-divider);
  }

  .setting:last-child {
    border-bottom: none;
  }

  .setting__text {
    flex: 1;
    min-width: 0;
  }

  .setting__label {
    display: block;
    font-size: var(--cy-text-title);
    color: var(--cy-fg-primary);
    cursor: pointer;
  }

  .setting__description,
  .setting__restart {
    margin: 2px 0 0;
    font-size: var(--cy-text-subtitle);
    color: var(--cy-fg-secondary);
  }

  .setting__restart {
    color: var(--cy-fg-tertiary);
    font-style: italic;
  }

  .setting__control {
    display: flex;
    align-items: center;
    gap: var(--cy-space-2);
    flex-shrink: 0;
  }

  .setting__value {
    min-width: 3.5ch;
    font-size: var(--cy-text-subtitle);
    color: var(--cy-fg-secondary);
    text-align: right;
  }

  .setting__value--mono {
    font-family: var(--cy-font-mono);
  }

  .setting__select,
  .setting__number,
  .setting__text-input {
    padding: 4px 8px;
    border: 1px solid var(--cy-border-panel);
    border-radius: var(--cy-radius-chip);
    background: var(--cy-bg-chip);
    color: var(--cy-fg-primary);
    font-family: inherit;
    font-size: var(--cy-text-subtitle);
    user-select: text;
  }

  .setting__number {
    width: 6rem;
  }

  .setting__text-input {
    width: 12rem;
  }

  .setting__slider {
    width: 10rem;
    accent-color: var(--cy-accent);
  }

  .setting__switch {
    width: 18px;
    height: 18px;
    accent-color: var(--cy-accent);
    cursor: pointer;
  }

  .setting__color {
    width: 36px;
    height: 24px;
    padding: 0;
    border: 1px solid var(--cy-border-panel);
    border-radius: var(--cy-radius-chip);
    background: none;
    cursor: pointer;
  }
</style>
