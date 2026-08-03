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

export function DeleteZoneButton({
  zoneId,
  name,
  recordsCount,
}: {
  zoneId: string;
  name: string;
  recordsCount: number;
}) {
  const c = useTranslations("common");
  const d = useTranslations("components.destructive");
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
        {d("zone_title")}
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{d("zone_title")} "{name}"</DialogTitle>
            <DialogDescription>
              {d("zone_warning")}
              cifrado y el seguimiento local.
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
