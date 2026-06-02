"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";

export function DeleteZoneButton({
  zoneId,
  name,
  recordsCount,
}: {
  zoneId: string;
  name: string;
  recordsCount: number;
}) {
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onClick() {
    if (recordsCount > 0) {
      window.alert(
        `La zona tiene ${recordsCount} record(s) gestionados. Eliminalos primero.`,
      );
      return;
    }
    const ok = window.confirm(
      `Eliminar la zona "${name}" del registro de Aethra?\n\nNo se elimina la zona en Cloudflare, solo se quita el token cifrado y el seguimiento local.`,
    );
    if (!ok) return;
    setError(null);
    setLoading(true);
    try {
      await api(`/api/cloudflare/zones/${zoneId}`, { method: "DELETE" });
      router.push("/cloudflare");
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const body = e.body as { message?: string; detail?: string } | undefined;
        setError(body?.message ?? body?.detail ?? `Error ${e.status}`);
      } else {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
      setLoading(false);
    }
  }

  return (
    <div className="flex flex-col items-end gap-1">
      <button
        type="button"
        onClick={onClick}
        disabled={loading}
        className="rounded-full border border-rose-500/30 px-3 py-1 text-xs font-medium text-rose-300 transition hover:bg-rose-500/10 disabled:opacity-50"
      >
        {loading ? "Eliminando..." : "Eliminar zona"}
      </button>
      {error && <span className="text-[11px] text-rose-300">{error}</span>}
    </div>
  );
}
