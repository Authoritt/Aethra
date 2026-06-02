"use client";

import { useEffect, useState, useTransition } from "react";
import { api, ApiError } from "@/lib/api";
import type {
  NoteDetail,
  NoteScopeType,
  NoteSummary,
} from "@/lib/types";
import { NewNoteForm, NoteCard } from "./NoteEditor";

/**
 * Lista interactiva de notas para un scope dado. Carga el detalle bajo demanda
 * cuando el usuario expande/edita.
 */
export function NotesList({
  scopeType,
  scopeId,
}: {
  scopeType: NoteScopeType;
  scopeId: string;
}) {
  const [summaries, setSummaries] = useState<NoteSummary[]>([]);
  const [details, setDetails] = useState<Record<string, NoteDetail>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [, startTransition] = useTransition();

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setLoading(true);
      try {
        const list = await api<NoteSummary[]>(
          `/api/notes/?scope_type=${scopeType}&scope_id=${encodeURIComponent(scopeId)}`,
        );
        if (cancelled) return;
        setSummaries(list);
        // Pre-fetch full detail (small N en MVP).
        const fullDetails = await Promise.all(
          list.map((s) => api<NoteDetail>(`/api/notes/${s.id}`)),
        );
        if (cancelled) return;
        const map: Record<string, NoteDetail> = {};
        for (const n of fullDetails) {
          map[n.id] = n;
        }
        setDetails(map);
      } catch (e) {
        if (cancelled) return;
        if (e instanceof ApiError) {
          const b = e.body as { message?: string } | undefined;
          setError(b?.message ?? `Error ${e.status}`);
        } else {
          setError(e instanceof Error ? e.message : "Error desconocido");
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }
    load();
    return () => {
      cancelled = true;
    };
  }, [scopeType, scopeId]);

  function onCreated(note: NoteDetail) {
    startTransition(() => {
      setDetails((d) => ({ ...d, [note.id]: note }));
      setSummaries((s) => [
        {
          id: note.id,
          scopeType: note.scopeType,
          scopeId: note.scopeId,
          title: note.title,
          isPinned: note.isPinned,
          imageCount: note.images.length,
          authorId: note.authorId,
          createdAt: note.createdAt,
          updatedAt: note.updatedAt,
        },
        ...s,
      ]);
    });
  }

  function onChanged(note: NoteDetail) {
    startTransition(() => {
      setDetails((d) => ({ ...d, [note.id]: note }));
      setSummaries((s) =>
        s
          .map((x) =>
            x.id === note.id
              ? {
                  ...x,
                  title: note.title,
                  isPinned: note.isPinned,
                  imageCount: note.images.length,
                  updatedAt: note.updatedAt,
                }
              : x,
          )
          .sort((a, b) => {
            if (a.isPinned !== b.isPinned) {
              return a.isPinned ? -1 : 1;
            }
            return b.updatedAt.localeCompare(a.updatedAt);
          }),
      );
    });
  }

  function onDeleted(id: string) {
    startTransition(() => {
      setDetails((d) => {
        const next = { ...d };
        delete next[id];
        return next;
      });
      setSummaries((s) => s.filter((n) => n.id !== id));
    });
  }

  return (
    <div className="flex flex-col gap-5">
      <NewNoteForm
        scopeType={scopeType}
        scopeId={scopeId}
        onCreated={onCreated}
      />
      {loading && (
        <p className="text-sm text-zinc-500">Cargando notas...</p>
      )}
      {error && (
        <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
          {error}
        </p>
      )}
      {!loading && summaries.length === 0 && (
        <p className="text-sm text-zinc-500">Aún no hay notas en este scope.</p>
      )}
      <div className="flex flex-col gap-4">
        {summaries.map((s) => {
          const detail = details[s.id];
          if (!detail) {
            return (
              <article
                key={s.id}
                className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5"
              >
                <h3 className="text-lg font-semibold text-zinc-100">
                  {s.title}
                </h3>
                <p className="mt-2 text-xs text-zinc-500">Cargando...</p>
              </article>
            );
          }
          return (
            <NoteCard
              key={detail.id}
              note={detail}
              onChanged={onChanged}
              onDeleted={onDeleted}
            />
          );
        })}
      </div>
    </div>
  );
}
