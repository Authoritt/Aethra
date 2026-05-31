"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";

interface Props {
  monitorId: string;
  isEnabled: boolean;
}

export function EnableDisableButtons({ monitorId, isEnabled }: Props) {
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function toggle() {
    setError(null);
    setLoading(true);
    try {
      const path = isEnabled
        ? `/api/monitors/${monitorId}/disable`
        : `/api/monitors/${monitorId}/enable`;
      await api(path, { method: "POST" });
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
        onClick={toggle}
        disabled={loading}
        className={`rounded-full border px-4 py-1.5 text-xs font-medium transition disabled:opacity-50 ${
          isEnabled
            ? "border-amber-500/30 text-amber-300 hover:bg-amber-500/10"
            : "border-emerald-500/30 text-emerald-300 hover:bg-emerald-500/10"
        }`}
      >
        {loading
          ? "Procesando..."
          : isEnabled
            ? "Deshabilitar"
            : "Habilitar"}
      </button>
      {error && <span className="text-[11px] text-rose-300">{error}</span>}
    </div>
  );
}
