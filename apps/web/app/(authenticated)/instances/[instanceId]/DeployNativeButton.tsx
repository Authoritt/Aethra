"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Boxes, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, api } from "@/lib/api";

/**
 * F13 — dispara el deploy NATIVO multi-contenedor de la instancia (un contenedor por servicio
 * del template). Solo visible si el template define Services.
 */
export function DeployNativeButton({
  instanceId,
  hostname,
}: {
  instanceId: string;
  hostname?: string | null;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function deploy() {
    setBusy(true);
    try {
      const r = await api<{ healthy: boolean; services: string[] }>(
        `/api/instances/${encodeURIComponent(instanceId)}/deploy-native`,
        {
          method: "POST",
          body: JSON.stringify(hostname ? { hostname } : {}),
        },
      );
      toast.success(
        `Deploy nativo OK · ${r.services.length} servicio(s)${r.healthy ? " · healthy" : ""}`,
      );
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? ((e.body as { detail?: string; message?: string } | undefined)
              ?.detail ??
            (e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`)
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Button type="button" onClick={deploy} disabled={busy}>
      {busy ? (
        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
      ) : (
        <Boxes className="mr-2 h-4 w-4" />
      )}
      Deploy nativo
    </Button>
  );
}
