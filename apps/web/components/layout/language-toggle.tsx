"use client";

import { useTransition } from "react";
import { useRouter } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { Languages } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

/**
 * Toggle de idioma — vive en el topbar al lado del ThemeToggle.
 *
 * Setea la cookie `aethra.locale` (path=/, max-age=1 año, SameSite=Lax) y
 * llama a `router.refresh()` para forzar re-render del server con el nuevo
 * locale. No requiere reload del navegador.
 *
 * No usamos `/es/` ni `/en/` en la URL — explícitamente decidido en F9.8.
 */

const LOCALE_COOKIE = "aethra.locale";
const COOKIE_MAX_AGE = 60 * 60 * 24 * 365; // 1 año

type AvailableLocale = "es" | "en";

const LABEL_KEYS: Record<AvailableLocale, "language_es" | "language_en"> = {
  es: "language_es",
  en: "language_en",
};

const FLAGS: Record<AvailableLocale, string> = {
  es: "🇪🇸",
  en: "🇬🇧",
};

export function LanguageToggle() {
  const t = useTranslations("topbar");
  const locale = useLocale() as AvailableLocale;
  const router = useRouter();
  const [pending, startTransition] = useTransition();

  function setLocale(next: AvailableLocale) {
    if (next === locale) return;
    document.cookie = `${LOCALE_COOKIE}=${next}; path=/; max-age=${COOKIE_MAX_AGE}; SameSite=Lax`;
    startTransition(() => router.refresh());
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          aria-label={t("language_toggle_aria")}
          disabled={pending}
        >
          <Languages className="size-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
          {t("language_label")}
        </DropdownMenuLabel>
        {(["es", "en"] as const).map((code) => (
          <DropdownMenuItem key={code} onSelect={() => setLocale(code)}>
            <span aria-hidden="true">{FLAGS[code]}</span>
            {t(LABEL_KEYS[code])}
            {locale === code && (
              <span className="ml-auto text-xs text-muted-foreground">●</span>
            )}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
