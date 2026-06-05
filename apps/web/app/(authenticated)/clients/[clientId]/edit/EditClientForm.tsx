"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, Save } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError, api } from "@/lib/api";
import type { ClientDetail } from "@/lib/types";

/** Edita los campos de un cliente existente (PATCH /api/clients/{id}). El slug no cambia. */
export function EditClientForm({ client }: { client: ClientDetail }) {
  const router = useRouter();
  const [displayName, setDisplayName] = useState(client.displayName);
  const [description, setDescription] = useState(client.description ?? "");
  const [contactEmail, setContactEmail] = useState(client.contactEmail ?? "");
  const [billingTag, setBillingTag] = useState(client.billingTag ?? "");
  const [busy, setBusy] = useState(false);

  async function save() {
    setBusy(true);
    try {
      await api(`/api/clients/${encodeURIComponent(client.id)}`, {
        method: "PATCH",
        body: JSON.stringify({
          displayName: displayName.trim(),
          description: description.trim() || null,
          contactEmail: contactEmail.trim() || null,
          billingTag: billingTag.trim() || null,
        }),
      });
      toast.success("Cliente actualizado");
      router.push(`/clients/${client.id}`);
      router.refresh();
    } catch (e) {
      toast.error(
        e instanceof ApiError
          ? ((e.body as { message?: string } | undefined)?.message ?? `Error ${e.status}`)
          : "Error guardando el cliente",
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex max-w-2xl flex-col gap-4">
      <Field label="Nombre">
        <Input value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
      </Field>
      <Field label="Descripción">
        <Input value={description} onChange={(e) => setDescription(e.target.value)} />
      </Field>
      <Field label="Email de contacto">
        <Input type="email" value={contactEmail} onChange={(e) => setContactEmail(e.target.value)} />
      </Field>
      <Field label="Billing tag">
        <Input value={billingTag} onChange={(e) => setBillingTag(e.target.value)} className="font-mono text-xs" />
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
