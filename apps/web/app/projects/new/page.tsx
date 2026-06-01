"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { ApiError, api } from "@/lib/api";
import type { CreateProjectV2Request, ProjectDetailV2 } from "@/lib/types";

const SLUG_RE = /^[a-z][a-z0-9-]{0,30}$/;

const COLOR_SWATCHES = [
  "#10b981",
  "#06b6d4",
  "#3b82f6",
  "#6366f1",
  "#a855f7",
  "#ec4899",
  "#f43f5e",
  "#f97316",
  "#facc15",
  "#84cc16",
  "#22c55e",
  "#14b8a6",
];

const ICON_OPTIONS = [
  "cube",
  "folder",
  "rocket",
  "globe",
  "server",
  "database",
  "spark",
  "shield",
  "flame",
  "leaf",
];

export default function NewProjectPage() {
  const router = useRouter();
  const [slug, setSlug] = useState("");
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [color, setColor] = useState(COLOR_SWATCHES[0]);
  const [icon, setIcon] = useState(ICON_OPTIONS[0]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const slugError = useMemo(() => {
    if (!slug) return null;
    return SLUG_RE.test(slug)
      ? null
      : "Slug debe iniciar con letra, lowercase con guiones (max 31 chars).";
  }, [slug]);

  const canSubmit = !loading && slug && !slugError && name.trim().length > 0;

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;
    setError(null);
    setLoading(true);
    try {
      const body: CreateProjectV2Request = {
        slug,
        name: name.trim(),
        description: description.trim() ? description.trim() : null,
        color,
        icon,
      };
      const response = await api<ProjectDetailV2>("/api/projects", {
        method: "POST",
        body: JSON.stringify(body),
      });
      router.push(`/projects/${response.id}`);
      router.refresh();
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

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-xl flex-col gap-6">
        <header>
          <h1 className="text-3xl font-semibold">Nuevo proyecto</h1>
          <p className="mt-1 text-sm text-zinc-500">
            Una agrupacion logica para tus templates y clients multi-tenant.
          </p>
        </header>

        <form
          onSubmit={onSubmit}
          className="flex flex-col gap-5 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6"
        >
          <Field
            label="Slug"
            required
            hint="Identificador URL-friendly: lowercase, alfanumerico + guiones."
          >
            <input
              type="text"
              value={slug}
              onChange={(e) => setSlug(e.target.value.toLowerCase())}
              placeholder="mi-proyecto"
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
              placeholder="Mi proyecto"
              className={inputClass}
              required
            />
          </Field>

          <Field label="Descripcion">
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
              className={inputClass}
            />
          </Field>

          <Field label="Color">
            <div className="flex flex-wrap items-center gap-2">
              {COLOR_SWATCHES.map((c) => (
                <button
                  key={c}
                  type="button"
                  onClick={() => setColor(c)}
                  aria-label={`Color ${c}`}
                  className={`size-7 rounded-full border-2 transition ${
                    color === c
                      ? "border-zinc-100 ring-2 ring-emerald-500/40"
                      : "border-zinc-700 hover:border-zinc-500"
                  }`}
                  style={{ backgroundColor: c }}
                />
              ))}
              <input
                type="color"
                value={color}
                onChange={(e) => setColor(e.target.value)}
                className="h-7 w-9 cursor-pointer rounded-md border border-zinc-700 bg-zinc-950"
                aria-label="Color custom"
              />
              <span className="font-mono text-xs text-zinc-500">{color}</span>
            </div>
          </Field>

          <Field label="Icon">
            <select
              value={icon}
              onChange={(e) => setIcon(e.target.value)}
              className={inputClass}
            >
              {ICON_OPTIONS.map((i) => (
                <option key={i} value={i}>
                  {i}
                </option>
              ))}
            </select>
          </Field>

          {error && (
            <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
              {error}
            </p>
          )}

          <div className="flex justify-end gap-3">
            <button
              type="button"
              onClick={() => router.push("/projects")}
              className="rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-300 transition hover:bg-zinc-800"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={!canSubmit}
              className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
            >
              {loading ? "Creando..." : "Crear proyecto"}
            </button>
          </div>
        </form>
      </div>
    </main>
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
