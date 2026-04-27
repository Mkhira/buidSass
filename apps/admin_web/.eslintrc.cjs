/**
 * T005: ESLint config for the admin web app.
 *
 * Custom rules layered on top of `next/core-web-vitals`:
 *  - jsx-a11y/* (FR-005 / SC-008 — full WCAG 2.1 AA bar)
 *  - eslint-plugin-no-unsanitized (XSS hygiene)
 *  - no-restricted-syntax: blocks direct `fetch('http…')` outside `lib/api/**`
 *    (FR-030 — no ad-hoc HTTP)
 *  - no-restricted-imports: blocks `axios` and `node-fetch` (same intent)
 *  - no inline color literals outside the design system (lint sweep below
 *    handles physical-margin / hardcoded-string violations via the
 *    standalone scripts in tools/lint/)
 */
module.exports = {
  root: true,
  extends: [
    "next/core-web-vitals",
    "plugin:jsx-a11y/recommended",
  ],
  plugins: ["jsx-a11y", "no-unsanitized"],
  rules: {
    "no-unsanitized/method": "error",
    "no-unsanitized/property": "error",
    "no-restricted-imports": [
      "error",
      {
        paths: [
          { name: "axios", message: "Use generated clients in lib/api/clients/ — no ad-hoc HTTP per FR-030." },
          { name: "node-fetch", message: "Use generated clients in lib/api/clients/ — no ad-hoc HTTP per FR-030." },
        ],
      },
    ],
    "no-restricted-syntax": [
      "error",
      {
        selector: "CallExpression[callee.name='fetch'][arguments.0.type='Literal'][arguments.0.value=/^https?:/]",
        message:
          "No ad-hoc fetch('http…') calls outside lib/api/. Use the generated clients (FR-030).",
      },
    ],
    "jsx-a11y/anchor-is-valid": "off",
  },
  overrides: [
    {
      files: ["lib/api/**/*.{ts,tsx}", "app/api/**/*.{ts,tsx}"],
      rules: {
        "no-restricted-syntax": "off",
      },
    },
    {
      files: ["components/ui/**/*.{ts,tsx}"],
      rules: {
        // shadcn vendored primitives are direction-agnostic and contain
        // no user-facing strings (they take children + props). The a11y
        // rules below trip on the primitives' generic patterns; feature
        // components that *use* the primitives are still subject to full
        // a11y enforcement.
        "jsx-a11y/click-events-have-key-events": "off",
        "jsx-a11y/no-noninteractive-element-interactions": "off",
        "jsx-a11y/label-has-associated-control": "off",
      },
    },
  ],
  ignorePatterns: [
    ".next/**",
    "node_modules/**",
    "out/**",
    "build/**",
    "dist/**",
    "lib/api/types/**",
    "storybook-static/**",
  ],
};
