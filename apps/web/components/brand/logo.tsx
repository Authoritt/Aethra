import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

type LogoVariant = "mark" | "lockup";

interface LogoProps {
  variant?: LogoVariant;
  className?: string;
  size?: number;
  /**
   * Si `true`, muestra debajo del wordmark un subtítulo. El texto se controla
   * con `subtitleText` (típicamente proveniente de `useTranslations("logo")`).
   * Si no se pasa `subtitleText`, cae al default español (mantiene compat
   * para llamadores que aún no han migrado a i18n).
   */
  showSubtitle?: boolean;
  subtitleText?: ReactNode;
}

/**
 * Logo de Aethra.
 *
 * Concepto: un nodo central (el "central" / hub) conectado por líneas a tres
 * satélites en triángulo. Refleja la arquitectura literal del producto
 * (1 controladora + N satélites) y la metáfora "AetherEye" (un ojo unificado
 * que observa toda la infraestructura).
 *
 * Es monocromático con `currentColor` para integrarse con el tema activo.
 * Aplica `text-primary` (emerald) o `text-foreground` (zinc) según el caller.
 *
 * - `variant="mark"`: solo el símbolo (square, ideal para avatar y sidebar collapsado).
 * - `variant="lockup"`: símbolo + wordmark "Aethra" inline.
 *
 * El componente es server-compatible (no usa hooks). Para evitar atarlo a
 * `next-intl` y forzar a sus callers server a convertirse en client, el
 * subtítulo se recibe ya resuelto via `subtitleText`.
 */
export function Logo({
  variant = "lockup",
  className,
  size = 24,
  showSubtitle = false,
  subtitleText,
}: LogoProps) {
  const mark = (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 64 64"
      fill="none"
      width={size}
      height={size}
      aria-hidden="true"
      className="shrink-0"
    >
      <g
        stroke="currentColor"
        strokeWidth="2.4"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M32 12 L52 44 L12 44 Z" strokeOpacity="0.5" />
        <line x1="32" y1="32" x2="32" y2="12" />
        <line x1="32" y1="32" x2="52" y2="44" />
        <line x1="32" y1="32" x2="12" y2="44" />
      </g>
      <circle cx="32" cy="32" r="5" fill="currentColor" />
      <circle cx="32" cy="12" r="3.4" fill="currentColor" />
      <circle cx="52" cy="44" r="3.4" fill="currentColor" />
      <circle cx="12" cy="44" r="3.4" fill="currentColor" />
    </svg>
  );

  if (variant === "mark") {
    return <span className={cn("inline-flex text-primary", className)}>{mark}</span>;
  }

  return (
    <span
      className={cn("inline-flex items-center gap-2.5 text-foreground", className)}
    >
      <span className="text-primary">{mark}</span>
      <span className="flex flex-col leading-tight">
        <span className="font-semibold tracking-tight text-base">Aethra</span>
        {showSubtitle && (
          <span className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
            {subtitleText ?? "plataforma unificada"}
          </span>
        )}
      </span>
    </span>
  );
}
