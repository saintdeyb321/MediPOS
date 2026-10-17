import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vitest/config'
import { contingencyPwa } from './build/pwa.js'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss(), contingencyPwa()],
  server: { host: '127.0.0.1', port: 5173, strictPort: true },
  test: {
    environment: 'jsdom',
    environmentOptions: { jsdom: { url: 'https://medipos.test/' } },
    setupFiles: './src/test/setup.ts',
    clearMocks: true,
  },
})
