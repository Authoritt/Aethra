"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import {
  clearSecretFromSession,
  readSecretFromSession,
} from "../CreateKeyForm";

type LoadState =
  | { kind: "loading" }
  | { kind: "missing" }
  | { kind: "loaded"; secret: string };

export function CreatedSecretCard({ id }: { id: string | null }) {
  const router = useRouter();
  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const [revealed, setRevealed] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const [copyState, setCopyState] = useState<"idle" | "copied" | "error">("idle");

  useEffect(() => {
    if (!id) {
      setState({ kind: "missing" });
      return;
    }
    const secret = readSecretFromSession(id);
    if (!secret) {
      setState({ kind: "missing" });
      return;
    }
    setState({ kind: "loaded", secret });
  }, [id]);

  async function copy() {
    if (state.kind !== "loaded") return;
    try {
      await navigator.clipboard.writeText(state.secret);
      setCopyState("copied");
      setTimeout(() => setCopyState("idle"), 2500);
    } catch {
      setCopyState("error");
      setTimeout(() => setCopyState("idle"), 2500);
    }
  }

  function done() {
    if (id) clearSecretFromSession(id);
    router.push("/settings/api-keys");
    router.refresh();
  }

  if (state.kind === "loading") {
    return (
      <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6 text-sm text-zinc-400">
        Cargando secret...
      </div>
    );
  }

  if (state.kind === "missing") {
    return (
      <div className="flex flex-col gap-4 rounded-2xl border border-amber-500/40 bg-amber-500/5 p-6 text-sm">
        <h2 className="text-base font-semibold text-amber-200">
          No encontramos el secret en esta sesion
        </h2>
        <p className="text-amber-100/80">
          El secret solo existe en memoria del navegador durante el momento
          inmediatamente posterior a su creacion. Si recargaste la pagina,
          cerraste la pestana o abriste el enlace desde otro lugar, ya no es
          recuperable.
        </p>
        <p className="text-amber-100/80">
          Si crees que perdiste un secret recien creado, revoca la key y crea
          una nueva.
        </p>
        <div className="flex gap-3">
          <Link
            href="/settings/api-keys"
            className="rounded-full border border-zinc-700 px-4 py-2 text-xs text-zinc-200 transition hover:bg-zinc-800"
          >
            Volver al listado
          </Link>
          <Link
            href="/settings/api-keys/new"
            className="rounded-full bg-emerald-500 px-4 py-2 text-xs font-medium text-emerald-950 transition hover:bg-emerald-400"
          >
            Crear otra key
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-5">
      <div className="flex items-start gap-3 rounded-2xl border border-amber-500/40 bg-amber-500/10 p-4 text-sm">
        <div className="mt-0.5 flex size-6 shrink-0 items-center justify-center rounded-full border border-amber-400/40 bg-amber-500/20 text-[11px] font-bold text-amber-200">
          !
        </div>
        <div className="flex flex-col gap-1">
          <p className="font-medium text-amber-100">
            Este es el unico momento en que puedes ver el secret.
          </p>
          <p className="text-amber-100/80">
            Guardalo en tu password manager o variable de entorno antes de
            cerrar esta pagina. Aethra solo almacena el hash.
          </p>
        </div>
      </div>

      <div className="flex flex-col gap-3 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
        <div className="flex items-center justify-between gap-3">
          <span className="text-xs uppercase tracking-wider text-zinc-500">
            Secret
          </span>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => setRevealed((v) => !v)}
              className="rounded-full border border-zinc-700 px-3 py-1 text-[11px] text-zinc-300 transition hover:bg-zinc-800"
            >
              {revealed ? "Ocultar" : "Mostrar"}
            </button>
            <button
              type="button"
              onClick={copy}
              className="rounded-full bg-emerald-500 px-3 py-1 text-[11px] font-medium text-emerald-950 transition hover:bg-emerald-400"
            >
              {copyState === "copied"
                ? "Copiado!"
                : copyState === "error"
                  ? "Error al copiar"
                  : "Copiar al portapapeles"}
            </button>
          </div>
        </div>
        <pre
          className="overflow-x-auto rounded-lg border border-zinc-800 bg-zinc-950 px-4 py-3 font-mono text-sm text-zinc-100"
          aria-label={revealed ? "API key secret" : "API key secret oculto"}
        >
          {revealed ? state.secret : maskSecret(state.secret)}
        </pre>
        <p className="text-[11px] text-zinc-500">
          Usalo como{" "}
          <code className="rounded border border-zinc-800 bg-zinc-950 px-1 py-0.5 font-mono text-[10px] text-zinc-200">
            Authorization: Bearer {revealed ? state.secret.slice(0, 18) : "aethra_********"}
            ...
          </code>
        </p>
      </div>

      <label className="flex items-start gap-3 rounded-xl border border-zinc-800 bg-zinc-900/30 p-3 text-sm text-zinc-300">
        <input
          type="checkbox"
          checked={acknowledged}
          onChange={(e) => setAcknowledged(e.target.checked)}
          className="mt-0.5 size-4 accent-emerald-500"
        />
        <span>
          Confirmo que copie y guarde el secret en un lugar seguro. Entiendo
          que despues de salir no podre verlo de nuevo.
        </span>
      </label>

      <div className="flex justify-end">
        <button
          type="button"
          onClick={done}
          disabled={!acknowledged}
          className="rounded-full bg-emerald-500 px-5 py-2 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-40"
        >
          Listo
        </button>
      </div>
    </div>
  );
}

function maskSecret(secret: string): string {
  if (secret.length <= 12) return "*".repeat(secret.length);
  const head = secret.slice(0, 10);
  return `${head}${"*".repeat(Math.max(8, secret.length - 10))}`;
}
