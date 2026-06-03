"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
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

interface MetadataRow {
  key: string;
  value: string;
}

export function CreateIntegrationForm() {
  const t = useTranslations("pages.settings_integrations.new");
  const tParent = useTranslations("pages.settings_integrations");
  const router = useRouter();
  const [metadata, setMetadata] = useState<MetadataRow[]>([]);
  const [revealValue, setRevealValue] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [createdValuePreview, setCreatedValuePreview] = useState<
    { dto: IntegrationCredentialDto; plainValue: string } | null
  >(null);

  const TYPE_OPTIONS: { value: IntegrationCredentialType; label: string }[] = [
    { value: "Cloudflare", label: t("type_cloudflare") },
    { value: "GitHubPat", label: t("type_github_pat") },
    { value: "Smtp", label: t("type_smtp") },
    { value: "Registry", label: t("type_registry") },
    { value: "GenericApiKey", label: t("type_generic") },
  ];

  const schema = useMemo(
    () =>
      z.object({
        name: z
          .string()
          .min(1, t("validation_required"))
          .max(NAME_MAX, t("validation_max", { max: NAME_MAX }))
          .regex(NAME_RE, t("validation_format")),
        type: z.enum([
          "Cloudflare",
          "GitHubPat",
          "Smtp",
          "Registry",
          "GenericApiKey",
        ]),
        displayName: z
          .string()
          .min(1, t("validation_required"))
          .max(DISPLAY_MAX, t("validation_max", { max: DISPLAY_MAX })),
        description: z.string().max(500).optional().or(z.literal("")),
        plainValue: z.string().min(1, t("validation_required")),
      }),
    [t],
  );

  type FormValues = z.infer<typeof schema>;

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
      toast.success(t("toast_created"));
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
            : tParent("error_unknown");
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
                  <FormLabel>{tParent("label_name")}</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      onChange={(e) =>
                        field.onChange(e.target.value.toLowerCase())
                      }
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

            <FormField
              control={form.control}
              name="type"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("label_type")}</FormLabel>
                  <Select
                    value={field.value}
                    onValueChange={(v) =>
                      field.onChange(v as IntegrationCredentialType)
                    }
                  >
                    <FormControl>
                      <SelectTrigger>
                        <SelectValue placeholder={t("type_placeholder")} />
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
                    {t("type_hint")}
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
                  <FormLabel>{t("label_display_name")}</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder={t("placeholder_display_name")}
                      maxLength={DISPLAY_MAX}
                    />
                  </FormControl>
                  <FormDescription>
                    {t("display_name_hint")}
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
                  <FormLabel>{t("label_description")}</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder={t("placeholder_description")}
                      maxLength={500}
                    />
                  </FormControl>
                  <FormDescription>
                    {t("description_hint")}
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
                  <FormLabel>{t("label_value")}</FormLabel>
                  <FormControl>
                    <div className="flex items-stretch gap-2">
                      <Textarea
                        {...field}
                        placeholder={
                          revealValue
                            ? t("placeholder_value")
                            : t("placeholder_value_hidden")
                        }
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
                  <Card className="mt-1 border-warning/30 bg-warning/5">
                    <CardContent className="flex items-start gap-2 p-2 text-xs text-muted-foreground">
                      <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-warning" />
                      <span>
                        {t("value_warning")}
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
                    {t("metadata_title")}
                  </span>
                  <p className="text-xs text-muted-foreground">
                    {t("metadata_hint")}
                  </p>
                </div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={addMetadataRow}
                >
                  <Plus className="mr-2 h-3.5 w-3.5" />
                  {t("metadata_add_row")}
                </Button>
              </div>
              {metadata.length === 0 && (
                <p className="text-xs text-muted-foreground">
                  {t("metadata_empty")}
                </p>
              )}
              {metadata.map((row, i) => (
                <div key={i} className="flex items-center gap-2">
                  <Input
                    value={row.key}
                    onChange={(e) =>
                      updateMetadataRow(i, { key: e.target.value })
                    }
                    placeholder={t("metadata_key_placeholder")}
                    maxLength={64}
                    className="w-1/3 font-mono text-xs"
                  />
                  <Input
                    value={row.value}
                    onChange={(e) =>
                      updateMetadataRow(i, { value: e.target.value })
                    }
                    placeholder={t("metadata_value_placeholder")}
                    className="flex-1 font-mono text-xs"
                  />
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    onClick={() => removeMetadataRow(i)}
                    aria-label={t("metadata_remove_aria")}
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
                {tParent("cancel")}
              </Button>
              <Button type="submit" disabled={submitting}>
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

function CreatedConfirmation({
  dto,
  plainValue,
  onContinue,
}: {
  dto: IntegrationCredentialDto;
  plainValue: string;
  onContinue: () => void;
}) {
  const t = useTranslations("pages.settings_integrations.new");
  const [copied, setCopied] = useState(false);

  async function onCopy() {
    try {
      await navigator.clipboard.writeText(plainValue);
      setCopied(true);
      toast.success(t("copy_value_success"));
      setTimeout(() => setCopied(false), 1500);
    } catch {
      toast.error(t("copy_value_fail"));
    }
  }

  return (
    <Card className="border-success/40 bg-success/5">
      <CardContent className="flex flex-col gap-5 p-6">
        <div>
          <h2 className="text-xl font-semibold text-foreground">
            {t("confirmation_title")}
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("confirmation_description")}
          </p>
        </div>

        <dl className="grid grid-cols-1 gap-3 text-sm">
          <Row label={t("row_name")} value={dto.name} mono />
          <Row label={t("row_type")} value={dto.type} />
          <Row label={t("row_display")} value={dto.displayName} />
        </dl>

        <Card>
          <CardContent className="flex flex-col gap-2 p-3">
            <div className="text-xs uppercase tracking-wider text-muted-foreground">
              {t("plain_value_label")}
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
                  {t("copy_value_copied")}
                </>
              ) : (
                <>
                  <Copy className="mr-2 h-3.5 w-3.5" />
                  {t("copy_value")}
                </>
              )}
            </Button>
          </CardContent>
        </Card>

        <div className="flex justify-end">
          <Button type="button" onClick={onContinue}>
            {t("continue")}
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
