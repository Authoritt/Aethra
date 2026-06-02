"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Loader2, Trash2 } from "lucide-react";
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

export function DeleteZoneButton({
  zoneId,
  name,
  recordsCount,
}: {
  zoneId: string;
  name: string;
  recordsCount: number;
}) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(false);

  function onClick() {
    if (recordsCount > 0) {
      toast.error(
        `La zona tiene ${recordsCount} record(s) gestionados. Eliminalos primero.`,
      );
      return;
    }
    setOpen(true);
  }

  async function onConfirm() {
    setLoading(true);
    try {
      await api(`/api/cloudflare/zones/${zoneId}`, { method: "DELETE" });
      toast.success(`Zona "${name}" eliminada`);
      router.push("/cloudflare");
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
      setLoading(false);
    }
  }

  return (
    <>
      <Button variant="destructive" size="sm" onClick={onClick}>
        <Trash2 className="mr-2 h-4 w-4" />
        Eliminar zona
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Eliminar zona "{name}"</DialogTitle>
            <DialogDescription>
              No se elimina la zona en Cloudflare, solo se quita el token
              cifrado y el seguimiento local.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setOpen(false)}>
              Cancelar
            </Button>
            <Button
              variant="destructive"
              onClick={onConfirm}
              disabled={loading}
            >
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              Eliminar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
