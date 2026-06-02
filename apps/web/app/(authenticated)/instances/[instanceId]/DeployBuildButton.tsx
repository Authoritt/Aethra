"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Loader2, Rocket } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, api } from "@/lib/api";
import type { DeploymentDetail } from "@/lib/types";

export function DeployBuildButton({
  buildId,
  instanceId,
  disabled,
}: {
  buildId: string;
  instanceId: string;
  disabled?: boolean;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function deploy() {
    setBusy(true);
    try {
      const response = await api<DeploymentDetail>(
        `/api/deployments/builds/${encodeURIComponent(buildId)}/instances/${encodeURIComponent(instanceId)}/trigger`,
        { method: "POST" },
      );
      toast.success("Deployment disparado");
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
      onClick={deploy}
      disabled={busy || disabled}
      title={
        disabled
          ? "Solo builds con imagen y status Completed se pueden desplegar."
          : undefined
      }
    >
      {busy ? (
        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
      ) : (
        <Rocket className="mr-2 h-4 w-4" />
      )}
      Deploy
    </Button>
  );
}
