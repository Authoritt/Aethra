"use client";

import { useTranslations } from "next-intl";
import { StatusPill, type StatusPillVariant } from "@/components/ui/status-pill";

/**
 * Status pill para certificado TLS (F3): none/pending/issued/failed/renewing.
 */
const VARIANTS: Record<string, StatusPillVariant> = {
  none: "muted",
  pending: "warning",
  issued: "success",
  failed: "destructive",
  renewing: "running",
};

const KEYS = ["none", "pending", "issued", "failed", "renewing"] as const;

export interface CertStatusPillProps {
  status: string;
  className?: string;
}

export function CertStatusPill({ status, className }: CertStatusPillProps) {
  const t = useTranslations("status.cert");
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
