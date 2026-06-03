"use client";

import { useEffect, useState, useTransition } from "react";
import { Loader2 } from "lucide-react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Card, CardContent } from "@/components/ui/card";
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
  const t = useTranslations("pages.notes");
  const tCommon = useTranslations("common");
  const [summaries, setSummaries] = useState<NoteSummary[]>([]);
  const [details, setDetails] = useState<Record<string, NoteDetail>>({});
  const [loading, setLoading] = useState(true);
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
        const msg =
          e instanceof ApiError
            ? (e.body as { message?: string } | undefined)?.message ??
              `Error ${e.status}`
            : e instanceof Error
              ? e.message
              : t("error_unknown");
        toast.error(msg);
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
        <p className="flex items-center gap-2 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" />
          {tCommon("loading")}
        </p>
      )}
      {!loading && summaries.length === 0 && (
        <Card>
          <CardContent className="p-6 text-sm text-muted-foreground">
            {t("empty_description")}
          </CardContent>
        </Card>
      )}
      <div className="flex flex-col gap-4">
        {summaries.map((s) => {
          const detail = details[s.id];
          if (!detail) {
            return (
              <Card key={s.id}>
                <CardContent className="p-5">
                  <h3 className="text-lg font-semibold text-foreground">
                    {s.title}
                  </h3>
                  <p className="mt-2 text-xs text-muted-foreground">
                    {tCommon("loading_short")}
                  </p>
                </CardContent>
              </Card>
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
