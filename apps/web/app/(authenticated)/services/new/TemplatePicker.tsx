"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { ApiError, api } from "@/lib/api";
import type {
  CreateServiceRequest,
  ManagedServiceDetailDto,
  ServiceTemplateDto,
  VmDto,
} from "@/lib/types";

const SLUG_RE = /^[a-z][a-z0-9-]{0,30}$/;

export function TemplatePicker({
  templates,
  vms,
}: {
  templates: ServiceTemplateDto[];
  vms: VmDto[];
}) {
  const router = useRouter();
  const [selected, setSelected] = useState<ServiceTemplateDto | null>(null);
  const [slug, setSlug] = useState("");
  const [name, setName] = useState("");
  const [targetVmId, setTargetVmId] = useState<string>(vms[0]?.id ?? "");
  const [exposedExternally, setExposedExternally] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const slugError = useMemo(() => {
    if (!slug) return null;
    if (!SLUG_RE.test(slug)) {
      return "Solo minúsculas, dígitos y guiones; debe iniciar con letra (max 31 chars).";
    }
    return null;
  }, [slug]);

  function pickTemplate(tpl: ServiceTemplateDto) {
    setSelected(tpl);
    if (!name) setName(tpl.display_name);
    if (!slug) setSlug(suggestSlug(tpl));
    setError(null);
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!selected) return;
    if (slugError) {
      setError(slugError);
      return;
    }
    if (!targetVmId) {
      setError("Selecciona una VM target.");
      return;
    }
    setError(null);
    setLoading(true);
    try {
      const body: CreateServiceRequest = {
        template_id: selected.id,
        slug: slug.trim(),
        name: name.trim(),
        target_vm_id: targetVmId,
        exposed_externally: exposedExternally,
      };
      const created = await api<ManagedServiceDetailDto>("/api/services", {
        method: "POST",
        body: JSON.stringify(body),
      });
      router.push(`/services/${created.id}`);
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        const data = e.body as { detail?: string } | undefined;
        setError(data?.detail ?? `Error ${e.status}`);
      } else {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
    } finally {
      setLoading(false);
    }
  }

  if (!selected) {
    return (
      <section className="flex flex-col gap-4">
        <h2 className="text-sm uppercase tracking-wider text-zinc-500">
          Elige una plantilla
        </h2>
        <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {templates.map((tpl) => (
            <li key={tpl.id}>
              <button
                type="button"
                onClick={() => pickTemplate(tpl)}
                className="flex h-full w-full flex-col gap-3 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5 text-left transition hover:border-emerald-500/40 hover:bg-zinc-900/80"
              >
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <h3 className="truncate text-lg font-semibold text-zinc-100">
                      {tpl.display_name}
                    </h3>
                    <p className="mt-0.5 font-mono text-[10px] uppercase tracking-wider text-zinc-500">
                      {tpl.type}
                    </p>
                  </div>
                  <span className="shrink-0 rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 font-mono text-[10px] text-zinc-300">
                    v{tpl.version}
                  </span>
                </div>
                <p className="line-clamp-3 text-xs text-zinc-400">
                  {tpl.notes || "—"}
                </p>
                <div className="mt-auto flex items-center justify-between text-[11px] text-zinc-500">
                  <span className="font-mono">{tpl.image}</span>
                  <span className="font-mono">:{tpl.internal_port}</span>
                </div>
              </button>
            </li>
          ))}
        </ul>
      </section>
    );
  }

  return (
    <section className="flex flex-col gap-4">
      <header className="flex items-center justify-between gap-3 rounded-2xl border border-zinc-800 bg-zinc-900/40 px-5 py-3">
        <div className="min-w-0">
          <div className="text-[10px] uppercase tracking-wider text-zinc-500">
            Plantilla
          </div>
          <div className="flex items-center gap-2">
            <span className="text-sm font-semibold text-zinc-100">
              {selected.display_name}
            </span>
            <span className="rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 font-mono text-[10px] text-zinc-300">
              v{selected.version}
            </span>
          </div>
          <div className="mt-1 font-mono text-[11px] text-zinc-500">
            {selected.image}:{selected.internal_port}
          </div>
        </div>
        <button
          type="button"
          onClick={() => setSelected(null)}
          className="rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-300 transition hover:bg-zinc-800"
        >
          Cambiar
        </button>
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
            placeholder={selected.display_name}
            className={inputClass}
            required
            autoFocus
          />
        </Field>

        <Field
          label="Slug"
          required
          hint="Identificador del servicio. Minúsculas, dígitos y guiones; inicia con letra."
        >
          <input
            type="text"
            value={slug}
            onChange={(e) => setSlug(e.target.value)}
            placeholder="prod-postgres"
            className={inputClass}
            required
            autoComplete="off"
            spellCheck={false}
          />
          {slugError && (
            <span className="text-xs text-rose-400">{slugError}</span>
          )}
        </Field>

        <Field
          label="VM target"
          required
          hint="VM donde se ejecutará el contenedor del servicio."
        >
          <select
            value={targetVmId}
            onChange={(e) => setTargetVmId(e.target.value)}
            className={inputClass}
            required
          >
            {vms.length === 0 && <option value="">(no hay VMs)</option>}
            {vms.map((vm) => (
              <option key={vm.id} value={vm.id}>
                {vm.name} ({vm.slug})
              </option>
            ))}
          </select>
        </Field>

        <label className="flex items-start gap-3 rounded-lg border border-zinc-800 bg-zinc-950/40 p-3 text-sm text-zinc-300">
          <input
            type="checkbox"
            checked={exposedExternally}
            onChange={(e) => setExposedExternally(e.target.checked)}
            className="mt-0.5 size-4 accent-emerald-500"
          />
          <span className="flex flex-col gap-1">
            <span className="font-medium text-zinc-100">
              Exponer externamente
            </span>
            <span
              className="text-xs text-zinc-500"
              title="Por defecto el servicio solo es accesible vía red Docker interna. Actívalo solo si necesitas conectar herramientas externas (ej. DBeaver) — Aethra publicará el puerto en la VM."
            >
              Por defecto el servicio solo es accesible vía red Docker
              interna. Actívalo para abrir el puerto en la VM.
            </span>
          </span>
        </label>

        {error && (
          <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
            {error}
          </p>
        )}

        <div className="flex justify-end gap-3">
          <button
            type="button"
            onClick={() => router.push("/services")}
            className="rounded-full border border-zinc-700 px-5 py-2 text-sm text-zinc-300 transition hover:bg-zinc-800"
          >
            Cancelar
          </button>
          <button
            type="submit"
            disabled={loading || !!slugError || !name || !slug || !targetVmId}
            className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
          >
            {loading ? "Creando..." : "Crear servicio"}
          </button>
        </div>
      </form>
    </section>
  );
}

function suggestSlug(tpl: ServiceTemplateDto): string {
  const base = tpl.type.toLowerCase().replace(/[^a-z0-9-]/g, "-");
  return base.length > 0 && /^[a-z]/.test(base) ? base : `srv-${base}`;
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
