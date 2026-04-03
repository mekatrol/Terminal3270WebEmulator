import { globalIgnores } from 'eslint/config'
import { defineConfigWithVueTs, vueTsConfigs } from '@vue/eslint-config-typescript'
import pluginVue from 'eslint-plugin-vue'
import pluginVitest from '@vitest/eslint-plugin'
import pluginOxlint from 'eslint-plugin-oxlint'
import skipFormatting from 'eslint-config-prettier/flat'

// To allow more languages other than `ts` in `.vue` files, uncomment the following lines:
// import { configureVueProject } from '@vue/eslint-config-typescript'
// configureVueProject({ scriptLangs: ['ts', 'tsx'] })
// More info at https://github.com/vuejs/eslint-config-typescript/#advanced-setup

export default defineConfigWithVueTs(
  {
    name: 'app/files-to-lint',
    files: ['**/*.{vue,ts,mts,tsx}'],
  },

  globalIgnores(['**/dist/**', '**/dist-ssr/**', '**/coverage/**', 'eslint.config.ts']),

  ...pluginVue.configs['flat/essential'],
  vueTsConfigs.recommended,

  {
    ...pluginVitest.configs.recommended,
    files: ['src/**/__tests__/*'],
  },

  {
    rules: {
      'prefer-promise-reject-errors': 'error',
      'max-len': ['error', 200],

      quotes: [
        'error',
        'single',
        {
          avoidEscape: true,
          allowTemplateLiterals: false,
        },
      ],

      semi: ['error', 'always'],
      'comma-dangle': ['error', 'never'],

      '@typescript-eslint/explicit-function-return-type': 'error',
      '@typescript-eslint/no-var-requires': 'off',

      'no-debugger': process.env.NODE_ENV === 'production' ? 'error' : 'off',

      'array-element-newline': [
        'error',
        {
          ArrayExpression: 'consistent',
          ArrayPattern: { minItems: 3 },
        },
      ],

      'no-console':
        process.env.NODE_ENV === 'production'
          ? ['error', { allow: ['info', 'warn', 'error'] }]
          : ['warn', { allow: ['log', 'info', 'warn', 'error'] }],

      'no-unused-vars': 'off',
      'no-var': 'error',

      '@typescript-eslint/no-unused-vars': [
        process.env.NODE_ENV === 'production' ? 'error' : 'warn',
        {
          argsIgnorePattern: '^_.*$',
          varsIgnorePattern: '^_.*$',
          caughtErrorsIgnorePattern: '^_.*$',
        },
      ],
    },
  },

  ...pluginOxlint.buildFromOxlintConfigFile('./oxlint.config.ts'),

  skipFormatting,
)