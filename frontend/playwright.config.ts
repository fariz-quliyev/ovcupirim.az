import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end configuration.
 *
 * These tests drive a real browser against the real API and a real PostgreSQL. That is the point:
 * everything below the browser is already covered by 672 unit and integration tests, and what those
 * cannot prove is that eleven steps in sequence actually work for a person. The blocker that reached
 * Phase 7 was exactly this shape — every layer green, the journey broken.
 *
 * Both servers are started by Playwright, so `npx playwright test` is the whole command. The API
 * must be reachable on 5080 with a migrated database; CI provides one as a service container.
 */
export default defineConfig({
  testDir: './e2e',
  // Clears what previous runs created, so the queues these tests read are theirs alone.
  globalSetup: './e2e/global-setup.ts',
  // Sequential by default: these tests share one database and several of them move a listing
  // through moderation, so parallel workers would fight over the same queues.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  // These are multi-step journeys against a real API that decodes and re-encodes images; 60s is
  // not enough for the ones that register, create, illustrate and submit in sequence.
  timeout: 120_000,
  expect: { timeout: 10_000 },

  reporter: process.env['CI'] ? [['list'], ['html', { open: 'never' }]] : [['list']],

  use: {
    baseURL: 'http://localhost:5173',
    // Kept on the first retry only: a trace for every green run is noise, and for a red one it is
    // the difference between a guess and an answer.
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    locale: 'az-AZ',
  },

  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],

  webServer: [
    {
      // The API in Development: the OTP probe and the seeded taxonomy both depend on it.
      command: 'dotnet run --project ../backend/src/Ovcuprim.Api --no-launch-profile --urls http://localhost:5080',
      url: 'http://localhost:5080/health',
      timeout: 180_000,
      reuseExistingServer: !process.env['CI'],
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
      },
    },
    {
      // A production build rather than the dev server: Vite's on-demand transformation stalls for
      // seconds under sustained driving, which shows up as tests that "sometimes" fail waiting for
      // a button. This is also closer to what a visitor actually loads.
      command: 'npm run build && npm run preview -- --port 5173 --strictPort',
      url: 'http://localhost:5173',
      timeout: 180_000,
      // Never reused, unlike the API above. A dev server left running on this port answers just as
      // readily as the preview build and Playwright cannot tell them apart, so reuse quietly swaps
      // out the thing under test — including React's development-only double-invoked effects, which
      // behave differently from what a visitor loads. --strictPort turns a squatted port into a
      // loud failure rather than a silent substitution.
      reuseExistingServer: false,
      env: {
        VITE_API_BASE_URL: 'http://localhost:5080/api/v1',
      },
    },
  ],
})
