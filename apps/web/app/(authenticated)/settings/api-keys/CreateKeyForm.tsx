"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { ApiError, api } from "@/lib/api";
import type { CreateApiKeyRequest, CreateApiKeyResult } from "@/lib/types";
import { ScopesGrid } from "./ScopesGrid";

const NAME_MAX = 80;

type ExpiresPreset = "never" | "30d" | "90d" | "365d" | "custom";

const PRESET_LABELS: Record<ExpiresPreset, string> = {
  never: "Sin expiracion",
  "30d": "30 dias",
  "90d": "90 dias",
  "365d": "1 ano",
  custom: "Personalizado",
};

const SESSION_KEY_PREFIX = "aethra.api-key.secret.";

export function persistSecretInSession(id: string, secret: string) {
  if (typeof window === "undefined") return;
  try {
    window.sessionStorage.setItem(SESSION_KEY_PREFIX + id, secret);
  } catch {
    // sessionStorage puede fallar en modo privado; ignoramos silenciosamente.
  }
}

export function readSecretFromSession(id: string): string | null {
  if (typeof window === "undefined") return null;
  try {
    return window.sessionStorage.getItem(SESSION_KEY_PREFIX + id);
  } catch {
    return null;
  }
}

export function clearSecretFromSession(id: string) {
  if (typeof window === "undefined") return;
  try {
    window.sessionStorage.removeItem(SESSION_KEY_PREFIX + id);
  } catch {
    /* no-op */
  }
}

function presetToIso(preset: ExpiresPreset, custom: string): string | null {
  if (preset === "never") return null;
  if (preset === "custom") {
    if (!custom) return null;
    const d = new Date(custom);
    if (Number.isNaN(d.getTime())) return null;
    return d.toISOString();
  }
  const days = preset === "30d" ? 30 : preset === "90d" ? 90 : 365;
  const d = new Date();
  d.setDate(d.getDate() + days);
  return d.toISOString();
}

export function CreateKeyForm() {
  const router = useRouter();
  const [name, setName] = useState("");
  const [scopes, setScopes] = useState<string[]>([]);
  const [preset, setPreset] = useState<ExpiresPreset>("never");
  const [customDate, setCustomDate] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const today = useMemo(() => new Date().toISOString().slice(0, 10), []);

  const nameError = useMemo(() => {
    const trimmed = name.trim();
    if (!trimmed) return null; // se valida en submit
    if (trimmed.length > NAME_MAX) return `Maximo ${NAME_MAX} caracteres.`;
    return null;
  }, [name]);

  const customError = useMemo(() => {
    if (preset !== "custom") return null;
    if (!customDate) return "Selecciona una fecha.";
    const d = new Date(customDate);
    if (Number.isNaN(d.getTime())) return "Fecha invalida.";
    if (d.getTime() <= Date.now()) return "Debe ser una fecha futura.";
    return null;
  }, [preset, customDate]);

  const canSubmit =
    !loading &&
    name.trim().length > 0 &&
    !nameError &&
    !customError &&
    scopes.length > 0;

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;

    setError(null);
    setLoading(true);
    try {
      const body: CreateApiKeyRequest = {
        name: name.trim(),
        scopes,
        expires_at: presetToIso(preset, customDate),
      };
      const created = await api<CreateApiKeyResult>(
        "/api/identity/api-keys",
        {
          method: "POST",
          body: JSON.stringify(body),
        },
      );

      // Guardamos el secret en sessionStorage; nunca en la URL.
      persistSecretInSession(created.id, created.secret);
      router.push(`/settings/api-keys/created?id=${encodeURIComponent(created.id)}`);
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const data = e.body as { detail?: string } | undefined;
        setError(data?.detail ?? `Error ${e.status}`);
      } else {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
      setLoading(false);
    }
  }

  return (
    <form
      onSubmit={onSubmit}
      className="flex flex-col gap-6 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6"
    >
      <Field
        label="Nombre"
        required
        hint="Identifica para que se usara esta key (ej: CI/CD GitHub Actions, claude agent dev)."
      >
        <input
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          maxLength={NAME_MAX}
          placeholder="CI deploy bot"
          className={inputClass}
          required
          autoFocus
        />
        {nameError && (
          <span className="text-xs text-rose-400">{nameError}</span>
        )}
      </Field>

      <div className="flex flex-col gap-2 text-sm text-zinc-300">
        <span className="flex items-center justify-between">
          <span>
            Scopes
            <span className="text-rose-400"> *</span>
          </span>
          {scopes.length === 0 && (
            <span className="text-xs text-zinc-500">
              Selecciona al menos uno
            </span>
          )}
        </span>
        <ScopesGrid
          selected={scopes}
          onChange={setScopes}
          disabled={loading}
        />
      </div>

      <Field
        label="Expiracion"
        hint="Una expiracion corta limita el blast radius si el secret se filtra."
      >
        <div className="flex flex-wrap gap-2">
          {(Object.keys(PRESET_LABELS) as ExpiresPreset[]).map((p) => {
            const active = preset === p;
            return (
              <button
                key={p}
                type="button"
                onClick={() => setPreset(p)}
                className={`rounded-full border px-3 py-1 text-xs transition ${
                  active
                    ? "border-emerald-500/60 bg-emerald-500/15 text-emerald-200"
                    : "border-zinc-700 text-zinc-300 hover:bg-zinc-800"
                }`}
              >
                {PRESET_LABELS[p]}
              </button>
            );
          })}
        </div>
        {preset === "custom" && (
          <input
            type="date"
            value={customDate}
            min={today}
            onChange={(e) => setCustomDate(e.target.value)}
            className={`${inputClass} mt-2 max-w-xs`}
          />
        )}
        {customError && (
          <span className="text-xs text-rose-400">{customError}</span>
        )}
      </Field>

      {error && (
        <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
          {error}
        </p>
      )}

      <div className="flex justify-end gap-3">
        <button
          type="button"
          onClick={() => router.push("/settings/api-keys")}
          className="rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-300 transition hover:bg-zinc-800"
        >
          Cancelar
        </button>
        <button
          type="submit"
          disabled={!canSubmit}
          className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
        >
          {loading ? "Creando..." : "Crear API key"}
        </button>
      </div>
    </form>
  );
}

const inputClass =
  "rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-zinc-100 outline-none focus:border-emerald-500";

function Field({
  label,
  required,
  hint,
  children,
}: {
  label: string;
  required?: boolean;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <label className="flex flex-col gap-1 text-sm text-zinc-300">
      <span>
        {label}
        {required && <span className="text-rose-400"> *</span>}
      </span>
      {children}
      {hint && <span className="text-xs text-zinc-500">{hint}</span>}
    </label>
  );
}
