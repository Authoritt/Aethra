"use client";

import { useEffect, useState, useTransition } from "react";
import { api, ApiError } from "@/lib/api";
import type {
  NoteScopeType,
  PinnedFactDto,
  UpsertPinnedFactRequest,
} from "@/lib/types";

/**
 * Panel CRUD de pinned facts del scope. Los secretos se enmascaran por default;
 * el botón "Revelar" recarga la lista con <c>reveal=true</c> para obtener el plaintext.
 */
export function PinnedFactsPanel({
  scopeType,
  scopeId,
}: {
  scopeType: NoteScopeType;
  scopeId: string;
}) {
  const [facts, setFacts] = useState<PinnedFactDto[]>([]);
  const [revealed, setRevealed] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function reload(reveal: boolean) {
    setLoading(true);
    setError(null);
    try {
      const list = await api<PinnedFactDto[]>(
        `/api/pinned-facts/?scope_type=${scopeType}&scope_id=${encodeURIComponent(
          scopeId,
        )}&reveal=${reveal ? "true" : "false"}`,
      );
      setFacts(list);
      setRevealed(reveal);
    } catch (e) {
      if (e instanceof ApiError) {
        const b = e.body as { message?: string } | undefined;
        setError(b?.message ?? `Error ${e.status}`);
      } else {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setLoading(true);
      try {
        const list = await api<PinnedFactDto[]>(
          `/api/pinned-facts/?scope_type=${scopeType}&scope_id=${encodeURIComponent(
            scopeId,
          )}&reveal=false`,
        );
        if (cancelled) return;
        setFacts(list);
        setRevealed(false);
      } catch (e) {
        if (cancelled) return;
        if (e instanceof ApiError) {
          const b = e.body as { message?: string } | undefined;
          setError(b?.message ?? `Error ${e.status}`);
        } else {
          setError(e instanceof Error ? e.message : "Error desconocido");
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }
    load();
    return () => {
      cancelled = true;
    };
  }, [scopeType, scopeId]);

  function onUpserted(fact: PinnedFactDto) {
    setFacts((arr) => {
      const idx = arr.findIndex((f) => f.id === fact.id || f.key === fact.key);
      if (idx >= 0) {
        const next = [...arr];
        next[idx] = fact;
        return next.sort((a, b) => a.key.localeCompare(b.key));
      }
      return [...arr, fact].sort((a, b) => a.key.localeCompare(b.key));
    });
  }

  function onDeleted(id: string) {
    setFacts((arr) => arr.filter((f) => f.id !== id));
  }

  return (
    <section className="flex flex-col gap-3">
      <div className="flex items-center justify-between">
        <h2 className="text-sm uppercase tracking-wider text-zinc-500">
          Pinned facts
        </h2>
        <button
          type="button"
          onClick={() => reload(!revealed)}
          className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-300 hover:bg-zinc-800"
        >
          {revealed ? "Ocultar secretos" : "Revelar secretos"}
        </button>
      </div>

      <PinnedFactForm
        scopeType={scopeType}
        scopeId={scopeId}
        onUpserted={onUpserted}
      />

      {loading && <p className="text-xs text-zinc-500">Cargando...</p>}
      {error && (
        <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-xs text-rose-300">
          {error}
        </p>
      )}
      {!loading && facts.length === 0 && (
        <p className="text-xs text-zinc-500">Sin facts fijados todavía.</p>
      )}
      {facts.length > 0 && (
        <ul className="grid grid-cols-1 gap-2 md:grid-cols-2">
          {facts.map((f) => (
            <PinnedFactRow
              key={f.id}
              fact={f}
              revealed={revealed}
              onDeleted={onDeleted}
            />
          ))}
        </ul>
      )}
    </section>
  );
}

function PinnedFactForm({
  scopeType,
  scopeId,
  onUpserted,
}: {
  scopeType: NoteScopeType;
  scopeId: string;
  onUpserted: (fact: PinnedFactDto) => void;
}) {
  const [key, setKey] = useState("");
  const [value, setValue] = useState("");
  const [description, setDescription] = useState("");
  const [isSecret, setIsSecret] = useState(true);
  const [isPending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);

  function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    const payload: UpsertPinnedFactRequest = {
      scopeType,
      scopeId,
      key,
      value,
      isSecret,
      description: description.trim() ? description : undefined,
    };
    startTransition(async () => {
      try {
        const fact = await api<PinnedFactDto>("/api/pinned-facts/", {
          method: "PUT",
          body: JSON.stringify(payload),
        });
        onUpserted(fact);
        setKey("");
        setValue("");
        setDescription("");
      } catch (e) {
        if (e instanceof ApiError) {
          const b = e.body as { message?: string } | undefined;
          setError(b?.message ?? `Error ${e.status}`);
        } else {
          setError(e instanceof Error ? e.message : "Error desconocido");
        }
      }
    });
  }

  return (
    <form
      onSubmit={onSubmit}
      className="grid grid-cols-1 gap-2 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-4 md:grid-cols-[1fr_1fr_auto]"
    >
      <input
        type="text"
        placeholder="key (ej. admin_password)"
        value={key}
        onChange={(e) => setKey(e.target.value)}
        className="rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-1.5 font-mono text-sm text-zinc-100 outline-none focus:border-emerald-500"
        required
        maxLength={128}
        pattern="[A-Za-z0-9_.\-]+"
      />
      <input
        type={isSecret ? "password" : "text"}
        placeholder="valor"
        value={value}
        onChange={(e) => setValue(e.target.value)}
        className="rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-1.5 font-mono text-sm text-zinc-100 outline-none focus:border-emerald-500"
        required
      />
      <button
        type="submit"
        disabled={isPending || !key.trim()}
        className="rounded-full bg-emerald-500 px-4 py-1.5 text-xs font-medium text-emerald-950 hover:bg-emerald-400 disabled:opacity-50"
      >
        {isPending ? "Guardando..." : "Guardar"}
      </button>
      <input
        type="text"
        placeholder="descripción (opcional)"
        value={description}
        onChange={(e) => setDescription(e.target.value)}
        className="rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-1.5 text-sm text-zinc-100 outline-none focus:border-emerald-500 md:col-span-2"
        maxLength={500}
      />
      <label className="flex items-center gap-2 text-xs text-zinc-400">
        <input
          type="checkbox"
          checked={isSecret}
          onChange={(e) => setIsSecret(e.target.checked)}
        />
        Es secreto
      </label>
      {error && (
        <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-xs text-rose-300 md:col-span-3">
          {error}
        </p>
      )}
    </form>
  );
}

function PinnedFactRow({
  fact,
  revealed,
  onDeleted,
}: {
  fact: PinnedFactDto;
  revealed: boolean;
  onDeleted: (id: string) => void;
}) {
  const [isPending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);

  function remove() {
    if (!confirm(`¿Eliminar el fact "${fact.key}"?`)) {
      return;
    }
    startTransition(async () => {
      try {
        await api<void>(`/api/pinned-facts/${fact.id}`, { method: "DELETE" });
        onDeleted(fact.id);
      } catch (e) {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
    });
  }

  async function copyValue() {
    try {
      await navigator.clipboard.writeText(fact.value);
    } catch {
      // Silencio: clipboard puede fallar en HTTP sin contexto seguro.
    }
  }

  const masked = fact.isSecret && !revealed;

  return (
    <li className="flex flex-col gap-1 rounded-xl border border-zinc-800 bg-zinc-900/40 px-3 py-2">
      <div className="flex items-center justify-between">
        <span className="font-mono text-xs text-zinc-200">{fact.key}</span>
        <div className="flex gap-2 text-[10px]">
          {fact.isSecret && (
            <span className="rounded-full bg-amber-500/10 px-2 py-0.5 text-amber-400">
              secret
            </span>
          )}
          <button
            type="button"
            onClick={copyValue}
            className="rounded-full border border-zinc-700 px-2 py-0.5 text-zinc-300 hover:bg-zinc-800"
            disabled={masked}
            title={masked ? "Revelar primero" : "Copiar"}
          >
            Copiar
          </button>
          <button
            type="button"
            onClick={remove}
            disabled={isPending}
            className="rounded-full border border-rose-500/40 px-2 py-0.5 text-rose-300 hover:bg-rose-500/10"
          >
            Eliminar
          </button>
        </div>
      </div>
      <div className="break-all font-mono text-xs text-zinc-500">{fact.value}</div>
      {fact.description && (
        <div className="text-[10px] text-zinc-600">{fact.description}</div>
      )}
      {error && <div className="text-[10px] text-rose-400">{error}</div>}
    </li>
  );
}
