import { StatusPill, getStatusVariant } from "@/components/ui/status-pill";

/**
 * Status pill para Build (F4/F9.3): queued -> building -> completed/failed.
 */
const LABELS: Record<string, string> = {
  queued: "En cola",
  building: "Construyendo",
  completed: "Completado",
  failed: "Falló",
  cancelled: "Cancelado",
};

export interface BuildStatusPillProps {
  status: string;
  className?: string;
}

export function BuildStatusPill({ status, className }: BuildStatusPillProps) {
  const variant = getStatusVariant(status);
  const label = LABELS[status.toLowerCase()] ?? status;
  return <StatusPill variant={variant} className={className}>{label}</StatusPill>;
}
