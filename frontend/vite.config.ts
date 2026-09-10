import { fileURLToPath, URL } from 'node:url'

import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    proxy: {
      // Keeps the browser same-origin in development; production uses VITE_API_BASE_URL.
      '/api': {
        target: 'http://localhost:5080',
        changeOrigin: true,
      },
    },
  },
  build: {
    target: 'es2022',
    sourcemap: true,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: true,
    // Component tests only. The browser journeys under e2e/ are Playwright's, and they use the
    // same .spec.ts suffix — without this Vitest collects them, cannot run them, and reports five
    // failed suites that have nothing to do with the code.
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
  },
})
