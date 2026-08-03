"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useRouter } from "next/navigation";
import { GitBranch, Loader2, Save, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError, api } from "@/lib/api";

function errMsg(e: unknown): string {
  return e instanceof ApiError
    ? ((e.body as { message?: string; detail?: string } | undefined)?.message ??
        (e.body as { detail?: string } | undefined)?.detail ?? `Error ${e.status}`)
    : "Error";
}

/**
 * Editor del tracked-ref (rama) de la instancia. Vacío = usar la cascada del template
 * (EnvironmentMapping → DefaultBranch). PATCH /api/instances/{id}/tracked-ref.
 */
export function TrackedRefEditor({
  instanceId,
  trackedRef,
  effectiveTrackedRef,
}: {
  instanceId: string;
  trackedRef: string | null;
  effectiveTrackedRef: string | null;
}) {
  const router = useRouter();
  const [value, setValue] = useState(trackedRef ?? "");
  const [busy, setBusy] = useState(false);

  async function save() {
    setBusy(true);
    try {
      await api(`/api/instances/${encodeURIComponent(instanceId)}/tracked-ref`, {
        method: "PATCH",
        body: JSON.stringify({ trackedRef: value.trim() || null }),
      });
      toast.success("Rama (tracked-ref) actualizada");
      router.refresh();
    } catch (e) {
      toast.error(errMsg(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-2">
      <p className="text-xs text-muted-foreground">
        Rama efectiva actual:{" "}
        <span className="font-mono text-foreground">{effectiveTrackedRef ?? "—"}</span>. Deja vacío para
        heredar del template (mapping del ambiente o branch por defecto), o fija una rama explícita
        (ej. <code className="font-mono">refs/heads/feature-x</code> o <code className="font-mono">develop</code>).
      </p>
      <div className="flex items-center gap-2">
        <GitBranch className="h-4 w-4 text-muted-foreground" />
        <Input value={value} onChange={(e) => setValue(e.target.value)} placeholder="(heredar del template)" className="font-mono text-xs" />
        <Button type="button" size="sm" onClick={save} disabled={busy}>
          {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Save className="mr-2 h-4 w-4" />} Guardar
        </Button>
      </div>
    </div>
  );
}

/** Botón de borrado de instancia (DELETE /api/instances/{id}?force=true). */
export function DeleteInstanceButton({ instanceId, slug }: { instanceId: string; slug: string }) {
  const d = useTranslations("components.destructive");
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function del() {
    if (!confirm(d("confirm_instance", { slug }))) {
      return;
    }
    setBusy(true);
    try {
      await api(`/api/instances/${encodeURIComponent(instanceId)}?force=true`, { method: "DELETE" });
      toast.success("Instancia borrada");
      router.push("/instances");
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
      {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Trash2 className="mr-2 h-4 w-4" />} Borrar instancia
    </Button>
  );
}
