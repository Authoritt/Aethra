"use client";

import { useState } from "react";
import { ApiError, api } from "@/lib/api";
import type { RotateWebhookSecretResponse } from "@/lib/types";

export function RotateWebhookSecretButton({ templateId }: { templateId: string }) {
  const [loading, setLoading] = useState(false);
  const [secret, setSecret] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  async function rotate() {
    const ok = window.confirm(
      "Rotar el webhook secret invalida el anterior inmediatamente. Tendras que reconfigurar el provider Git. Continuar?",
    );
    if (!ok) return;
    setError(null);
    setLoading(true);
    try {
      const response = await api<RotateWebhookSecretResponse>(
        `/api/templates/${encodeURIComponent(templateId)}/rotate-webhook-secret`,
        { method: "POST" },
      );
      setSecret(response.webhookSecret);
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

  async function copy() {
    if (!secret) return;
    try {
      await navigator.clipboard.writeText(secret);
      setCopied(true);
      setTimeout(() => setCopied(false), 1800);
    } catch {
      // clipboard may not be available
    }
  }

  if (secret) {
    return (
      <div className="flex w-full max-w-lg flex-col gap-2 rounded-2xl border border-amber-500/40 bg-amber-500/10 p-3">
        <div className="flex items-center justify-between">
          <span className="text-xs uppercase tracking-wider text-amber-200">
            Nuevo webhook secret (one-time)
          </span>
          <button
            type="button"
            onClick={copy}
            className="rounded-full border border-amber-500/40 px-3 py-1 text-xs text-amber-200 transition hover:bg-amber-500/20"
          >
            {copied ? "Copiado" : "Copiar"}
          </button>
        </div>
        <pre className="overflow-x-auto whitespace-nowrap rounded-lg bg-zinc-950/80 px-3 py-2 font-mono text-xs text-amber-100">
          {secret}
        </pre>
        <button
          type="button"
          onClick={() => setSecret(null)}
          className="self-end rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-300 transition hover:bg-zinc-800"
        >
          Cerrar
        </button>
      </div>
    );
  }

  return (
    <div className="flex flex-col items-end gap-1">
      <button
        type="button"
        onClick={rotate}
        disabled={loading}
        className="rounded-full border border-amber-500/40 px-4 py-2 text-sm text-amber-300 transition hover:bg-amber-500/10 disabled:opacity-50"
      >
        {loading ? "Rotando..." : "Rotar webhook secret"}
      </button>
      {error && (
        <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-2 py-1 text-[11px] text-rose-300">
          {error}
        </p>
      )}
    </div>
  );
}
