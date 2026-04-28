/**
 * T046: Storybook config — Next.js framework + locale/theme toolbar.
 *
 * Stories live next to components under `**\/*.stories.tsx`. Visual
 * regression (T047 / FR-028h) runs Playwright snapshots against the
 * built static Storybook in CI.
 */
import type { StorybookConfig } from "@storybook/nextjs";
import path from "node:path";

const config: StorybookConfig = {
  stories: ["../components/**/*.stories.@(ts|tsx)", "../app/**/*.stories.@(ts|tsx)"],
  addons: ["@storybook/addon-essentials", "@storybook/addon-a11y", "@storybook/addon-themes"],
  framework: { name: "@storybook/nextjs", options: {} },
  staticDirs: ["../public"],
  webpackFinal: async (config) => {
    if (config.resolve) {
      config.resolve.alias = {
        ...(config.resolve.alias ?? {}),
        "@": path.resolve(__dirname, ".."),
      };
    }
    return config;
  },
};

export default config;
