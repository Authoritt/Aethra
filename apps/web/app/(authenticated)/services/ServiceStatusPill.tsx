import { StatusPill, type StatusPillVariant } from "@/components/ui/status-pill";
import type { ManagedServiceStatus } from "@/lib/types";

const MAP: Record<
  ManagedServiceStatus,
  { variant: StatusPillVariant; label: string }
> = {
  provisioning: { variant: "running", label: "Aprovisionando" },
  ready: { variant: "success", label: "Listo" },
  failed: { variant: "destructive", label: "Falló" },
  stopped: { variant: "muted", label: "Detenido" },
};

export function ServiceStatusPill({
  status,
}: {
  status: ManagedServiceStatus;
}) {
  const entry = MAP[status] ?? {
    variant: "muted" as StatusPillVariant,
    label: status,
  };
  return <StatusPill variant={entry.variant}>{entry.label}</StatusPill>;
}
