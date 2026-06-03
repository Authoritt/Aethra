"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import {
  AlertTriangle,
  ArrowRight,
  CheckCircle2,
  Copy,
  Loader2,
  Plus,
  Search,
  Trash2,
} from "lucide-react";
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
  DiscoverTemplateResult,
  TemplateBuildArg,
  TemplateDetail,
} from "@/lib/types";

const SLUG_RE = /^[a-z][a-z0-9-]{0,30}$/;

export function NewTemplateForm({ projectId }: { projectId: string }) {
  const t = useTranslations("pages.templates_new");
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
  // F11.2 — estado del discover (Detectar) que prellena el form a partir del repo.
  const [detecting, setDetecting] = useState(false);
  const [detection, setDetection] = useState<DiscoverTemplateResult | null>(null);

  const slugError = useMemo(() => {
    if (!slug) return null;
    return SLUG_RE.test(slug) ? null : t("slug_invalid");
  }, [slug, t]);

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

  /**
   * F11.2 — Llama a `POST /api/templates/discover` con el repo + branch del form,
   * prellena `buildType` segun `suggestedBuildType` del backend y guarda la respuesta
   * en `detection` para mostrar los detalles (lenguaje, puertos, archivos detectados).
   */
  async function onDetect() {
    if (!gitRepoUrl.trim()) {
      toast.error(t("detect_missing_url"));
      return;
    }
    setDetecting(true);
    setDetection(null);
    try {
      const result = await api<DiscoverTemplateResult>(
        `/api/templates/discover`,
        {
          method: "POST",
          body: JSON.stringify({
            gitRepoUrl: gitRepoUrl.trim(),
            branch: branch.trim() || null,
          }),
        },
      );
      setDetection(result);
      setBuildType(result.suggestedBuildType);
      toast.success(
        result.detectedLanguages.length > 0
          ? t("detect_success_with_langs", {
              type: result.suggestedBuildType,
              langs: result.detectedLanguages.join(", "),
            })
          : t("detect_success", { type: result.suggestedBuildType }),
      );
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : t("error_unknown");
      toast.error(t("detect_failed", { message: msg }));
    } finally {
      setDetecting(false);
    }
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
      toast.success(t("toast_simple_created"));
      setCreated(response);
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ?? `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : t("error_unknown");
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
              <Label htmlFor="slug">{t("label_slug")}</Label>
              <Input
                id="slug"
                value={slug}
                onChange={(e) => setSlug(e.target.value.toLowerCase())}
                placeholder={t("placeholder_slug")}
                className="font-mono text-xs"
                maxLength={31}
                required
              />
              {slugError ? (
                <p className="text-xs text-destructive">{slugError}</p>
              ) : (
                <p className="text-xs text-muted-foreground">
                  {t("slug_hint")}
                </p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="name">{t("label_name")}</Label>
              <Input
                id="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder={t("placeholder_name")}
                required
              />
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="description">{t("label_description")}</Label>
            <Textarea
              id="description"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={2}
            />
          </div>

          <fieldset className="rounded-md border border-border bg-muted/30 p-4 space-y-4">
            <legend className="px-2 text-xs font-medium uppercase tracking-wider text-muted-foreground">
              {t("section_source")}
            </legend>
            <div className="space-y-2">
              <Label htmlFor="git">{t("label_git_repo")}</Label>
              <div className="flex gap-2">
                <Input
                  id="git"
                  value={gitRepoUrl}
                  onChange={(e) => {
                    setGitRepoUrl(e.target.value);
                    // Si el operador edita el URL, el resultado del detect previo deja
                    // de ser confiable: limpiamos para no mostrar info engañosa.
                    if (detection) setDetection(null);
                  }}
                  placeholder={t("placeholder_git_url")}
                  className="font-mono text-xs"
                  required
                />
                <Button
                  type="button"
                  variant="outline"
                  onClick={onDetect}
                  disabled={detecting || !gitRepoUrl.trim()}
                  title={t("detect_title")}
                >
                  {detecting ? (
                    <Loader2 className="h-4 w-4 animate-spin" />
                  ) : (
                    <Search className="h-4 w-4" />
                  )}
                  <span className="ml-2">{t("detect_label")}</span>
                </Button>
              </div>
              {detection ? (
                <div className="rounded-md border border-success/30 bg-success/5 p-3 text-xs space-y-1">
                  <div className="flex items-center gap-2 text-success-foreground">
                    <CheckCircle2 className="h-4 w-4 text-success" />
                    <strong>{t("detection_suggested")}</strong>
                    <span className="font-mono">{detection.suggestedBuildType}</span>
                  </div>
                  <ul className="ml-6 list-disc text-muted-foreground space-y-0.5">
                    <li>
                      {t("detection_languages")}{" "}
                      <span className="font-mono">
                        {detection.detectedLanguages.length > 0
                          ? detection.detectedLanguages.join(", ")
                          : t("detection_none")}
                      </span>
                    </li>
                    <li>
                      {t("detection_files_root")}{" "}
                      {detection.hasDockerfile ? "Dockerfile " : ""}
                      {detection.hasCompose ? "compose.yml " : ""}
                      {detection.hasNixpacksToml ? "nixpacks.toml " : ""}
                      {!detection.hasDockerfile &&
                      !detection.hasCompose &&
                      !detection.hasNixpacksToml
                        ? t("detection_none_typical")
                        : ""}
                    </li>
                    {detection.exposedPorts.length > 0 ? (
                      <li>
                        {t("detection_ports")}{" "}
                        <span className="font-mono">
                          {detection.exposedPorts.join(", ")}
                        </span>
                      </li>
                    ) : null}
                  </ul>
                </div>
              ) : null}
            </div>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="branch">{t("label_branch")}</Label>
                <Input
                  id="branch"
                  value={branch}
                  onChange={(e) => setBranch(e.target.value)}
                  className="font-mono text-xs"
                  required
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="basedir">{t("label_base_dir")}</Label>
                <Input
                  id="basedir"
                  value={baseDirectory}
                  onChange={(e) => setBaseDirectory(e.target.value)}
                  className="font-mono text-xs"
                />
                <p className="text-xs text-muted-foreground">
                  {t("base_dir_hint")}
                </p>
              </div>
            </div>
            <div className="space-y-2">
              <Label htmlFor="watch">{t("label_watch_paths")} *</Label>
              <Textarea
                id="watch"
                value={watchPathsRaw}
                onChange={(e) => setWatchPathsRaw(e.target.value)}
                rows={3}
                className="font-mono text-xs"
                placeholder={t("placeholder_watch_paths")}
              />
              <p className="text-xs text-muted-foreground">
                {t("watch_paths_hint")}
              </p>
            </div>
          </fieldset>

          <fieldset className="rounded-md border border-border bg-muted/30 p-4 space-y-4">
            <legend className="px-2 text-xs font-medium uppercase tracking-wider text-muted-foreground">
              {t("section_build")}
            </legend>
            <div className="space-y-2">
              <Label>{t("label_build_type")}</Label>
              <Select
                value={buildType}
                onValueChange={(v) => setBuildType(v as BuildType)}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="Dockerfile">
                    {t("build_type_dockerfile")}
                  </SelectItem>
                  <SelectItem value="DockerCompose">
                    {t("build_type_compose")}
                  </SelectItem>
                  <SelectItem value="Nixpacks">
                    {t("build_type_nixpacks")}
                  </SelectItem>
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                {t("build_strategy_hint")}
              </p>
            </div>

            {buildType === "Dockerfile" ? (
              <div className="space-y-2">
                <Label htmlFor="dockerfile">{t("dockerfile_path_label")}</Label>
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
                <Label htmlFor="compose">{t("compose_path_label")}</Label>
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
                {t("nixpacks_hint")}
              </p>
            )}

            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <Label>{t("label_build_args")}</Label>
                <Button type="button" variant="outline" size="sm" onClick={addArg}>
                  <Plus className="mr-2 h-4 w-4" />
                  {t("build_args_add")}
                </Button>
              </div>
              {buildArgs.length === 0 ? (
                <p className="text-xs text-muted-foreground">
                  {t("build_args_empty")}
                </p>
              ) : (
                <ul className="space-y-2">
                  {buildArgs.map((arg, i) => (
                    <li key={i} className="flex gap-2">
                      <Input
                        value={arg.key}
                        onChange={(e) => setArg(i, { key: e.target.value })}
                        placeholder={t("build_args_key_placeholder")}
                        className="w-40 font-mono text-xs"
                      />
                      <Input
                        value={arg.value}
                        onChange={(e) => setArg(i, { value: e.target.value })}
                        placeholder={t("build_args_value_placeholder")}
                        className="flex-1 font-mono text-xs"
                      />
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        onClick={() => removeArg(i)}
                        aria-label={t("remove_aria")}
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
              {t("cancel")}
            </Button>
            <Button type="submit" disabled={!canSubmit}>
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              {t("submit")}
            </Button>
          </div>
        </CardContent>
      </Card>
    </form>
  );
}

function WebhookSecretScreen({ template }: { template: TemplateDetail }) {
  const t = useTranslations("pages.templates_new");
  const secret = template.webhookSecret ?? "";

  async function copy() {
    if (!secret) return;
    try {
      await navigator.clipboard.writeText(secret);
      toast.success(t("webhook_secret_copied"));
    } catch {
      toast.error(t("webhook_secret_copy_failed"));
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <Card className="border-success/30 bg-success/5">
        <CardContent className="p-4 text-sm text-success-foreground">
          <strong>{t("webhook_screen_title")}</strong> {template.name} ({template.slug})
        </CardContent>
      </Card>

      <Card className="border-warning/40 bg-warning/5">
        <CardHeader className="flex-row items-start gap-3 space-y-0 pb-2">
          <AlertTriangle className="h-5 w-5 shrink-0 text-warning" />
          <div>
            <CardTitle className="text-sm">
              {t("webhook_secret_one_time_title")}
            </CardTitle>
            <p className="mt-1 text-xs text-muted-foreground">
              {t("webhook_secret_one_time_description")}
            </p>
          </div>
        </CardHeader>
        {secret ? (
          <CardContent className="space-y-2 pt-0">
            <div className="rounded-md border border-border bg-card">
              <div className="flex items-center justify-between border-b border-border px-3 py-1.5">
                <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                  {t("webhook_secret_label")}
                </span>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={copy}
                >
                  <Copy className="mr-2 h-4 w-4" />
                  {t("webhook_secret_copy")}
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
              {t("webhook_secret_missing")}
            </p>
          </CardContent>
        )}
      </Card>

      <div className="flex justify-end">
        <Button asChild>
          <Link href={`/templates/${template.id}`}>
            {t("webhook_go_to_detail")}
            <ArrowRight className="ml-2 h-4 w-4" />
          </Link>
        </Button>
      </div>
    </div>
  );
}
