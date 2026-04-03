import { defineConfig } from 'oxlint'

export default defineConfig({
  env: {
    browser: true,
  },

  categories: {
    correctness: 'error',
    suspicious: 'error',
  },
})