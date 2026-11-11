import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './e2e', fullyParallel: false, workers: 1, timeout: 60000,
  expect: { timeout: 15000 },
  use: { headless: true, screenshot: 'only-on-failure' },
  reporter: 'list',
});
