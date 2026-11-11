import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5180, strictPort: true,
    proxy: {
      '/api': { target: 'http://127.0.0.1:5078' },
      '/hubs': { target: 'http://127.0.0.1:5078', ws: true },
    },
  },
});
