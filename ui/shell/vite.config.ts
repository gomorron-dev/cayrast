import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

export default defineConfig({
  plugins: [svelte()],

  build: {
    outDir: 'dist',
    emptyOutDir: true,

    // The host serves this through WebView2's virtual host mapping, which always
    // runs a current Chromium. Targeting anything older would only cost bundle size
    // and startup parse time for transpilation nobody needs.
    target: 'esnext',

    // Source maps ship in the build. This is an open-source project whose users are
    // invited to write themes and modules, and the inspector is how they learn the
    // UI; a few hundred KB of maps is a fair trade for that.
    sourcemap: true,

    rollupOptions: {
      output: {
        // Stable, hash-free names. The shell is loaded from a fixed origin with no
        // CDN in front of it, so content hashing buys nothing and makes the host's
        // Content-Security-Policy harder to pin.
        entryFileNames: 'assets/[name].js',
        chunkFileNames: 'assets/[name].js',
        assetFileNames: 'assets/[name].[ext]',
      },
    },
  },

  server: {
    port: 5173,
    strictPort: true,
  },
});
