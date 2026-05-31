"use client";

import Link from "next/link";
import { useRouter, useParams } from "next/navigation";
import { useState } from "react";
import { ApiError, api } from "@/lib/api";
import type {
  CreateDnsRecordRequest,
  DnsRecordDto,
  DnsRecordType,
} from "@/lib/types";

const TYPES: DnsRecordType[] = ["A", "AAAA", "CNAME", "TXT", "MX"];
const FQDN_RE =
  /^(?=.{1,253}$)(\*\.)?([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/i;

export default function NewDnsRecordPage() {
  const router = useRouter();
  const params = useParams<{ zoneId: string }>();
  const zoneId = params.zoneId;

  const [type, setType] = useState<DnsRecordType>("A");
  const [name, setName] = useState("");
  const [content, setContent] = useState("");
  const [ttl, setTtl] = useState<number>(300);
  const [proxied, setProxied] = useState(false);
  const [comment, setComment] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  function validate(): string | null {
    if (!FQDN_RE.test(name.trim())) {
      return "El nombre debe ser un FQDN valido (ej. api.example.com).";
    }
    if (!content.trim()) {
      return "El contenido es obligatorio.";
    }
    if (ttl < 1 || ttl > 86400) {
      return "TTL debe estar entre 1 y 86400.";
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
      const body: CreateDnsRecordRequest = {
        type,
        name: name.trim().toLowerCase(),
        content: content.trim(),
        ttl,
        proxied,
        comment: comment.trim() || undefined,
      };
      await api<DnsRecordDto>(`/api/cloudflare/zones/${zoneId}/records`, {
        method: "POST",
        body: JSON.stringify(body),
      });
      router.push(`/cloudflare/${zoneId}`);
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
          <Link href={`/cloudflare/${zoneId}`} className="hover:text-zinc-300">
            Zona
          </Link>
          <span> / </span>
          <span className="text-zinc-300">Nuevo record</span>
        </nav>

        <header>
          <h1 className="text-3xl font-semibold">Nuevo DNS record</h1>
          <p className="mt-1 text-sm text-zinc-500">
            Se crea en Cloudflare y se guarda localmente con el id devuelto por
            la API.
          </p>
        </header>

        <form
          onSubmit={onSubmit}
          className="flex flex-col gap-5 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6"
        >
          <Field label="Tipo" required>
            <select
              value={type}
              onChange={(e) => setType(e.target.value as DnsRecordType)}
              className={inputClass}
            >
              {TYPES.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          </Field>

          <Field label="Nombre" required hint="FQDN sin acortar (ej. api.example.com).">
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="api.example.com"
              className={inputClass}
              autoComplete="off"
              spellCheck={false}
              required
            />
          </Field>

          <Field
            label="Contenido"
            required
            hint={contentHint(type)}
          >
            <input
              type="text"
              value={content}
              onChange={(e) => setContent(e.target.value)}
              placeholder={contentPlaceholder(type)}
              className={inputClass}
              autoComplete="off"
              spellCheck={false}
              required
            />
          </Field>

          <div className="grid grid-cols-2 gap-3">
            <Field label="TTL" required hint="Segundos. 1 = auto en Cloudflare.">
              <input
                type="number"
                value={ttl}
                min={1}
                max={86400}
                onChange={(e) => setTtl(Number(e.target.value))}
                className={inputClass}
                required
              />
            </Field>

            <label className="flex flex-col gap-1 text-sm text-zinc-300">
              <span>Proxied</span>
              <label className="flex items-center gap-2 rounded-lg border border-zinc-800 bg-zinc-950/40 px-3 py-2">
                <input
                  type="checkbox"
                  checked={proxied}
                  onChange={(e) => setProxied(e.target.checked)}
                  className="size-4 accent-emerald-500"
                />
                <span className="text-xs text-zinc-400">
                  Trafico via proxy Cloudflare
                </span>
              </label>
            </label>
          </div>

          <Field label="Comentario" hint="Opcional. Aparece en el panel de Cloudflare.">
            <input
              type="text"
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              placeholder="gestionado por Aethra"
              className={inputClass}
              spellCheck={false}
            />
          </Field>

          {error && (
            <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
              {error}
            </p>
          )}

          <div className="flex justify-end gap-3">
            <button
              type="button"
              onClick={() => router.push(`/cloudflare/${zoneId}`)}
              className="rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-300 transition hover:bg-zinc-800"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={loading}
              className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
            >
              {loading ? "Creando..." : "Crear record"}
            </button>
          </div>
        </form>
      </div>
    </main>
  );
}

function contentHint(type: DnsRecordType): string {
  switch (type) {
    case "A":
      return "IPv4 de destino (ej. 203.0.113.10).";
    case "AAAA":
      return "IPv6 de destino.";
    case "CNAME":
      return "FQDN de destino al que apunta el alias.";
    case "MX":
      return "Servidor de correo, con prioridad si Cloudflare lo requiere.";
    case "TXT":
      return "Texto libre. SPF/DKIM/etc.";
  }
}

function contentPlaceholder(type: DnsRecordType): string {
  switch (type) {
    case "A":
      return "203.0.113.10";
    case "AAAA":
      return "2001:db8::1";
    case "CNAME":
      return "target.example.com";
    case "MX":
      return "mail.example.com";
    case "TXT":
      return "v=spf1 include:_spf.example.com ~all";
  }
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
