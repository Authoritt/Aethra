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

/**
 * Formatea bytes a la unidad binaria más legible (B/KB/MB/GB/TB/PB).
 * `formatBytes(1536)` → `"1.5 KB"`. Valores no finitos o <= 0 → `"0 B"`.
 */
export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB", "PB"];
  let i = 0;
  let n = bytes;
  while (n >= 1024 && i < units.length - 1) {
    n /= 1024;
    i++;
  }
  return `${n.toFixed(n >= 100 || i === 0 ? 0 : 1)} ${units[i]}`;
}
