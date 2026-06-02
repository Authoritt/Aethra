"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { AlertTriangle, Check, Copy, Eye, EyeOff, Loader2, Plus, X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import {
  Form,
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { ApiError, api } from "@/lib/api";
import { cn } from "@/lib/utils";
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
  { value: "GenericApiKey", label: "API Key genérica" },
];

interface MetadataRow {
  key: string;
  value: string;
}

const schema = z.object({
  name: z
    .string()
    .min(1, "Requerido")
    .max(NAME_MAX, `Máximo ${NAME_MAX} caracteres.`)
    .regex(
      NAME_RE,
      "Debe seguir el formato 'namespace:slug' (lowercase, alfanumérico y guiones). Ej: cloudflare:default.",
    ),
  type: z.enum([
    "Cloudflare",
    "GitHubPat",
    "Smtp",
    "Registry",
    "GenericApiKey",
  ]),
  displayName: z
    .string()
    .min(1, "Requerido")
    .max(DISPLAY_MAX, `Máximo ${DISPLAY_MAX} caracteres.`),
  description: z.string().max(500).optional().or(z.literal("")),
  plainValue: z.string().min(1, "Requerido"),
});

type FormValues = z.infer<typeof schema>;

export function CreateIntegrationForm() {
  const router = useRouter();
  const [metadata, setMetadata] = useState<MetadataRow[]>([]);
  const [revealValue, setRevealValue] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [createdValuePreview, setCreatedValuePreview] = useState<
    { dto: IntegrationCredentialDto; plainValue: string } | null
  >(null);

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: "",
      type: "Cloudflare",
      displayName: "",
      description: "",
      plainValue: "",
    },
  });

  function addMetadataRow() {
    setMetadata((rows) => [...rows, { key: "", value: "" }]);
  }

  function updateMetadataRow(index: number, patch: Partial<MetadataRow>) {
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

  async function onSubmit(values: FormValues) {
    setSubmitting(true);
    try {
      const body: CreateIntegrationCredentialRequest = {
        name: values.name.trim(),
        type: values.type,
        displayName: values.displayName.trim(),
        plainValue: values.plainValue,
        metadata: metadataObject(),
        description: values.description?.trim() || null,
      };
      const created = await api<IntegrationCredentialDto>(
        "/api/settings/integrations/",
        {
          method: "POST",
          body: JSON.stringify(body),
        },
      );
      toast.success("Credencial creada");
      setCreatedValuePreview({ dto: created, plainValue: values.plainValue });
      form.setValue("plainValue", "");
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
    <Card>
      <CardContent className="p-6">
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit(onSubmit)}
            className="flex flex-col gap-5"
          >
            <FormField
              control={form.control}
              name="name"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Name *</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      onChange={(e) =>
                        field.onChange(e.target.value.toLowerCase())
                      }
                      placeholder="cloudflare:default"
                      maxLength={NAME_MAX}
                      autoFocus
                    />
                  </FormControl>
                  <FormDescription>
                    Identificador estable formato 'namespace:slug'. Ej:
                    cloudflare:default, registry:internal, github:bot.
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="type"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Tipo *</FormLabel>
                  <Select
                    value={field.value}
                    onValueChange={(v) =>
                      field.onChange(v as IntegrationCredentialType)
                    }
                  >
                    <FormControl>
                      <SelectTrigger>
                        <SelectValue placeholder="Seleccionar tipo" />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      {TYPE_OPTIONS.map((opt) => (
                        <SelectItem key={opt.value} value={opt.value}>
                          {opt.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <FormDescription>
                    Solo es metadata para la UI; el resolver siempre usa el nombre.
                  </FormDescription>
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
                    <Input
                      {...field}
                      placeholder="Cloudflare cuenta principal"
                      maxLength={DISPLAY_MAX}
                    />
                  </FormControl>
                  <FormDescription>
                    Nombre legible que verás en listados.
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="description"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Descripción (opcional)</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder="Token con scope Zone.DNS.Edit creado por mayra"
                      maxLength={500}
                    />
                  </FormControl>
                  <FormDescription>
                    Útil cuando hay varias credenciales del mismo tipo.
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="plainValue"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Valor *</FormLabel>
                  <FormControl>
                    <div className="flex items-stretch gap-2">
                      <Textarea
                        {...field}
                        placeholder={revealValue ? "tu-token-aqui" : "••••••••"}
                        className="min-h-[88px] flex-1 font-mono text-xs"
                        spellCheck={false}
                        autoComplete="off"
                        style={
                          revealValue
                            ? undefined
                            : ({
                                WebkitTextSecurity: "disc",
                                textSecurity: "disc",
                              } as React.CSSProperties)
                        }
                      />
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        className="self-start"
                        onClick={() => setRevealValue((v) => !v)}
                      >
                        {revealValue ? (
                          <>
                            <EyeOff className="mr-2 h-3.5 w-3.5" />
                            Ocultar
                          </>
                        ) : (
                          <>
                            <Eye className="mr-2 h-3.5 w-3.5" />
                            Mostrar
                          </>
                        )}
                      </Button>
                    </div>
                  </FormControl>
                  <Card className="mt-1 border-warning/30 bg-warning/5">
                    <CardContent className="flex items-start gap-2 p-2 text-xs text-muted-foreground">
                      <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-warning" />
                      <span>
                        Solo verás este valor una vez. Si lo olvidás tendrás
                        que rotar la credencial.
                      </span>
                    </CardContent>
                  </Card>
                  <FormMessage />
                </FormItem>
              )}
            />

            <div className="flex flex-col gap-2">
              <div className="flex items-center justify-between">
                <div>
                  <span className="text-sm font-medium text-foreground">
                    Metadata (opcional)
                  </span>
                  <p className="text-xs text-muted-foreground">
                    Pares clave-valor para datos no secretos (account_id,
                    region...).
                  </p>
                </div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={addMetadataRow}
                >
                  <Plus className="mr-2 h-3.5 w-3.5" />
                  Añadir fila
                </Button>
              </div>
              {metadata.length === 0 && (
                <p className="text-xs text-muted-foreground">
                  Sin entradas. Añadí filas solo si el proveedor necesita
                  parámetros extra.
                </p>
              )}
              {metadata.map((row, i) => (
                <div key={i} className="flex items-center gap-2">
                  <Input
                    value={row.key}
                    onChange={(e) =>
                      updateMetadataRow(i, { key: e.target.value })
                    }
                    placeholder="clave"
                    maxLength={64}
                    className="w-1/3 font-mono text-xs"
                  />
                  <Input
                    value={row.value}
                    onChange={(e) =>
                      updateMetadataRow(i, { value: e.target.value })
                    }
                    placeholder="valor"
                    className="flex-1 font-mono text-xs"
                  />
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    onClick={() => removeMetadataRow(i)}
                    aria-label="Eliminar fila"
                  >
                    <X className="h-4 w-4 text-destructive" />
                  </Button>
                </div>
              ))}
            </div>

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push("/settings/integrations")}
              >
                Cancelar
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                Crear credencial
              </Button>
            </div>
          </form>
        </Form>
      </CardContent>
    </Card>
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

  async function onCopy() {
    try {
      await navigator.clipboard.writeText(plainValue);
      setCopied(true);
      toast.success("Valor copiado al portapapeles");
      setTimeout(() => setCopied(false), 1500);
    } catch {
      toast.error("No se pudo copiar; copialo a mano.");
    }
  }

  return (
    <Card className="border-success/40 bg-success/5">
      <CardContent className="flex flex-col gap-5 p-6">
        <div>
          <h2 className="text-xl font-semibold text-foreground">
            Credencial creada
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Copiá el valor ahora. Es la única vez que podrás verlo: a partir
            de aquí solo persiste el blob cifrado.
          </p>
        </div>

        <dl className="grid grid-cols-1 gap-3 text-sm">
          <Row label="Name" value={dto.name} mono />
          <Row label="Tipo" value={dto.type} />
          <Row label="Display" value={dto.displayName} />
        </dl>

        <Card>
          <CardContent className="flex flex-col gap-2 p-3">
            <div className="text-xs uppercase tracking-wider text-muted-foreground">
              Valor en claro
            </div>
            <pre className="max-h-40 overflow-auto whitespace-pre-wrap break-all font-mono text-xs text-foreground">
              {plainValue}
            </pre>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="self-start"
              onClick={onCopy}
            >
              {copied ? (
                <>
                  <Check className="mr-2 h-3.5 w-3.5" />
                  Copiado
                </>
              ) : (
                <>
                  <Copy className="mr-2 h-3.5 w-3.5" />
                  Copiar al portapapeles
                </>
              )}
            </Button>
          </CardContent>
        </Card>

        <div className="flex justify-end">
          <Button type="button" onClick={onContinue}>
            Continuar
          </Button>
        </div>
      </CardContent>
    </Card>
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
      <dt className="text-xs uppercase tracking-wider text-muted-foreground">
        {label}
      </dt>
      <dd
        className={cn(
          "text-foreground",
          mono ? "font-mono text-xs" : "text-sm",
        )}
      >
        {value}
      </dd>
    </div>
  );
}
