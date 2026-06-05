"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, Save } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError, api } from "@/lib/api";
import type { VmDto } from "@/lib/types";

/** Edita los datos editables de una VM (PATCH /api/vms/{id}). El slug no cambia. */
export function EditVmForm({ vm }: { vm: VmDto }) {
  const router = useRouter();
  const [name, setName] = useState(vm.name);
  const [publicIp, setPublicIp] = useState(vm.publicIp ?? "");
  const [privateIp, setPrivateIp] = useState(vm.privateIp ?? "");
  const [description, setDescription] = useState(vm.description ?? "");
  const [busy, setBusy] = useState(false);

  async function save() {
    setBusy(true);
    try {
      await api(`/api/vms/${encodeURIComponent(vm.id)}`, {
        method: "PATCH",
        body: JSON.stringify({
          name: name.trim(),
          publicIp: publicIp.trim() || null,
          privateIp: privateIp.trim() || null,
          description: description.trim() || null,
        }),
      });
      toast.success("VM actualizada");
      router.push(`/vms/${vm.id}`);
      router.refresh();
    } catch (e) {
      toast.error(
        e instanceof ApiError
          ? ((e.body as { message?: string } | undefined)?.message ?? `Error ${e.status}`)
          : "Error guardando la VM",
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex max-w-2xl flex-col gap-4">
      <Field label="Nombre">
        <Input value={name} onChange={(e) => setName(e.target.value)} />
      </Field>
      <div className="grid grid-cols-2 gap-3">
        <Field label="IP pública">
          <Input value={publicIp} onChange={(e) => setPublicIp(e.target.value)} className="font-mono text-xs" />
        </Field>
        <Field label="IP privada">
          <Input value={privateIp} onChange={(e) => setPrivateIp(e.target.value)} className="font-mono text-xs" />
        </Field>
      </div>
      <Field label="Descripción">
        <Input value={description} onChange={(e) => setDescription(e.target.value)} />
      </Field>

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
