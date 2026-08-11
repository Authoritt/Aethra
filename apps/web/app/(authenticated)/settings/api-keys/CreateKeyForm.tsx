"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { useForm, useWatch } from "react-hook-form";
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
import { cn } from "@/lib/utils";
import { ApiError, api } from "@/lib/api";
import { handOffApiKeySecret } from "@/lib/api-key-secret-handoff";
import type { CreateApiKeyRequest, CreateApiKeyResult } from "@/lib/types";
import { ScopesGrid } from "./ScopesGrid";

const NAME_MAX = 80;

type ExpiresPreset = "never" | "30d" | "90d" | "365d" | "custom";

function presetToIso(preset: ExpiresPreset, custom: string): string | null {
  if (preset === "never") return null;
  if (preset === "custom") {
    if (!custom) return null;
    const d = new Date(custom);
    if (Number.isNaN(d.getTime())) return null;
    return d.toISOString();
  }
  const days = preset === "30d" ? 30 : preset === "90d" ? 90 : 365;
  const d = new Date();
  d.setDate(d.getDate() + days);
  return d.toISOString();
}

export function CreateKeyForm() {
  const t = useTranslations("pages.settings_api_keys.create");
  const tParent = useTranslations("pages.settings_api_keys");
  const router = useRouter();
  const [scopes, setScopes] = useState<string[]>([]);
  const [scopesTouched, setScopesTouched] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [createdAtMs] = useState(() => Date.now());

  const today = useMemo(() => new Date().toISOString().slice(0, 10), []);

  const PRESET_LABELS: Record<ExpiresPreset, string> = {
    never: t("preset_never"),
    "30d": t("preset_30d"),
    "90d": t("preset_90d"),
    "365d": t("preset_365d"),
    custom: t("preset_custom"),
  };

  const schema = useMemo(
    () =>
      z
        .object({
          name: z
            .string()
            .min(1, t("validation_required"))
            .max(NAME_MAX, t("validation_max", { max: NAME_MAX })),
          preset: z.enum(["never", "30d", "90d", "365d", "custom"]),
          customDate: z.string().optional().or(z.literal("")),
        })
        .refine(
          (values) => {
            if (values.preset !== "custom") return true;
            if (!values.customDate) return false;
            const d = new Date(values.customDate);
            if (Number.isNaN(d.getTime())) return false;
            return d.getTime() > createdAtMs;
          },
          {
            message: t("validation_future"),
            path: ["customDate"],
          },
        ),
    [createdAtMs, t],
  );

  type FormValues = z.infer<typeof schema>;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: "",
      preset: "never",
      customDate: "",
    },
  });

  const preset = useWatch({
    control: form.control,
    name: "preset",
  });

  async function onSubmit(values: FormValues) {
    if (scopes.length === 0) {
      setScopesTouched(true);
      toast.error(t("scopes_required"));
      return;
    }
    setSubmitting(true);
    try {
      const body: CreateApiKeyRequest = {
        name: values.name.trim(),
        scopes,
        expiresAt: presetToIso(values.preset, values.customDate ?? ""),
      };
      const created = await api<CreateApiKeyResult>(
        "/api/identity/api-keys",
        {
          method: "POST",
          body: JSON.stringify(body),
        },
      );
      handOffApiKeySecret(created.id, created.secret);
      toast.success(t("toast_created"));
      router.push(`/settings/api-keys/created?id=${encodeURIComponent(created.id)}`);
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { detail?: string } | undefined)?.detail ??
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
              name="name"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{tParent("label_name")}</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder={t("placeholder_name")}
                      maxLength={NAME_MAX}
                      autoFocus
                    />
                  </FormControl>
                  <FormDescription>
                    {t("name_hint")}
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <div className="flex flex-col gap-2">
              <div className="flex items-center justify-between">
                <Label asLabel>{t("scopes_label")}</Label>
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

            <FormField
              control={form.control}
              name="preset"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("expiration_label")}</FormLabel>
                  <FormControl>
                    <div className="flex flex-wrap gap-2">
                      {(Object.keys(PRESET_LABELS) as ExpiresPreset[]).map((p) => {
                        const active = field.value === p;
                        return (
                          <button
                            key={p}
                            type="button"
                            onClick={() => field.onChange(p)}
                            className={cn(
                              "rounded-full border px-3 py-1 text-xs transition",
                              active
                                ? "border-primary/60 bg-primary/15 text-primary"
                                : "border-border bg-background text-foreground hover:bg-secondary",
                            )}
                          >
                            {PRESET_LABELS[p]}
                          </button>
                        );
                      })}
                    </div>
                  </FormControl>
                  <FormDescription>
                    {t("expiration_hint")}
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            {preset === "custom" && (
              <FormField
                control={form.control}
                name="customDate"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("custom_date_label")}</FormLabel>
                    <FormControl>
                      <Input
                        {...field}
                        type="date"
                        min={today}
                        className="max-w-xs"
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            )}

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push("/settings/api-keys")}
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

function Label({
  children,
  asLabel,
}: {
  children: React.ReactNode;
  asLabel?: boolean;
}) {
  return (
    <span
      className={cn(
        "text-sm font-medium leading-none",
        asLabel && "text-foreground",
      )}
    >
      {children}
    </span>
  );
}
