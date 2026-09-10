import tsParser from '@typescript-eslint/parser';
import vueParser from 'vue-eslint-parser';
import vue from 'eslint-plugin-vue';

export default [
  ...vue.configs['flat/essential'],
  {
    ignores: ['dist/**', 'node_modules/**', 'coverage/**']
  },
  {
    files: ['src/**/*.ts', '*.ts'],
    languageOptions: {
      parser: tsParser,
      parserOptions: {
        ecmaVersion: 'latest',
        sourceType: 'module'
      }
    },
    rules: {
      'no-constant-condition': 'error',
      'no-debugger': 'error',
      'no-duplicate-imports': 'error'
    }
  },
  {
    files: ['src/**/*.vue'],
    languageOptions: {
      parser: vueParser,
      parserOptions: {
        parser: tsParser,
        ecmaVersion: 'latest',
        sourceType: 'module',
        extraFileExtensions: ['.vue']
      }
    },
    rules: {
      'no-constant-condition': 'error',
      'no-debugger': 'error',
      'no-duplicate-imports': 'error',
      'vue/multi-word-component-names': ['error', { ignores: ['Header'] }],
      'vue/no-restricted-syntax': ['error',
        { selector: 'ConditionalExpression ConditionalExpression', message: 'Use computed values or explicit branching instead of nested template ternaries.' },
        { selector: 'Identifier[name="$event"]', message: 'Use a named event handler instead of an inline $event expression.' }
      ]
    }
  }
];

