"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, api } from "@/lib/api";

/** Desconecta el túnel gestionado (DELETE /api/cloudflare/tunnel). */
export function DeleteTunnelButton({ name }: { name: string }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function del() {
    if (!confirm(`¿Desconectar el túnel "${name}"? Aethra dejará de gestionar su ingress por API.`)) {
      return;
    }
    setBusy(true);
    try {
      await api("/api/cloudflare/tunnel", { method: "DELETE" });
      toast.success("Túnel desconectado");
      router.push("/cloudflare");
      router.refresh();
    } catch (e) {
      toast.error(
        e instanceof ApiError
          ? ((e.body as { message?: string } | undefined)?.message ?? `Error ${e.status}`)
          : "Error desconectando el túnel",
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <Button type="button" variant="ghost" size="sm" onClick={del} disabled={busy}
      className="text-destructive hover:bg-destructive/10 hover:text-destructive">
      {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Trash2 className="mr-2 h-4 w-4" />} Desconectar túnel
    </Button>
  );
}
