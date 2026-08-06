<script lang="ts">
  interface Props {
    /** Text produced by a command, shown verbatim. */
    text: string;
    oncopy: () => void;
  }

  const { text, oncopy }: Props = $props();

  let copied = $state(false);

  function copy(): void {
    oncopy();
    copied = true;

    // Reverts so the affordance is available again, and so the label does not lie if
    // the user copies something else afterwards.
    setTimeout(() => (copied = false), 1400);
  }
</script>

<div class="output">
  <pre class="output__text">{text}</pre>

  <div class="output__actions">
    <button class="output__button" type="button" onclick={copy}>
      {copied ? 'Copied' : 'Copy'}
    </button>
    <span class="output__hint">Esc to dismiss</span>
  </div>
</div>

<style>
  .output {
    display: flex;
    flex-direction: column;
    min-height: 0;
    flex: 1;
  }

  .output__text {
    margin: 0;
    padding: var(--cy-space-4) var(--cy-space-5);
    overflow: auto;
    flex: 1;

    font-family: var(--cy-font-mono);
    font-size: var(--cy-text-title);
    line-height: 1.5;
    color: var(--cy-fg-primary);

    /* Command output is the one thing in the launcher a user genuinely wants to
       select by hand, so it opts out of the application-wide selection lock. */
    user-select: text;
    cursor: text;

    /* Long lines wrap rather than forcing the panel to scroll sideways, which would
       hide the start of every line. */
    white-space: pre-wrap;
    word-break: break-word;
  }

  .output__actions {
    display: flex;
    align-items: center;
    gap: var(--cy-space-3);
    padding: var(--cy-space-2) var(--cy-space-5) var(--cy-space-3);
    border-top: 1px solid var(--cy-border-divider);
    flex-shrink: 0;
  }

  .output__button {
    padding: 4px 12px;
    border: 1px solid var(--cy-border-panel);
    border-radius: var(--cy-radius-chip);
    background: var(--cy-bg-chip);
    color: var(--cy-fg-primary);
    font-family: inherit;
    font-size: var(--cy-text-subtitle);
    cursor: pointer;
    transition: background-color var(--cy-duration-fast) var(--cy-ease);
  }

  .output__button:hover {
    background: var(--cy-bg-row-hover);
  }

  .output__hint {
    font-size: var(--cy-text-label);
    color: var(--cy-fg-tertiary);
  }
</style>
