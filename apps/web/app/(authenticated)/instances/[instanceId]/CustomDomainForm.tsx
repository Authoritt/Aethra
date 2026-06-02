"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";
import type {
  SetCustomDomainRequest,
  SetCustomDomainResponse,
} from "@/lib/types";

export function CustomDomainForm({
  instanceId,
  initialDomain,
}: {
  instanceId: string;
  initialDomain: string | null;
}) {
  const router = useRouter();
  const [domain, setDomain] = useState(initialDomain ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [okMsg, setOkMsg] = useState<string | null>(null);

  async function submit(payload: string | null) {
    setError(null);
    setOkMsg(null);
    setBusy(true);
    try {
      const body: SetCustomDomainRequest = { customDomain: payload };
      const response = await api<SetCustomDomainResponse>(
        `/api/instances/${encodeURIComponent(instanceId)}/custom-domain`,
        {
          method: "POST",
          body: JSON.stringify(body),
        },
      );
      setDomain(response.customDomain ?? "");
      setOkMsg(
        response.customDomain
          ? "Custom domain guardado."
          : "Custom domain limpio; ahora se usa el auto-hostname.",
      );
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const body = e.body as { message?: string; detail?: string } | undefined;
        setError(body?.message ?? body?.detail ?? `Error ${e.status}`);
      } else {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
    } finally {
      setBusy(false);
    }
  }

  function onSave(e: React.FormEvent) {
    e.preventDefault();
    const trimmed = domain.trim();
    void submit(trimmed.length > 0 ? trimmed : null);
  }

  function onClear() {
    if (!initialDomain && !domain) return;
    const ok = window.confirm(
      "Quitar el custom domain? Las requests volveran a resolverse por el auto-hostname.",
    );
    if (!ok) return;
    setDomain("");
    void submit(null);
  }

  return (
    <form onSubmit={onSave} className="flex flex-col gap-2">
      <label className="text-xs uppercase tracking-wider text-zinc-500">
        Custom domain
      </label>
      <div className="flex gap-2">
        <input
          type="text"
          value={domain}
          onChange={(e) => setDomain(e.target.value)}
          placeholder="app.mi-cliente.com"
          className="min-w-0 flex-1 rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 font-mono text-xs text-zinc-100 outline-none focus:border-emerald-500"
        />
        <button
          type="submit"
          disabled={busy}
          className="rounded-full bg-emerald-500 px-4 py-2 text-xs font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
        >
          Guardar
        </button>
        <button
          type="button"
          onClick={onClear}
          disabled={busy || (!initialDomain && !domain)}
          className="rounded-full border border-zinc-700 px-4 py-2 text-xs text-zinc-300 transition hover:bg-zinc-800 disabled:opacity-50"
        >
          Limpiar
        </button>
      </div>
      {error && (
        <p className="text-[11px] text-rose-400">{error}</p>
      )}
      {okMsg && !error && (
        <p className="text-[11px] text-emerald-300">{okMsg}</p>
      )}
    </form>
  );
}
