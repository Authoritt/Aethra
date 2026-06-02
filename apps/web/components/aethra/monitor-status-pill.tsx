import { StatusPill, type StatusPillVariant } from "@/components/ui/status-pill";

/**
 * Status pill para Monitor (F6): Up/Down/Degraded/Unknown + Disabled.
 */
const MAP: Record<string, { variant: StatusPillVariant; label: string }> = {
  up: { variant: "success", label: "Up" },
  down: { variant: "destructive", label: "Down" },
  degraded: { variant: "warning", label: "Degradado" },
  unknown: { variant: "muted", label: "Desconocido" },
  disabled: { variant: "muted", label: "Deshabilitado" },
};

export interface MonitorStatusPillProps {
  status: string;
  enabled?: boolean;
  className?: string;
}

export function MonitorStatusPill({
  status,
  enabled = true,
  className,
}: MonitorStatusPillProps) {
  if (!enabled) {
    return (
      <StatusPill variant="muted" className={className}>
        Deshabilitado
      </StatusPill>
    );
  }
  const key = status.toLowerCase();
  const entry = MAP[key] ?? { variant: "info" as StatusPillVariant, label: status };
  return (
    <StatusPill variant={entry.variant} className={className}>
      {entry.label}
    </StatusPill>
  );
}
