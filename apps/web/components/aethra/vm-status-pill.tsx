import { StatusPill, type StatusPillVariant } from "@/components/ui/status-pill";

/**
 * Status pill para VM (F2): Pending/Connected/Disconnected.
 */
const MAP: Record<string, { variant: StatusPillVariant; label: string }> = {
  pending: { variant: "warning", label: "Pendiente" },
  connected: { variant: "success", label: "Conectada" },
  disconnected: { variant: "destructive", label: "Desconectada" },
  unknown: { variant: "muted", label: "Desconocido" },
};

export interface VmStatusPillProps {
  status: string;
  className?: string;
}

export function VmStatusPill({ status, className }: VmStatusPillProps) {
  const key = status.toLowerCase();
  const entry = MAP[key] ?? { variant: "info" as StatusPillVariant, label: status };
  return (
    <StatusPill variant={entry.variant} className={className}>
      {entry.label}
    </StatusPill>
  );
}
