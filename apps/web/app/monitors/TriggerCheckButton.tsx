"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";
import type { MonitorCheckDto } from "@/lib/types";

interface Props {
  monitorId: string;
}

export function TriggerCheckButton({ monitorId }: Props) {
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastResult, setLastResult] = useState<MonitorCheckDto | null>(null);

  async function onClick() {
    setError(null);
    setLoading(true);
    try {
      const result = await api<MonitorCheckDto>(
        `/api/monitors/${monitorId}/trigger`,
        { method: "POST" },
      );
      setLastResult(result);
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
        className="rounded-full bg-emerald-500 px-4 py-1.5 text-xs font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
      >
        {loading ? "Probing..." : "Probar ahora"}
      </button>
      {lastResult && !error && (
        <span className="text-[11px] text-zinc-400">
          {lastResult.status} · {lastResult.http_status_code ?? "—"} ·{" "}
          {lastResult.latency_ms === null ? "—" : `${lastResult.latency_ms}ms`}
        </span>
      )}
      {error && <span className="text-[11px] text-rose-300">{error}</span>}
    </div>
  );
}
