import vue from '@vitejs/plugin-vue';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5175,
    proxy: {
      '/api': 'http://127.0.0.1:5600',
      '/mcp': 'http://127.0.0.1:5600'
    }
  },
  test: {
    environment: 'jsdom'
  }
});

