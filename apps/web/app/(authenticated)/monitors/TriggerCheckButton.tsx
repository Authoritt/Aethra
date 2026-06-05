"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Loader2, Play } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, api } from "@/lib/api";
import type { MonitorCheckDto } from "@/lib/types";

export function TriggerCheckButton({ monitorId }: { monitorId: string }) {
  const router = useRouter();
  const [loading, setLoading] = useState(false);

  async function onClick() {
    setLoading(true);
    try {
      const result = await api<MonitorCheckDto>(
        `/api/monitors/${monitorId}/trigger`,
        { method: "POST" },
      );
      toast.success(
        `Check disparado: ${result.status} · ${result.httpStatusCode ?? "—"} · ${
          result.latencyMs === null ? "—" : `${result.latencyMs}ms`
        }`,
      );
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
    <Button variant="outline" size="sm" onClick={onClick} disabled={loading}>
      {loading ? (
        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
      ) : (
        <Play className="mr-2 h-4 w-4" />
      )}
      Probar ahora
    </Button>
  );
}
