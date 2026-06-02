import { Inter, JetBrains_Mono } from "next/font/google";

/**
 * Fonts del DS Aethra.
 *
 * - `Inter` para sans (UI general).
 * - `JetBrains Mono` para mono (logs, código, IDs).
 *
 * Las exponemos como CSS vars (`--font-sans`, `--font-mono`) consumidas por
 * `tailwind.config.ts` (fontFamily) y por `globals.css` (body fallback).
 */
export const interFont = Inter({
  subsets: ["latin"],
  variable: "--font-sans",
  display: "swap",
});

export const monoFont = JetBrains_Mono({
  subsets: ["latin"],
  variable: "--font-mono",
  display: "swap",
});
