"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useRouter } from "next/navigation";
import { Loader2, Plus, Save, Trash2, GitBranch } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError, api } from "@/lib/api";
import type { TemplateEnvironmentMapping } from "@/lib/types";

function errMsg(e: unknown): string {
  return e instanceof ApiError
    ? ((e.body as { message?: string } | undefined)?.message ?? `Error ${e.status}`)
    : "Error";
}

/** Editor del mapping environment→branch (PATCH /api/templates/{id}/environment-mapping). */
export function EnvironmentMappingEditor({
  templateId,
  initial,
  defaultBranch,
}: {
  templateId: string;
  initial: TemplateEnvironmentMapping[];
  defaultBranch: string;
}) {
  const p = useTranslations("components.template_mapping");
  const router = useRouter();
  const [rows, setRows] = useState<TemplateEnvironmentMapping[]>(initial.length ? initial : []);
  const [busy, setBusy] = useState(false);

  async function save() {
    setBusy(true);
    try {
      await api(`/api/templates/${encodeURIComponent(templateId)}/environment-mapping`, {
        method: "PATCH",
        body: JSON.stringify({
          mappings: rows
            .filter((r) => r.environment.trim() && r.branch.trim())
            .map((r) => ({ environment: r.environment.trim(), branch: r.branch.trim() })),
        }),
      });
      toast.success("Mapping environment→branch guardado");
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
        {p("help", { branch: defaultBranch })}
      </p>
      {rows.map((r, i) => (
        <div key={i} className="flex items-center gap-2">
          <Input value={r.environment} placeholder="environment (ej. test)" className="font-mono text-xs"
            onChange={(e) => setRows((xs) => xs.map((x, j) => (j === i ? { ...x, environment: e.target.value } : x)))} />
          <GitBranch className="h-4 w-4 text-muted-foreground" />
          <Input value={r.branch} placeholder="rama (ej. develop)" className="font-mono text-xs"
            onChange={(e) => setRows((xs) => xs.map((x, j) => (j === i ? { ...x, branch: e.target.value } : x)))} />
          <Button type="button" variant="ghost" size="icon" onClick={() => setRows((xs) => xs.filter((_, j) => j !== i))}>
            <Trash2 className="h-4 w-4 text-destructive" />
          </Button>
        </div>
      ))}
      <div className="flex items-center justify-between">
        <Button type="button" variant="outline" size="sm" onClick={() => setRows((xs) => [...xs, { environment: "", branch: "" }])}>
          <Plus className="mr-2 h-4 w-4" /> Agregar mapeo
        </Button>
        <Button type="button" size="sm" onClick={save} disabled={busy}>
          {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Save className="mr-2 h-4 w-4" />} Guardar
        </Button>
      </div>
    </div>
  );
}

/** Botón de borrado de plantilla (DELETE /api/templates/{id}?force=true). */
export function DeleteTemplateButton({ templateId, name }: { templateId: string; name: string }) {
  const d = useTranslations("components.destructive");
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function del() {
    if (!confirm(d("confirm_template", { name }))) {
      return;
    }
    setBusy(true);
    try {
      await api(`/api/templates/${encodeURIComponent(templateId)}?force=true`, { method: "DELETE" });
      toast.success("Plantilla borrada");
      router.push("/templates");
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
      {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Trash2 className="mr-2 h-4 w-4" />} Eliminar
    </Button>
  );
}

/** Toggle auto-preview de PRs (PATCH /api/templates/{id}/auto-preview). */
export function AutoPreviewToggle({ templateId, initial }: { templateId: string; initial: boolean }) {
  const router = useRouter();
  const [enabled, setEnabled] = useState(initial);
  const [busy, setBusy] = useState(false);

  async function toggle() {
    setBusy(true);
    const next = !enabled;
    try {
      await api(`/api/templates/${encodeURIComponent(templateId)}/auto-preview`, {
        method: "PATCH",
        body: JSON.stringify({ enabled: next }),
      });
      setEnabled(next);
      toast.success(next ? "Auto-preview de PRs activado" : "Auto-preview de PRs desactivado");
      router.refresh();
    } catch (e) {
      toast.error(errMsg(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Button type="button" variant={enabled ? "default" : "outline"} size="sm" onClick={toggle} disabled={busy}>
      {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
      Auto-preview PRs: {enabled ? "ON" : "OFF"}
    </Button>
  );
}
