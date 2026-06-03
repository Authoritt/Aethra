"use client";

import { useTranslations } from "next-intl";
import { StatusPill, type StatusPillVariant } from "@/components/ui/status-pill";

/**
 * Status pill para VM (F2): Pending/Connected/Disconnected.
 */
const VARIANTS: Record<string, StatusPillVariant> = {
  pending: "warning",
  connected: "success",
  disconnected: "destructive",
  unknown: "muted",
};

const KEYS = ["pending", "connected", "disconnected", "unknown"] as const;

export interface VmStatusPillProps {
  status: string;
  className?: string;
}

export function VmStatusPill({ status, className }: VmStatusPillProps) {
  const t = useTranslations("status.vm");
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
