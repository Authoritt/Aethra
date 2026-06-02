"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";

export function MarkWildcardButton({ id }: { id: string }) {
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onClick() {
    const ok = window.confirm(
      "Has verificado en tu DNS que el registro wildcard (*.<hostname>) esta creado y resuelve a la IP del Edge VM?",
    );
    if (!ok) return;
    setError(null);
    setLoading(true);
    try {
      await api(
        `/api/settings/domains/${encodeURIComponent(id)}/wildcard-configured`,
        { method: "POST" },
      );
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const body = e.body as
          | { message?: string; detail?: string }
          | undefined;
        setError(body?.message ?? body?.detail ?? `Error ${e.status}`);
      } else {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="inline-flex flex-col items-start gap-1">
      <button
        type="button"
        onClick={onClick}
        disabled={loading}
        className="rounded-full border border-zinc-700 px-3 py-1 text-[10px] font-medium uppercase tracking-wider text-zinc-300 transition hover:bg-zinc-800 disabled:opacity-50"
      >
        {loading ? "marcando..." : "marcar como configurado"}
      </button>
      {error && <span className="text-[11px] text-rose-300">{error}</span>}
    </div>
  );
}
