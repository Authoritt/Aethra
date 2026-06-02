import type { CloudflareZoneStatus } from "@/lib/types";

const STYLES: Record<CloudflareZoneStatus, string> = {
  Active: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300",
  Pending: "border-amber-500/40 bg-amber-500/10 text-amber-300",
  Suspended: "border-rose-500/40 bg-rose-500/10 text-rose-300",
  Unknown: "border-zinc-700 bg-zinc-800/40 text-zinc-300",
};

const DOTS: Record<CloudflareZoneStatus, string> = {
  Active: "bg-emerald-400",
  Pending: "bg-amber-400",
  Suspended: "bg-rose-400",
  Unknown: "bg-zinc-400",
};

export function ZoneStatusPill({ status }: { status: CloudflareZoneStatus }) {
  const klass = STYLES[status] ?? STYLES.Unknown;
  const dot = DOTS[status] ?? DOTS.Unknown;
  return (
    <span
      className={`inline-flex shrink-0 items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-medium ${klass}`}
    >
      <span className={`size-1.5 rounded-full ${dot}`} />
      {status}
    </span>
  );
}
