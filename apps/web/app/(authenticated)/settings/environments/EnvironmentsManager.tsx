"use client";

import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import {
  SortableContext,
  arrayMove,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { GripVertical, Loader2, Trash2 } from "lucide-react";
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
import { cn } from "@/lib/utils";
import { ApiError, api } from "@/lib/api";
import type {
  CreateEnvironmentDefinitionRequest,
  EnvironmentDefinitionDto,
  ReorderEnvironmentDefinitionsRequest,
} from "@/lib/types";

const SLUG_RE = /^[a-z][a-z0-9-]{0,30}[a-z0-9]$/;

export function EnvironmentsManager({
  initial,
}: {
  initial: EnvironmentDefinitionDto[];
}) {
  const router = useRouter();
  const sortedInitial = useMemo(
    () => [...initial].sort((a, b) => a.order - b.order),
    [initial],
  );
  const [items, setItems] = useState<EnvironmentDefinitionDto[]>(sortedInitial);
  const [pending, setPending] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] =
    useState<EnvironmentDefinitionDto | null>(null);

  useEffect(() => {
    setItems(sortedInitial);
  }, [sortedInitial]);

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    }),
  );

  async function applyReorder(nextOrder: EnvironmentDefinitionDto[]) {
    setPending("reorder");
    setItems(nextOrder);
    try {
      const body: ReorderEnvironmentDefinitionsRequest = {
        ids: nextOrder.map((i) => i.id),
      };
      await api("/api/settings/environments/reorder", {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success("Orden actualizado");
      router.refresh();
    } catch (e) {
      setItems(sortedInitial);
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ??
            (e.body as { detail?: string } | undefined)?.detail ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setPending(null);
    }
  }

  function onDragEnd(event: DragEndEvent) {
    const { active, over } = event;
    if (!over || active.id === over.id) return;
    const oldIndex = items.findIndex((i) => i.id === active.id);
    const newIndex = items.findIndex((i) => i.id === over.id);
    if (oldIndex === -1 || newIndex === -1) return;
    const next = arrayMove(items, oldIndex, newIndex);
    void applyReorder(next);
  }

  async function handleConfirmDelete() {
    if (!deleteTarget) return;
    const target = deleteTarget;
    setPending(target.id);
    try {
      await api(`/api/settings/environments/${encodeURIComponent(target.id)}`, {
        method: "DELETE",
      });
      toast.success(`Ambiente "${target.slug}" eliminado`);
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ??
            (e.body as { detail?: string } | undefined)?.detail ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setPending(null);
      setDeleteTarget(null);
    }
  }

  return (
    <div className="flex flex-col gap-6">
      {items.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center gap-2 p-12 text-center">
            <h2 className="text-xl font-semibold text-foreground">
              Aún sin ambientes
            </h2>
            <p className="text-sm text-muted-foreground">
              Creá el primero abajo. La convención es{" "}
              <span className="font-mono">preview</span> →{" "}
              <span className="font-mono">test</span> →{" "}
              <span className="font-mono">staging</span> →{" "}
              <span className="font-mono">production</span>.
            </p>
          </CardContent>
        </Card>
      ) : (
        <DndContext
          sensors={sensors}
          collisionDetection={closestCenter}
          onDragEnd={onDragEnd}
        >
          <SortableContext
            items={items.map((i) => i.id)}
            strategy={verticalListSortingStrategy}
          >
            <ul className="flex flex-col gap-2">
              {items.map((env) => (
                <EnvironmentRow
                  key={env.id}
                  env={env}
                  busy={pending === env.id || pending === "reorder"}
                  onDelete={() => setDeleteTarget(env)}
                />
              ))}
            </ul>
          </SortableContext>
        </DndContext>
      )}

      <NewEnvironmentForm
        existingSlugs={items.map((i) => i.slug)}
        onCreated={() => router.refresh()}
      />

      <Dialog
        open={deleteTarget !== null}
        onOpenChange={(o) => !o && setDeleteTarget(null)}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              Borrar ambiente "{deleteTarget?.slug}"
            </DialogTitle>
            <DialogDescription>
              Si algún proyecto lo referencia, esos referers quedarán
              apuntando a un slug inválido. Esta acción no se puede deshacer.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              variant="ghost"
              onClick={() => setDeleteTarget(null)}
              disabled={pending !== null}
            >
              Cancelar
            </Button>
            <Button
              variant="destructive"
              onClick={handleConfirmDelete}
              disabled={pending !== null}
            >
              {pending === deleteTarget?.id ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              Borrar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function EnvironmentRow({
  env,
  busy,
  onDelete,
}: {
  env: EnvironmentDefinitionDto;
  busy: boolean;
  onDelete: () => void;
}) {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id: env.id });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  };

  return (
    <li ref={setNodeRef} style={style} className="list-none">
      <Card
        className={cn(
          "border-border bg-card transition-colors hover:bg-secondary/40",
          isDragging && "z-10 shadow-md",
        )}
      >
        <CardContent className="flex items-center gap-3 p-3">
          <button
            type="button"
            className="cursor-grab touch-none rounded p-1 text-muted-foreground hover:bg-secondary hover:text-foreground active:cursor-grabbing"
            aria-label="Arrastrar para reordenar"
            disabled={busy}
            {...attributes}
            {...listeners}
          >
            <GripVertical className="h-5 w-5" />
          </button>
          <div className="flex flex-1 items-center gap-3">
            <span className="rounded border border-border bg-muted px-2 py-0.5 font-mono text-[11px] text-foreground">
              {env.slug}
            </span>
            <span className="text-sm text-foreground">{env.displayName}</span>
            <span className="ml-auto hidden font-mono text-[10px] text-muted-foreground md:inline">
              {env.id}
            </span>
          </div>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={onDelete}
            disabled={busy}
            className="text-destructive hover:bg-destructive/10 hover:text-destructive"
          >
            <Trash2 className="mr-1 h-3.5 w-3.5" />
            Borrar
          </Button>
        </CardContent>
      </Card>
    </li>
  );
}

function NewEnvironmentForm({
  existingSlugs,
  onCreated,
}: {
  existingSlugs: string[];
  onCreated: () => void;
}) {
  const [submitting, setSubmitting] = useState(false);

  const schema = z.object({
    slug: z
      .string()
      .min(1, "Requerido")
      .regex(
        SLUG_RE,
        "Slug lowercase alfanumérico con guiones (2-32 chars, sin guión al inicio/fin).",
      )
      .refine((s) => !existingSlugs.includes(s.trim().toLowerCase()), {
        message: "Ya existe un ambiente con ese slug.",
      }),
    displayName: z.string().min(1, "Requerido").max(100),
  });

  type FormValues = z.infer<typeof schema>;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { slug: "", displayName: "" },
  });

  async function onSubmit(values: FormValues) {
    setSubmitting(true);
    try {
      const body: CreateEnvironmentDefinitionRequest = {
        slug: values.slug.trim().toLowerCase(),
        displayName: values.displayName.trim(),
        order: null,
      };
      await api("/api/settings/environments/", {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success("Ambiente creado");
      form.reset({ slug: "", displayName: "" });
      onCreated();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ??
            (e.body as { detail?: string } | undefined)?.detail ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Card>
      <CardContent className="flex flex-col gap-4 p-5">
        <h3 className="text-sm font-semibold text-foreground">
          Nuevo ambiente
        </h3>
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit(onSubmit)}
            className="grid grid-cols-1 gap-3 md:grid-cols-[1fr_1fr_auto]"
          >
            <FormField
              control={form.control}
              name="slug"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Slug *</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      onChange={(e) =>
                        field.onChange(e.target.value.toLowerCase())
                      }
                      maxLength={32}
                      placeholder="preview"
                      className="font-mono text-xs"
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="displayName"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Display name *</FormLabel>
                  <FormControl>
                    <Input {...field} maxLength={100} placeholder="Preview" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <div className="flex items-end">
              <Button type="submit" disabled={submitting} className="w-full">
                {submitting ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                Crear ambiente
              </Button>
            </div>
            <p className="text-xs text-muted-foreground md:col-span-3">
              Aethra aplica order = max(order) + 1 automáticamente; después
              podés reordenarlo arrastrando con el handle.
            </p>
          </form>
        </Form>
      </CardContent>
    </Card>
  );
}
