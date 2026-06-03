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

interface LoginResponse {
  email: string;
  requires_totp?: boolean;
  totp_token?: string;
}

export default function LoginPage() {
  const router = useRouter();
  const t = useTranslations("auth");
  const tCommon = useTranslations("common");
  const [submitting, setSubmitting] = useState(false);
  const [totpChallenge, setTotpChallenge] = useState<string | null>(null);
  const [totpCode, setTotpCode] = useState("");

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
      const data = await api<LoginResponse>("/auth/login", {
        method: "POST",
        body: JSON.stringify(values),
      });
      if (data.requires_totp && data.totp_token) {
        // F12.1B: el backend nos pide el segundo factor.
        setTotpChallenge(data.totp_token);
        return;
      }
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

  async function submitTotp() {
    if (!totpChallenge || !totpCode.trim()) {
      return;
    }
    setSubmitting(true);
    try {
      await api<LoginResponse>("/auth/login/totp", {
        method: "POST",
        body: JSON.stringify({
          totp_token: totpChallenge,
          totpToken: totpChallenge,
          code: totpCode.trim(),
        }),
      });
      toast.success(t("session_started"));
      router.push("/dashboard");
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        const body = e.body as { error?: string } | undefined;
        const code = body?.error ?? "invalid";
        if (code === "totp_token_invalid_or_expired") {
          toast.error("Session expired, restart login.");
          setTotpChallenge(null);
          setTotpCode("");
        } else {
          toast.error("Invalid 2FA code");
        }
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
          <CardTitle>
            {totpChallenge ? "Two-factor authentication" : t("login_title")}
          </CardTitle>
          <CardDescription>
            {totpChallenge
              ? "Enter the 6-digit code from your authenticator app, or use an 8-character recovery code."
              : t("login_description")}
          </CardDescription>
        </CardHeader>
        <CardContent>
          {totpChallenge ? (
            <div className="flex flex-col gap-4">
              <Input
                value={totpCode}
                onChange={(e) => setTotpCode(e.target.value)}
                placeholder="123456"
                className="font-mono"
                maxLength={8}
                autoFocus
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault();
                    submitTotp();
                  }
                }}
              />
              <Button onClick={submitTotp} disabled={submitting || !totpCode}>
                {submitting ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : (
                  <LogIn className="mr-2 h-4 w-4" />
                )}
                Verify and continue
              </Button>
              <Button
                type="button"
                variant="outline"
                onClick={() => {
                  setTotpChallenge(null);
                  setTotpCode("");
                }}
                disabled={submitting}
              >
                Cancel
              </Button>
            </div>
          ) : (
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
          )}
        </CardContent>
        <CardFooter className="justify-center text-xs text-muted-foreground">
          {tCommon("version_label")}
        </CardFooter>
      </Card>
    </main>
  );
}
