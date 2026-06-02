"use client";

import { useState } from "react";
import { Copy, KeyRound, Loader2 } from "lucide-react";
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
import type { RotateWebhookSecretResponse } from "@/lib/types";

export function RotateWebhookSecretButton({
  templateId,
}: {
  templateId: string;
}) {
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const [secret, setSecret] = useState<string | null>(null);

  async function rotate() {
    setLoading(true);
    try {
      const response = await api<RotateWebhookSecretResponse>(
        `/api/templates/${encodeURIComponent(templateId)}/rotate-webhook-secret`,
        { method: "POST" },
      );
      setSecret(response.webhookSecret);
      setConfirmOpen(false);
      toast.success("Webhook secret rotado");
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ?? `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }

  async function copy() {
    if (!secret) return;
    try {
      await navigator.clipboard.writeText(secret);
      toast.success("Copiado al portapapeles");
    } catch {
      toast.error("No se pudo copiar");
    }
  }

  return (
    <>
      <Button variant="outline" onClick={() => setConfirmOpen(true)}>
        <KeyRound className="mr-2 h-4 w-4" />
        Rotar webhook secret
      </Button>

      <Dialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Rotar webhook secret</DialogTitle>
            <DialogDescription>
              Rotar el webhook secret invalida el anterior inmediatamente.
              Tendrás que reconfigurar el provider Git (GitHub/Gitlab).
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setConfirmOpen(false)}>
              Cancelar
            </Button>
            <Button variant="destructive" onClick={rotate} disabled={loading}>
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              Rotar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog
        open={!!secret}
        onOpenChange={(open) => {
          if (!open) setSecret(null);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Nuevo webhook secret</DialogTitle>
            <DialogDescription>
              Copialo y configurarlo en tu provider Git. No volverá a mostrarse.
            </DialogDescription>
          </DialogHeader>
          {secret ? (
            <div className="rounded-md border border-border bg-card">
              <div className="flex items-center justify-between border-b border-border px-3 py-1.5">
                <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                  Secret
                </span>
                <Button variant="outline" size="sm" onClick={copy}>
                  <Copy className="mr-2 h-4 w-4" />
                  Copiar
                </Button>
              </div>
              <pre className="overflow-x-auto whitespace-nowrap px-3 py-2 font-mono text-xs text-foreground">
                {secret}
              </pre>
            </div>
          ) : null}
          <DialogFooter>
            <Button onClick={() => setSecret(null)}>Cerrar</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
