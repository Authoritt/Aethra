"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { ApiError, api } from "@/lib/api";
import type { ClientDetail, CreateClientRequest } from "@/lib/types";

const SLUG_RE = /^[a-z][a-z0-9-]{0,30}$/;

export function NewClientForm({ projectId }: { projectId: string }) {
  const router = useRouter();
  const [slug, setSlug] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [description, setDescription] = useState("");
  const [contactEmail, setContactEmail] = useState("");
  const [billingTag, setBillingTag] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const slugError = useMemo(() => {
    if (!slug) return null;
    return SLUG_RE.test(slug)
      ? null
      : "Slug debe iniciar con letra, lowercase con guiones (max 31 chars).";
  }, [slug]);

  const canSubmit =
    !loading && slug && !slugError && displayName.trim().length > 0;

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;
    setError(null);
    setLoading(true);
    try {
      const body: CreateClientRequest = {
        slug,
        displayName: displayName.trim(),
        description: description.trim() ? description.trim() : null,
        contactEmail: contactEmail.trim() ? contactEmail.trim() : null,
        billingTag: billingTag.trim() ? billingTag.trim() : null,
      };
      const response = await api<ClientDetail>(
        `/api/projects/${encodeURIComponent(projectId)}/clients`,
        {
          method: "POST",
          body: JSON.stringify(body),
        },
      );
      router.push(`/clients/${response.id}`);
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
    <form
      onSubmit={onSubmit}
      className="flex flex-col gap-5 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6"
    >
      <Field label="Slug" required hint="URL-friendly, lowercase con guiones.">
        <input
          type="text"
          value={slug}
          onChange={(e) => setSlug(e.target.value.toLowerCase())}
          placeholder="acme-corp"
          className={`${inputClass} font-mono text-xs`}
          maxLength={31}
          required
        />
        {slugError && (
          <span className="text-[11px] text-rose-400">{slugError}</span>
        )}
      </Field>

      <Field label="Display name" required>
        <input
          type="text"
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
          placeholder="ACME Corp"
          className={inputClass}
          required
        />
      </Field>

      <Field label="Descripcion">
        <textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          rows={2}
          className={inputClass}
        />
      </Field>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <Field label="Contact email">
          <input
            type="email"
            value={contactEmail}
            onChange={(e) => setContactEmail(e.target.value)}
            placeholder="ops@acme.example"
            className={inputClass}
          />
        </Field>
        <Field
          label="Billing tag"
          hint="Identificador opcional para reportes de costo."
        >
          <input
            type="text"
            value={billingTag}
            onChange={(e) => setBillingTag(e.target.value)}
            placeholder="cost-center-A"
            className={`${inputClass} font-mono text-xs`}
          />
        </Field>
      </div>

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
          {loading ? "Creando..." : "Crear client"}
        </button>
      </div>
    </form>
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
