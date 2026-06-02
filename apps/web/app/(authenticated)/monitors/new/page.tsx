"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";
import type {
  CreateMonitorRequest,
  MonitorDetailDto,
  MonitorHttpMethod,
} from "@/lib/types";

const URL_RE = /^https?:\/\/[^\s/$.?#].[^\s]*$/i;
const SLUG_RE = /^[a-z0-9]+(-[a-z0-9]+)*$/;

export default function NewMonitorPage() {
  const router = useRouter();
  const [slug, setSlug] = useState("");
  const [name, setName] = useState("");
  const [url, setUrl] = useState("");
  const [method, setMethod] = useState<MonitorHttpMethod>("GET");
  const [expected, setExpected] = useState("200");
  const [interval, setInterval] = useState(60);
  const [timeout, setTimeout] = useState(10000);
  const [headersText, setHeadersText] = useState("");
  const [body, setBody] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  function validate(): string | null {
    if (!SLUG_RE.test(slug.trim())) {
      return "Slug inválido: solo minúsculas, dígitos y guiones (ej. mi-monitor).";
    }
    if (name.trim().length === 0) {
      return "Nombre requerido.";
    }
    if (!URL_RE.test(url.trim())) {
      return "URL debe ser http(s):// absoluta.";
    }
    const codes = parseExpected(expected);
    if (codes.length === 0) {
      return "Códigos esperados inválidos: usa comas, ej. '200,204'.";
    }
    if (interval < 30 || interval > 3600) {
      return "Intervalo entre 30 y 3600 segundos.";
    }
    if (timeout < 1000 || timeout > 60000) {
      return "Timeout entre 1000 y 60000 ms.";
    }
    if (headersText.trim() && parseHeaders(headersText) === null) {
      return "Headers mal formados. Usa 'Clave: valor' por línea.";
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
      const body_template = body.trim() === "" ? undefined : body;
      const headers = headersText.trim() === "" ? undefined : parseHeaders(headersText) ?? undefined;
      const payload: CreateMonitorRequest = {
        slug: slug.trim(),
        name: name.trim(),
        url: url.trim(),
        http_method: method,
        expected_status_codes: parseExpected(expected),
        interval_sec: interval,
        timeout_ms: timeout,
        headers,
        body_template,
      };
      const created = await api<MonitorDetailDto>("/api/monitors/", {
        method: "POST",
        body: JSON.stringify(payload),
      });
      router.push(`/monitors/${created.id}`);
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const body = e.body as { detail?: string; Message?: string } | undefined;
        setError(body?.detail ?? body?.Message ?? `Error ${e.status}`);
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
          <h1 className="text-3xl font-semibold">Nuevo monitor</h1>
          <p className="mt-1 text-sm text-zinc-500">
            Configura un probe HTTP. El worker lo ejecuta a partir del próximo tick (10s).
          </p>
        </header>

        <form
          onSubmit={onSubmit}
          className="flex flex-col gap-5 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6"
        >
          <Field label="Slug" required hint="Identificador URL-friendly único (ej. api-prod).">
            <input
              type="text"
              value={slug}
              onChange={(e) => setSlug(e.target.value)}
              placeholder="api-prod"
              className={inputClass}
              autoComplete="off"
              required
            />
          </Field>
          <Field label="Nombre" required>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="API producción"
              className={inputClass}
              required
            />
          </Field>
          <Field label="URL" required hint="Absoluta, http(s)://.">
            <input
              type="text"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              placeholder="https://api.example.com/health"
              className={inputClass}
              autoComplete="off"
              required
            />
          </Field>

          <div className="grid grid-cols-2 gap-4">
            <Field label="Método HTTP">
              <select
                value={method}
                onChange={(e) => setMethod(e.target.value as MonitorHttpMethod)}
                className={inputClass}
              >
                <option value="GET">GET</option>
                <option value="HEAD">HEAD</option>
                <option value="POST">POST</option>
              </select>
            </Field>
            <Field label="Códigos OK" hint="Comas. Ej: 200,204.">
              <input
                type="text"
                value={expected}
                onChange={(e) => setExpected(e.target.value)}
                placeholder="200"
                className={inputClass}
                required
              />
            </Field>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <Field label="Intervalo (s)" hint="30..3600.">
              <input
                type="number"
                value={interval}
                onChange={(e) => setInterval(Number(e.target.value) || 60)}
                min={30}
                max={3600}
                step={10}
                className={inputClass}
              />
            </Field>
            <Field label="Timeout (ms)" hint="1000..60000.">
              <input
                type="number"
                value={timeout}
                onChange={(e) => setTimeout(Number(e.target.value) || 10000)}
                min={1000}
                max={60000}
                step={500}
                className={inputClass}
              />
            </Field>
          </div>

          <Field
            label="Headers"
            hint="Una por línea, 'Clave: valor'. Opcional (auth, etc.)."
          >
            <textarea
              value={headersText}
              onChange={(e) => setHeadersText(e.target.value)}
              placeholder="Authorization: Bearer xxx"
              className={`${inputClass} h-20`}
            />
          </Field>

          {method === "POST" && (
            <Field label="Body" hint="JSON o texto plano enviado en el POST.">
              <textarea
                value={body}
                onChange={(e) => setBody(e.target.value)}
                placeholder='{"ping": true}'
                className={`${inputClass} h-24 font-mono text-xs`}
              />
            </Field>
          )}

          {error && (
            <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
              {error}
            </p>
          )}

          <div className="flex justify-end gap-3">
            <button
              type="button"
              onClick={() => router.push("/monitors")}
              className="rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-300 transition hover:bg-zinc-800"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={loading}
              className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
            >
              {loading ? "Creando..." : "Crear monitor"}
            </button>
          </div>
        </form>
      </div>
    </main>
  );
}

const inputClass =
  "w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-zinc-100 outline-none focus:border-emerald-500";

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

function parseExpected(raw: string): number[] {
  return raw
    .split(",")
    .map((s) => s.trim())
    .filter((s) => s.length > 0)
    .map((s) => Number(s))
    .filter((n) => Number.isInteger(n) && n >= 100 && n <= 599);
}

function parseHeaders(raw: string): Record<string, string> | null {
  const result: Record<string, string> = {};
  for (const line of raw.split(/\r?\n/)) {
    const trimmed = line.trim();
    if (trimmed === "") continue;
    const idx = trimmed.indexOf(":");
    if (idx <= 0) return null;
    const key = trimmed.slice(0, idx).trim();
    const value = trimmed.slice(idx + 1).trim();
    if (key === "") return null;
    result[key] = value;
  }
  return result;
}
