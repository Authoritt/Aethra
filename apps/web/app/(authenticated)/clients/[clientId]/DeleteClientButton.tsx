"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError, api } from "@/lib/api";

function errMsg(e: unknown): string {
  return e instanceof ApiError
    ? ((e.body as { message?: string; detail?: string } | undefined)?.message ??
        (e.body as { detail?: string } | undefined)?.detail ?? `Error ${e.status}`)
    : "Error";
}

/** Botón de borrado de cliente (DELETE /api/clients/{id}?force=true). */
export function DeleteClientButton({ clientId, displayName }: { clientId: string; displayName: string }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function del() {
    if (!confirm(`¿Borrar el cliente "${displayName}"? Esto lo quita de Aethra junto con sus instancias.`)) {
      return;
    }
    setBusy(true);
    try {
      await api(`/api/clients/${encodeURIComponent(clientId)}?force=true`, { method: "DELETE" });
      toast.success("Cliente borrado");
      router.push("/clients");
      router.refresh();
    } catch (e) {
      toast.error(errMsg(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Button type="button" variant="ghost" size="sm" onClick={del} disabled={busy}
      className="text-destructive hover:bg-destructive/10 hover:text-destructive">
      {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Trash2 className="mr-2 h-4 w-4" />} Borrar cliente
    </Button>
  );
}
