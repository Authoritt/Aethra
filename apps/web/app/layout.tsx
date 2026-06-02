import type { Metadata } from "next";
import { Toaster } from "sonner";
import { ThemeProvider } from "@/components/providers/theme-provider";
import { interFont, monoFont } from "./fonts";
import "./globals.css";

export const metadata: Metadata = {
  title: "Aethra",
  description:
    "Plataforma unificada de despliegue, proxy, TLS, monitoreo y operación.",
};

/**
 * Root layout — sin shell (sidebar/topbar viven en los route groups
 * `(authenticated)` y `(public)`). Aquí solo configuramos:
 * - Fuentes via CSS vars (Inter + JetBrains Mono).
 * - `ThemeProvider` (next-themes) para light/dark/branded.
 * - `Toaster` (sonner) global para feedback de acciones.
 *
 * `suppressHydrationWarning` en `<html>` es requerido por next-themes para
 * evitar el warning cuando el tema persistido del cliente difiere del SSR.
 */
export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html
      lang="es"
      className={`${interFont.variable} ${monoFont.variable} h-full antialiased`}
      suppressHydrationWarning
    >
      <body className="min-h-full flex flex-col bg-background text-foreground font-sans">
        <ThemeProvider>
          {children}
          <Toaster richColors position="bottom-right" closeButton />
        </ThemeProvider>
      </body>
    </html>
  );
}
