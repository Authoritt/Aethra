import type { ManagedServiceStatus } from "@/lib/types";

const STYLES: Record<ManagedServiceStatus, string> = {
  provisioning: "border-amber-500/40 bg-amber-500/10 text-amber-300",
  ready: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300",
  failed: "border-rose-500/40 bg-rose-500/10 text-rose-300",
  stopped: "border-slate-500/40 bg-slate-500/10 text-slate-300",
};

const DOTS: Record<ManagedServiceStatus, string> = {
  provisioning: "bg-amber-400",
  ready: "bg-emerald-400",
  failed: "bg-rose-400",
  stopped: "bg-slate-400",
};

export function ServiceStatusPill({
  status,
}: {
  status: ManagedServiceStatus;
}) {
  const klass = STYLES[status] ?? STYLES.provisioning;
  const dot = DOTS[status] ?? DOTS.provisioning;
  return (
    <span
      className={`inline-flex shrink-0 items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-medium ${klass}`}
    >
      <span className={`size-1.5 rounded-full ${dot}`} />
      {status}
    </span>
  );
}
