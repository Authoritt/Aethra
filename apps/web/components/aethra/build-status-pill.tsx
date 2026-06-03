"use client";

import { useTranslations } from "next-intl";
import { StatusPill, getStatusVariant } from "@/components/ui/status-pill";

/**
 * Status pill para Build (F4/F9.3): queued -> building -> completed/failed.
 */
const KEYS = ["queued", "building", "completed", "failed", "cancelled"] as const;
type BuildKey = (typeof KEYS)[number];

export interface BuildStatusPillProps {
  status: string;
  className?: string;
}

export function BuildStatusPill({ status, className }: BuildStatusPillProps) {
  const t = useTranslations("status.build");
  const variant = getStatusVariant(status);
  const key = status.toLowerCase() as BuildKey;
  const label = (KEYS as readonly string[]).includes(key) ? t(key) : status;
  return (
    <StatusPill variant={variant} className={className}>
      {label}
    </StatusPill>
  );
}
