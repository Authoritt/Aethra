"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";
import type {
  IntegrationCredentialDto,
  RotateIntegrationCredentialRequest,
} from "@/lib/types";

export function RotateIntegrationForm({
  credential,
}: {
  credential: IntegrationCredentialDto;
}) {
  const router = useRouter();
  const [newPlainValue, setNewPlainValue] = useState("");
  const [confirm, setConfirm] = useState("");
  const [reveal, setReveal] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const confirmExpected = credential.name;
  const canSubmit =
    !loading &&
    newPlainValue.trim().length > 0 &&
    confirm.trim() === confirmExpected;

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;
    setError(null);
    setLoading(true);
    try {
      const body: RotateIntegrationCredentialRequest = { newPlainValue };
      await api(
        `/api/settings/integrations/${encodeURIComponent(credential.id)}/rotate`,
        {
          method: "POST",
          body: JSON.stringify(body),
        },
      );
      router.push("/settings/integrations");
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
    <form
      onSubmit={onSubmit}
      className="flex flex-col gap-6 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6"
    >
      <dl className="grid grid-cols-2 gap-3 text-sm">
        <div>
          <dt className="text-xs uppercase tracking-wider text-zinc-500">
            Name
          </dt>
          <dd className="mt-0.5 font-mono text-xs text-zinc-100">
            {credential.name}
          </dd>
        </div>
        <div>
          <dt className="text-xs uppercase tracking-wider text-zinc-500">
            Tipo
          </dt>
          <dd className="mt-0.5 text-zinc-100">{credential.type}</dd>
        </div>
        <div className="col-span-2">
          <dt className="text-xs uppercase tracking-wider text-zinc-500">
            Display
          </dt>
          <dd className="mt-0.5 text-zinc-100">{credential.displayName}</dd>
        </div>
      </dl>

      <Field
        label="Nuevo valor"
        required
        hint="Texto plano del nuevo token / secret. Se cifra al guardar y reemplaza al anterior."
      >
        <div className="flex items-stretch gap-2">
          <textarea
            value={newPlainValue}
            onChange={(e) => setNewPlainValue(e.target.value)}
            className={`${inputClass} min-h-[88px] flex-1 font-mono text-xs`}
            spellCheck={false}
            autoComplete="off"
            required
            autoFocus
            style={
              reveal
                ? undefined
                : ({
                    WebkitTextSecurity: "disc",
                    textSecurity: "disc",
                  } as React.CSSProperties)
            }
          />
          <button
            type="button"
            onClick={() => setReveal((v) => !v)}
            className="self-start rounded-lg border border-zinc-700 px-3 py-2 text-xs text-zinc-300 transition hover:bg-zinc-800"
          >
            {reveal ? "Ocultar" : "Mostrar"}
          </button>
        </div>
      </Field>

      <Field
        label={`Para confirmar, escribe el name: ${credential.name}`}
        required
      >
        <input
          type="text"
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
          className={`${inputClass} font-mono text-xs`}
          spellCheck={false}
          autoComplete="off"
          required
        />
      </Field>

      <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 text-xs text-amber-200">
        Cualquier modulo que ya resolvio el valor anterior y lo este usando en
        memoria seguira funcionando hasta su proximo lookup. Si quieres
        invalidar inmediatamente, despues de rotar reinicia el servicio
        consumidor.
      </div>

      {error && (
        <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
          {error}
        </p>
      )}

      <div className="flex justify-end gap-3">
        <button
          type="button"
          onClick={() => router.push("/settings/integrations")}
          className="rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-300 transition hover:bg-zinc-800"
        >
          Cancelar
        </button>
        <button
          type="submit"
          disabled={!canSubmit}
          className="rounded-full bg-amber-500 px-5 py-2 text-sm font-medium text-amber-950 transition hover:bg-amber-400 disabled:opacity-50"
        >
          {loading ? "Rotando..." : "Rotar credencial"}
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
