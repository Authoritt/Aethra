"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";

export function BindingActions({
  bindingId,
  appLabel,
}: {
  bindingId: string;
  appLabel: string;
}) {
  const router = useRouter();
  const [loading, setLoading] = useState<"rotate" | "revoke" | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function rotate() {
    const ok = window.confirm(
      `¿Rotar credenciales del binding de "${appLabel}"?\n\nLa application recibirá nuevas credenciales en su próximo deploy o restart.`,
    );
    if (!ok) return;
    setError(null);
    setLoading("rotate");
    try {
      await api(`/api/bindings/${bindingId}/rotate`, { method: "POST" });
      alert("Credenciales rotadas. Redeploy la application para aplicar.");
      router.refresh();
    } catch (e) {
      handleError(e);
    } finally {
      setLoading(null);
    }
  }

  async function revoke() {
    const ok = window.confirm(
      `¿Revocar el binding de "${appLabel}"?\n\nLa application perderá acceso al servicio inmediatamente. El recurso (DB/cola/usuario) se eliminará.`,
    );
    if (!ok) return;
    setError(null);
    setLoading("revoke");
    try {
      await api(`/api/bindings/${bindingId}`, { method: "DELETE" });
      router.refresh();
    } catch (e) {
      handleError(e);
    } finally {
      setLoading(null);
    }
  }

  function handleError(e: unknown) {
    if (e instanceof ApiError) {
      const body = e.body as { detail?: string } | undefined;
      setError(body?.detail ?? `Error ${e.status}`);
    } else {
      setError(e instanceof Error ? e.message : "Error desconocido");
    }
  }

  return (
    <div className="flex flex-col items-end gap-1">
      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={rotate}
          disabled={loading !== null}
          className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-300 transition hover:bg-zinc-800 disabled:opacity-50"
        >
          {loading === "rotate" ? "Rotando..." : "Rotar credenciales"}
        </button>
        <button
          type="button"
          onClick={revoke}
          disabled={loading !== null}
          className="rounded-full border border-rose-500/30 px-3 py-1 text-xs font-medium text-rose-300 transition hover:bg-rose-500/10 disabled:opacity-50"
        >
          {loading === "revoke" ? "Revocando..." : "Revocar"}
        </button>
      </div>
      {error && <span className="text-[11px] text-rose-300">{error}</span>}
    </div>
  );
}
