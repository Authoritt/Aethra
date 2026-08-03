"use client";

import { useTranslations } from "next-intl";
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

export function DeleteServiceButton({
  serviceId,
  slug,
  bindingsCount,
}: {
  serviceId: string;
  slug: string;
  bindingsCount: number;
}) {
  const c = useTranslations("common");
  const d = useTranslations("components.destructive");
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(false);

  async function onConfirm() {
    setLoading(true);
    try {
      await api(`/api/services/${serviceId}`, { method: "DELETE" });
      toast.success(`Servicio "${slug}" eliminado`);
      router.push("/services");
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
      setLoading(false);
    }
  }

  return (
    <>
      <Button variant="destructive" onClick={() => setOpen(true)}>
        <Trash2 className="mr-2 h-4 w-4" />
        {c("delete")}
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Eliminar servicio "{slug}"</DialogTitle>
            <DialogDescription>
              {d("service_warning")}
              stopped.
              {bindingsCount > 0 ? (
                <>
                  {" "}
                  <strong className="text-destructive">
                    Atención: este servicio tiene {bindingsCount} binding(s)
                    activo(s)
                  </strong>{" "}
                  que se verán afectados.
                </>
              ) : null}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setOpen(false)}>
              {c("cancel")}
            </Button>
            <Button
              variant="destructive"
              onClick={onConfirm}
              disabled={loading}
            >
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              {c("delete")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
