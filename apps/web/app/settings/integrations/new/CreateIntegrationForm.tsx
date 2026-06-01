"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { ApiError, api } from "@/lib/api";
import type {
  CreateIntegrationCredentialRequest,
  IntegrationCredentialDto,
  IntegrationCredentialType,
} from "@/lib/types";

const NAME_RE = /^[a-z]+:[a-z0-9-]+$/;
const NAME_MAX = 100;
const DISPLAY_MAX = 200;

const TYPE_OPTIONS: { value: IntegrationCredentialType; label: string }[] = [
  { value: "Cloudflare", label: "Cloudflare API Token" },
  { value: "GitHubPat", label: "GitHub Personal Access Token" },
  { value: "Smtp", label: "SMTP (usuario + password)" },
  { value: "Registry", label: "Docker Registry" },
  { value: "GenericApiKey", label: "API Key generica" },
];

interface MetadataRow {
  key: string;
  value: string;
}

export function CreateIntegrationForm() {
  const router = useRouter();
  const [name, setName] = useState("");
  const [type, setType] = useState<IntegrationCredentialType>("Cloudflare");
  const [displayName, setDisplayName] = useState("");
  const [description, setDescription] = useState("");
  const [plainValue, setPlainValue] = useState("");
  const [metadata, setMetadata] = useState<MetadataRow[]>([]);
  const [revealValue, setRevealValue] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [createdValuePreview, setCreatedValuePreview] = useState<
    { dto: IntegrationCredentialDto; plainValue: string } | null
  >(null);

  const nameError = useMemo(() => {
    const trimmed = name.trim();
    if (!trimmed) return null;
    if (trimmed.length > NAME_MAX) return `Maximo ${NAME_MAX} caracteres.`;
    if (!NAME_RE.test(trimmed)) {
      return "Debe seguir el formato 'namespace:slug' (lowercase, alfanumerico y guiones). Ej: cloudflare:default.";
    }
    return null;
  }, [name]);

  const displayNameError = useMemo(() => {
    if (displayName.trim().length > DISPLAY_MAX) {
      return `Maximo ${DISPLAY_MAX} caracteres.`;
    }
    return null;
  }, [displayName]);

  const canSubmit =
    !loading &&
    name.trim().length > 0 &&
    !nameError &&
    displayName.trim().length > 0 &&
    !displayNameError &&
    plainValue.trim().length > 0;

  function addMetadataRow() {
    setMetadata((rows) => [...rows, { key: "", value: "" }]);
  }

  function updateMetadataRow(
    index: number,
    patch: Partial<MetadataRow>,
  ) {
    setMetadata((rows) =>
      rows.map((r, i) => (i === index ? { ...r, ...patch } : r)),
    );
  }

  function removeMetadataRow(index: number) {
    setMetadata((rows) => rows.filter((_, i) => i !== index));
  }

  function metadataObject(): Record<string, string> | null {
    const entries = metadata
      .map((r) => [r.key.trim(), r.value])
      .filter(([k]) => k.length > 0);
    if (entries.length === 0) return null;
    return Object.fromEntries(entries) as Record<string, string>;
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;
    setError(null);
    setLoading(true);
    try {
      const body: CreateIntegrationCredentialRequest = {
        name: name.trim(),
        type,
        displayName: displayName.trim(),
        plainValue,
        metadata: metadataObject(),
        description: description.trim() || null,
      };
      const created = await api<IntegrationCredentialDto>(
        "/api/settings/integrations/",
        {
          method: "POST",
          body: JSON.stringify(body),
        },
      );
      setCreatedValuePreview({ dto: created, plainValue });
      // Limpia el value en memoria del form (queda solo en createdValuePreview
      // mientras la pestania siga abierta) — no se persiste en sessionStorage.
      setPlainValue("");
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

  if (createdValuePreview) {
    return (
      <CreatedConfirmation
        dto={createdValuePreview.dto}
        plainValue={createdValuePreview.plainValue}
        onContinue={() => router.push("/settings/integrations")}
      />
    );
  }

  return (
    <form
      onSubmit={onSubmit}
      className="flex flex-col gap-6 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6"
    >
      <Field
        label="Name"
        required
        hint="Identificador estable formato 'namespace:slug'. Ej: cloudflare:default, registry:internal, github:bot."
      >
        <input
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value.toLowerCase())}
          maxLength={NAME_MAX}
          placeholder="cloudflare:default"
          className={inputClass}
          required
          autoFocus
        />
        {nameError && (
          <span className="text-xs text-rose-400">{nameError}</span>
        )}
      </Field>

      <Field
        label="Tipo"
        required
        hint="Solo es metadata para la UI; el resolver siempre usa el nombre."
      >
        <select
          value={type}
          onChange={(e) =>
            setType(e.target.value as IntegrationCredentialType)
          }
          className={inputClass}
          required
        >
          {TYPE_OPTIONS.map((opt) => (
            <option key={opt.value} value={opt.value}>
              {opt.label}
            </option>
          ))}
        </select>
      </Field>

      <Field
        label="Display name"
        required
        hint="Nombre legible que veras en listados. Ej: 'Cloudflare cuenta principal'."
      >
        <input
          type="text"
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
          maxLength={DISPLAY_MAX}
          placeholder="Cloudflare cuenta principal"
          className={inputClass}
          required
        />
        {displayNameError && (
          <span className="text-xs text-rose-400">{displayNameError}</span>
        )}
      </Field>

      <Field
        label="Descripcion (opcional)"
        hint="Util cuando hay varias credenciales del mismo tipo."
      >
        <input
          type="text"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          maxLength={500}
          placeholder="Token con scope Zone.DNS.Edit creado por mayra"
          className={inputClass}
        />
      </Field>

      <Field
        label="Valor"
        required
        hint="Texto plano del token / secret / password. Se cifra con DataProtection al guardar — solo lo veras una vez."
      >
        <div className="flex items-stretch gap-2">
          <textarea
            value={plainValue}
            onChange={(e) => setPlainValue(e.target.value)}
            placeholder={revealValue ? "tu-token-aqui" : "••••••••"}
            className={`${inputClass} min-h-[88px] flex-1 font-mono text-xs`}
            spellCheck={false}
            autoComplete="off"
            required
            style={
              revealValue
                ? undefined
                : ({
                    WebkitTextSecurity: "disc",
                    textSecurity: "disc",
                  } as React.CSSProperties)
            }
          />
          <button
            type="button"
            onClick={() => setRevealValue((v) => !v)}
            className="self-start rounded-lg border border-zinc-700 px-3 py-2 text-xs text-zinc-300 transition hover:bg-zinc-800"
          >
            {revealValue ? "Ocultar" : "Mostrar"}
          </button>
        </div>
        <div className="mt-1 rounded-lg border border-amber-500/30 bg-amber-500/5 p-2 text-xs text-amber-200">
          Solo veras este valor una vez. Si lo olvidas tendras que rotar la
          credencial.
        </div>
      </Field>

      <div className="flex flex-col gap-2 text-sm text-zinc-300">
        <div className="flex items-center justify-between">
          <span>
            Metadata (opcional)
            <span className="ml-2 text-xs font-normal text-zinc-500">
              Pares clave-valor para datos no secretos (account_id, region...)
            </span>
          </span>
          <button
            type="button"
            onClick={addMetadataRow}
            className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-300 transition hover:bg-zinc-800"
          >
            + Anadir fila
          </button>
        </div>
        {metadata.length === 0 && (
          <p className="text-xs text-zinc-500">
            Sin entradas. Anade filas solo si el proveedor necesita parametros
            extra.
          </p>
        )}
        {metadata.map((row, i) => (
          <div key={i} className="flex items-center gap-2">
            <input
              type="text"
              value={row.key}
              onChange={(e) =>
                updateMetadataRow(i, { key: e.target.value })
              }
              placeholder="clave"
              maxLength={64}
              className={`${inputClass} w-1/3 font-mono text-xs`}
            />
            <input
              type="text"
              value={row.value}
              onChange={(e) =>
                updateMetadataRow(i, { value: e.target.value })
              }
              placeholder="valor"
              className={`${inputClass} flex-1 font-mono text-xs`}
            />
            <button
              type="button"
              onClick={() => removeMetadataRow(i)}
              className="rounded-full border border-rose-500/30 px-2 py-1 text-xs text-rose-300 transition hover:bg-rose-500/10"
            >
              X
            </button>
          </div>
        ))}
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
          className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
        >
          {loading ? "Creando..." : "Crear credencial"}
        </button>
      </div>
    </form>
  );
}

function CreatedConfirmation({
  dto,
  plainValue,
  onContinue,
}: {
  dto: IntegrationCredentialDto;
  plainValue: string;
  onContinue: () => void;
}) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="flex flex-col gap-5 rounded-2xl border border-emerald-500/40 bg-emerald-500/5 p-6">
      <div>
        <h2 className="text-xl font-semibold text-emerald-200">
          Credencial creada
        </h2>
        <p className="mt-1 text-sm text-zinc-400">
          Copia el valor ahora. Es la unica vez que podras verlo: a partir de
          aqui solo persiste el blob cifrado.
        </p>
      </div>

      <dl className="grid grid-cols-1 gap-3 text-sm">
        <Row label="Name" value={dto.name} mono />
        <Row label="Tipo" value={dto.type} />
        <Row label="Display" value={dto.displayName} />
      </dl>

      <div className="rounded-lg border border-zinc-800 bg-zinc-950 p-3">
        <div className="text-xs uppercase tracking-wider text-zinc-500">
          Valor en claro
        </div>
        <pre className="mt-1 max-h-40 overflow-auto whitespace-pre-wrap break-all font-mono text-xs text-emerald-200">
          {plainValue}
        </pre>
        <button
          type="button"
          onClick={async () => {
            try {
              await navigator.clipboard.writeText(plainValue);
              setCopied(true);
              setTimeout(() => setCopied(false), 1500);
            } catch {
              /* clipboard puede fallar en http inseguro; ignoramos */
            }
          }}
          className="mt-2 rounded-full border border-emerald-500/40 px-3 py-1 text-xs text-emerald-200 transition hover:bg-emerald-500/10"
        >
          {copied ? "Copiado" : "Copiar al portapapeles"}
        </button>
      </div>

      <div className="flex justify-end">
        <button
          type="button"
          onClick={onContinue}
          className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400"
        >
          Continuar
        </button>
      </div>
    </div>
  );
}

function Row({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <dt className="text-xs uppercase tracking-wider text-zinc-500">
        {label}
      </dt>
      <dd
        className={`text-zinc-100 ${mono ? "font-mono text-xs" : "text-sm"}`}
      >
        {value}
      </dd>
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
