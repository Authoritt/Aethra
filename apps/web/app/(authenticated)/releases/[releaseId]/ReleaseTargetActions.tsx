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
  deploymentId,
  instanceId,
  status,
}: {
  buildId: string;
  deploymentId: string;
  instanceId: string;
  status: string;
}) {
  const router = useRouter();
  const [busyAction, setBusyAction] = useState<"redeploy" | "rollback" | null>(null);
  const failed = status.toLowerCase() === "failed";
  const completed = status.toLowerCase() === "completed";

  async function redeploy() {
    setBusyAction("redeploy");
    try {
      const response = await api<DeploymentSummary>(
        `/api/deployments/builds/${encodeURIComponent(buildId)}/instances/${encodeURIComponent(instanceId)}/trigger`,
        { method: "POST" },
      );
      toast.success(failed ? "Retry disparado" : "Redeploy disparado");
      router.push(`/deployments/${response.id}`);
      router.refresh();
    } catch (e) {
      setBusyAction(null);
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

  async function rollback() {
    setBusyAction("rollback");
    try {
      const response = await api<DeploymentSummary>(
        `/api/deployments/${encodeURIComponent(deploymentId)}/rollback`,
        { method: "POST" },
      );
      toast.success("Rollback disparado");
      router.push(`/deployments/${response.id}`);
      router.refresh();
    } catch (e) {
      setBusyAction(null);
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
    <>
      {completed ? (
        <Button
          type="button"
          size="sm"
          variant="outline"
          onClick={rollback}
          disabled={busyAction !== null}
        >
          {busyAction === "rollback" ? (
            <Loader2 className="mr-2 h-4 w-4 animate-spin" />
          ) : (
            <RotateCcw className="mr-2 h-4 w-4" />
          )}
          Rollback
        </Button>
      ) : null}
      <Button
        type="button"
        size="sm"
        variant="outline"
        onClick={redeploy}
        disabled={busyAction !== null}
      >
        {busyAction === "redeploy" ? (
          <Loader2 className="mr-2 h-4 w-4 animate-spin" />
        ) : (
          <RotateCcw className="mr-2 h-4 w-4" />
        )}
        {failed ? "Retry" : "Redeploy"}
      </Button>
    </>
  );
}
