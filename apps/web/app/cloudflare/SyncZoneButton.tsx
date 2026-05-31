"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";

export function SyncZoneButton({ zoneId }: { zoneId: string }) {
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onClick() {
    setError(null);
    setLoading(true);
    try {
      await api(`/api/cloudflare/zones/${zoneId}/sync`, { method: "POST" });
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
    <div className="flex flex-col items-end gap-1">
      <button
        type="button"
        onClick={onClick}
        disabled={loading}
        className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-200 transition hover:bg-zinc-800 disabled:opacity-50"
      >
        {loading ? "Sincronizando..." : "Sincronizar"}
      </button>
      {error && <span className="text-[11px] text-rose-300">{error}</span>}
    </div>
  );
}
