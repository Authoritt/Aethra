import { StatusPill, type StatusPillVariant } from "@/components/ui/status-pill";
import type { CloudflareZoneStatus } from "@/lib/types";

const MAP: Record<
  CloudflareZoneStatus,
  { variant: StatusPillVariant; label: string }
> = {
  Active: { variant: "success", label: "Activa" },
  Pending: { variant: "warning", label: "Pendiente" },
  Suspended: { variant: "destructive", label: "Suspendida" },
  Unknown: { variant: "muted", label: "Desconocida" },
};

export function ZoneStatusPill({ status }: { status: CloudflareZoneStatus }) {
  const entry = MAP[status] ?? {
    variant: "muted" as StatusPillVariant,
    label: status,
  };
  return <StatusPill variant={entry.variant}>{entry.label}</StatusPill>;
}
