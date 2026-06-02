"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { AlertTriangle, Loader2 } from "lucide-react";
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
import { ApiError, api } from "@/lib/api";
import type { BaseDomainDto, CreateBaseDomainRequest } from "@/lib/types";

interface CloudflareZoneOption {
  id: string;
  name: string;
}

// FQDN simple: dos o mas labels lowercase alfanumericos con guiones.
const FQDN_RE =
  /^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$/;

const NO_ZONE_VALUE = "__none__";

const schema = z.object({
  hostname: z
    .string()
    .min(1, "Requerido")
    .max(253, "Máximo 253 caracteres.")
    .regex(
      FQDN_RE,
      "Debe ser un FQDN válido (lowercase, mínimo dos labels, sin guiones al inicio/fin).",
    ),
  cloudflareZoneId: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

export function CreateBaseDomainForm({
  zones,
}: {
  zones: CloudflareZoneOption[];
}) {
  const router = useRouter();
  const [submitting, setSubmitting] = useState(false);

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      hostname: "",
      cloudflareZoneId: NO_ZONE_VALUE,
    },
  });

  async function onSubmit(values: FormValues) {
    setSubmitting(true);
    try {
      const zoneId =
        values.cloudflareZoneId &&
        values.cloudflareZoneId !== NO_ZONE_VALUE &&
        values.cloudflareZoneId.trim().length > 0
          ? values.cloudflareZoneId.trim()
          : null;
      const body: CreateBaseDomainRequest = {
        hostname: values.hostname.trim().toLowerCase(),
        cloudflareZoneId: zoneId,
      };
      const created = await api<BaseDomainDto>("/api/settings/domains/", {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success("Base domain registrado");
      router.push(
        `/settings/domains?created=${encodeURIComponent(created.id)}`,
      );
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
      setSubmitting(false);
    }
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
              name="hostname"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Hostname *</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      onChange={(e) =>
                        field.onChange(e.target.value.toLowerCase())
                      }
                      maxLength={253}
                      placeholder="aethra.tu-empresa.com"
                      autoComplete="off"
                      spellCheck={false}
                      autoFocus
                    />
                  </FormControl>
                  <FormDescription>
                    FQDN bajo el cual Aethra creará subdominios. Ej:
                    aethra.tu-empresa.com.
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="cloudflareZoneId"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Zona Cloudflare (opcional)</FormLabel>
                  {zones.length === 0 ? (
                    <Card className="border-border">
                      <CardContent className="p-3 text-xs text-muted-foreground">
                        Aún no hay zonas registradas en el módulo
                        Cloudflare.{" "}
                        <Link
                          href="/cloudflare/new"
                          className="text-primary underline-offset-4 hover:underline"
                        >
                          Registrar una zona
                        </Link>
                        .
                      </CardContent>
                    </Card>
                  ) : (
                    <Select
                      value={field.value || NO_ZONE_VALUE}
                      onValueChange={field.onChange}
                    >
                      <FormControl>
                        <SelectTrigger>
                          <SelectValue placeholder="— sin enlazar —" />
                        </SelectTrigger>
                      </FormControl>
                      <SelectContent>
                        <SelectItem value={NO_ZONE_VALUE}>
                          — sin enlazar —
                        </SelectItem>
                        {zones.map((z) => (
                          <SelectItem key={z.id} value={z.id}>
                            {z.name} ({z.id.slice(0, 12)}...)
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  )}
                  <FormDescription>
                    Si la zona ya está registrada en el módulo Cloudflare,
                    enlazala para que la UI muestre el vínculo. Podés dejarla
                    en blanco y enlazarla después.
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <Card className="border-warning/30 bg-warning/5">
              <CardContent className="flex items-start gap-2 p-3 text-xs text-muted-foreground">
                <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-warning" />
                <span>
                  Crear un base domain no lo activa automáticamente. Después
                  de registrarlo, marcá el wildcard DNS como configurado y
                  luego activalo.
                </span>
              </CardContent>
            </Card>

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push("/settings/domains")}
              >
                Cancelar
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                Registrar base domain
              </Button>
            </div>
          </form>
        </Form>
      </CardContent>
    </Card>
  );
}
