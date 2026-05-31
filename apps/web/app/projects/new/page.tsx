"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";
import type { CreateProjectResponse } from "@/lib/types";

export default function NewProjectPage() {
  const router = useRouter();
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [description, setDescription] = useState("");
  const [color, setColor] = useState("#10b981");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const response = await api<CreateProjectResponse>("/api/projects/", {
        method: "POST",
        body: JSON.stringify({
          name,
          slug: slug || undefined,
          description: description || undefined,
          color,
        }),
      });
      router.push(`/projects/${response.project.id}`);
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const body = e.body as { detail?: string } | undefined;
        setError(body?.detail ?? `Error ${e.status}`);
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
            Una agrupación lógica que contendrá environments y applications.
          </p>
        </header>

        <form
          onSubmit={onSubmit}
          className="flex flex-col gap-5 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6"
        >
          <Field label="Nombre" required>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Mi sitio personal"
              className={inputClass}
              required
            />
          </Field>

          <Field
            label="Slug"
            hint="Identificador URL-friendly. Si lo dejas vacío, lo sugerimos desde el nombre."
          >
            <input
              type="text"
              value={slug}
              onChange={(e) => setSlug(e.target.value)}
              placeholder="mi-sitio-personal"
              className={inputClass}
              pattern="[a-z0-9]+(-[a-z0-9]+)*"
            />
          </Field>

          <Field label="Descripción">
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
              className={inputClass}
            />
          </Field>

          <Field label="Color">
            <div className="flex items-center gap-3">
              <input
                type="color"
                value={color}
                onChange={(e) => setColor(e.target.value)}
                className="h-9 w-16 cursor-pointer rounded-md border border-zinc-700 bg-zinc-950"
              />
              <span className="font-mono text-xs text-zinc-500">{color}</span>
            </div>
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
              disabled={loading}
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
