"use client";

import { useTranslations } from "next-intl";
import { useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, Save } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError, api } from "@/lib/api";
import type { ManagedServiceDetailDto } from "@/lib/types";

/** Edita los campos mutables de un servicio (PATCH /api/services/{id}). Slug/imagen/puerto/VM no cambian. */
export function EditServiceForm({ service }: { service: ManagedServiceDetailDto }) {
  const c = useTranslations("common");
  const ef = useTranslations("components.edit_forms");
  const router = useRouter();
  const [name, setName] = useState(service.name);
  const [exposedExternally, setExposedExternally] = useState(service.exposedExternally);
  const [busy, setBusy] = useState(false);

  async function save() {
    setBusy(true);
    try {
      await api(`/api/services/${encodeURIComponent(service.id)}`, {
        method: "PATCH",
        body: JSON.stringify({
          name: name.trim(),
          exposedExternally,
        }),
      });
      toast.success("Servicio actualizado");
      router.push(`/services/${service.id}`);
      router.refresh();
    } catch (e) {
      toast.error(
        e instanceof ApiError
          ? ((e.body as { message?: string; detail?: string } | undefined)?.message ??
              (e.body as { detail?: string } | undefined)?.detail ?? `Error ${e.status}`)
          : ef("error_service"),
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex max-w-2xl flex-col gap-4">
      <Field label={c("name")}>
        <Input value={name} onChange={(e) => setName(e.target.value)} />
      </Field>
      <Field label={ef("exposure")}>
        <label className="flex items-center gap-2 text-sm text-foreground">
          <input
            type="checkbox"
            checked={exposedExternally}
            onChange={(e) => setExposedExternally(e.target.checked)}
            className="h-4 w-4 rounded border-border accent-primary"
          />
          Exponer externamente
        </label>
      </Field>

      <div className="flex justify-end">
        <Button type="button" onClick={save} disabled={busy}>
          {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Save className="mr-2 h-4 w-4" />}
          {c("save_changes")}
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
