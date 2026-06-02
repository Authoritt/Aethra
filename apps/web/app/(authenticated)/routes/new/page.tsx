"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";
import type { CreateRouteRequest, RouteDto } from "@/lib/types";

const FQDN_RE = /^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$/i;
const BACKEND_RE = /^https?:\/\/[^\s/$.?#].[^\s]*$/i;

export default function NewRoutePage() {
  const router = useRouter();
  const [hostname, setHostname] = useState("");
  const [backendUrl, setBackendUrl] = useState("");
  const [tlsEnabled, setTlsEnabled] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  function validate(): string | null {
    if (!FQDN_RE.test(hostname.trim())) {
      return "El hostname debe ser un FQDN válido (ej. app.example.com).";
    }
    if (!BACKEND_RE.test(backendUrl.trim())) {
      return "El backend debe ser una URL http(s) (ej. http://10.0.0.5:8080).";
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
      const body: CreateRouteRequest = {
        hostname: hostname.trim(),
        backend_url: backendUrl.trim(),
        tls_enabled: tlsEnabled,
      };
      await api<RouteDto>("/api/proxy/routes", {
        method: "POST",
        body: JSON.stringify(body),
      });
      router.push("/routes");
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
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-xl flex-col gap-6">
        <header>
          <h1 className="text-3xl font-semibold">Nueva ruta</h1>
          <p className="mt-1 text-sm text-zinc-500">
            Expone un backend interno a través del reverse proxy YARP.
          </p>
        </header>

        <form
          onSubmit={onSubmit}
          className="flex flex-col gap-5 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6"
        >
          <Field
            label="Hostname"
            required
            hint="FQDN público que se servirá (ej. app.example.com)."
          >
            <input
              type="text"
              value={hostname}
              onChange={(e) => setHostname(e.target.value)}
              placeholder="app.example.com"
              className={inputClass}
              autoComplete="off"
              spellCheck={false}
              required
            />
          </Field>

          <Field
            label="Backend URL"
            required
            hint="Destino interno al que se enrutará el tráfico."
          >
            <input
              type="text"
              value={backendUrl}
              onChange={(e) => setBackendUrl(e.target.value)}
              placeholder="http://10.0.0.5:8080"
              className={inputClass}
              autoComplete="off"
              spellCheck={false}
              required
            />
          </Field>

          <label className="flex items-start gap-3 rounded-lg border border-zinc-800 bg-zinc-950/40 p-3 text-sm text-zinc-300">
            <input
              type="checkbox"
              checked={tlsEnabled}
              onChange={(e) => setTlsEnabled(e.target.checked)}
              className="mt-0.5 size-4 accent-emerald-500"
            />
            <span className="flex flex-col gap-1">
              <span className="font-medium text-zinc-100">
                Habilitar TLS (HTTPS)
              </span>
              <span className="text-xs text-zinc-500">
                Termina TLS en el reverse proxy y redirige HTTP → HTTPS.
              </span>
            </span>
          </label>

          {tlsEnabled && (
            <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 text-xs text-amber-200">
              Aethra solicitará un certificado Let&apos;s Encrypt automáticamente.
              El dominio debe apuntar a esta IP y el puerto 80 debe estar
              abierto para el HTTP-01 challenge.
            </div>
          )}

          {error && (
            <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
              {error}
            </p>
          )}

          <div className="flex justify-end gap-3">
            <button
              type="button"
              onClick={() => router.push("/routes")}
              className="rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-300 transition hover:bg-zinc-800"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={loading}
              className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
            >
              {loading ? "Creando..." : "Crear ruta"}
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
