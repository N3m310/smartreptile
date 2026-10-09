import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 8081,
    host: true,
    // The copy deck lives in `app/lib/l10n` and is imported from `src/i18n/strings.ts` as `?raw` — one deck,
    // two clients (`TC-I-15`). The dev server refuses to serve files outside its root unless told to.
    fs: { allow: ['..'] }
  }
})

