import type { ApiKeySummary } from "@/lib/types";

export type ApiKeyStatus = "active" | "expired" | "revoked";

export function deriveStatus(key: ApiKeySummary, now: Date = new Date()): ApiKeyStatus {
  if (key.revoked_at) return "revoked";
  if (key.expires_at) {
    const expires = new Date(key.expires_at);
    if (!Number.isNaN(expires.getTime()) && expires.getTime() <= now.getTime()) {
      return "expired";
    }
  }
  return "active";
}

const STYLES: Record<ApiKeyStatus, string> = {
  active: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300",
  expired: "border-slate-500/40 bg-slate-500/10 text-slate-300",
  revoked: "border-rose-500/40 bg-rose-500/10 text-rose-300",
};

const DOTS: Record<ApiKeyStatus, string> = {
  active: "bg-emerald-400",
  expired: "bg-slate-400",
  revoked: "bg-rose-400",
};

const LABELS: Record<ApiKeyStatus, string> = {
  active: "active",
  expired: "expired",
  revoked: "revoked",
};

export function ApiKeyStatusPill({ status }: { status: ApiKeyStatus }) {
  const klass = STYLES[status];
  const dot = DOTS[status];
  return (
    <span
      className={`inline-flex shrink-0 items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-medium ${klass}`}
    >
      <span className={`size-1.5 rounded-full ${dot}`} />
      {LABELS[status]}
    </span>
  );
}
