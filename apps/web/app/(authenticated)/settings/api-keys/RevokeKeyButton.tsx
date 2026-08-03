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

export function RevokeKeyButton({
  id,
  name,
  alreadyRevoked,
}: {
  id: string;
  name: string;
  alreadyRevoked: boolean;
}) {
  const c = useTranslations("common");
  const d = useTranslations("components.destructive");
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(false);

  if (alreadyRevoked) {
    return (
      <span className="text-[11px] uppercase tracking-wider text-muted-foreground">
        revocada
      </span>
    );
  }

  async function onConfirm() {
    setLoading(true);
    try {
      await api(`/api/identity/api-keys/${id}`, { method: "DELETE" });
      toast.success("API key revocada");
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
      setLoading(false);
      setOpen(false);
    }
  }

  return (
    <>
      <Button
        type="button"
        variant="destructive"
        size="sm"
        onClick={() => setOpen(true)}
      >
        <Trash2 className="mr-2 h-4 w-4" />
        Revocar
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Revocar API key "{name}"</DialogTitle>
            <DialogDescription>
              {d("apikey_warning")}
              {c("irreversible")}
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
              Revocar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
