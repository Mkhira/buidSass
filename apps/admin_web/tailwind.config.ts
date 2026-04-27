import type { Config } from "tailwindcss";

/**
 * T004: Tailwind config consuming tokens from packages/design_system/tokens.css
 * (Constitution Principle 7 — primary #1F6F5F, secondary #2FA084, accent #6FCF97,
 * neutral #EEEEEE) plus brand-overlay semantics. RTL-aware: shadcn's logical
 * properties are preferred — `tools/lint/no-physical-margins.ts` enforces.
 */
const config: Config = {
  darkMode: "class",
  content: [
    "./pages/**/*.{js,ts,jsx,tsx,mdx}",
    "./components/**/*.{js,ts,jsx,tsx,mdx}",
    "./app/**/*.{js,ts,jsx,tsx,mdx}",
    "./lib/**/*.{js,ts,jsx,tsx}",
    "./stories/**/*.{js,ts,jsx,tsx,mdx}",
  ],
  theme: {
    extend: {
      colors: {
        background: "hsl(var(--background))",
        foreground: "hsl(var(--foreground))",
        primary: {
          DEFAULT: "var(--color-primary)",
          foreground: "hsl(var(--primary-foreground))",
        },
        secondary: {
          DEFAULT: "var(--color-secondary)",
          foreground: "hsl(var(--secondary-foreground))",
        },
        accent: {
          DEFAULT: "var(--color-accent)",
          foreground: "hsl(var(--accent-foreground))",
        },
        neutral: {
          DEFAULT: "var(--color-neutral)",
        },
        brand: {
          success: "hsl(var(--brand-success, 142 71% 45%))",
          warning: "hsl(var(--brand-warning, 38 92% 50%))",
          error: "hsl(var(--brand-error, 0 84% 60%))",
          info: "hsl(var(--brand-info, 217 91% 60%))",
        },
        muted: {
          DEFAULT: "hsl(var(--muted))",
          foreground: "hsl(var(--muted-foreground))",
        },
        border: "hsl(var(--border))",
        input: "hsl(var(--input))",
        ring: "hsl(var(--ring))",
        destructive: {
          DEFAULT: "hsl(var(--destructive))",
          foreground: "hsl(var(--destructive-foreground))",
        },
        card: {
          DEFAULT: "hsl(var(--card))",
          foreground: "hsl(var(--card-foreground))",
        },
        popover: {
          DEFAULT: "hsl(var(--popover))",
          foreground: "hsl(var(--popover-foreground))",
        },
      },
      spacing: {
        "ds-xs": "var(--spacing-xs)",
        "ds-sm": "var(--spacing-sm)",
        "ds-md": "var(--spacing-md)",
        "ds-lg": "var(--spacing-lg)",
        "ds-xl": "var(--spacing-xl)",
      },
      borderRadius: {
        lg: "var(--radius, 0.5rem)",
        md: "calc(var(--radius, 0.5rem) - 2px)",
        sm: "calc(var(--radius, 0.5rem) - 4px)",
      },
    },
  },
  plugins: [],
};

export default config;
