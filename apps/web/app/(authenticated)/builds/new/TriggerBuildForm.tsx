"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
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

const schema = z.object({
  templateId: z.string().min(1, "Requerido"),
  gitSha: z.string().regex(SHA_RE, "SHA inválido (7-64 hex chars)"),
  gitRef: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

export function TriggerBuildForm({
  templates,
}: {
  templates: TemplateOption[];
}) {
  const router = useRouter();
  const [submitting, setSubmitting] = useState(false);

  const defaultTemplate = templates[0];
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      templateId: defaultTemplate?.id ?? "",
      gitSha: "",
      gitRef: defaultTemplate ? `refs/heads/${defaultTemplate.branch}` : "",
    },
  });

  const selectedTemplate = templates.find(
    (t) => t.id === form.watch("templateId"),
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
      toast.success("Build disparado");
      router.push(`/builds/${response.id}`);
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
              name="templateId"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Template *</FormLabel>
                  <Select
                    value={field.value}
                    onValueChange={(v) => {
                      field.onChange(v);
                      const t = templates.find((x) => x.id === v);
                      if (t) {
                        form.setValue("gitRef", `refs/heads/${t.branch}`);
                      }
                    }}
                  >
                    <FormControl>
                      <SelectTrigger>
                        <SelectValue placeholder="Seleccioná un template" />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      {templates.map((t) => (
                        <SelectItem key={t.id} value={t.id}>
                          {t.projectName} · {t.name}{" "}
                          <span className="ml-1 font-mono text-[10px] text-muted-foreground">
                            {t.slug}
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
                  <FormLabel>Git SHA *</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder="abc123def4567890..."
                      className="font-mono text-xs"
                    />
                  </FormControl>
                  <FormDescription>
                    Commit exacto a buildear. La UI no lo resuelve automáticamente
                    contra el remote — copialo desde GitHub/Gitlab.
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="gitRef"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Git ref</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder="refs/heads/main"
                      className="font-mono text-xs"
                    />
                  </FormControl>
                  <FormDescription>
                    Por defecto se usa el branch declarado en el template.
                  </FormDescription>
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
                Cancelar
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : (
                  <Rocket className="mr-2 h-4 w-4" />
                )}
                Disparar build
              </Button>
            </div>
          </form>
        </Form>
      </CardContent>
    </Card>
  );
}
