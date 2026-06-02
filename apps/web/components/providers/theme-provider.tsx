"use client";

import { ThemeProvider as NextThemesProvider } from "next-themes";
import type { ComponentProps } from "react";

/**
 * Wrapper de `next-themes` con los defaults de Aethra:
 * - 3 temas activos: `light`, `dark`, `branded`.
 * - Default `system` (Window matchMedia decide light/dark).
 * - Attribute `class` para que Tailwind `.dark` aplique.
 * - `enableSystem` permite "Auto" en el toggle.
 *
 * Los consumers usan `useTheme()` de `next-themes` para leer/cambiar.
 */
export function ThemeProvider({
  children,
  ...props
}: ComponentProps<typeof NextThemesProvider>) {
  return (
    <NextThemesProvider
      attribute="class"
      defaultTheme="system"
      enableSystem
      themes={["light", "dark", "branded"]}
      disableTransitionOnChange
      {...props}
    >
      {children}
    </NextThemesProvider>
  );
}
