import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

/**
 * Helper para componer clases Tailwind con merge inteligente.
 * Convención shadcn-style. Resuelve conflictos cuando dos utilidades pisan la
 * misma propiedad: `cn("p-2", "p-4")` → `"p-4"`.
 */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
