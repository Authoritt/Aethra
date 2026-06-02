import { StatusPill, type StatusPillVariant } from "@/components/ui/status-pill";

/**
 * Status pill para certificado TLS (F3): none/pending/issued/failed/renewing.
 */
const MAP: Record<string, { variant: StatusPillVariant; label: string }> = {
  none: { variant: "muted", label: "Sin TLS" },
  pending: { variant: "warning", label: "Pendiente" },
  issued: { variant: "success", label: "Emitido" },
  failed: { variant: "destructive", label: "Falló" },
  renewing: { variant: "running", label: "Renovando" },
};

export interface CertStatusPillProps {
  status: string;
  className?: string;
}

export function CertStatusPill({ status, className }: CertStatusPillProps) {
  const key = status.toLowerCase();
  const entry = MAP[key] ?? { variant: "info" as StatusPillVariant, label: status };
  return (
    <StatusPill variant={entry.variant} className={className}>
      {entry.label}
    </StatusPill>
  );
}
