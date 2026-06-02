"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";

export function RevokeKeyButton({
  id,
  name,
  alreadyRevoked,
}: {
  id: string;
  name: string;
  alreadyRevoked: boolean;
}) {
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (alreadyRevoked) {
    return (
      <span className="text-[11px] uppercase tracking-wider text-zinc-600">
        revocada
      </span>
    );
  }

  async function onClick() {
    const ok = window.confirm(
      `Revocar la API key "${name}"?\n\nCualquier integracion que la use perdera acceso inmediatamente. Esta accion no se puede deshacer.`,
    );
    if (!ok) return;
    setError(null);
    setLoading(true);
    try {
      await api(`/api/identity/api-keys/${id}`, { method: "DELETE" });
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const body = e.body as { detail?: string } | undefined;
        setError(body?.detail ?? `Error ${e.status}`);
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
        className="rounded-full border border-rose-500/30 px-3 py-1 text-xs font-medium text-rose-300 transition hover:bg-rose-500/10 disabled:opacity-50"
      >
        {loading ? "Revocando..." : "Revocar"}
      </button>
      {error && <span className="text-[11px] text-rose-300">{error}</span>}
    </div>
  );
}
