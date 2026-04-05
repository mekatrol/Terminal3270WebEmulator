import { fileURLToPath, URL } from 'node:url'

import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const terminalProxyTarget = env.VITE_TERMINAL_DEV_PROXY_TARGET

  return {
    plugins: [
      vue(),
      vueDevTools(),
    ],
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
    server: terminalProxyTarget
      ? {
          proxy: {
            '/api/app-config': {
              target: terminalProxyTarget,
              changeOrigin: true,
              secure: false,
            },
            '/api/admin/sessions': {
              target: terminalProxyTarget,
              changeOrigin: true,
              secure: false,
            },
            '/ws/terminal': {
              target: terminalProxyTarget,
              ws: true,
              changeOrigin: true,
              secure: false,
            },
          },
        }
      : undefined,
  }
})
