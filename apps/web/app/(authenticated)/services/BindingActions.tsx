"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Loader2, RefreshCw, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { ApiError, api } from "@/lib/api";

export function BindingActions({
  bindingId,
  appLabel,
}: {
  bindingId: string;
  appLabel: string;
}) {
  const router = useRouter();
  const [loading, setLoading] = useState<"rotate" | "revoke" | null>(null);
  const [confirm, setConfirm] = useState<"rotate" | "revoke" | null>(null);

  async function execute(action: "rotate" | "revoke") {
    setLoading(action);
    try {
      if (action === "rotate") {
        await api(`/api/bindings/${bindingId}/rotate`, { method: "POST" });
        toast.success("Credenciales rotadas. Redeploy la application para aplicar.");
      } else {
        await api(`/api/bindings/${bindingId}`, { method: "DELETE" });
        toast.success("Binding revocado");
      }
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { detail?: string } | undefined)?.detail ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setLoading(null);
      setConfirm(null);
    }
  }

  return (
    <>
      <div className="flex items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => setConfirm("rotate")}
          disabled={loading !== null}
        >
          {loading === "rotate" ? (
            <Loader2 className="mr-2 h-4 w-4 animate-spin" />
          ) : (
            <RefreshCw className="mr-2 h-4 w-4" />
          )}
          Rotar
        </Button>
        <Button
          type="button"
          variant="destructive"
          size="sm"
          onClick={() => setConfirm("revoke")}
          disabled={loading !== null}
        >
          <Trash2 className="mr-2 h-4 w-4" />
          Revocar
        </Button>
      </div>

      <Dialog
        open={confirm !== null}
        onOpenChange={(o) => {
          if (!o) setConfirm(null);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              {confirm === "rotate"
                ? `Rotar credenciales de "${appLabel}"`
                : `Revocar el binding de "${appLabel}"`}
            </DialogTitle>
            <DialogDescription>
              {confirm === "rotate"
                ? "La application recibirá nuevas credenciales en su próximo deploy o restart."
                : "La application perderá acceso al servicio inmediatamente. El recurso (DB/cola/usuario) se eliminará."}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setConfirm(null)}>
              Cancelar
            </Button>
            <Button
              variant={confirm === "rotate" ? "default" : "destructive"}
              onClick={() => confirm && execute(confirm)}
              disabled={loading !== null}
            >
              {loading !== null ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              {confirm === "rotate" ? "Rotar" : "Revocar"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
