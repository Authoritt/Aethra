"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";
import type { RotateCloudflareTokenRequest } from "@/lib/types";

export function RotateTokenButton({ zoneId }: { zoneId: string }) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [token, setToken] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (token.trim().length < 8) {
      setError("Token demasiado corto.");
      return;
    }
    setError(null);
    setLoading(true);
    try {
      const body: RotateCloudflareTokenRequest = { api_token: token.trim() };
      await api(`/api/cloudflare/zones/${zoneId}/rotate-token`, {
        method: "POST",
        body: JSON.stringify(body),
      });
      setOpen(false);
      setToken("");
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const body = e.body as { message?: string; detail?: string } | undefined;
        setError(body?.message ?? body?.detail ?? `Error ${e.status}`);
      } else {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
    } finally {
      setLoading(false);
    }
  }

  if (!open) {
    return (
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-200 transition hover:bg-zinc-800"
      >
        Rotar token
      </button>
    );
  }

  return (
    <form
      onSubmit={onSubmit}
      className="flex flex-col gap-2 rounded-xl border border-zinc-700 bg-zinc-900 p-3"
    >
      <label className="text-[10px] uppercase tracking-wider text-zinc-500">
        Nuevo API token
      </label>
      <input
        type="password"
        value={token}
        onChange={(e) => setToken(e.target.value)}
        placeholder="••••••••••••••••"
        className="w-64 rounded-md border border-zinc-700 bg-zinc-950 px-2 py-1 text-xs text-zinc-100 outline-none focus:border-emerald-500"
        autoComplete="off"
        spellCheck={false}
        required
      />
      {error && <span className="text-[11px] text-rose-300">{error}</span>}
      <div className="flex justify-end gap-2">
        <button
          type="button"
          onClick={() => {
            setOpen(false);
            setError(null);
            setToken("");
          }}
          className="rounded-full border border-zinc-700 px-3 py-1 text-[11px] text-zinc-300 transition hover:bg-zinc-800"
        >
          Cancelar
        </button>
        <button
          type="submit"
          disabled={loading}
          className="rounded-full bg-emerald-500 px-3 py-1 text-[11px] font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
        >
          {loading ? "Verificando..." : "Rotar"}
        </button>
      </div>
    </form>
  );
}
