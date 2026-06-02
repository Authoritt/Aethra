"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";

export function DeleteIntegrationButton({
  id,
  name,
}: {
  id: string;
  name: string;
}) {
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onClick() {
    const ok = window.confirm(
      `Eliminar la credencial "${name}"?\n\nCualquier modulo que la resuelva por nombre quedara sin acceso. Esta accion no se puede deshacer.`,
    );
    if (!ok) return;
    setError(null);
    setLoading(true);
    try {
      await api(`/api/settings/integrations/${encodeURIComponent(id)}`, {
        method: "DELETE",
      });
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
        className="rounded-full border border-rose-500/30 px-3 py-1 text-xs font-medium text-rose-300 transition hover:bg-rose-500/10 disabled:opacity-50"
      >
        {loading ? "Borrando..." : "Borrar"}
      </button>
      {error && <span className="text-[11px] text-rose-300">{error}</span>}
    </div>
  );
}
