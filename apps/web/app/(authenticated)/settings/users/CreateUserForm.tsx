"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
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
  const t = useTranslations("pages.settings_users.new");
  const tParent = useTranslations("pages.settings_users");
  const router = useRouter();
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);
  const [submitting, setSubmitting] = useState(false);

  const schema = useMemo(
    () =>
      z.object({
        email: z.string().email(t("validation_email")).max(256),
        password: z
          .string()
          .min(8, t("validation_password_min"))
          .max(256, t("validation_password_max")),
        displayName: z.string().max(100).optional().or(z.literal("")),
      }),
    [t],
  );

  type FormValues = z.infer<typeof schema>;

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
      toast.error(t("validation_roles_required"));
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
      toast.success(t("toast_created", { email: created.email }));
      router.push("/settings/users");
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ?? `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : tParent("error_unknown");
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
                  <FormLabel>{tParent("label_email")}</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      type="email"
                      placeholder={t("placeholder_email")}
                      autoFocus
                      autoComplete="off"
                    />
                  </FormControl>
                  <FormDescription>
                    {t("email_hint")}
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
                  <FormLabel>{t("label_full_name")}</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder={t("placeholder_full_name")}
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
                  <FormLabel>{tParent("label_password")}</FormLabel>
                  <div className="flex gap-2">
                    <FormControl>
                      <Input
                        {...field}
                        type="text"
                        placeholder={t("password_placeholder")}
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
                      {t("generate")}
                    </Button>
                  </div>
                  <FormDescription>
                    {t("password_hint")}
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <div className="flex flex-col gap-2">
              <label className="text-sm font-medium leading-none">
                {t("roles_label")}
              </label>
              <p className="text-xs text-muted-foreground">
                {t("roles_hint")}
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
                              {t("badge_builtin")}
                            </Badge>
                          ) : null}
                          {role.slug === "admin" ? (
                            <Badge variant="warning" className="text-[10px]">
                              {t("badge_admin")}
                            </Badge>
                          ) : null}
                        </div>
                        <span className="font-mono text-[10px] text-muted-foreground">
                          {role.slug}
                        </span>
                        <span className="text-[10px] text-muted-foreground">
                          {t("scopes_count", { count: role.scopes.length })}
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
                {tParent("cancel")}
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                {tParent("submit")}
              </Button>
            </div>
          </form>
        </Form>
      </CardContent>
    </Card>
  );
}
