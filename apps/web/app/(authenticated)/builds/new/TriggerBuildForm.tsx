"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useTranslations } from "next-intl";
import { z } from "zod";
import { Loader2, Rocket } from "lucide-react";
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
import type { BuildDetail } from "@/lib/types";

export interface TemplateOption {
  id: string;
  name: string;
  slug: string;
  projectName: string;
  branch: string;
}

const SHA_RE = /^[0-9a-f]{7,64}$/i;

type FormValues = {
  templateId: string;
  gitSha: string;
  gitRef?: string;
};

export function TriggerBuildForm({
  templates,
}: {
  templates: TemplateOption[];
}) {
  const t = useTranslations("pages.builds_new");
  const tValidation = useTranslations("forms.validation");
  const router = useRouter();
  const [submitting, setSubmitting] = useState(false);

  const schema = z.object({
    templateId: z.string().min(1, tValidation("required")),
    gitSha: z.string().regex(SHA_RE, tValidation("required")),
    gitRef: z.string().optional(),
  });

  const defaultTemplate = templates[0];
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      templateId: defaultTemplate?.id ?? "",
      gitSha: "",
      gitRef: defaultTemplate ? `refs/heads/${defaultTemplate.branch}` : "",
    },
  });
  const selectedTemplateId = useWatch({
    control: form.control,
    name: "templateId",
  });

  const selectedTemplate = templates.find(
    (tpl) => tpl.id === selectedTemplateId,
  );

  async function onSubmit(values: FormValues) {
    setSubmitting(true);
    try {
      const ref =
        values.gitRef?.trim() ||
        `refs/heads/${selectedTemplate?.branch ?? "main"}`;
      const response = await api<BuildDetail>(
        `/api/builds/templates/${encodeURIComponent(values.templateId)}/trigger`,
        {
          method: "POST",
          body: JSON.stringify({ gitSha: values.gitSha, gitRef: ref }),
        },
      );
      toast.success(t("toast_triggered"));
      router.push(`/builds/${response.id}`);
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ?? `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : t("error_unknown");
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
            <div className="rounded-md border bg-muted/30 p-4 text-sm">
              <p className="font-medium">{t("local_info_title")}</p>
              <p className="mt-1 text-xs text-muted-foreground">
                {t("local_info_description")}
              </p>
            </div>

            <FormField
              control={form.control}
              name="templateId"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("label_template")}</FormLabel>
                  <Select
                    value={field.value}
                    onValueChange={(v) => {
                      field.onChange(v);
                      const tpl = templates.find((x) => x.id === v);
                      if (tpl) {
                        form.setValue("gitRef", `refs/heads/${tpl.branch}`);
                      }
                    }}
                  >
                    <FormControl>
                      <SelectTrigger>
                        <SelectValue placeholder={t("placeholder_template")} />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      {templates.map((tpl) => (
                        <SelectItem key={tpl.id} value={tpl.id}>
                          {tpl.projectName} · {tpl.name}{" "}
                          <span className="ml-1 font-mono text-[10px] text-muted-foreground">
                            {tpl.slug}
                          </span>
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="gitSha"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("label_commit")}</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder="abc123def4567890..."
                      className="font-mono text-xs"
                    />
                  </FormControl>
                  <FormDescription>{t("help_commit")}</FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="gitRef"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("label_git_ref")}</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder={t("placeholder_git_ref")}
                      className="font-mono text-xs"
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push("/builds")}
              >
                {t("cancel")}
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : (
                  <Rocket className="mr-2 h-4 w-4" />
                )}
                {t("submit")}
              </Button>
            </div>
          </form>
        </Form>
      </CardContent>
    </Card>
  );
}
