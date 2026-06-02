"use client";

import { useState, useTransition } from "react";
import ReactMarkdown from "react-markdown";
import { api, ApiError } from "@/lib/api";
import type {
  CreateNoteRequest,
  NoteDetail,
  NoteScopeType,
} from "@/lib/types";

/**
 * Editor de creación de notas. Textarea con preview lado-a-lado (renderizado por
 * react-markdown). Tras enviar, recarga la página para reflejar la lista.
 */
export function NewNoteForm({
  scopeType,
  scopeId,
  onCreated,
}: {
  scopeType: NoteScopeType;
  scopeId: string;
  onCreated?: (note: NoteDetail) => void;
}) {
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();
  const [showPreview, setShowPreview] = useState(false);

  function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    const payload: CreateNoteRequest = {
      scopeType,
      scopeId,
      title,
      markdownBody: body,
    };
    startTransition(async () => {
      try {
        const created = await api<NoteDetail>("/api/notes/", {
          method: "POST",
          body: JSON.stringify(payload),
        });
        setTitle("");
        setBody("");
        setShowPreview(false);
        onCreated?.(created);
      } catch (e) {
        if (e instanceof ApiError) {
          const body = e.body as { message?: string } | undefined;
          setError(body?.message ?? `Error ${e.status}`);
        } else {
          setError(e instanceof Error ? e.message : "Error desconocido");
        }
      }
    });
  }

  return (
    <form
      onSubmit={onSubmit}
      className="flex flex-col gap-3 rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5"
    >
      <input
        type="text"
        placeholder="Título de la nota"
        value={title}
        onChange={(e) => setTitle(e.target.value)}
        className="rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-zinc-100 outline-none focus:border-emerald-500"
        required
        maxLength={255}
      />
      {showPreview ? (
        <div className="min-h-[160px] rounded-lg border border-zinc-800 bg-zinc-950 p-3 prose prose-invert prose-sm max-w-none">
          {body.trim() ? (
            <ReactMarkdown>{body}</ReactMarkdown>
          ) : (
            <span className="text-zinc-500">Nada que previsualizar.</span>
          )}
        </div>
      ) : (
        <textarea
          placeholder="Markdown..."
          value={body}
          onChange={(e) => setBody(e.target.value)}
          rows={6}
          className="rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 font-mono text-sm text-zinc-100 outline-none focus:border-emerald-500"
        />
      )}
      {error && (
        <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
          {error}
        </p>
      )}
      <div className="flex items-center justify-end gap-2">
        <button
          type="button"
          onClick={() => setShowPreview((p) => !p)}
          className="rounded-full border border-zinc-700 px-4 py-1.5 text-xs text-zinc-300 hover:bg-zinc-800"
        >
          {showPreview ? "Editar" : "Previsualizar"}
        </button>
        <button
          type="submit"
          disabled={isPending || !title.trim()}
          className="rounded-full bg-emerald-500 px-5 py-1.5 text-sm font-medium text-emerald-950 transition hover:bg-emerald-400 disabled:opacity-50"
        >
          {isPending ? "Creando..." : "Crear nota"}
        </button>
      </div>
    </form>
  );
}

/**
 * Visor + editor en línea de una nota existente. Permite editar title/body, fijar/
 * desfijar y eliminar.
 */
export function NoteCard({
  note,
  onChanged,
  onDeleted,
}: {
  note: NoteDetail;
  onChanged: (note: NoteDetail) => void;
  onDeleted: (id: string) => void;
}) {
  const [editing, setEditing] = useState(false);
  const [title, setTitle] = useState(note.title);
  const [body, setBody] = useState(note.markdownBody);
  const [isPending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);

  function save() {
    setError(null);
    startTransition(async () => {
      try {
        const updated = await api<NoteDetail>(`/api/notes/${note.id}`, {
          method: "PATCH",
          body: JSON.stringify({ title, markdownBody: body }),
        });
        onChanged(updated);
        setEditing(false);
      } catch (e) {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
    });
  }

  function togglePin() {
    startTransition(async () => {
      try {
        const updated = await api<NoteDetail>(`/api/notes/${note.id}/pin`, {
          method: "POST",
          body: JSON.stringify({ pinned: !note.isPinned }),
        });
        onChanged(updated);
      } catch (e) {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
    });
  }

  function remove() {
    if (!confirm(`¿Eliminar la nota "${note.title}"?`)) {
      return;
    }
    startTransition(async () => {
      try {
        await api<void>(`/api/notes/${note.id}`, { method: "DELETE" });
        onDeleted(note.id);
      } catch (e) {
        setError(e instanceof Error ? e.message : "Error desconocido");
      }
    });
  }

  return (
    <article
      className={`flex flex-col gap-3 rounded-2xl border p-5 ${
        note.isPinned
          ? "border-amber-500/40 bg-amber-500/5"
          : "border-zinc-800 bg-zinc-900/40"
      }`}
    >
      <header className="flex items-start justify-between gap-3">
        {editing ? (
          <input
            type="text"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            className="flex-1 rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-1.5 text-zinc-100 outline-none focus:border-emerald-500"
            maxLength={255}
          />
        ) : (
          <h3 className="flex-1 text-lg font-semibold text-zinc-100">
            {note.isPinned && (
              <span className="mr-2 text-amber-400" title="Fijada">
                ★
              </span>
            )}
            {note.title}
          </h3>
        )}
        <div className="flex shrink-0 gap-2 text-xs">
          <button
            type="button"
            onClick={togglePin}
            disabled={isPending}
            className="rounded-full border border-zinc-700 px-3 py-1 text-zinc-300 hover:bg-zinc-800"
          >
            {note.isPinned ? "Desfijar" : "Fijar"}
          </button>
          {editing ? (
            <>
              <button
                type="button"
                onClick={save}
                disabled={isPending}
                className="rounded-full bg-emerald-500 px-3 py-1 text-emerald-950 hover:bg-emerald-400 disabled:opacity-50"
              >
                Guardar
              </button>
              <button
                type="button"
                onClick={() => {
                  setEditing(false);
                  setTitle(note.title);
                  setBody(note.markdownBody);
                }}
                className="rounded-full border border-zinc-700 px-3 py-1 text-zinc-300 hover:bg-zinc-800"
              >
                Cancelar
              </button>
            </>
          ) : (
            <>
              <button
                type="button"
                onClick={() => setEditing(true)}
                className="rounded-full border border-zinc-700 px-3 py-1 text-zinc-300 hover:bg-zinc-800"
              >
                Editar
              </button>
              <button
                type="button"
                onClick={remove}
                disabled={isPending}
                className="rounded-full border border-rose-500/40 px-3 py-1 text-rose-300 hover:bg-rose-500/10"
              >
                Eliminar
              </button>
            </>
          )}
        </div>
      </header>

      {editing ? (
        <textarea
          value={body}
          onChange={(e) => setBody(e.target.value)}
          rows={8}
          className="rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 font-mono text-sm text-zinc-100 outline-none focus:border-emerald-500"
        />
      ) : (
        <div className="prose prose-invert prose-sm max-w-none">
          {note.markdownBody.trim() ? (
            <ReactMarkdown>{note.markdownBody}</ReactMarkdown>
          ) : (
            <p className="text-zinc-500">(vacía)</p>
          )}
        </div>
      )}

      {note.images.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {note.images.map((img) => (
            <a
              key={img.imageId}
              href={img.url}
              target="_blank"
              rel="noopener noreferrer"
              className="block w-32 overflow-hidden rounded-lg border border-zinc-800 bg-zinc-950"
              title={img.originalFilename}
            >
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src={img.url}
                alt={img.originalFilename}
                className="h-24 w-full object-cover"
              />
              <div className="truncate px-2 py-1 text-[10px] text-zinc-500">
                {img.originalFilename}
              </div>
            </a>
          ))}
        </div>
      )}

      <NoteImageUploader noteId={note.id} onUploaded={onChanged} />

      {error && (
        <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-xs text-rose-300">
          {error}
        </p>
      )}

      <footer className="flex justify-between text-[10px] text-zinc-600">
        <span>Actualizada: {new Date(note.updatedAt).toLocaleString()}</span>
        <span>
          {note.images.length} {note.images.length === 1 ? "imagen" : "imágenes"}
        </span>
      </footer>
    </article>
  );
}

function NoteImageUploader({
  noteId,
  onUploaded,
}: {
  noteId: string;
  onUploaded: (note: NoteDetail) => void;
}) {
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) {
      return;
    }
    setError(null);
    setUploading(true);
    try {
      const form = new FormData();
      form.append("file", file);
      const res = await fetch(
        `${
          process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000"
        }/api/notes/${noteId}/images`,
        {
          method: "POST",
          body: form,
          credentials: "include",
        },
      );
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || `Error ${res.status}`);
      }
      // Refrescar la nota para incluir la imagen recién subida.
      const updated = await api<NoteDetail>(`/api/notes/${noteId}`);
      onUploaded(updated);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error desconocido");
    } finally {
      setUploading(false);
      e.target.value = "";
    }
  }

  return (
    <div className="flex items-center gap-3">
      <label className="cursor-pointer rounded-full border border-zinc-700 px-3 py-1 text-xs text-zinc-300 hover:bg-zinc-800">
        <input
          type="file"
          accept="image/jpeg,image/png,image/webp,image/gif"
          onChange={onChange}
          disabled={uploading}
          className="hidden"
        />
        {uploading ? "Subiendo..." : "Adjuntar imagen"}
      </label>
      <span className="text-[10px] text-zinc-600">JPG/PNG/WEBP/GIF · máx 5 MB</span>
      {error && <span className="text-xs text-rose-400">{error}</span>}
    </div>
  );
}
