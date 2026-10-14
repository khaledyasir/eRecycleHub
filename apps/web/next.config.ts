import type { NextConfig } from "next";

/** Server-side address of the ASP.NET Core API. The browser never talks to it directly: /api/* is proxied (same origin for cookies + CSRF). */
const apiUrl = process.env.API_INTERNAL_URL ?? "http://localhost:5080";

const isDev = process.env.NODE_ENV !== "production";

// Next.js inline bootstrap scripts need 'unsafe-inline' without nonces; everything else is same-origin only.
const csp = [
  "default-src 'self'",
  `script-src 'self' 'unsafe-inline'${isDev ? " 'unsafe-eval'" : ""}`,
  "style-src 'self' 'unsafe-inline'",
  "img-src 'self' data: blob:",
  "font-src 'self'",
  "connect-src 'self'" + (isDev ? " ws:" : ""),
  "frame-src 'self' https://www.google.com https://www.youtube.com https://www.youtube-nocookie.com",
  "frame-ancestors 'none'",
  "base-uri 'self'",
  "form-action 'self'",
  "object-src 'none'",
].join("; ");

// Legacy URL redirects live in src/proxy.ts (they need case-sensitive matching, which next.config redirects don't offer).
const nextConfig: NextConfig = {
  poweredByHeader: false,
  // ponytail: Cache Components off — almost every page reads per-visitor cookies (session, country) and live prices. Revisit for static shells.
  cacheComponents: false,
  turbopack: {
    // What next-intl/plugin does, without loading the plugin (it pulls in @swc/core, whose native cache check fails on this machine).
    resolveAlias: { "next-intl/config": "./src/i18n/request.ts" },
    rules: {
      "*.css": { loaders: ["@tailwindcss/turbopack"], as: "*.css" },
    },
  },
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${apiUrl}/api/:path*` }];
  },
  async headers() {
    return [
      {
        source: "/((?!api/).*)",
        headers: [
          { key: "Content-Security-Policy", value: csp },
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
          { key: "X-Frame-Options", value: "DENY" },
          { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=()" },
          ...(isDev ? [] : [{ key: "Strict-Transport-Security", value: "max-age=31536000; includeSubDomains" }]),
        ],
      },
    ];
  },
};

export default nextConfig;
