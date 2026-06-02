"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";

export function DeleteServiceButton({
  serviceId,
  slug,
  bindingsCount,
}: {
  serviceId: string;
  slug: string;
  bindingsCount: number;
}) {
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onClick() {
    const extra =
      bindingsCount > 0
        ? `\n\nAtención: este servicio tiene ${bindingsCount} binding(s) activo(s) que se verán afectados.`
        : "";
    const ok = window.confirm(
      `¿Eliminar el servicio "${slug}"?\n\nEl contenedor se detendrá y el servicio quedará marcado como stopped.${extra}`,
    );
    if (!ok) return;
    setError(null);
    setLoading(true);
    try {
      await api(`/api/services/${serviceId}`, { method: "DELETE" });
      router.push("/services");
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const body = e.body as { detail?: string } | undefined;
        setError(body?.detail ?? `Error ${e.status}`);
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
        className="rounded-full border border-rose-500/30 px-4 py-2 text-xs font-medium text-rose-300 transition hover:bg-rose-500/10 disabled:opacity-50"
      >
        {loading ? "Eliminando..." : "Eliminar servicio"}
      </button>
      {error && <span className="text-[11px] text-rose-300">{error}</span>}
    </div>
  );
}
