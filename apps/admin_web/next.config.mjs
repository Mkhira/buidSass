import createNextIntlPlugin from "next-intl/plugin";

const withNextIntl = createNextIntlPlugin("./lib/i18n/server.ts");

/** @type {import('next').NextConfig} */
const nextConfig = {
  // T009: standalone build for the multi-stage Dockerfile
  output: "standalone",
  reactStrictMode: true,
  // FR-028c — connect-src host whitelist is enforced by middleware.ts
  // (T026 + T032a). Image hosts here are the storage abstraction's
  // signed-URL host pattern.
  images: {
    remotePatterns: [
      {
        protocol: "https",
        hostname: "**",
      },
    ],
  },
};

export default withNextIntl(nextConfig);
