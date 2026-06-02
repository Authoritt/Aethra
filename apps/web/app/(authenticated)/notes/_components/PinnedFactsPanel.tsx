"use client";

import { useEffect, useState, useTransition } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import {
  Check,
  Copy,
  Eye,
  EyeOff,
  Loader2,
  Trash2,
} from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
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
import { Label } from "@/components/ui/label";
import { api, ApiError } from "@/lib/api";
import { cn } from "@/lib/utils";
import type {
  NoteScopeType,
  PinnedFactDto,
  UpsertPinnedFactRequest,
} from "@/lib/types";

/**
 * Panel CRUD de pinned facts del scope. Los secretos se enmascaran por default;
 * el botón "Revelar" recarga la lista con <c>reveal=true</c> para obtener el plaintext.
 */
export function PinnedFactsPanel({
  scopeType,
  scopeId,
}: {
  scopeType: NoteScopeType;
  scopeId: string;
}) {
  const [facts, setFacts] = useState<PinnedFactDto[]>([]);
  const [revealed, setRevealed] = useState(false);
  const [loading, setLoading] = useState(true);

  async function reload(reveal: boolean) {
    setLoading(true);
    try {
      const list = await api<PinnedFactDto[]>(
        `/api/pinned-facts/?scope_type=${scopeType}&scope_id=${encodeURIComponent(
          scopeId,
        )}&reveal=${reveal ? "true" : "false"}`,
      );
      setFacts(list);
      setRevealed(reveal);
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setLoading(true);
      try {
        const list = await api<PinnedFactDto[]>(
          `/api/pinned-facts/?scope_type=${scopeType}&scope_id=${encodeURIComponent(
            scopeId,
          )}&reveal=false`,
        );
        if (cancelled) return;
        setFacts(list);
        setRevealed(false);
      } catch (e) {
        if (cancelled) return;
        const msg =
          e instanceof ApiError
            ? (e.body as { message?: string } | undefined)?.message ??
              `Error ${e.status}`
            : e instanceof Error
              ? e.message
              : "Error desconocido";
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

  function onUpserted(fact: PinnedFactDto) {
    setFacts((arr) => {
      const idx = arr.findIndex((f) => f.id === fact.id || f.key === fact.key);
      if (idx >= 0) {
        const next = [...arr];
        next[idx] = fact;
        return next.sort((a, b) => a.key.localeCompare(b.key));
      }
      return [...arr, fact].sort((a, b) => a.key.localeCompare(b.key));
    });
  }

  function onDeleted(id: string) {
    setFacts((arr) => arr.filter((f) => f.id !== id));
  }

  return (
    <section className="flex flex-col gap-3">
      <div className="flex items-center justify-between">
        <h2 className="text-sm uppercase tracking-wider text-muted-foreground">
          Pinned facts
        </h2>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => reload(!revealed)}
        >
          {revealed ? (
            <>
              <EyeOff className="mr-2 h-3.5 w-3.5" />
              Ocultar secretos
            </>
          ) : (
            <>
              <Eye className="mr-2 h-3.5 w-3.5" />
              Revelar secretos
            </>
          )}
        </Button>
      </div>

      <PinnedFactForm
        scopeType={scopeType}
        scopeId={scopeId}
        onUpserted={onUpserted}
      />

      {loading && (
        <p className="flex items-center gap-2 text-xs text-muted-foreground">
          <Loader2 className="h-3.5 w-3.5 animate-spin" />
          Cargando...
        </p>
      )}
      {!loading && facts.length === 0 && (
        <p className="text-xs text-muted-foreground">
          Sin facts fijados todavía.
        </p>
      )}
      {facts.length > 0 && (
        <ul className="grid grid-cols-1 gap-2 md:grid-cols-2">
          {facts.map((f) => (
            <PinnedFactRow
              key={f.id}
              fact={f}
              revealed={revealed}
              onDeleted={onDeleted}
            />
          ))}
        </ul>
      )}
    </section>
  );
}

const factSchema = z.object({
  key: z
    .string()
    .min(1, "Requerido")
    .max(128)
    .regex(/^[A-Za-z0-9_.\-]+$/, "Solo alfanumérico, _, . o -"),
  value: z.string().min(1, "Requerido"),
  description: z.string().max(500).optional().or(z.literal("")),
  isSecret: z.boolean(),
});

type FactFormValues = z.infer<typeof factSchema>;

function PinnedFactForm({
  scopeType,
  scopeId,
  onUpserted,
}: {
  scopeType: NoteScopeType;
  scopeId: string;
  onUpserted: (fact: PinnedFactDto) => void;
}) {
  const [isPending, startTransition] = useTransition();

  const form = useForm<FactFormValues>({
    resolver: zodResolver(factSchema),
    defaultValues: {
      key: "",
      value: "",
      description: "",
      isSecret: true,
    },
  });

  const isSecret = form.watch("isSecret");

  function onSubmit(values: FactFormValues) {
    const payload: UpsertPinnedFactRequest = {
      scopeType,
      scopeId,
      key: values.key,
      value: values.value,
      isSecret: values.isSecret,
      description: values.description?.trim() ? values.description : undefined,
    };
    startTransition(async () => {
      try {
        const fact = await api<PinnedFactDto>("/api/pinned-facts/", {
          method: "PUT",
          body: JSON.stringify(payload),
        });
        toast.success("Fact guardado");
        onUpserted(fact);
        form.reset({
          key: "",
          value: "",
          description: "",
          isSecret: values.isSecret,
        });
      } catch (e) {
        const msg =
          e instanceof ApiError
            ? (e.body as { message?: string } | undefined)?.message ??
              `Error ${e.status}`
            : e instanceof Error
              ? e.message
              : "Error desconocido";
        toast.error(msg);
      }
    });
  }

  return (
    <Card>
      <CardContent className="p-4">
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit(onSubmit)}
            className="grid grid-cols-1 gap-2 md:grid-cols-[1fr_1fr_auto]"
          >
            <FormField
              control={form.control}
              name="key"
              render={({ field }) => (
                <FormItem>
                  <FormLabel className="sr-only">Key</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder="key (ej. admin_password)"
                      maxLength={128}
                      className="font-mono text-sm"
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="value"
              render={({ field }) => (
                <FormItem>
                  <FormLabel className="sr-only">Valor</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      type={isSecret ? "password" : "text"}
                      placeholder="valor"
                      className="font-mono text-sm"
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <Button type="submit" disabled={isPending}>
              {isPending ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              Guardar
            </Button>

            <FormField
              control={form.control}
              name="description"
              render={({ field }) => (
                <FormItem className="md:col-span-2">
                  <FormLabel className="sr-only">Descripción</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder="descripción (opcional)"
                      maxLength={500}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="isSecret"
              render={({ field }) => (
                <FormItem className="flex items-center gap-2 space-y-0">
                  <FormControl>
                    <Checkbox
                      id="pf-is-secret"
                      checked={field.value}
                      onCheckedChange={(v) => field.onChange(v === true)}
                    />
                  </FormControl>
                  <Label
                    htmlFor="pf-is-secret"
                    className="cursor-pointer text-xs text-muted-foreground"
                  >
                    Es secreto
                  </Label>
                </FormItem>
              )}
            />
          </form>
        </Form>
      </CardContent>
    </Card>
  );
}

function PinnedFactRow({
  fact,
  revealed,
  onDeleted,
}: {
  fact: PinnedFactDto;
  revealed: boolean;
  onDeleted: (id: string) => void;
}) {
  const [isPending, startTransition] = useTransition();
  const [copied, setCopied] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);

  function confirmRemove() {
    startTransition(async () => {
      try {
        await api<void>(`/api/pinned-facts/${fact.id}`, { method: "DELETE" });
        toast.success(`Fact "${fact.key}" eliminado`);
        onDeleted(fact.id);
      } catch (e) {
        toast.error(e instanceof Error ? e.message : "Error desconocido");
      } finally {
        setDeleteOpen(false);
      }
    });
  }

  async function copyValue() {
    try {
      await navigator.clipboard.writeText(fact.value);
      setCopied(true);
      toast.success("Valor copiado");
      setTimeout(() => setCopied(false), 1500);
    } catch {
      toast.error("No se pudo copiar; copialo a mano.");
    }
  }

  const masked = fact.isSecret && !revealed;

  return (
    <li className="list-none">
      <Card>
        <CardContent className="flex flex-col gap-1 p-3">
          <div className="flex items-center justify-between gap-2">
            <span className="font-mono text-xs text-foreground">
              {fact.key}
            </span>
            <div className="flex items-center gap-2 text-[10px]">
              {fact.isSecret && (
                <Badge
                  variant="outline"
                  className="border-warning/40 bg-warning/10 text-warning"
                >
                  secret
                </Badge>
              )}
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={copyValue}
                disabled={masked}
                title={masked ? "Revelar primero" : "Copiar"}
                className="h-7 px-2 text-xs"
              >
                {copied ? (
                  <>
                    <Check className="mr-1 h-3 w-3" />
                    Copiado
                  </>
                ) : (
                  <>
                    <Copy className="mr-1 h-3 w-3" />
                    Copiar
                  </>
                )}
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() => setDeleteOpen(true)}
                disabled={isPending}
                className={cn(
                  "h-7 px-2 text-xs text-destructive",
                  "hover:bg-destructive/10 hover:text-destructive",
                )}
              >
                <Trash2 className="mr-1 h-3 w-3" />
                Eliminar
              </Button>
            </div>
          </div>
          <div className="break-all font-mono text-xs text-muted-foreground">
            {fact.value}
          </div>
          {fact.description && (
            <div className="text-[10px] text-muted-foreground">
              {fact.description}
            </div>
          )}
        </CardContent>
      </Card>

      <Dialog open={deleteOpen} onOpenChange={setDeleteOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Eliminar fact "{fact.key}"</DialogTitle>
            <DialogDescription>
              Esta acción no se puede deshacer. Si algún módulo lo está
              referenciando, dejará de resolverse.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setDeleteOpen(false)}>
              Cancelar
            </Button>
            <Button
              variant="destructive"
              onClick={confirmRemove}
              disabled={isPending}
            >
              {isPending ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              Eliminar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </li>
  );
}
