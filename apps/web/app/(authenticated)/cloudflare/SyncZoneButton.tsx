"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Loader2, RefreshCw } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, api } from "@/lib/api";

export function SyncZoneButton({ zoneId }: { zoneId: string }) {
  const router = useRouter();
  const [loading, setLoading] = useState(false);

  async function onClick() {
    setLoading(true);
    try {
      await api(`/api/cloudflare/zones/${zoneId}/sync`, { method: "POST" });
      toast.success("Zona sincronizada");
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ??
            (e.body as { detail?: string } | undefined)?.detail ??
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
        <RefreshCw className="mr-2 h-4 w-4" />
      )}
      Sincronizar
    </Button>
  );
}
