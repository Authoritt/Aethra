"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Loader2, Pause, Play } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, api } from "@/lib/api";

export function EnableDisableButtons({
  monitorId,
  isEnabled,
}: {
  monitorId: string;
  isEnabled: boolean;
}) {
  const router = useRouter();
  const [loading, setLoading] = useState(false);

  async function toggle() {
    setLoading(true);
    try {
      const path = isEnabled
        ? `/api/monitors/${monitorId}/disable`
        : `/api/monitors/${monitorId}/enable`;
      await api(path, { method: "POST" });
      toast.success(isEnabled ? "Monitor deshabilitado" : "Monitor habilitado");
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { detail?: string; Message?: string } | undefined)
              ?.detail ??
            (e.body as { Message?: string } | undefined)?.Message ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }

  return (
    <Button variant="outline" size="sm" onClick={toggle} disabled={loading}>
      {loading ? (
        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
      ) : isEnabled ? (
        <Pause className="mr-2 h-4 w-4" />
      ) : (
        <Play className="mr-2 h-4 w-4" />
      )}
      {isEnabled ? "Deshabilitar" : "Habilitar"}
    </Button>
  );
}
