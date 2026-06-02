"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { ApiError, api } from "@/lib/api";
import type {
  CreateEnvironmentDefinitionRequest,
  EnvironmentDefinitionDto,
  ReorderEnvironmentDefinitionsRequest,
} from "@/lib/types";

const SLUG_RE = /^[a-z][a-z0-9-]{0,30}[a-z0-9]$/;

export function EnvironmentsManager({
  initial,
}: {
  initial: EnvironmentDefinitionDto[];
}) {
  const router = useRouter();
  // El estado local refleja el orden visualmente; las llamadas a la API se
  // confirman con router.refresh() para volver a leer la fuente de verdad.
  const sortedInitial = useMemo(
    () => [...initial].sort((a, b) => a.order - b.order),
    [initial],
  );
  const [items, setItems] = useState<EnvironmentDefinitionDto[]>(sortedInitial);
  const [actionError, setActionError] = useState<string | null>(null);
  const [pending, setPending] = useState<string | null>(null);

  // ---- Formulario inline ----
  const [newSlug, setNewSlug] = useState("");
  const [newDisplay, setNewDisplay] = useState("");
  const [createError, setCreateError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const slugError = useMemo(() => {
    const trimmed = newSlug.trim().toLowerCase();
    if (!trimmed) return null;
    if (!SLUG_RE.test(trimmed)) {
      return "Slug lowercase alfanumerico con guiones (2-32 chars, sin guion al inicio/fin).";
    }
    if (items.some((i) => i.slug === trimmed)) {
      return "Ya existe un ambiente con ese slug.";
    }
    return null;
  }, [newSlug, items]);

  const canCreate =
    !creating &&
    newSlug.trim().length > 0 &&
    !slugError &&
    newDisplay.trim().length > 0;

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    if (!canCreate) return;
    setCreateError(null);
    setCreating(true);
    try {
      const body: CreateEnvironmentDefinitionRequest = {
        slug: newSlug.trim().toLowerCase(),
        displayName: newDisplay.trim(),
        order: null,
      };
      await api("/api/settings/environments/", {
        method: "POST",
        body: JSON.stringify(body),
      });
      setNewSlug("");
      setNewDisplay("");
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const body = e.body as
          | { message?: string; detail?: string }
          | undefined;
        setCreateError(
          body?.message ?? body?.detail ?? `Error ${e.status}`,
        );
      } else {
        setCreateError(e instanceof Error ? e.message : "Error desconocido");
      }
    } finally {
      setCreating(false);
    }
  }

  async function handleDelete(id: string, slug: string) {
    const ok = window.confirm(
      `Borrar el ambiente "${slug}"?\n\nSi algun proyecto lo referencia, esos referers quedaran apuntando a un slug invalido.`,
    );
    if (!ok) return;
    setActionError(null);
    setPending(id);
    try {
      await api(`/api/settings/environments/${encodeURIComponent(id)}`, {
        method: "DELETE",
      });
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const body = e.body as
          | { message?: string; detail?: string }
          | undefined;
        setActionError(
          body?.message ?? body?.detail ?? `Error ${e.status}`,
        );
      } else {
        setActionError(e instanceof Error ? e.message : "Error desconocido");
      }
    } finally {
      setPending(null);
    }
  }

  async function applyReorder(nextOrder: EnvironmentDefinitionDto[]) {
    setActionError(null);
    setPending("reorder");
    // Optimistic update: la UI ya refleja el nuevo orden mientras llamamos.
    setItems(nextOrder);
    try {
      const body: ReorderEnvironmentDefinitionsRequest = {
        ids: nextOrder.map((i) => i.id),
      };
      await api("/api/settings/environments/reorder", {
        method: "POST",
        body: JSON.stringify(body),
      });
      router.refresh();
    } catch (e) {
      // Si falla, revertimos visualmente al orden previo.
      setItems(sortedInitial);
      if (e instanceof ApiError) {
        const body = e.body as
          | { message?: string; detail?: string }
          | undefined;
        setActionError(
          body?.message ?? body?.detail ?? `Error ${e.status}`,
        );
      } else {
        setActionError(e instanceof Error ? e.message : "Error desconocido");
      }
    } finally {
      setPending(null);
    }
  }

  function moveUp(index: number) {
    if (index <= 0) return;
    const next = [...items];
    [next[index - 1], next[index]] = [next[index], next[index - 1]];
    void applyReorder(next);
  }

  function moveDown(index: number) {
    if (index >= items.length - 1) return;
    const next = [...items];
    [next[index], next[index + 1]] = [next[index + 1], next[index]];
    void applyReorder(next);
  }

  return (
    <div className="flex flex-col gap-6">
      <section className="overflow-hidden rounded-2xl border border-zinc-800 bg-zinc-900/40">
        {items.length === 0 ? (
          <div className="p-12 text-center">
            <h2 className="text-xl font-semibold text-zinc-100">
              Aun sin ambientes
            </h2>
            <p className="mt-2 text-sm text-zinc-500">
              Crea el primero abajo. La convencion es
              {" "}<span className="font-mono">preview</span> → <span className="font-mono">test</span>{" "}
              → <span className="font-mono">staging</span> →{" "}
              <span className="font-mono">production</span>.
            </p>
          </div>
        ) : (
          <table className="w-full text-left text-sm">
            <thead className="bg-zinc-900/60 text-xs uppercase tracking-wider text-zinc-500">
              <tr>
                <th className="px-4 py-3 w-16">Orden</th>
                <th className="px-4 py-3">Slug</th>
                <th className="px-4 py-3">Display</th>
                <th className="px-4 py-3 text-right">Acciones</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-800">
              {items.map((env, i) => {
                const isPending = pending === env.id || pending === "reorder";
                return (
                  <tr
                    key={env.id}
                    className="transition hover:bg-zinc-900/60"
                  >
                    <td className="px-4 py-3 align-top">
                      <div className="flex flex-col gap-1">
                        <button
                          type="button"
                          onClick={() => moveUp(i)}
                          disabled={i === 0 || isPending}
                          aria-label="Mover arriba"
                          className="rounded border border-zinc-700 px-2 py-0.5 text-[11px] text-zinc-300 transition hover:bg-zinc-800 disabled:opacity-30"
                        >
                          ↑
                        </button>
                        <button
                          type="button"
                          onClick={() => moveDown(i)}
                          disabled={i === items.length - 1 || isPending}
                          aria-label="Mover abajo"
                          className="rounded border border-zinc-700 px-2 py-0.5 text-[11px] text-zinc-300 transition hover:bg-zinc-800 disabled:opacity-30"
                        >
                          ↓
                        </button>
                      </div>
                    </td>
                    <td className="px-4 py-3 align-top">
                      <span className="rounded border border-zinc-800 bg-zinc-950 px-2 py-0.5 font-mono text-[11px] text-zinc-200">
                        {env.slug}
                      </span>
                      <div className="mt-0.5 font-mono text-[10px] text-zinc-500">
                        {env.id}
                      </div>
                    </td>
                    <td className="px-4 py-3 align-top text-zinc-100">
                      {env.displayName}
                    </td>
                    <td className="px-4 py-3 text-right align-top">
                      <button
                        type="button"
                        onClick={() => handleDelete(env.id, env.slug)}
                        disabled={isPending}
                        className="rounded-full border border-rose-500/30 px-3 py-1 text-xs font-medium text-rose-300 transition hover:bg-rose-500/10 disabled:opacity-50"
                      >
                        {pending === env.id ? "Borrando..." : "Borrar"}
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </section>

      {actionError && (
        <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
          {actionError}
        </p>
      )}

      <form
        onSubmit={handleCreate}
        className="flex flex-col gap-4 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5"
      >
        <h3 className="text-sm font-semibold text-zinc-100">
          Nuevo ambiente
        </h3>
        <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
          <Field label="Slug" required>
            <input
              type="text"
              value={newSlug}
              onChange={(e) => setNewSlug(e.target.value.toLowerCase())}
              maxLength={32}
              placeholder="preview"
              className={`${inputClass} font-mono text-xs`}
              required
            />
            {slugError && (
              <span className="text-[11px] text-rose-400">{slugError}</span>
            )}
          </Field>
          <Field label="Display name" required>
            <input
              type="text"
              value={newDisplay}
              onChange={(e) => setNewDisplay(e.target.value)}
              maxLength={100}
              placeholder="Preview"
              className={inputClass}
              required
            />
          </Field>
          <div className="flex items-end">
            <button
              type="submit"
              disabled={!canCreate}
              className="w-full rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
            >
              {creating ? "Creando..." : "Crear ambiente"}
            </button>
          </div>
        </div>
        {createError && (
          <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
            {createError}
          </p>
        )}
        <p className="text-xs text-zinc-500">
          Aethra aplica order = max(order) + 1 automaticamente; despues puedes
          reordenarlo con las flechas.
        </p>
      </form>
    </div>
  );
}

const inputClass =
  "rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-zinc-100 outline-none focus:border-emerald-500";

function Field({
  label,
  required,
  children,
}: {
  label: string;
  required?: boolean;
  children: React.ReactNode;
}) {
  return (
    <label className="flex flex-col gap-1 text-sm text-zinc-300">
      <span>
        {label}
        {required && <span className="text-rose-400"> *</span>}
      </span>
      {children}
    </label>
  );
}
