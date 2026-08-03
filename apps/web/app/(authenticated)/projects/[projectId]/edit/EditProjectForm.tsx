"use client";

import { useTranslations } from "next-intl";
import { useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, Save } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError, api } from "@/lib/api";
import type { ProjectDetailV2 } from "@/lib/types";

/** Edita los campos de un proyecto existente (PATCH /api/projects/{id}). El slug no cambia. */
export function EditProjectForm({ project }: { project: ProjectDetailV2 }) {
  const c = useTranslations("common");
  const ef = useTranslations("components.edit_forms");
  const router = useRouter();
  const [name, setName] = useState(project.name);
  const [description, setDescription] = useState(project.description ?? "");
  const [color, setColor] = useState(project.color ?? "");
  const [icon, setIcon] = useState(project.icon ?? "");
  const [busy, setBusy] = useState(false);

  async function save() {
    setBusy(true);
    try {
      await api(`/api/projects/${encodeURIComponent(project.id)}`, {
        method: "PATCH",
        body: JSON.stringify({
          name: name.trim(),
          description: description.trim() || null,
          color: color.trim() || null,
          icon: icon.trim() || null,
        }),
      });
      toast.success("Proyecto actualizado");
      router.push(`/projects/${project.id}`);
      router.refresh();
    } catch (e) {
      toast.error(
        e instanceof ApiError
          ? ((e.body as { message?: string } | undefined)?.message ?? `Error ${e.status}`)
          : ef("error_project"),
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
      <Field label={c("description")}>
        <Input value={description} onChange={(e) => setDescription(e.target.value)} />
      </Field>
      <div className="grid grid-cols-2 gap-3">
        <Field label="Color">
          <Input value={color} onChange={(e) => setColor(e.target.value)} className="font-mono text-xs" />
        </Field>
        <Field label="Icono">
          <Input value={icon} onChange={(e) => setIcon(e.target.value)} className="font-mono text-xs" />
        </Field>
      </div>

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
