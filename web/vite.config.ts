import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// The API runs on Kestrel (http://localhost:5080). The proxy keeps the browser's Host header
// (changeOrigin: false) so the backend can resolve the tenant from e.g. afn.localhost.
const apiTarget = process.env.FIDOCO_API_URL ?? 'http://localhost:5080';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    host: true,
    allowedHosts: ['.localhost'],
    proxy: {
      '/api': { target: apiTarget, changeOrigin: false },
      '/hubs': { target: apiTarget, changeOrigin: false, ws: true },
    },
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
});
