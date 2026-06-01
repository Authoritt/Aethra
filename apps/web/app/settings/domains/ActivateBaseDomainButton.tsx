"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";

export function ActivateBaseDomainButton({
  id,
  hostname,
}: {
  id: string;
  hostname: string;
}) {
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onClick() {
    const ok = window.confirm(
      `Activar el base domain "${hostname}"?\n\nCualquier otro base domain activo se desactivara automaticamente: solo uno puede estar activo a la vez.`,
    );
    if (!ok) return;
    setError(null);
    setLoading(true);
    try {
      await api(
        `/api/settings/domains/${encodeURIComponent(id)}/activate`,
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
    <div className="inline-flex flex-col items-end gap-1">
      <button
        type="button"
        onClick={onClick}
        disabled={loading}
        className="rounded-full border border-emerald-500/40 px-3 py-1 text-xs font-medium text-emerald-200 transition hover:bg-emerald-500/10 disabled:opacity-50"
      >
        {loading ? "Activando..." : "Activar"}
      </button>
      {error && <span className="text-[11px] text-rose-300">{error}</span>}
    </div>
  );
}
