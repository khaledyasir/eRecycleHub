import { defineConfig, devices } from "@playwright/test";

/**
 * End-to-end tests run against a running stack (API on :5080, web on :3000) whose database was created with
 * `dotnet run -- migrate` and `dotnet run -- seed-dev` (see scripts/). They never touch a production database:
 * the API refuses seed-dev outside Development/Testing, and the well-known test accounts exist only in that seed.
 * Tests that change data create their own records, so they can be re-run on the same database.
 */
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1, // the specs share one dev database; serial keeps bid and wallet assertions deterministic
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [["list"], ["html", { open: "never", outputFolder: "playwright-report" }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:3000",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    { name: "desktop", use: { ...devices["Desktop Chrome"] }, testIgnore: /responsive\.spec/ },
    { name: "phone", use: { ...devices["Pixel 7"] }, testMatch: /responsive\.spec/ },
  ],
});
