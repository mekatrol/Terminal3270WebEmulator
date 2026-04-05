/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_APP_CONFIG_URL?: string
  readonly VITE_TERMINAL_API_BASE_URL?: string
  readonly VITE_TERMINAL_DEV_PROXY_TARGET?: string
  readonly VITE_TERMINAL_WS_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
