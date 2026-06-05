"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
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
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { ApiError, api } from "@/lib/api";
import type { RoleDto } from "@/lib/types";
import { ScopesGrid } from "../../../api-keys/ScopesGrid";

export function EditRoleForm({ role }: { role: RoleDto }) {
  const t = useTranslations("pages.settings_roles.new");
  const tParent = useTranslations("pages.settings_roles");
  const router = useRouter();
  const [scopes, setScopes] = useState<string[]>(role.scopes);
  const [scopesTouched, setScopesTouched] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const schema = useMemo(
    () =>
      z.object({
        displayName: z
          .string()
          .min(1, t("validation_required"))
          .max(100, t("validation_display_max")),
      }),
    [t],
  );

  type FormValues = z.infer<typeof schema>;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { displayName: role.displayName },
  });

  async function onSubmit(values: FormValues) {
    if (scopes.length === 0) {
      setScopesTouched(true);
      toast.error(t("validation_scopes_required"));
      return;
    }
    setSubmitting(true);
    try {
      await api(`/api/identity/roles/${encodeURIComponent(role.id)}`, {
        method: "PATCH",
        body: JSON.stringify({
          displayName: values.displayName.trim(),
          scopes,
        }),
      });
      toast.success(tParent("rename_toast", { name: values.displayName.trim() }));
      router.push("/settings/roles");
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`
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
            <FormItem>
              <FormLabel>{t("label_slug")}</FormLabel>
              <FormControl>
                <Input value={role.slug} className="font-mono" disabled />
              </FormControl>
            </FormItem>

            <FormField
              control={form.control}
              name="displayName"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("label_display")}</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder={t("placeholder_display")}
                      autoFocus
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <div className="flex flex-col gap-2">
              <div className="flex items-center justify-between">
                <span className="text-sm font-medium">{t("scopes_label")}</span>
                {scopes.length === 0 && scopesTouched && (
                  <span className="text-xs text-destructive">
                    {t("scopes_required_inline")}
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
