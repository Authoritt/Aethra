"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Loader2, RotateCcw } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, api } from "@/lib/api";
import type { DeploymentSummary } from "@/lib/types";

export function ReleaseTargetActions({
  buildId,
  instanceId,
  status,
}: {
  buildId: string;
  instanceId: string;
  status: string;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const failed = status.toLowerCase() === "failed";

  async function redeploy() {
    setBusy(true);
    try {
      const response = await api<DeploymentSummary>(
        `/api/deployments/builds/${encodeURIComponent(buildId)}/instances/${encodeURIComponent(instanceId)}/trigger`,
        { method: "POST" },
      );
      toast.success(failed ? "Retry disparado" : "Redeploy disparado");
      router.push(`/deployments/${response.id}`);
      router.refresh();
    } catch (e) {
      setBusy(false);
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ?? `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    }
  }

  return (
    <Button type="button" size="sm" variant="outline" onClick={redeploy} disabled={busy}>
      {busy ? (
        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
      ) : (
        <RotateCcw className="mr-2 h-4 w-4" />
      )}
      {failed ? "Retry" : "Redeploy"}
    </Button>
  );
}
