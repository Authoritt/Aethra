import { StatusPill, getStatusVariant } from "@/components/ui/status-pill";

/**
 * Status pill para Deployment (F4/F9.3): queued -> pulling -> healthcheck ->
 * swapping -> completed/failed.
 */
const LABELS: Record<string, string> = {
  queued: "En cola",
  pulling: "Descargando imagen",
  starting: "Arrancando",
  healthcheck: "Healthcheck",
  swapping: "Cambiando tráfico",
  completed: "Completado",
  failed: "Falló",
  cancelled: "Cancelado",
};

export interface DeploymentStatusPillProps {
  status: string;
  className?: string;
}

export function DeploymentStatusPill({
  status,
  className,
}: DeploymentStatusPillProps) {
  const variant = getStatusVariant(status);
  const label = LABELS[status.toLowerCase()] ?? status;
  return <StatusPill variant={variant} className={className}>{label}</StatusPill>;
}
