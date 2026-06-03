"use client";

import { useMemo, useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import ReactMarkdown from "react-markdown";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import {
  Image as ImageIcon,
  Loader2,
  Pin,
  PinOff,
  Star,
  Trash2,
} from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs";
import { Textarea } from "@/components/ui/textarea";
import { cn } from "@/lib/utils";
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
  const t = useTranslations("pages.notes.editor");
  const tParent = useTranslations("pages.notes");
  const [isPending, startTransition] = useTransition();

  const schema = useMemo(
    () =>
      z.object({
        title: z.string().min(1, t("validation_required")).max(255),
        markdownBody: z.string().optional().or(z.literal("")),
      }),
    [t],
  );

  type FormValues = z.infer<typeof schema>;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { title: "", markdownBody: "" },
  });

  function onSubmit(values: FormValues) {
    const payload: CreateNoteRequest = {
      scopeType,
      scopeId,
      title: values.title,
      markdownBody: values.markdownBody ?? "",
    };
    startTransition(async () => {
      try {
        const created = await api<NoteDetail>("/api/notes/", {
          method: "POST",
          body: JSON.stringify(payload),
        });
        toast.success(t("toast_created"));
        form.reset({ title: "", markdownBody: "" });
        onCreated?.(created);
      } catch (e) {
        const msg =
          e instanceof ApiError
            ? (e.body as { message?: string } | undefined)?.message ??
              `Error ${e.status}`
            : e instanceof Error
              ? e.message
              : tParent("error_unknown");
        toast.error(msg);
      }
    });
  }

  const body = form.watch("markdownBody") ?? "";

  return (
    <Card>
      <CardContent className="p-5">
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit(onSubmit)}
            className="flex flex-col gap-3"
          >
            <FormField
              control={form.control}
              name="title"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("label_title")}</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder={t("placeholder_title")}
                      maxLength={255}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="markdownBody"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("label_body")}</FormLabel>
                  <FormControl>
                    <Tabs defaultValue="edit">
                      <TabsList>
                        <TabsTrigger value="edit">{t("tab_edit")}</TabsTrigger>
                        <TabsTrigger value="preview">{t("tab_preview")}</TabsTrigger>
                      </TabsList>
                      <TabsContent value="edit">
                        <Textarea
                          {...field}
                          placeholder={t("placeholder_markdown")}
                          rows={6}
                          className="font-mono text-sm"
                        />
                      </TabsContent>
                      <TabsContent value="preview">
                        <div className="prose prose-sm min-h-[160px] max-w-none rounded-md border border-border bg-muted p-3 text-foreground dark:prose-invert">
                          {body.trim() ? (
                            <ReactMarkdown>{body}</ReactMarkdown>
                          ) : (
                            <span className="text-muted-foreground">
                              {t("nothing_to_preview")}
                            </span>
                          )}
                        </div>
                      </TabsContent>
                    </Tabs>
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <div className="flex items-center justify-end">
              <Button type="submit" disabled={isPending}>
                {isPending ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                {t("create")}
              </Button>
            </div>
          </form>
        </Form>
      </CardContent>
    </Card>
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
  const t = useTranslations("pages.notes.editor");
  const tParent = useTranslations("pages.notes");
  const [editing, setEditing] = useState(false);
  const [title, setTitle] = useState(note.title);
  const [body, setBody] = useState(note.markdownBody);
  const [isPending, startTransition] = useTransition();
  const [deleteOpen, setDeleteOpen] = useState(false);

  function save() {
    startTransition(async () => {
      try {
        const updated = await api<NoteDetail>(`/api/notes/${note.id}`, {
          method: "PATCH",
          body: JSON.stringify({ title, markdownBody: body }),
        });
        toast.success(t("toast_updated"));
        onChanged(updated);
        setEditing(false);
      } catch (e) {
        toast.error(e instanceof Error ? e.message : tParent("error_unknown"));
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
        toast.success(note.isPinned ? t("toast_unpinned") : t("toast_pinned"));
        onChanged(updated);
      } catch (e) {
        toast.error(e instanceof Error ? e.message : tParent("error_unknown"));
      }
    });
  }

  function remove() {
    startTransition(async () => {
      try {
        await api<void>(`/api/notes/${note.id}`, { method: "DELETE" });
        toast.success(t("toast_deleted"));
        onDeleted(note.id);
      } catch (e) {
        toast.error(e instanceof Error ? e.message : tParent("error_unknown"));
      } finally {
        setDeleteOpen(false);
      }
    });
  }

  return (
    <Card
      className={cn(
        "transition-colors",
        note.isPinned ? "border-warning/40 bg-warning/5" : "bg-card",
      )}
    >
      <CardContent className="flex flex-col gap-3 p-5">
        <header className="flex items-start justify-between gap-3">
          {editing ? (
            <Input
              type="text"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              maxLength={255}
              className="flex-1"
            />
          ) : (
            <h3 className="flex flex-1 items-center gap-2 text-lg font-semibold text-foreground">
              {note.isPinned && (
                <Star
                  className="h-4 w-4 fill-warning text-warning"
                  aria-label={t("pinned_aria")}
                />
              )}
              {note.title}
            </h3>
          )}
          <div className="flex shrink-0 gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={togglePin}
              disabled={isPending}
            >
              {note.isPinned ? (
                <>
                  <PinOff className="mr-1 h-3.5 w-3.5" />
                  {t("unpin")}
                </>
              ) : (
                <>
                  <Pin className="mr-1 h-3.5 w-3.5" />
                  {t("pin")}
                </>
              )}
            </Button>
            {editing ? (
              <>
                <Button
                  type="button"
                  size="sm"
                  onClick={save}
                  disabled={isPending}
                >
                  {isPending ? (
                    <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" />
                  ) : null}
                  {t("save")}
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    setEditing(false);
                    setTitle(note.title);
                    setBody(note.markdownBody);
                  }}
                >
                  {t("cancel")}
                </Button>
              </>
            ) : (
              <>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => setEditing(true)}
                >
                  {t("edit")}
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => setDeleteOpen(true)}
                  disabled={isPending}
                  className="text-destructive hover:bg-destructive/10 hover:text-destructive"
                >
                  <Trash2 className="mr-1 h-3.5 w-3.5" />
                  {t("delete")}
                </Button>
              </>
            )}
          </div>
        </header>

        {editing ? (
          <Tabs defaultValue="edit">
            <TabsList>
              <TabsTrigger value="edit">{t("tab_edit")}</TabsTrigger>
              <TabsTrigger value="preview">{t("tab_preview")}</TabsTrigger>
            </TabsList>
            <TabsContent value="edit">
              <Textarea
                value={body}
                onChange={(e) => setBody(e.target.value)}
                rows={8}
                className="font-mono text-sm"
              />
            </TabsContent>
            <TabsContent value="preview">
              <div className="prose prose-sm min-h-[160px] max-w-none rounded-md border border-border bg-muted p-3 text-foreground dark:prose-invert">
                {body.trim() ? (
                  <ReactMarkdown>{body}</ReactMarkdown>
                ) : (
                  <span className="text-muted-foreground">
                    {t("nothing_to_preview")}
                  </span>
                )}
              </div>
            </TabsContent>
          </Tabs>
        ) : (
          <div className="prose prose-sm max-w-none text-foreground dark:prose-invert">
            {note.markdownBody.trim() ? (
              <ReactMarkdown>{note.markdownBody}</ReactMarkdown>
            ) : (
              <p className="text-muted-foreground">{t("empty_body")}</p>
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
                className="block w-32 overflow-hidden rounded-md border border-border bg-muted transition-colors hover:border-primary/40"
                title={img.originalFilename}
              >
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={img.url}
                  alt={img.originalFilename}
                  className="h-24 w-full object-cover"
                />
                <div className="truncate px-2 py-1 text-[10px] text-muted-foreground">
                  {img.originalFilename}
                </div>
              </a>
            ))}
          </div>
        )}

        <NoteImageUploader noteId={note.id} onUploaded={onChanged} />

        <footer className="flex justify-between text-[10px] text-muted-foreground">
          <span>
            {t("updated_at", { date: new Date(note.updatedAt).toLocaleString() })}
          </span>
          <span>
            {note.images.length === 1
              ? t("images_count_one", { count: note.images.length })
              : t("images_count_other", { count: note.images.length })}
          </span>
        </footer>

        <Dialog open={deleteOpen} onOpenChange={setDeleteOpen}>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>
                {t("delete_dialog_title", { title: note.title })}
              </DialogTitle>
              <DialogDescription>
                {t("delete_dialog_description")}
              </DialogDescription>
            </DialogHeader>
            <DialogFooter>
              <Button variant="ghost" onClick={() => setDeleteOpen(false)}>
                {t("cancel")}
              </Button>
              <Button
                variant="destructive"
                onClick={remove}
                disabled={isPending}
              >
                {isPending ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                {t("delete")}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </CardContent>
    </Card>
  );
}

function NoteImageUploader({
  noteId,
  onUploaded,
}: {
  noteId: string;
  onUploaded: (note: NoteDetail) => void;
}) {
  const t = useTranslations("pages.notes.editor");
  const tParent = useTranslations("pages.notes");
  const [uploading, setUploading] = useState(false);

  async function onChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) {
      return;
    }
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
      const updated = await api<NoteDetail>(`/api/notes/${noteId}`);
      toast.success(t("image_uploaded"));
      onUploaded(updated);
    } catch (e) {
      toast.error(e instanceof Error ? e.message : tParent("error_unknown"));
    } finally {
      setUploading(false);
      e.target.value = "";
    }
  }

  return (
    <div className="flex items-center gap-3">
      <Button
        type="button"
        variant="outline"
        size="sm"
        asChild
        disabled={uploading}
      >
        <label className="cursor-pointer">
          <input
            type="file"
            accept="image/jpeg,image/png,image/webp,image/gif"
            onChange={onChange}
            disabled={uploading}
            className="hidden"
          />
          {uploading ? (
            <Loader2 className="mr-2 h-3.5 w-3.5 animate-spin" />
          ) : (
            <ImageIcon className="mr-2 h-3.5 w-3.5" />
          )}
          {uploading ? t("image_uploading") : t("image_attach")}
        </label>
      </Button>
      <span className="text-[10px] text-muted-foreground">
        {t("image_hint")}
      </span>
    </div>
  );
}
