"use client";

import { useRouter } from "next/navigation";
import { useState, useMemo } from "react";
import { useTranslations } from "next-intl";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Loader2, LogIn } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Logo } from "@/components/brand/logo";
import { ApiError, api } from "@/lib/api";

type FormValues = {
  email: string;
  password: string;
};

export default function LoginPage() {
  const router = useRouter();
  const t = useTranslations("auth");
  const tCommon = useTranslations("common");
  const [submitting, setSubmitting] = useState(false);

  // Schema con mensajes traducidos. Memoizamos para no recrearlo en cada render
  // y mantener referencia estable para zodResolver.
  const schema = useMemo(
    () =>
      z.object({
        email: z.string().email(t("email_invalid")),
        password: z.string().min(1, t("password_required")),
      }),
    [t],
  );

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { email: "", password: "" },
  });

  async function onSubmit(values: FormValues) {
    setSubmitting(true);
    try {
      await api<{ email: string }>("/auth/login", {
        method: "POST",
        body: JSON.stringify(values),
      });
      toast.success(t("session_started"));
      router.push("/dashboard");
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        toast.error(t("invalid_credentials"));
        form.setError("password", { message: t("invalid_credentials") });
      } else {
        const msg = e instanceof Error ? e.message : t("unknown_error");
        toast.error(msg);
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-background px-6 py-12">
      <Card className="w-full max-w-sm">
        <CardHeader className="items-center space-y-3 text-center">
          <Logo variant="lockup" className="mb-1" />
          <CardTitle>{t("login_title")}</CardTitle>
          <CardDescription>{t("login_description")}</CardDescription>
        </CardHeader>
        <CardContent>
          <Form {...form}>
            <form
              onSubmit={form.handleSubmit(onSubmit)}
              className="flex flex-col gap-4"
            >
              <FormField
                control={form.control}
                name="email"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("email_label")}</FormLabel>
                    <FormControl>
                      <Input
                        type="email"
                        autoComplete="email"
                        placeholder={t("email_placeholder")}
                        {...field}
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
                    <FormLabel>{t("password_label")}</FormLabel>
                    <FormControl>
                      <Input
                        type="password"
                        autoComplete="current-password"
                        placeholder={t("password_placeholder")}
                        {...field}
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <Button type="submit" disabled={submitting} className="mt-2 w-full">
                {submitting ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : (
                  <LogIn className="mr-2 h-4 w-4" />
                )}
                {submitting ? t("submitting") : t("submit")}
              </Button>
            </form>
          </Form>
        </CardContent>
        <CardFooter className="justify-center text-xs text-muted-foreground">
          {tCommon("version_label")}
        </CardFooter>
      </Card>
    </main>
  );
}
