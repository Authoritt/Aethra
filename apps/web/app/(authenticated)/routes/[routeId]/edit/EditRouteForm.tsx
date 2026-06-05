"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, Save } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError, api } from "@/lib/api";
import type { RouteDto } from "@/lib/types";

/** Edita el backend y el TLS de una ruta (PATCH /api/proxy/routes/{id}). El host y el path no cambian. */
export function EditRouteForm({ route }: { route: RouteDto }) {
  const router = useRouter();
  const [backendUrl, setBackendUrl] = useState(route.backendUrl);
  const [tlsEnabled, setTlsEnabled] = useState(route.tlsEnabled);
  const [busy, setBusy] = useState(false);

  async function save() {
    setBusy(true);
    try {
      await api(`/api/proxy/routes/${encodeURIComponent(route.id)}`, {
        method: "PATCH",
        body: JSON.stringify({
          backendUrl: backendUrl.trim(),
          tlsEnabled,
        }),
      });
      toast.success("Ruta actualizada");
      router.push(`/routes/${route.id}`);
      router.refresh();
    } catch (e) {
      toast.error(
        e instanceof ApiError
          ? ((e.body as { message?: string } | undefined)?.message ?? `Error ${e.status}`)
          : "Error guardando la ruta",
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex max-w-2xl flex-col gap-4">
      <Field label="Hostname (inmutable)">
        <Input value={route.hostname} disabled className="font-mono text-xs" />
      </Field>
      <Field label="Path prefix (inmutable)">
        <Input value={route.pathPrefix || "/"} disabled className="font-mono text-xs" />
      </Field>
      <Field label="Backend URL">
        <Input value={backendUrl} onChange={(e) => setBackendUrl(e.target.value)} className="font-mono text-xs" />
      </Field>
      <label className="flex items-center gap-2">
        <input
          type="checkbox"
          checked={tlsEnabled}
          onChange={(e) => setTlsEnabled(e.target.checked)}
          className="h-4 w-4 rounded border-border"
        />
        <span className="text-sm text-foreground">TLS habilitado</span>
      </label>

      <div className="flex justify-end">
        <Button type="button" onClick={save} disabled={busy}>
          {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Save className="mr-2 h-4 w-4" />}
          Guardar cambios
        </Button>
      </div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">{label}</span>
      {children}
    </label>
  );
}
