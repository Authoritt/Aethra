"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, RotateCcw } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, api } from "@/lib/api";
import type { DeploymentSummary } from "@/lib/types";

export function RollbackDeploymentButton({
  deploymentId,
  disabled,
}: {
  deploymentId: string;
  disabled: boolean;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function rollback() {
    if (disabled) return;
    const confirmed = window.confirm(
      "Rollback this App Environment to the selected completed deployment?",
    );
    if (!confirmed) return;

    setBusy(true);
    try {
      const response = await api<DeploymentSummary>(
        `/api/deployments/${encodeURIComponent(deploymentId)}/rollback`,
        { method: "POST" },
      );
      toast.success("Rollback disparado");
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
    <Button
      type="button"
      size="sm"
      variant="outline"
      onClick={rollback}
      disabled={disabled || busy}
    >
      {busy ? (
        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
      ) : (
        <RotateCcw className="mr-2 h-4 w-4" />
      )}
      Rollback
    </Button>
  );
}
