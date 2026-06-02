"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { ApiError, api } from "@/lib/api";
import type { RegisterVmResponse } from "@/lib/types";

function slugify(input: string): string {
  return input
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[̀-ͯ]/g, "")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 64);
}

export default function NewVmPage() {
  const router = useRouter();
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [slugTouched, setSlugTouched] = useState(false);
  const [publicIp, setPublicIp] = useState("");
  const [privateIp, setPrivateIp] = useState("");
  const [description, setDescription] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<RegisterVmResponse | null>(null);

  const suggestedSlug = useMemo(() => slugify(name), [name]);
  const effectiveSlug = slugTouched ? slug : suggestedSlug;

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const response = await api<RegisterVmResponse>("/api/vms/", {
        method: "POST",
        body: JSON.stringify({
          name,
          slug: effectiveSlug || undefined,
          public_ip: publicIp || undefined,
          private_ip: privateIp || undefined,
          description: description || undefined,
        }),
      });
      setResult(response);
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

  if (result) {
    return <SuccessScreen result={result} onContinue={() => router.refresh()} />;
  }

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-xl flex-col gap-6">
        <header>
          <h1 className="text-3xl font-semibold">Registrar VM</h1>
          <p className="mt-1 text-sm text-zinc-500">
            Genera un token de satélite para que el agente reporte métricas a
            Aethra.
          </p>
        </header>

        <form
          onSubmit={onSubmit}
          className="flex flex-col gap-5 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6"
        >
          <Field label="Nombre" required>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="vm-oracle-fra-01"
              className={inputClass}
              required
              autoFocus
            />
          </Field>

          <Field
            label="Slug"
            hint="URL-friendly. Sugerido desde el nombre si lo dejas vacío."
          >
            <input
              type="text"
              value={effectiveSlug}
              onChange={(e) => {
                setSlug(e.target.value);
                setSlugTouched(true);
              }}
              placeholder="vm-oracle-fra-01"
              className={inputClass}
              pattern="[a-z0-9]+(-[a-z0-9]+)*"
            />
          </Field>

          <div className="grid grid-cols-2 gap-4">
            <Field label="IP pública">
              <input
                type="text"
                value={publicIp}
                onChange={(e) => setPublicIp(e.target.value)}
                placeholder="203.0.113.10"
                className={inputClass}
              />
            </Field>

            <Field label="IP privada">
              <input
                type="text"
                value={privateIp}
                onChange={(e) => setPrivateIp(e.target.value)}
                placeholder="10.0.0.10"
                className={inputClass}
              />
            </Field>
          </div>

          <Field label="Descripción">
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
              className={inputClass}
              placeholder="Oracle Free Tier ARM, ámsterdam"
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
              onClick={() => router.push("/vms")}
              className="rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-300 transition hover:bg-zinc-800"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={loading || !name}
              className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
            >
              {loading ? "Registrando..." : "Registrar VM"}
            </button>
          </div>
        </form>
      </div>
    </main>
  );
}

function SuccessScreen({
  result,
  onContinue,
}: {
  result: RegisterVmResponse;
  onContinue: () => void;
}) {
  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-2xl flex-col gap-6">
        <header>
          <p className="text-xs uppercase tracking-wider text-emerald-400">
            VM registrada
          </p>
          <h1 className="mt-1 text-3xl font-semibold">{result.name}</h1>
          <p className="mt-1 font-mono text-xs text-zinc-500">{result.slug}</p>
        </header>

        <div className="rounded-xl border border-amber-500/40 bg-amber-500/10 p-4 text-sm text-amber-200">
          <p className="font-medium">
            Este token solo se muestra una vez.
          </p>
          <p className="mt-1 text-amber-200/80">
            Cópialo y guárdalo en el satélite ahora. Si lo pierdes tendrás que
            generar uno nuevo.
          </p>
        </div>

        <CopyBlock
          label="Token de satélite"
          value={result.token_plaintext}
          mono
          oneLine
        />

        <CopyBlock
          label="Script de instalación"
          value={result.install_script}
          mono
        />

        <div className="flex justify-end gap-3">
          <button
            type="button"
            onClick={onContinue}
            className="rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-300 transition hover:bg-zinc-800"
          >
            Volver al listado
          </button>
          <Link
            href={`/vms/${result.vm_id}`}
            className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
          >
            Ir al detalle
          </Link>
        </div>
      </div>
    </main>
  );
}

function CopyBlock({
  label,
  value,
  mono,
  oneLine,
}: {
  label: string;
  value: string;
  mono?: boolean;
  oneLine?: boolean;
}) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 1800);
    } catch {
      // navigator.clipboard puede no estar disponible (http no-localhost)
    }
  }

  return (
    <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40">
      <div className="flex items-center justify-between border-b border-zinc-800 px-4 py-2">
        <span className="text-xs uppercase tracking-wider text-zinc-500">
          {label}
        </span>
        <button
          type="button"
          onClick={copy}
          className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-300 transition hover:bg-zinc-800"
        >
          {copied ? "Copiado" : "Copiar"}
        </button>
      </div>
      <pre
        className={`overflow-x-auto px-4 py-3 text-xs text-zinc-200 ${
          mono ? "font-mono" : ""
        } ${oneLine ? "whitespace-nowrap" : "whitespace-pre"}`}
      >
        {value}
      </pre>
    </div>
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
