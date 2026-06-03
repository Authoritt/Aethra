"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useTranslations } from "next-intl";
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
import { Textarea } from "@/components/ui/textarea";
import { ApiError, api } from "@/lib/api";
import type { ClientDetail, CreateClientRequest } from "@/lib/types";

const SLUG_RE = /^[a-z][a-z0-9-]{0,30}$/;

type FormValues = {
  slug: string;
  displayName: string;
  description?: string;
  contactEmail?: string;
  billingTag?: string;
};

export function NewClientForm({ projectId }: { projectId: string }) {
  const t = useTranslations("pages.clients_new");
  const tValidation = useTranslations("forms.validation");
  const router = useRouter();
  const [submitting, setSubmitting] = useState(false);

  const schema = z.object({
    slug: z.string().regex(SLUG_RE, tValidation("slug_invalid")),
    displayName: z.string().min(1, tValidation("required")),
    description: z.string().optional(),
    contactEmail: z
      .string()
      .email(tValidation("email_invalid"))
      .or(z.literal(""))
      .optional(),
    billingTag: z.string().optional(),
  });

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      slug: "",
      displayName: "",
      description: "",
      contactEmail: "",
      billingTag: "",
    },
  });

  async function onSubmit(values: FormValues) {
    setSubmitting(true);
    try {
      const body: CreateClientRequest = {
        slug: values.slug,
        displayName: values.displayName.trim(),
        description: values.description?.trim() || null,
        contactEmail: values.contactEmail?.trim() || null,
        billingTag: values.billingTag?.trim() || null,
      };
      const response = await api<ClientDetail>(
        `/api/projects/${encodeURIComponent(projectId)}/clients`,
        { method: "POST", body: JSON.stringify(body) },
      );
      toast.success(t("toast_created", { name: response.displayName }));
      router.push(`/clients/${response.id}`);
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
            <FormField
              control={form.control}
              name="slug"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("label_slug")}</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      onChange={(e) =>
                        field.onChange(e.target.value.toLowerCase())
                      }
                      placeholder="acme-corp"
                      className="font-mono text-xs"
                      maxLength={31}
                    />
                  </FormControl>
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
                    <Input {...field} placeholder="ACME Corp" />
                  </FormControl>
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
                    <Textarea {...field} rows={2} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <FormField
                control={form.control}
                name="contactEmail"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("label_contact_email")}</FormLabel>
                    <FormControl>
                      <Input
                        {...field}
                        type="email"
                        placeholder="ops@acme.example"
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="billingTag"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("label_billing_tag")}</FormLabel>
                    <FormControl>
                      <Input
                        {...field}
                        placeholder="cost-center-A"
                        className="font-mono text-xs"
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </div>

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push(`/projects/${projectId}`)}
              >
                {t("cancel")}
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
