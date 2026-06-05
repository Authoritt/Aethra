"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2, Plus, Save, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { ApiError, api } from "@/lib/api";
import type { TemplateBuildArg, TemplateDetail } from "@/lib/types";

/** Edita los campos de una plantilla existente (PATCH /api/templates/{id}). El slug no cambia. */
export function EditTemplateForm({ template }: { template: TemplateDetail }) {
  const router = useRouter();
  const [name, setName] = useState(template.name);
  const [description, setDescription] = useState(template.description ?? "");
  const [gitRepoUrl, setGitRepoUrl] = useState(template.gitRepoUrl);
  const [branch, setBranch] = useState(template.branch);
  const [baseDirectory, setBaseDirectory] = useState(template.baseDirectory ?? ".");
  const [watchPaths, setWatchPaths] = useState((template.watchPaths ?? []).join(", "));
  const [buildType, setBuildType] = useState(template.buildType);
  const [dockerfilePath, setDockerfilePath] = useState(template.dockerfilePath ?? "Dockerfile");
  const [composeFilePath, setComposeFilePath] = useState(template.composeFilePath ?? "docker-compose.yml");
  const [buildArgs, setBuildArgs] = useState<TemplateBuildArg[]>(template.buildArgs ?? []);
  const [busy, setBusy] = useState(false);

  async function save() {
    setBusy(true);
    try {
      await api(`/api/templates/${encodeURIComponent(template.id)}`, {
        method: "PATCH",
        body: JSON.stringify({
          name: name.trim(),
          description: description.trim() || null,
          gitRepoUrl: gitRepoUrl.trim(),
          branch: branch.trim(),
          baseDirectory: baseDirectory.trim() || ".",
          watchPaths: watchPaths.split(",").map((p) => p.trim()).filter(Boolean),
          accessTokenCredentialName: template.accessTokenCredentialName,
          buildType,
          dockerfilePath: buildType === "Dockerfile" ? dockerfilePath.trim() || "Dockerfile" : null,
          composeFilePath: buildType === "DockerCompose" ? composeFilePath.trim() || "docker-compose.yml" : null,
          buildArgs: buildArgs.filter((a) => a.key.trim()),
        }),
      });
      toast.success("Plantilla actualizada");
      router.push(`/templates/${template.id}`);
      router.refresh();
    } catch (e) {
      toast.error(
        e instanceof ApiError
          ? ((e.body as { message?: string } | undefined)?.message ?? `Error ${e.status}`)
          : "Error guardando la plantilla",
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
      <Field label="Descripción">
        <Input value={description} onChange={(e) => setDescription(e.target.value)} />
      </Field>
      <Field label="Repo Git">
        <Input value={gitRepoUrl} onChange={(e) => setGitRepoUrl(e.target.value)} className="font-mono text-xs" />
      </Field>
      <div className="grid grid-cols-2 gap-3">
        <Field label="Branch por defecto">
          <Input value={branch} onChange={(e) => setBranch(e.target.value)} className="font-mono text-xs" />
        </Field>
        <Field label="Base directory">
          <Input value={baseDirectory} onChange={(e) => setBaseDirectory(e.target.value)} className="font-mono text-xs" />
        </Field>
      </div>
      <Field label="Watch paths (CSV)">
        <Input value={watchPaths} onChange={(e) => setWatchPaths(e.target.value)} className="font-mono text-xs" />
      </Field>
      <Field label="Build type">
        <select
          value={buildType}
          onChange={(e) => setBuildType(e.target.value as TemplateDetail["buildType"])}
          className="h-9 w-full rounded-md border border-border bg-background px-3 text-sm"
        >
          <option value="Dockerfile">Dockerfile</option>
          <option value="DockerCompose">DockerCompose</option>
          <option value="Nixpacks">Nixpacks</option>
        </select>
      </Field>
      {buildType === "Dockerfile" ? (
        <Field label="Dockerfile path">
          <Input value={dockerfilePath} onChange={(e) => setDockerfilePath(e.target.value)} className="font-mono text-xs" />
        </Field>
      ) : buildType === "DockerCompose" ? (
        <Field label="Compose file path">
          <Input value={composeFilePath} onChange={(e) => setComposeFilePath(e.target.value)} className="font-mono text-xs" />
        </Field>
      ) : null}

      <Field label="Build args (KEY=valor)">
        <Card>
          <CardContent className="flex flex-col gap-2 p-3">
            {buildArgs.map((a, i) => (
              <div key={i} className="flex gap-2">
                <Input value={a.key} placeholder="KEY" className="font-mono text-xs"
                  onChange={(e) => setBuildArgs((xs) => xs.map((x, j) => (j === i ? { ...x, key: e.target.value } : x)))} />
                <Input value={a.value} placeholder="valor" className="font-mono text-xs"
                  onChange={(e) => setBuildArgs((xs) => xs.map((x, j) => (j === i ? { ...x, value: e.target.value } : x)))} />
                <Button type="button" variant="ghost" size="icon" onClick={() => setBuildArgs((xs) => xs.filter((_, j) => j !== i))}>
                  <Trash2 className="h-4 w-4 text-destructive" />
                </Button>
              </div>
            ))}
            <Button type="button" variant="outline" size="sm" onClick={() => setBuildArgs((xs) => [...xs, { key: "", value: "" }])}>
              <Plus className="mr-2 h-4 w-4" /> Agregar build arg
            </Button>
          </CardContent>
        </Card>
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
