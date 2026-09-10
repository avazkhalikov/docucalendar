import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // In development the API runs on its own port; in production nginx serves both under one
    // origin, which is why the client always calls relative "/api/..." paths.
    proxy: {
      '/api': {
        target: 'http://localhost:5014',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: false,
  },
});
