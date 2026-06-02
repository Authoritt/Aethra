"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Loader2 } from "lucide-react";
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
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError, api } from "@/lib/api";
import type {
  SetCustomDomainRequest,
  SetCustomDomainResponse,
} from "@/lib/types";

export function CustomDomainForm({
  instanceId,
  initialDomain,
}: {
  instanceId: string;
  initialDomain: string | null;
}) {
  const router = useRouter();
  const [domain, setDomain] = useState(initialDomain ?? "");
  const [busy, setBusy] = useState(false);
  const [confirmClear, setConfirmClear] = useState(false);

  async function submit(payload: string | null) {
    setBusy(true);
    try {
      const body: SetCustomDomainRequest = { customDomain: payload };
      const response = await api<SetCustomDomainResponse>(
        `/api/instances/${encodeURIComponent(instanceId)}/custom-domain`,
        { method: "POST", body: JSON.stringify(body) },
      );
      setDomain(response.customDomain ?? "");
      toast.success(
        response.customDomain
          ? "Custom domain guardado"
          : "Custom domain limpio: ahora se usa el auto-hostname",
      );
      router.refresh();
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
      setBusy(false);
      setConfirmClear(false);
    }
  }

  function onSave(e: React.FormEvent) {
    e.preventDefault();
    const trimmed = domain.trim();
    void submit(trimmed.length > 0 ? trimmed : null);
  }

  return (
    <form onSubmit={onSave} className="flex flex-col gap-2">
      <Label className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
        Custom domain
      </Label>
      <div className="flex gap-2">
        <Input
          value={domain}
          onChange={(e) => setDomain(e.target.value)}
          placeholder="app.mi-cliente.com"
          className="font-mono text-xs"
        />
        <Button type="submit" disabled={busy}>
          {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
          Guardar
        </Button>
        <Button
          type="button"
          variant="outline"
          onClick={() => setConfirmClear(true)}
          disabled={busy || (!initialDomain && !domain)}
        >
          Limpiar
        </Button>
      </div>

      <Dialog open={confirmClear} onOpenChange={setConfirmClear}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Quitar custom domain</DialogTitle>
            <DialogDescription>
              Las requests volverán a resolverse por el auto-hostname.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setConfirmClear(false)}>
              Cancelar
            </Button>
            <Button
              variant="destructive"
              onClick={() => {
                setDomain("");
                void submit(null);
              }}
              disabled={busy}
            >
              Quitar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </form>
  );
}
