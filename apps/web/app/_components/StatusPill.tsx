/**
 * Pill generica para mostrar un status string. Mapea status conocidos
 * (Build/Deploy/Instance lifecycle) a un color; cualquier valor desconocido
 * cae al estilo neutro. Sin emojis. Solo CSS de tailwind + un dot.
 */

const VARIANTS: Record<string, { ring: string; dot: string }> = {
  // verdes — terminal exitoso / vivo
  Completed: { ring: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300", dot: "bg-emerald-400" },
  Succeeded: { ring: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300", dot: "bg-emerald-400" },
  Success: { ring: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300", dot: "bg-emerald-400" },
  Active: { ring: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300", dot: "bg-emerald-400" },
  Running: { ring: "border-sky-500/40 bg-sky-500/10 text-sky-300", dot: "bg-sky-400 animate-pulse" },
  InProgress: { ring: "border-sky-500/40 bg-sky-500/10 text-sky-300", dot: "bg-sky-400 animate-pulse" },
  Building: { ring: "border-sky-500/40 bg-sky-500/10 text-sky-300", dot: "bg-sky-400 animate-pulse" },
  Deploying: { ring: "border-sky-500/40 bg-sky-500/10 text-sky-300", dot: "bg-sky-400 animate-pulse" },
  Pulling: { ring: "border-sky-500/40 bg-sky-500/10 text-sky-300", dot: "bg-sky-400 animate-pulse" },
  Pending: { ring: "border-zinc-700 bg-zinc-800/40 text-zinc-300", dot: "bg-zinc-400" },
  Queued: { ring: "border-zinc-700 bg-zinc-800/40 text-zinc-300", dot: "bg-zinc-400" },
  Failed: { ring: "border-rose-500/40 bg-rose-500/10 text-rose-300", dot: "bg-rose-400" },
  Error: { ring: "border-rose-500/40 bg-rose-500/10 text-rose-300", dot: "bg-rose-400" },
  Cancelled: { ring: "border-amber-500/40 bg-amber-500/10 text-amber-300", dot: "bg-amber-400" },
  Canceled: { ring: "border-amber-500/40 bg-amber-500/10 text-amber-300", dot: "bg-amber-400" },
  Stopped: { ring: "border-amber-500/40 bg-amber-500/10 text-amber-300", dot: "bg-amber-400" },
  Skipped: { ring: "border-zinc-700 bg-zinc-800/40 text-zinc-400", dot: "bg-zinc-500" },
};

const NEUTRAL = { ring: "border-zinc-700 bg-zinc-800/40 text-zinc-300", dot: "bg-zinc-500" };

export function StatusPill({
  status,
  size = "md",
}: {
  status: string;
  size?: "sm" | "md";
}) {
  const variant = VARIANTS[status] ?? NEUTRAL;
  const padding = size === "sm" ? "px-2 py-0.5 text-[10px]" : "px-2.5 py-0.5 text-[11px]";
  return (
    <span
      className={`inline-flex shrink-0 items-center gap-1.5 rounded-full border font-medium ${variant.ring} ${padding}`}
    >
      <span className={`size-1.5 rounded-full ${variant.dot}`} />
      {status}
    </span>
  );
}
