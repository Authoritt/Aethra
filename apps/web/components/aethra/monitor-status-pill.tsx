"use client";

import { useTranslations } from "next-intl";
import { StatusPill, type StatusPillVariant } from "@/components/ui/status-pill";

/**
 * Status pill para Monitor (F6): Up/Down/Degraded/Unknown + Disabled.
 */
const VARIANTS: Record<string, StatusPillVariant> = {
  up: "success",
  down: "destructive",
  degraded: "warning",
  unknown: "muted",
  disabled: "muted",
};

const KEYS = ["up", "down", "degraded", "unknown", "disabled"] as const;

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
  const t = useTranslations("status.monitor");
  if (!enabled) {
    return (
      <StatusPill variant="muted" className={className}>
        {t("disabled")}
      </StatusPill>
    );
  }
  const key = status.toLowerCase();
  const variant = VARIANTS[key] ?? ("info" as StatusPillVariant);
  const label = (KEYS as readonly string[]).includes(key)
    ? t(key as (typeof KEYS)[number])
    : status;
  return (
    <StatusPill variant={variant} className={className}>
      {label}
    </StatusPill>
  );
}
