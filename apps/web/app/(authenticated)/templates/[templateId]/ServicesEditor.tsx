"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, Plus, Save, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { ApiError, api } from "@/lib/api";
import type { TemplateServiceDef } from "@/lib/types";

/** Fila editable: env como texto KEY=value (una por línea), pathPrefixes como CSV. */
interface Row {
  name: string;
  buildMode: string;
  image: string;
  dockerfilePath: string;
  port: string;
  pathPrefixes: string;
  envText: string;
}

function toRow(s: TemplateServiceDef): Row {
  return {
    name: s.name,
    buildMode: s.buildMode || "registry",
    image: s.image ?? "",
    dockerfilePath: s.dockerfilePath ?? "",
    port: String(s.port ?? ""),
    pathPrefixes: (s.pathPrefixes ?? []).join(", "),
    envText: (s.env ?? []).map((e) => `${e.key}=${e.value}`).join("\n"),
  };
}

function emptyRow(): Row {
  return { name: "", buildMode: "registry", image: "", dockerfilePath: "", port: "", pathPrefixes: "", envText: "" };
}

export function ServicesEditor({
  templateId,
  initial,
}: {
  templateId: string;
  initial: TemplateServiceDef[];
}) {
  const router = useRouter();
  const [rows, setRows] = useState<Row[]>(
    initial.length ? initial.map(toRow) : [emptyRow()],
  );
  const [busy, setBusy] = useState(false);

  function update(i: number, patch: Partial<Row>) {
    setRows((rs) => rs.map((r, idx) => (idx === i ? { ...r, ...patch } : r)));
  }

  async function save() {
    setBusy(true);
    try {
      const services = rows
        .filter((r) => r.name.trim())
        .map((r) => ({
          name: r.name.trim(),
          image: r.buildMode === "git" ? "" : r.image.trim(),
          port: Number.parseInt(r.port, 10) || 0,
          pathPrefixes: r.pathPrefixes
            .split(",")
            .map((p) => p.trim())
            .filter(Boolean),
          env: Object.fromEntries(
            r.envText
              .split("\n")
              .map((l) => l.trim())
              .filter((l) => l.includes("="))
              .map((l) => {
                const idx = l.indexOf("=");
                return [l.slice(0, idx).trim(), l.slice(idx + 1).trim()];
              }),
          ),
          buildMode: r.buildMode,
          dockerfilePath: r.buildMode === "git" ? r.dockerfilePath.trim() || "Dockerfile" : null,
        }));
      await api(`/api/templates/${encodeURIComponent(templateId)}/services`, {
        method: "PUT",
        body: JSON.stringify({ services }),
      });
      toast.success(`Servicios guardados (${services.length})`);
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? ((e.body as { message?: string } | undefined)?.message ?? `Error ${e.status}`)
          : e instanceof Error
            ? e.message
            : "Error guardando servicios";
      toast.error(msg);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-3">
      <p className="text-sm text-muted-foreground">
        Cada servicio se despliega como un contenedor <code className="font-mono">{"{instancia}-{nombre}"}</code> en
        la red interna. Modo <strong>registry</strong> (imagen prebuilt) o <strong>git</strong> (Aethra
        clona y construye). En env, <code className="font-mono">{"{instance}"}</code> se interpola al slug.
      </p>

      {rows.map((r, i) => (
        <Card key={i}>
          <CardContent className="grid grid-cols-1 gap-3 p-4 md:grid-cols-2">
            <Field label="Nombre">
              <Input value={r.name} onChange={(e) => update(i, { name: e.target.value })} placeholder="backend" />
            </Field>
            <Field label="Modo de build">
              <select
                value={r.buildMode}
                onChange={(e) => update(i, { buildMode: e.target.value })}
                className="h-9 w-full rounded-md border border-border bg-background px-3 text-sm"
              >
                <option value="registry">registry (imagen prebuilt)</option>
                <option value="git">git (build en satélite)</option>
              </select>
            </Field>
            {r.buildMode === "git" ? (
              <Field label="Dockerfile (ruta en repo)">
                <Input value={r.dockerfilePath} onChange={(e) => update(i, { dockerfilePath: e.target.value })} placeholder="Dockerfile" className="font-mono text-xs" />
              </Field>
            ) : (
              <Field label="Imagen">
                <Input value={r.image} onChange={(e) => update(i, { image: e.target.value })} placeholder="ghcr.io/org/app:tag" className="font-mono text-xs" />
              </Field>
            )}
            <Field label="Puerto interno">
              <Input value={r.port} onChange={(e) => update(i, { port: e.target.value })} placeholder="5006" inputMode="numeric" />
            </Field>
            <Field label="Rutas (pathPrefix, CSV)">
              <Input value={r.pathPrefixes} onChange={(e) => update(i, { pathPrefixes: e.target.value })} placeholder="/api, /hubs   ·   vacío = interno" className="font-mono text-xs" />
            </Field>
            <Field label="Env (KEY=valor por línea)">
              <textarea
                value={r.envText}
                onChange={(e) => update(i, { envText: e.target.value })}
                rows={3}
                placeholder={"API_BASE_URL=http://{instance}-backend:5006"}
                className="w-full rounded-md border border-border bg-background px-3 py-2 font-mono text-xs"
              />
            </Field>
            <div className="md:col-span-2 flex justify-end">
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="text-destructive hover:bg-destructive/10 hover:text-destructive"
                onClick={() => setRows((rs) => rs.filter((_, idx) => idx !== i))}
              >
                <Trash2 className="mr-2 h-4 w-4" />
                Quitar servicio
              </Button>
            </div>
          </CardContent>
        </Card>
      ))}

      <div className="flex items-center justify-between">
        <Button type="button" variant="outline" onClick={() => setRows((rs) => [...rs, emptyRow()])}>
          <Plus className="mr-2 h-4 w-4" />
          Agregar servicio
        </Button>
        <Button type="button" onClick={save} disabled={busy}>
          {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Save className="mr-2 h-4 w-4" />}
          Guardar servicios
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
