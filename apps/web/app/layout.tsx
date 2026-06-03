import type { Metadata } from "next";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages } from "next-intl/server";
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
 * - `NextIntlClientProvider` (i18n) — locale resuelto en server via cookie
 *   `aethra.locale` o `Accept-Language` (ver `i18n.ts`).
 * - `ThemeProvider` (next-themes) para light/dark/branded.
 * - `Toaster` (sonner) global para feedback de acciones.
 *
 * `suppressHydrationWarning` en `<html>` es requerido por next-themes para
 * evitar el warning cuando el tema persistido del cliente difiere del SSR.
 *
 * El `lang` del `<html>` se setea con el locale detectado para que screen
 * readers, accesibilidad y SEO reflejen el idioma activo.
 */
export default async function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  const locale = await getLocale();
  const messages = await getMessages();

  return (
    <html
      lang={locale}
      className={`${interFont.variable} ${monoFont.variable} h-full antialiased`}
      suppressHydrationWarning
    >
      <body className="min-h-full flex flex-col bg-background text-foreground font-sans">
        <NextIntlClientProvider locale={locale} messages={messages}>
          <ThemeProvider>
            {children}
            <Toaster richColors position="bottom-right" closeButton />
          </ThemeProvider>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
