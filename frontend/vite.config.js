import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The ASP.NET API runs on :5111; proxying /api avoids CORS in development.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5111',
    },
  },
})
