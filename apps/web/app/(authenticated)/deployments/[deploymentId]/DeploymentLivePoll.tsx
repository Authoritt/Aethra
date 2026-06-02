"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { toast } from "sonner";
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
export function DeploymentLivePoll({
  deploymentId,
}: {
  deploymentId: string;
}) {
  const router = useRouter();
  const [lastStatus, setLastStatus] = useState<string | null>(null);

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
      } catch (e) {
        if (cancelled) return;
        const msg =
          e instanceof ApiError
            ? (e.body as { message?: string; detail?: string } | undefined)
                ?.message ?? `Error ${e.status}`
            : e instanceof Error
              ? e.message
              : "Error desconocido";
        toast.error(msg);
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
    <span className="inline-flex items-center gap-1.5 rounded-full border border-info/30 bg-info/10 px-2.5 py-0.5 text-xs font-medium text-info-foreground">
      <span className="relative inline-flex h-2 w-2">
        <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-info opacity-60" />
        <span className="relative inline-flex h-2 w-2 rounded-full bg-info" />
      </span>
      Live · {lastStatus ?? "…"}
    </span>
  );
}
