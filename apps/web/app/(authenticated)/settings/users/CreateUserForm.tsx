"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Loader2, RefreshCw } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
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
import { cn } from "@/lib/utils";
import { ApiError, api } from "@/lib/api";
import type {
  CreatedUser,
  CreateUserRequest,
  RoleDto,
} from "@/lib/types";

const schema = z.object({
  email: z.string().email("Email inválido").max(256),
  password: z
    .string()
    .min(8, "Mínimo 8 caracteres")
    .max(256, "Máximo 256 caracteres"),
  displayName: z.string().max(100).optional().or(z.literal("")),
});

type FormValues = z.infer<typeof schema>;

function generatePassword(length = 16): string {
  const charset =
    "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$%^*";
  let result = "";
  const arr = new Uint32Array(length);
  crypto.getRandomValues(arr);
  for (let i = 0; i < length; i++) {
    result += charset[arr[i] % charset.length];
  }
  return result;
}

export function CreateUserForm({ roles }: { roles: RoleDto[] }) {
  const router = useRouter();
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);
  const [submitting, setSubmitting] = useState(false);

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      email: "",
      password: "",
      displayName: "",
    },
  });

  function toggleRole(slug: string) {
    setSelectedRoles((prev) =>
      prev.includes(slug) ? prev.filter((s) => s !== slug) : [...prev, slug],
    );
  }

  async function onSubmit(values: FormValues) {
    if (selectedRoles.length === 0) {
      toast.error("Seleccioná al menos un rol.");
      return;
    }
    setSubmitting(true);
    try {
      const body: CreateUserRequest = {
        email: values.email.trim(),
        password: values.password,
        displayName: values.displayName?.trim() || null,
        roleSlugs: selectedRoles,
      };
      const created = await api<CreatedUser>("/api/identity/users", {
        method: "POST",
        body: JSON.stringify(body),
      });
      toast.success(`Usuario ${created.email} creado`);
      router.push("/settings/users");
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ?? `Error ${e.status}`
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
              name="email"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Email *</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      type="email"
                      placeholder="dev@aethra.local"
                      autoFocus
                      autoComplete="off"
                    />
                  </FormControl>
                  <FormDescription>
                    Único en el workspace. Se usa para login y notificaciones.
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
                  <FormLabel>Nombre completo</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder="Juana Pérez"
                      autoComplete="off"
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="password"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Contraseña inicial *</FormLabel>
                  <div className="flex gap-2">
                    <FormControl>
                      <Input
                        {...field}
                        type="text"
                        placeholder="mínimo 8 caracteres"
                        autoComplete="new-password"
                        className="font-mono text-sm"
                      />
                    </FormControl>
                    <Button
                      type="button"
                      variant="outline"
                      onClick={() => field.onChange(generatePassword())}
                    >
                      <RefreshCw className="mr-2 h-4 w-4" />
                      Generar
                    </Button>
                  </div>
                  <FormDescription>
                    Comunicala al usuario por canal seguro; podrá cambiarla
                    desde su cuenta.
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <div className="flex flex-col gap-2">
              <label className="text-sm font-medium leading-none">
                Roles *
              </label>
              <p className="text-xs text-muted-foreground">
                Los permisos del usuario son la unión de los scopes de los
                roles asignados.
              </p>
              <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
                {roles.map((role) => {
                  const checked = selectedRoles.includes(role.slug);
                  return (
                    <label
                      key={role.id}
                      className={cn(
                        "flex cursor-pointer items-start gap-3 rounded-md border p-3 transition-colors",
                        checked
                          ? "border-primary/40 bg-primary/5"
                          : "border-border bg-background hover:border-border/80 hover:bg-secondary/40",
                      )}
                    >
                      <Checkbox
                        checked={checked}
                        onCheckedChange={() => toggleRole(role.slug)}
                        className="mt-0.5"
                      />
                      <div className="flex min-w-0 flex-col gap-1">
                        <div className="flex items-center gap-2">
                          <span className="text-sm font-medium text-foreground">
                            {role.displayName}
                          </span>
                          {role.isSystem ? (
                            <Badge
                              variant="outline"
                              className="text-[10px]"
                            >
                              builtin
                            </Badge>
                          ) : null}
                          {role.slug === "admin" ? (
                            <Badge variant="warning" className="text-[10px]">
                              admin
                            </Badge>
                          ) : null}
                        </div>
                        <span className="font-mono text-[10px] text-muted-foreground">
                          {role.slug}
                        </span>
                        <span className="text-[10px] text-muted-foreground">
                          {role.scopes.length} scopes
                        </span>
                      </div>
                    </label>
                  );
                })}
              </div>
            </div>

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push("/settings/users")}
              >
                Cancelar
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                Crear usuario
              </Button>
            </div>
          </form>
        </Form>
      </CardContent>
    </Card>
  );
}
