import { StatusPill, type StatusPillVariant } from "@/components/ui/status-pill";
import type { ApiKeySummary } from "@/lib/types";

export type ApiKeyStatus = "active" | "expired" | "revoked";

export function deriveStatus(
  key: ApiKeySummary,
  now: Date = new Date(),
): ApiKeyStatus {
  if (key.revokedAt) return "revoked";
  if (key.expiresAt) {
    const expires = new Date(key.expiresAt);
    if (
      !Number.isNaN(expires.getTime()) &&
      expires.getTime() <= now.getTime()
    ) {
      return "expired";
    }
  }
  return "active";
}

const MAP: Record<ApiKeyStatus, { variant: StatusPillVariant; label: string }> = {
  active: { variant: "success", label: "Activa" },
  expired: { variant: "muted", label: "Expirada" },
  revoked: { variant: "destructive", label: "Revocada" },
};

export function ApiKeyStatusPill({ status }: { status: ApiKeyStatus }) {
  return (
    <StatusPill variant={MAP[status].variant}>{MAP[status].label}</StatusPill>
  );
}
