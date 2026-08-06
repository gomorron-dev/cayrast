import { mount } from 'svelte';
import App from './App.svelte';
import './styles/global.css';

/**
 * Frontend entry point.
 *
 * The host has already created and warmed this WebView by the time this runs, so
 * everything here is on the path that decides how quickly the launcher can first
 * appear. Keep it to mounting.
 */
const target = document.getElementById('app');

if (!target) {
  throw new Error('Missing #app mount point in index.html.');
}

export default mount(App, { target });
