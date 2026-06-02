"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { AlertTriangle, ArrowRight, Copy, Loader2, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { ApiError, api } from "@/lib/api";
import type {
  BuildType,
  CreateTemplateRequest,
  TemplateBuildArg,
  TemplateDetail,
} from "@/lib/types";

const SLUG_RE = /^[a-z][a-z0-9-]{0,30}$/;

export function NewTemplateForm({ projectId }: { projectId: string }) {
  const router = useRouter();
  const [slug, setSlug] = useState("");
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [gitRepoUrl, setGitRepoUrl] = useState("");
  const [branch, setBranch] = useState("main");
  const [baseDirectory, setBaseDirectory] = useState(".");
  const [watchPathsRaw, setWatchPathsRaw] = useState("**");
  const [buildType, setBuildType] = useState<BuildType>("Dockerfile");
  const [dockerfilePath, setDockerfilePath] = useState("Dockerfile");
  const [composeFilePath, setComposeFilePath] = useState("docker-compose.yml");
  const [buildArgs, setBuildArgs] = useState<TemplateBuildArg[]>([]);
  const [loading, setLoading] = useState(false);
  const [created, setCreated] = useState<TemplateDetail | null>(null);

  const slugError = useMemo(() => {
    if (!slug) return null;
    return SLUG_RE.test(slug)
      ? null
      : "Slug debe iniciar con letra, lowercase con guiones (máx 31).";
  }, [slug]);

  const watchPaths = useMemo(
    () =>
      watchPathsRaw
        .split("\n")
        .map((s) => s.trim())
        .filter(Boolean),
    [watchPathsRaw],
  );

  const canSubmit =
    !loading &&
    slug &&
    !slugError &&
    name.trim().length > 0 &&
    gitRepoUrl.trim().length > 0 &&
    branch.trim().length > 0 &&
    watchPaths.length > 0 &&
    (buildType === "Nixpacks" ||
      (buildType === "Dockerfile" && dockerfilePath.trim().length > 0) ||
      (buildType === "DockerCompose" && composeFilePath.trim().length > 0));

  function setArg(i: number, patch: Partial<TemplateBuildArg>) {
    setBuildArgs((rows) =>
      rows.map((r, idx) => (idx === i ? { ...r, ...patch } : r)),
    );
  }
  function addArg() {
    setBuildArgs((rows) => [...rows, { key: "", value: "" }]);
  }
  function removeArg(i: number) {
    setBuildArgs((rows) => rows.filter((_, idx) => idx !== i));
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;
    setLoading(true);
    try {
      const body: CreateTemplateRequest = {
        slug,
        name: name.trim(),
        description: description.trim() || null,
        source: {
          gitRepoUrl: gitRepoUrl.trim(),
          branch: branch.trim(),
          baseDirectory: baseDirectory.trim() || ".",
          watchPaths,
        },
        build: {
          buildType,
          dockerfilePath:
            buildType === "Dockerfile" ? dockerfilePath.trim() : null,
          composeFilePath:
            buildType === "DockerCompose" ? composeFilePath.trim() : null,
          buildArgs: buildArgs.filter((a) => a.key.trim().length > 0),
        },
      };
      const response = await api<TemplateDetail>(
        `/api/projects/${encodeURIComponent(projectId)}/templates`,
        { method: "POST", body: JSON.stringify(body) },
      );
      toast.success("Template creado");
      setCreated(response);
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
      setLoading(false);
    }
  }

  if (created) {
    return <WebhookSecretScreen template={created} />;
  }

  return (
    <form onSubmit={onSubmit}>
      <Card>
        <CardContent className="space-y-6 p-6">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="slug">Slug *</Label>
              <Input
                id="slug"
                value={slug}
                onChange={(e) => setSlug(e.target.value.toLowerCase())}
                placeholder="api-service"
                className="font-mono text-xs"
                maxLength={31}
                required
              />
              {slugError ? (
                <p className="text-xs text-destructive">{slugError}</p>
              ) : (
                <p className="text-xs text-muted-foreground">
                  URL-friendly, lowercase con guiones (máx 31).
                </p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="name">Nombre *</Label>
              <Input
                id="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="API Service"
                required
              />
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="description">Descripción</Label>
            <Textarea
              id="description"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={2}
            />
          </div>

          <fieldset className="rounded-md border border-border bg-muted/30 p-4 space-y-4">
            <legend className="px-2 text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Source
            </legend>
            <div className="space-y-2">
              <Label htmlFor="git">Git repo URL *</Label>
              <Input
                id="git"
                value={gitRepoUrl}
                onChange={(e) => setGitRepoUrl(e.target.value)}
                placeholder="git@github.com:org/repo.git"
                className="font-mono text-xs"
                required
              />
            </div>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="branch">Branch *</Label>
                <Input
                  id="branch"
                  value={branch}
                  onChange={(e) => setBranch(e.target.value)}
                  className="font-mono text-xs"
                  required
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="basedir">Base directory</Label>
                <Input
                  id="basedir"
                  value={baseDirectory}
                  onChange={(e) => setBaseDirectory(e.target.value)}
                  className="font-mono text-xs"
                />
                <p className="text-xs text-muted-foreground">
                  Subdir dentro del repo. Default `.`.
                </p>
              </div>
            </div>
            <div className="space-y-2">
              <Label htmlFor="watch">Watch paths *</Label>
              <Textarea
                id="watch"
                value={watchPathsRaw}
                onChange={(e) => setWatchPathsRaw(e.target.value)}
                rows={3}
                className="font-mono text-xs"
                placeholder="**"
              />
              <p className="text-xs text-muted-foreground">
                Globs uno por línea. Solo cambios en estos paths disparan build.
              </p>
            </div>
          </fieldset>

          <fieldset className="rounded-md border border-border bg-muted/30 p-4 space-y-4">
            <legend className="px-2 text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Build
            </legend>
            <div className="space-y-2">
              <Label>Build type *</Label>
              <Select
                value={buildType}
                onValueChange={(v) => setBuildType(v as BuildType)}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="Dockerfile">
                    Dockerfile — usa tu propio <code className="font-mono">Dockerfile</code>
                  </SelectItem>
                  <SelectItem value="DockerCompose">
                    Docker Compose — varios servicios desde <code className="font-mono">compose.yml</code>
                  </SelectItem>
                  <SelectItem value="Nixpacks">
                    Nixpacks — auto-detecta lenguaje (sin Dockerfile)
                  </SelectItem>
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                Es la estrategia para construir la imagen. El runtime que la ejecuta (Docker o Podman) se configura por VM en{" "}
                <span className="font-mono">Satellite:ContainerRuntime</span>.
              </p>
            </div>

            {buildType === "Dockerfile" ? (
              <div className="space-y-2">
                <Label htmlFor="dockerfile">Dockerfile path *</Label>
                <Input
                  id="dockerfile"
                  value={dockerfilePath}
                  onChange={(e) => setDockerfilePath(e.target.value)}
                  className="font-mono text-xs"
                  required
                />
              </div>
            ) : buildType === "DockerCompose" ? (
              <div className="space-y-2">
                <Label htmlFor="compose">Compose file path *</Label>
                <Input
                  id="compose"
                  value={composeFilePath}
                  onChange={(e) => setComposeFilePath(e.target.value)}
                  className="font-mono text-xs"
                  required
                />
              </div>
            ) : (
              <p className="rounded-md border border-border bg-card px-3 py-2 text-xs text-muted-foreground">
                Nixpacks detecta el stack automáticamente. No requiere Dockerfile.
              </p>
            )}

            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <Label>Build args</Label>
                <Button type="button" variant="outline" size="sm" onClick={addArg}>
                  <Plus className="mr-2 h-4 w-4" />
                  Añadir
                </Button>
              </div>
              {buildArgs.length === 0 ? (
                <p className="text-xs text-muted-foreground">
                  Sin args. Añadí pares clave/valor para inyectar al build.
                </p>
              ) : (
                <ul className="space-y-2">
                  {buildArgs.map((arg, i) => (
                    <li key={i} className="flex gap-2">
                      <Input
                        value={arg.key}
                        onChange={(e) => setArg(i, { key: e.target.value })}
                        placeholder="KEY"
                        className="w-40 font-mono text-xs"
                      />
                      <Input
                        value={arg.value}
                        onChange={(e) => setArg(i, { value: e.target.value })}
                        placeholder="value"
                        className="flex-1 font-mono text-xs"
                      />
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        onClick={() => removeArg(i)}
                        aria-label="Quitar"
                      >
                        <Trash2 className="h-4 w-4 text-muted-foreground" />
                      </Button>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </fieldset>

          <div className="flex justify-end gap-2 pt-2">
            <Button
              type="button"
              variant="ghost"
              onClick={() => router.push(`/projects/${projectId}`)}
            >
              Cancelar
            </Button>
            <Button type="submit" disabled={!canSubmit}>
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              Crear template
            </Button>
          </div>
        </CardContent>
      </Card>
    </form>
  );
}

function WebhookSecretScreen({ template }: { template: TemplateDetail }) {
  const secret = template.webhookSecret ?? "";

  async function copy() {
    if (!secret) return;
    try {
      await navigator.clipboard.writeText(secret);
      toast.success("Webhook secret copiado");
    } catch {
      toast.error("No se pudo copiar al portapapeles");
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <Card className="border-success/30 bg-success/5">
        <CardContent className="p-4 text-sm text-success-foreground">
          <strong>Template creado.</strong> {template.name} ({template.slug})
        </CardContent>
      </Card>

      <Card className="border-warning/40 bg-warning/5">
        <CardHeader className="flex-row items-start gap-3 space-y-0 pb-2">
          <AlertTriangle className="h-5 w-5 shrink-0 text-warning" />
          <div>
            <CardTitle className="text-sm">
              El webhook secret solo se muestra esta vez
            </CardTitle>
            <p className="mt-1 text-xs text-muted-foreground">
              Copialo y configurarlo en tu provider Git (GitHub/Gitlab) ahora.
              Si lo perdés, rotalo desde el detalle del template.
            </p>
          </div>
        </CardHeader>
        {secret ? (
          <CardContent className="space-y-2 pt-0">
            <div className="rounded-md border border-border bg-card">
              <div className="flex items-center justify-between border-b border-border px-3 py-1.5">
                <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                  Webhook secret
                </span>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={copy}
                >
                  <Copy className="mr-2 h-4 w-4" />
                  Copiar
                </Button>
              </div>
              <pre className="overflow-x-auto whitespace-nowrap px-3 py-2 font-mono text-xs text-foreground">
                {secret}
              </pre>
            </div>
          </CardContent>
        ) : (
          <CardContent className="pt-0">
            <p className="rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
              La API no devolvió webhook secret. Verificá el contrato.
            </p>
          </CardContent>
        )}
      </Card>

      <div className="flex justify-end">
        <Button asChild>
          <Link href={`/templates/${template.id}`}>
            Ir al detalle
            <ArrowRight className="ml-2 h-4 w-4" />
          </Link>
        </Button>
      </div>
    </div>
  );
}
