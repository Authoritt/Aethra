"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { ApiError, api } from "@/lib/api";
import type { DeploymentDetail } from "@/lib/types";

const TERMINAL_STATUSES = new Set([
  "Completed",
  "Succeeded",
  "Failed",
  "Cancelled",
  "Canceled",
  "Error",
]);

/**
 * Polling minimalista del deployment cada 2s hasta llegar a un status
 * terminal; cuando cambia, dispara router.refresh() para que el server
 * component renderice el detalle final.
 */
export function DeploymentLivePoll({ deploymentId }: { deploymentId: string }) {
  const router = useRouter();
  const [lastStatus, setLastStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    let timeoutId: ReturnType<typeof setTimeout> | null = null;

    async function tick() {
      try {
        const detail = await api<DeploymentDetail>(
          `/api/deployments/${encodeURIComponent(deploymentId)}`,
        );
        if (cancelled) return;
        setLastStatus(detail.status);
        if (TERMINAL_STATUSES.has(detail.status)) {
          router.refresh();
          return;
        }
        setError(null);
      } catch (e) {
        if (cancelled) return;
        if (e instanceof ApiError) {
          const body = e.body as
            | { message?: string; detail?: string }
            | undefined;
          setError(body?.message ?? body?.detail ?? `Error ${e.status}`);
        } else {
          setError(e instanceof Error ? e.message : "Error desconocido");
        }
      }
      if (!cancelled) {
        timeoutId = setTimeout(tick, 2000);
      }
    }

    void tick();

    return () => {
      cancelled = true;
      if (timeoutId !== null) clearTimeout(timeoutId);
    };
  }, [deploymentId, router]);

  return (
    <div className="flex flex-col items-end gap-1 text-xs text-emerald-300">
      <span className="inline-flex items-center gap-1.5">
        <span className="size-1.5 animate-pulse rounded-full bg-emerald-400" />
        Live · {lastStatus ?? "..."}
      </span>
      {error && (
        <span className="rounded border border-rose-500/30 bg-rose-500/10 px-2 py-0.5 text-[10px] text-rose-300">
          {error}
        </span>
      )}
    </div>
  );
}
