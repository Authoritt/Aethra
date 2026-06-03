"use client";

import { useTranslations } from "next-intl";
import { StatusPill, type StatusPillVariant } from "@/components/ui/status-pill";
import type { ManagedServiceStatus } from "@/lib/types";

const VARIANTS: Record<ManagedServiceStatus, StatusPillVariant> = {
  provisioning: "running",
  ready: "success",
  failed: "destructive",
  stopped: "muted",
};

export function ServiceStatusPill({
  status,
}: {
  status: ManagedServiceStatus;
}) {
  const t = useTranslations("status.service");
  const variant = VARIANTS[status] ?? ("muted" as StatusPillVariant);
  const knownKeys: ManagedServiceStatus[] = [
    "provisioning",
    "ready",
    "failed",
    "stopped",
  ];
  const label = knownKeys.includes(status) ? t(status) : status;
  return <StatusPill variant={variant}>{label}</StatusPill>;
}
