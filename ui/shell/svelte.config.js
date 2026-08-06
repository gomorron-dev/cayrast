import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

export default {
  // Lets <script lang="ts"> and modern CSS work inside .svelte files.
  preprocess: vitePreprocess(),

  compilerOptions: {
    // Svelte 5 runes mode, explicitly. Without this the compiler infers the mode
    // per component, so a file that happens to use no runes silently falls back to
    // legacy reactivity and behaves differently from its neighbours.
    runes: true,
  },
};
