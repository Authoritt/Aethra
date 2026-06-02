"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";
import type {
  CloudflareZoneDto,
  RegisterCloudflareZoneRequest,
} from "@/lib/types";

const ZONE_ID_RE = /^[0-9a-f]{32}$/i;

export default function NewCloudflareZonePage() {
  const router = useRouter();
  const [zoneId, setZoneId] = useState("");
  const [apiToken, setApiToken] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  function validate(): string | null {
    if (!ZONE_ID_RE.test(zoneId.trim())) {
      return "El zone_id debe ser una cadena hex de 32 caracteres (ver Cloudflare > Overview de la zona).";
    }
    if (apiToken.trim().length < 8) {
      return "El API token parece demasiado corto.";
    }
    return null;
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    const v = validate();
    if (v) {
      setError(v);
      return;
    }
    setError(null);
    setLoading(true);
    try {
      const body: RegisterCloudflareZoneRequest = {
        zone_id: zoneId.trim().toLowerCase(),
        api_token: apiToken.trim(),
      };
      const created = await api<CloudflareZoneDto>("/api/cloudflare/zones/", {
        method: "POST",
        body: JSON.stringify(body),
      });
      router.push(`/cloudflare/${created.id}`);
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

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-xl flex-col gap-6">
        <nav className="text-xs text-zinc-500">
          <Link href="/cloudflare" className="hover:text-zinc-300">
            Cloudflare
          </Link>
          <span> / </span>
          <span className="text-zinc-300">Nueva zona</span>
        </nav>

        <header>
          <h1 className="text-3xl font-semibold">Registrar zona</h1>
          <p className="mt-1 text-sm text-zinc-500">
            Aethra verificara el token contra la API de Cloudflare y guardara la
            zona con su token cifrado.
          </p>
        </header>

        <form
          onSubmit={onSubmit}
          className="flex flex-col gap-5 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6"
        >
          <Field
            label="Zone ID"
            required
            hint="32 caracteres hex. Aparece en el panel de Cloudflare en la sidebar derecha (Overview > API)."
          >
            <input
              type="text"
              value={zoneId}
              onChange={(e) => setZoneId(e.target.value)}
              placeholder="023e105f4ecef8ad9ca31a8372d0c353"
              className={inputClass}
              autoComplete="off"
              spellCheck={false}
              required
            />
          </Field>

          <Field
            label="API Token"
            required
            hint="Token con scope 'Zone.DNS.Edit' sobre esta zona. Aethra lo cifra con DataProtection antes de guardar."
          >
            <input
              type="password"
              value={apiToken}
              onChange={(e) => setApiToken(e.target.value)}
              placeholder="••••••••••••••••"
              className={inputClass}
              autoComplete="off"
              spellCheck={false}
              required
            />
          </Field>

          <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 text-xs text-amber-200">
            Crea el token en Cloudflare desde <em>My Profile &gt; API Tokens</em>
            con permisos minimos <code>Zone:Read</code> y <code>DNS:Edit</code>
            limitados a esta zona.
          </div>

          {error && (
            <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
              {error}
            </p>
          )}

          <div className="flex justify-end gap-3">
            <button
              type="button"
              onClick={() => router.push("/cloudflare")}
              className="rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-300 transition hover:bg-zinc-800"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={loading}
              className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
            >
              {loading ? "Verificando..." : "Registrar"}
            </button>
          </div>
        </form>
      </div>
    </main>
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
