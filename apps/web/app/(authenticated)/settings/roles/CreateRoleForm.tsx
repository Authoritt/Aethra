"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Loader2 } from "lucide-react";
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
import { ApiError, api } from "@/lib/api";
import type { CreatedRole, CreateRoleRequest } from "@/lib/types";
import { ScopesGrid } from "../api-keys/ScopesGrid";

const schema = z.object({
  slug: z
    .string()
    .min(1, "Requerido")
    .max(64, "Máximo 64 caracteres")
    .regex(/^[a-z0-9_-]+$/, "Solo a-z, 0-9, guion (-) y guion bajo (_)."),
  displayName: z.string().min(1, "Requerido").max(100, "Máximo 100 caracteres"),
});

type FormValues = z.infer<typeof schema>;

export function CreateRoleForm() {
  const router = useRouter();
  const [scopes, setScopes] = useState<string[]>([]);
  const [scopesTouched, setScopesTouched] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { slug: "", displayName: "" },
  });

  async function onSubmit(values: FormValues) {
    if (scopes.length === 0) {
      setScopesTouched(true);
      toast.error("Seleccioná al menos un scope.");
      return;
    }
    setSubmitting(true);
    try {
      const body: CreateRoleRequest = {
        slug: values.slug.trim().toLowerCase(),
        displayName: values.displayName.trim(),
        scopes,
      };
      const created = await api<CreatedRole>("/api/identity/roles", {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success(`Rol ${created.displayName} creado`);
      router.push("/settings/roles");
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
      setSubmitting(false);
    }
  }

  return (
    <Card>
      <CardContent className="p-6">
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit(onSubmit)}
            className="flex flex-col gap-6"
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
                      placeholder="ops-readonly"
                      className="font-mono"
                      autoFocus
                    />
                  </FormControl>
                  <FormDescription>
                    Identificador único en URLs y APIs. Inmutable después de crear.
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
                  <FormLabel>Nombre visible *</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder="Operaciones (read-only)"
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <div className="flex flex-col gap-2">
              <div className="flex items-center justify-between">
                <span className="text-sm font-medium">Scopes *</span>
                {scopes.length === 0 && scopesTouched && (
                  <span className="text-xs text-destructive">
                    Seleccioná al menos uno
                  </span>
                )}
              </div>
              <ScopesGrid
                selected={scopes}
                onChange={(next) => {
                  setScopes(next);
                  setScopesTouched(true);
                }}
                disabled={submitting}
              />
            </div>

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push("/settings/roles")}
              >
                Cancelar
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                Crear rol
              </Button>
            </div>
          </form>
        </Form>
      </CardContent>
    </Card>
  );
}
