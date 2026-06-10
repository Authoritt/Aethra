"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Loader2, RotateCw } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, api } from "@/lib/api";

export function RestartNativeButton({ instanceId }: { instanceId: string }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function restart() {
    setBusy(true);
    try {
      const response = await api<{ services: string[] }>(
        `/api/instances/${encodeURIComponent(instanceId)}/restart-native`,
        { method: "POST" },
      );
      toast.success(`Restart OK · ${response.services.length} servicio(s)`);
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
    <Button type="button" variant="secondary" onClick={restart} disabled={busy}>
      {busy ? (
        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
      ) : (
        <RotateCw className="mr-2 h-4 w-4" />
      )}
      Restart runtime
    </Button>
  );
}
