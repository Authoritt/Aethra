"use client";

import { useTranslations } from "next-intl";
import { StatusPill, getStatusVariant } from "@/components/ui/status-pill";

/**
 * Status pill para Deployment (F4/F9.3): queued -> pulling -> healthcheck ->
 * swapping -> completed/failed.
 */
const KEYS = [
  "queued",
  "pulling",
  "starting",
  "healthcheck",
  "swapping",
  "completed",
  "failed",
  "cancelled",
] as const;
type DeploymentKey = (typeof KEYS)[number];

export interface DeploymentStatusPillProps {
  status: string;
  className?: string;
}

export function DeploymentStatusPill({
  status,
  className,
}: DeploymentStatusPillProps) {
  const t = useTranslations("status.deployment");
  const variant = getStatusVariant(status);
  const key = status.toLowerCase() as DeploymentKey;
  const label = (KEYS as readonly string[]).includes(key) ? t(key) : status;
  return (
    <StatusPill variant={variant} className={className}>
      {label}
    </StatusPill>
  );
}
