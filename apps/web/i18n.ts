import { cookies, headers } from "next/headers";
import { getRequestConfig } from "next-intl/server";

/**
 * Locales soportados por Aethra. `es` (castellano) es default, `en` es fallback.
 *
 * Detección por request en este orden:
 * 1. Cookie `aethra.locale` seteada por el `LanguageToggle` del topbar.
 * 2. Header `Accept-Language` del navegador (si empieza con `en` → `en`).
 * 3. Default `es`.
 *
 * No usamos `[locale]` en la URL — explícitamente decidido en F9.8: la URL no
 * cambia entre idiomas. Esto requiere `force-dynamic` o `dynamicIO` en pages
 * que lean cookies/headers (la mayoría ya lo hacen).
 */
export const SUPPORTED_LOCALES = ["es", "en"] as const;
export type SupportedLocale = (typeof SUPPORTED_LOCALES)[number];
export const DEFAULT_LOCALE: SupportedLocale = "es";
export const LOCALE_COOKIE = "aethra.locale";

function pickLocale(cookieValue: string | undefined, acceptLanguage: string | null): SupportedLocale {
  if (cookieValue && (SUPPORTED_LOCALES as readonly string[]).includes(cookieValue)) {
    return cookieValue as SupportedLocale;
  }
  if (acceptLanguage) {
    // Aceptamos prefijos en-*, "english", etc. Simple para no traer una lib extra.
    const lower = acceptLanguage.toLowerCase();
    if (lower.startsWith("en") || lower.includes(",en")) return "en";
    if (lower.startsWith("es") || lower.includes(",es")) return "es";
  }
  return DEFAULT_LOCALE;
}

export default getRequestConfig(async () => {
  const cookieStore = await cookies();
  const headerList = await headers();
  const cookieLocale = cookieStore.get(LOCALE_COOKIE)?.value;
  const acceptLanguage = headerList.get("accept-language");
  const locale = pickLocale(cookieLocale, acceptLanguage);

  const messages = (await import(`./messages/${locale}.json`)).default;

  return { locale, messages };
});
