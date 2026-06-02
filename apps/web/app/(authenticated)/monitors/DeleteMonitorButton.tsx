"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";

interface Props {
  monitorId: string;
  name: string;
}

export function DeleteMonitorButton({ monitorId, name }: Props) {
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onClick() {
    const ok = window.confirm(
      `¿Eliminar el monitor "${name}"?\n\nEsta acción borrará el monitor y todo su historial de checks.`,
    );
    if (!ok) return;
    setError(null);
    setLoading(true);
    try {
      await api(`/api/monitors/${monitorId}`, { method: "DELETE" });
      router.push("/monitors");
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const body = e.body as { detail?: string; Message?: string } | undefined;
        setError(body?.detail ?? body?.Message ?? `Error ${e.status}`);
      } else {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="inline-flex flex-col items-end gap-1">
      <button
        type="button"
        onClick={onClick}
        disabled={loading}
        className="rounded-full border border-rose-500/30 px-4 py-1.5 text-xs font-medium text-rose-300 transition hover:bg-rose-500/10 disabled:opacity-50"
      >
        {loading ? "Eliminando..." : "Eliminar"}
      </button>
      {error && <span className="text-[11px] text-rose-300">{error}</span>}
    </div>
  );
}
