import react from "@vitejs/plugin-react";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  resolve: { alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) } },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    include: ["src/**/*.test.{ts,tsx}"],
    env: { NEXT_PUBLIC_DISPLAY_TIMEZONE: "Asia/Amman", NEXT_PUBLIC_CURRENCY: "JOD" },
    css: false,
    server: { deps: { inline: ["next-intl"] } }, // so its "next/server" import resolves the way Next resolves it
  },
});
