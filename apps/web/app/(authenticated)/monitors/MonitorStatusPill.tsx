import type { MonitorStatus } from "@/lib/types";

const STATUS_STYLES: Record<MonitorStatus, { box: string; dot: string }> = {
  Up: {
    box: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300",
    dot: "bg-emerald-400",
  },
  Down: {
    box: "border-rose-500/40 bg-rose-500/10 text-rose-300",
    dot: "bg-rose-400",
  },
  Degraded: {
    box: "border-amber-500/40 bg-amber-500/10 text-amber-300",
    dot: "bg-amber-400",
  },
  Unknown: {
    box: "border-zinc-700 bg-zinc-800/40 text-zinc-400",
    dot: "bg-zinc-500",
  },
};

export function MonitorStatusPill({
  status,
  disabled,
  className,
}: {
  status: MonitorStatus;
  disabled?: boolean;
  className?: string;
}) {
  if (disabled) {
    return (
      <span
        className={`inline-flex items-center gap-1.5 rounded-full border border-zinc-700 bg-zinc-800/40 px-2.5 py-0.5 text-[11px] font-medium text-zinc-500 ${className ?? ""}`}
      >
        <span className="size-1.5 rounded-full bg-zinc-600" />
        deshabilitado
      </span>
    );
  }
  const styles = STATUS_STYLES[status] ?? STATUS_STYLES.Unknown;
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-medium ${styles.box} ${className ?? ""}`}
    >
      <span className={`size-1.5 rounded-full ${styles.dot}`} />
      {status}
    </span>
  );
}
