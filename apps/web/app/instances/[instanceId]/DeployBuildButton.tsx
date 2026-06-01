"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";
import type { DeploymentDetail } from "@/lib/types";

export function DeployBuildButton({
  buildId,
  instanceId,
  disabled,
}: {
  buildId: string;
  instanceId: string;
  disabled?: boolean;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function deploy() {
    setError(null);
    setBusy(true);
    try {
      const response = await api<DeploymentDetail>(
        `/api/deployments/builds/${encodeURIComponent(buildId)}/instances/${encodeURIComponent(instanceId)}/trigger`,
        { method: "POST" },
      );
      router.push(`/deployments/${response.id}`);
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const body = e.body as { message?: string; detail?: string } | undefined;
        setError(body?.message ?? body?.detail ?? `Error ${e.status}`);
      } else {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col items-end gap-1">
      <button
        type="button"
        onClick={deploy}
        disabled={busy || disabled}
        className="rounded-full bg-emerald-500 px-3 py-1 text-xs font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:cursor-not-allowed disabled:bg-zinc-700 disabled:text-zinc-400"
        title={
          disabled
            ? "Solo builds con imagen y status Completed se pueden desplegar."
            : undefined
        }
      >
        {busy ? "Disparando..." : "Deploy aqui"}
      </button>
      {error && (
        <p className="rounded border border-rose-500/30 bg-rose-500/10 px-2 py-0.5 text-[10px] text-rose-300">
          {error}
        </p>
      )}
    </div>
  );
}
