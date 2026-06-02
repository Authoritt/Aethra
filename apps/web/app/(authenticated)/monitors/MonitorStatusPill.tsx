import { MonitorStatusPill as DomainPill } from "@/components/aethra/monitor-status-pill";
import type { MonitorStatus } from "@/lib/types";

export function MonitorStatusPill({
  status,
  disabled,
  className,
}: {
  status: MonitorStatus;
  disabled?: boolean;
  className?: string;
}) {
  return (
    <DomainPill
      status={String(status)}
      enabled={!disabled}
      className={className}
    />
  );
}
