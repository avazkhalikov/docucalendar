import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  // Every host serves this app under /calendar/ — docurest.com, calendar.docurest.com and each
  // white-label portal alike — so one build works everywhere. Absolute (not './'): relative asset
  // URLs would resolve against a deep link like /calendar/schedule/<id> and 404.
  base: '/calendar/',
  plugins: [react()],
  server: {
    port: 5173,
    // In development the API runs on its own port; in production nginx maps /calendar/api/ to it.
    // The rewrite here mirrors what nginx does, so the client code is identical in both.
    proxy: {
      '/calendar/api': {
        target: 'http://localhost:5014',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/calendar/, ''),
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: false,
  },
});
