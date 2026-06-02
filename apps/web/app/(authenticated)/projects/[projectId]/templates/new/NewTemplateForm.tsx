"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
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
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [created, setCreated] = useState<TemplateDetail | null>(null);

  const slugError = useMemo(() => {
    if (!slug) return null;
    return SLUG_RE.test(slug)
      ? null
      : "Slug debe iniciar con letra, lowercase con guiones (max 31 chars).";
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
    setError(null);
    setLoading(true);
    try {
      const body: CreateTemplateRequest = {
        slug,
        name: name.trim(),
        description: description.trim() ? description.trim() : null,
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
        {
          method: "POST",
          body: JSON.stringify(body),
        },
      );
      setCreated(response);
    } catch (e) {
      if (e instanceof ApiError) {
        const body = e.body as { message?: string; detail?: string } | undefined;
        setError(body?.message ?? body?.detail ?? `Error ${e.status}`);
      } else {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
    } finally {
      setLoading(false);
    }
  }

  if (created) {
    return (
      <WebhookSecretScreen
        template={created}
        onContinue={() => router.refresh()}
      />
    );
  }

  return (
    <form
      onSubmit={onSubmit}
      className="flex flex-col gap-5 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6"
    >
      <div className="grid grid-cols-1 gap-5 md:grid-cols-2">
        <Field
          label="Slug"
          required
          hint="URL-friendly, lowercase con guiones (max 31)."
        >
          <input
            type="text"
            value={slug}
            onChange={(e) => setSlug(e.target.value.toLowerCase())}
            placeholder="api-service"
            className={`${inputClass} font-mono text-xs`}
            maxLength={31}
            required
          />
          {slugError && (
            <span className="text-[11px] text-rose-400">{slugError}</span>
          )}
        </Field>
        <Field label="Nombre" required>
          <input
            type="text"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="API Service"
            className={inputClass}
            required
          />
        </Field>
      </div>

      <Field label="Descripcion">
        <textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          rows={2}
          className={inputClass}
        />
      </Field>

      <fieldset className="flex flex-col gap-4 rounded-xl border border-zinc-800 bg-zinc-950/40 p-4">
        <legend className="px-2 text-xs uppercase tracking-wider text-zinc-500">
          Source
        </legend>
        <Field label="Git repo URL" required>
          <input
            type="text"
            value={gitRepoUrl}
            onChange={(e) => setGitRepoUrl(e.target.value)}
            placeholder="git@github.com:org/repo.git"
            className={`${inputClass} font-mono text-xs`}
            required
          />
        </Field>
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <Field label="Branch" required>
            <input
              type="text"
              value={branch}
              onChange={(e) => setBranch(e.target.value)}
              placeholder="main"
              className={`${inputClass} font-mono text-xs`}
              required
            />
          </Field>
          <Field label="Base directory" hint="Subdir dentro del repo. Default `.`">
            <input
              type="text"
              value={baseDirectory}
              onChange={(e) => setBaseDirectory(e.target.value)}
              placeholder="."
              className={`${inputClass} font-mono text-xs`}
            />
          </Field>
        </div>
        <Field
          label="Watch paths"
          required
          hint="Globs (uno por linea). Solo cambios en estos paths disparan build."
        >
          <textarea
            value={watchPathsRaw}
            onChange={(e) => setWatchPathsRaw(e.target.value)}
            rows={3}
            className={`${inputClass} font-mono text-xs`}
            placeholder="**"
          />
        </Field>
      </fieldset>

      <fieldset className="flex flex-col gap-4 rounded-xl border border-zinc-800 bg-zinc-950/40 p-4">
        <legend className="px-2 text-xs uppercase tracking-wider text-zinc-500">
          Build
        </legend>
        <Field label="Build type" required>
          <select
            value={buildType}
            onChange={(e) => setBuildType(e.target.value as BuildType)}
            className={inputClass}
          >
            <option value="Dockerfile">Dockerfile</option>
            <option value="DockerCompose">DockerCompose</option>
            <option value="Nixpacks">Nixpacks</option>
          </select>
        </Field>

        {buildType === "Dockerfile" && (
          <Field label="Dockerfile path" required>
            <input
              type="text"
              value={dockerfilePath}
              onChange={(e) => setDockerfilePath(e.target.value)}
              placeholder="Dockerfile"
              className={`${inputClass} font-mono text-xs`}
              required
            />
          </Field>
        )}
        {buildType === "DockerCompose" && (
          <Field label="Compose file path" required>
            <input
              type="text"
              value={composeFilePath}
              onChange={(e) => setComposeFilePath(e.target.value)}
              placeholder="docker-compose.yml"
              className={`${inputClass} font-mono text-xs`}
              required
            />
          </Field>
        )}
        {buildType === "Nixpacks" && (
          <p className="rounded-lg border border-zinc-800 bg-zinc-900/40 px-3 py-2 text-[11px] text-zinc-400">
            Nixpacks detecta el stack automaticamente. No requiere Dockerfile.
          </p>
        )}

        <div>
          <div className="flex items-center justify-between">
            <span className="text-xs uppercase tracking-wider text-zinc-500">
              Build args
            </span>
            <button
              type="button"
              onClick={addArg}
              className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-300 transition hover:bg-zinc-800"
            >
              Anadir
            </button>
          </div>
          {buildArgs.length === 0 ? (
            <p className="mt-2 text-[11px] text-zinc-500">
              Sin args. Anade pares clave/valor para inyectar al build.
            </p>
          ) : (
            <ul className="mt-2 flex flex-col gap-2">
              {buildArgs.map((arg, i) => (
                <li key={i} className="flex gap-2">
                  <input
                    type="text"
                    value={arg.key}
                    onChange={(e) => setArg(i, { key: e.target.value })}
                    placeholder="KEY"
                    className={`${inputClass} w-40 font-mono text-xs`}
                  />
                  <input
                    type="text"
                    value={arg.value}
                    onChange={(e) => setArg(i, { value: e.target.value })}
                    placeholder="value"
                    className={`${inputClass} flex-1 font-mono text-xs`}
                  />
                  <button
                    type="button"
                    onClick={() => removeArg(i)}
                    aria-label="Borrar arg"
                    className="rounded-full border border-zinc-700 px-3 text-xs text-zinc-300 transition hover:bg-zinc-800"
                  >
                    Quitar
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </fieldset>

      {error && (
        <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
          {error}
        </p>
      )}

      <div className="flex justify-end gap-3">
        <button
          type="button"
          onClick={() => router.push(`/projects/${projectId}`)}
          className="rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-300 transition hover:bg-zinc-800"
        >
          Cancelar
        </button>
        <button
          type="submit"
          disabled={!canSubmit}
          className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
        >
          {loading ? "Creando..." : "Crear template"}
        </button>
      </div>
    </form>
  );
}

function WebhookSecretScreen({
  template,
  onContinue,
}: {
  template: TemplateDetail;
  onContinue: () => void;
}) {
  const secret = template.webhookSecret ?? "";
  const [copied, setCopied] = useState(false);

  async function copy() {
    if (!secret) return;
    try {
      await navigator.clipboard.writeText(secret);
      setCopied(true);
      setTimeout(() => setCopied(false), 1800);
    } catch {
      // clipboard may not be available
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <header>
        <p className="text-xs uppercase tracking-wider text-emerald-400">
          Template creado
        </p>
        <h2 className="mt-1 text-2xl font-semibold">{template.name}</h2>
        <p className="mt-1 font-mono text-xs text-zinc-500">{template.slug}</p>
      </header>

      <div className="rounded-xl border border-amber-500/40 bg-amber-500/10 p-4 text-sm text-amber-200">
        <p className="font-medium">
          El webhook secret solo se muestra esta vez.
        </p>
        <p className="mt-1 text-amber-200/80">
          Copialo y configuralo en tu provider Git (GitHub/Gitlab) ahora. Si lo
          pierdes tendras que rotarlo desde el detalle del template.
        </p>
      </div>

      {secret ? (
        <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40">
          <div className="flex items-center justify-between border-b border-zinc-800 px-4 py-2">
            <span className="text-xs uppercase tracking-wider text-zinc-500">
              Webhook secret
            </span>
            <button
              type="button"
              onClick={copy}
              className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-300 transition hover:bg-zinc-800"
            >
              {copied ? "Copiado" : "Copiar"}
            </button>
          </div>
          <pre className="overflow-x-auto whitespace-nowrap px-4 py-3 font-mono text-xs text-zinc-200">
            {secret}
          </pre>
        </div>
      ) : (
        <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
          La API no devolvio webhook secret. Verifica el contrato.
        </p>
      )}

      <div className="flex justify-end gap-3">
        <button
          type="button"
          onClick={onContinue}
          className="rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-300 transition hover:bg-zinc-800"
        >
          Volver
        </button>
        <Link
          href={`/templates/${template.id}`}
          className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
        >
          Ir al detalle
        </Link>
      </div>
    </div>
  );
}

const inputClass =
  "rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-zinc-100 outline-none focus:border-emerald-500";

function Field({
  label,
  required,
  hint,
  children,
}: {
  label: string;
  required?: boolean;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <label className="flex flex-col gap-1 text-sm text-zinc-300">
      <span>
        {label}
        {required && <span className="text-rose-400"> *</span>}
      </span>
      {children}
      {hint && <span className="text-xs text-zinc-500">{hint}</span>}
    </label>
  );
}
