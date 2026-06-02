"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { ApiError, api } from "@/lib/api";
import type { BaseDomainDto, CreateBaseDomainRequest } from "@/lib/types";

interface CloudflareZoneOption {
  id: string;
  name: string;
}

// FQDN simple: dos o mas labels lowercase alfanumericos con guiones.
const FQDN_RE = /^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$/;

export function CreateBaseDomainForm({
  zones,
}: {
  zones: CloudflareZoneOption[];
}) {
  const router = useRouter();
  const [hostname, setHostname] = useState("");
  const [cloudflareZoneId, setCloudflareZoneId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const hostnameError = useMemo(() => {
    const trimmed = hostname.trim().toLowerCase();
    if (!trimmed) return null;
    if (trimmed.length > 253) return "Maximo 253 caracteres.";
    if (!FQDN_RE.test(trimmed)) {
      return "Debe ser un FQDN valido (lowercase, minimo dos labels, sin guiones al inicio/fin).";
    }
    return null;
  }, [hostname]);

  const canSubmit =
    !loading && hostname.trim().length > 0 && !hostnameError;

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;
    setError(null);
    setLoading(true);
    try {
      const body: CreateBaseDomainRequest = {
        hostname: hostname.trim().toLowerCase(),
        cloudflareZoneId: cloudflareZoneId.trim() || null,
      };
      const created = await api<BaseDomainDto>("/api/settings/domains/", {
        method: "POST",
        body: JSON.stringify(body),
      });
      router.push(`/settings/domains?created=${encodeURIComponent(created.id)}`);
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
      <Field
        label="Hostname"
        required
        hint="FQDN bajo el cual Aethra creara subdominios. Ej: aethra.tu-empresa.com."
      >
        <input
          type="text"
          value={hostname}
          onChange={(e) => setHostname(e.target.value.toLowerCase())}
          maxLength={253}
          placeholder="aethra.tu-empresa.com"
          className={inputClass}
          autoComplete="off"
          spellCheck={false}
          required
          autoFocus
        />
        {hostnameError && (
          <span className="text-xs text-rose-400">{hostnameError}</span>
        )}
      </Field>

      <Field
        label="Zona Cloudflare (opcional)"
        hint="Si la zona ya esta registrada en el modulo Cloudflare, enlazala para que la UI muestre el vinculo. Puedes dejarla en blanco y enlazarla despues."
      >
        {zones.length === 0 ? (
          <div className="rounded-lg border border-zinc-800 bg-zinc-950 px-3 py-2 text-xs text-zinc-400">
            No hay zonas registradas todavia en el modulo Cloudflare.{" "}
            <Link
              href="/cloudflare/new"
              className="text-emerald-300 hover:underline"
            >
              Registrar una zona
            </Link>
            .
          </div>
        ) : (
          <select
            value={cloudflareZoneId}
            onChange={(e) => setCloudflareZoneId(e.target.value)}
            className={inputClass}
          >
            <option value="">— sin enlazar —</option>
            {zones.map((z) => (
              <option key={z.id} value={z.id}>
                {z.name} ({z.id.slice(0, 12)}...)
              </option>
            ))}
          </select>
        )}
      </Field>

      <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 text-xs text-amber-200">
        Crear un base domain no lo activa automaticamente. Despues de
        registrarlo, marca el wildcard DNS como configurado y luego activalo.
      </div>

      {error && (
        <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
          {error}
        </p>
      )}

      <div className="flex justify-end gap-3">
        <button
          type="button"
          onClick={() => router.push("/settings/domains")}
          className="rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-300 transition hover:bg-zinc-800"
        >
          Cancelar
        </button>
        <button
          type="submit"
          disabled={!canSubmit}
          className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
        >
          {loading ? "Creando..." : "Registrar base domain"}
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
