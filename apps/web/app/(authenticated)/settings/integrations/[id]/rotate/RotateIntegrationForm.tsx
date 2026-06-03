"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { AlertTriangle, Eye, EyeOff, Loader2 } from "lucide-react";
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
import { Textarea } from "@/components/ui/textarea";
import { cn } from "@/lib/utils";
import { ApiError, api } from "@/lib/api";
import type {
  IntegrationCredentialDto,
  RotateIntegrationCredentialRequest,
} from "@/lib/types";

export function RotateIntegrationForm({
  credential,
}: {
  credential: IntegrationCredentialDto;
}) {
  const t = useTranslations("pages.settings_integrations.rotate");
  const tParent = useTranslations("pages.settings_integrations");
  const router = useRouter();
  const [reveal, setReveal] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const confirmExpected = credential.name;

  const schema = useMemo(
    () =>
      z.object({
        newPlainValue: z.string().min(1, t("validation_required")),
        confirm: z
          .string()
          .refine((v) => v.trim() === confirmExpected, {
            message: t("confirm_error", { name: confirmExpected }),
          }),
      }),
    [t, confirmExpected],
  );

  type FormValues = z.infer<typeof schema>;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { newPlainValue: "", confirm: "" },
  });

  async function onSubmit(values: FormValues) {
    setSubmitting(true);
    try {
      const body: RotateIntegrationCredentialRequest = {
        newPlainValue: values.newPlainValue,
      };
      await api(
        `/api/settings/integrations/${encodeURIComponent(credential.id)}/rotate`,
        {
          method: "POST",
          body: JSON.stringify(body),
        },
      );
      toast.success(t("toast_rotated"));
      router.push("/settings/integrations");
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
            : tParent("error_unknown");
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
            <dl className="grid grid-cols-2 gap-3 text-sm">
              <div>
                <dt className="text-xs uppercase tracking-wider text-muted-foreground">
                  {t("label_name")}
                </dt>
                <dd className="mt-0.5 font-mono text-xs text-foreground">
                  {credential.name}
                </dd>
              </div>
              <div>
                <dt className="text-xs uppercase tracking-wider text-muted-foreground">
                  {t("label_type")}
                </dt>
                <dd className="mt-0.5 text-foreground">{credential.type}</dd>
              </div>
              <div className="col-span-2">
                <dt className="text-xs uppercase tracking-wider text-muted-foreground">
                  {t("label_display")}
                </dt>
                <dd className="mt-0.5 text-foreground">
                  {credential.displayName}
                </dd>
              </div>
            </dl>

            <FormField
              control={form.control}
              name="newPlainValue"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("label_new_value")}</FormLabel>
                  <FormControl>
                    <div className="flex items-stretch gap-2">
                      <Textarea
                        {...field}
                        className={cn(
                          "min-h-[88px] flex-1 font-mono text-xs",
                        )}
                        spellCheck={false}
                        autoComplete="off"
                        autoFocus
                        style={
                          reveal
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
                        onClick={() => setReveal((v) => !v)}
                      >
                        {reveal ? (
                          <>
                            <EyeOff className="mr-2 h-3.5 w-3.5" />
                            {t("value_hide")}
                          </>
                        ) : (
                          <>
                            <Eye className="mr-2 h-3.5 w-3.5" />
                            {t("value_show")}
                          </>
                        )}
                      </Button>
                    </div>
                  </FormControl>
                  <FormDescription>
                    {t("new_value_hint")}
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="confirm"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>
                    {t("confirm_label")}{" "}
                    <span className="font-mono">{credential.name}</span>
                  </FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      className="font-mono text-xs"
                      spellCheck={false}
                      autoComplete="off"
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <Card className="border-warning/30 bg-warning/5">
              <CardContent className="flex items-start gap-2 p-3 text-xs text-muted-foreground">
                <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-warning" />
                <span>
                  {t("warning")}
                </span>
              </CardContent>
            </Card>

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push("/settings/integrations")}
              >
                {tParent("cancel")}
              </Button>
              <Button
                type="submit"
                variant="destructive"
                disabled={submitting}
              >
                {submitting ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                {t("submit")}
              </Button>
            </div>
          </form>
        </Form>
      </CardContent>
    </Card>
  );
}
