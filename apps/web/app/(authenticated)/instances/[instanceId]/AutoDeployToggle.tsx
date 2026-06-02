"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";

export function AutoDeployToggle({
  instanceId,
  initial,
}: {
  instanceId: string;
  initial: boolean;
}) {
  const router = useRouter();
  const [enabled, setEnabled] = useState(initial);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function toggle() {
    const target = !enabled;
    setError(null);
    setBusy(true);
    const path = target
      ? `/api/instances/${encodeURIComponent(instanceId)}/auto-deploy/enable`
      : `/api/instances/${encodeURIComponent(instanceId)}/auto-deploy/disable`;
    // Optimistic update; revertimos si la API responde error.
    setEnabled(target);
    try {
      await api(path, { method: "POST" });
      router.refresh();
    } catch (e) {
      setEnabled(!target);
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

  return (
    <div className="flex flex-col gap-2">
      <button
        type="button"
        onClick={toggle}
        disabled={busy}
        className={`inline-flex items-center gap-2 self-start rounded-full border px-4 py-2 text-sm transition disabled:opacity-50 ${
          enabled
            ? "border-emerald-500/40 bg-emerald-500/10 text-emerald-200 hover:bg-emerald-500/20"
            : "border-zinc-700 bg-zinc-900/40 text-zinc-300 hover:bg-zinc-800"
        }`}
      >
        <span
          className={`inline-block size-2.5 rounded-full ${
            enabled ? "bg-emerald-400" : "bg-zinc-500"
          }`}
        />
        {enabled ? "Activado" : "Desactivado"}
        <span className="text-[11px] uppercase tracking-wider text-zinc-500">
          {busy ? "guardando..." : "click para alternar"}
        </span>
      </button>
      {error && <p className="text-[11px] text-rose-400">{error}</p>}
    </div>
  );
}
