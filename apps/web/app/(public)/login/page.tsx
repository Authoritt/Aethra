"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
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

const schema = z.object({
  email: z.string().email("Correo inválido"),
  password: z.string().min(1, "Requerido"),
});

type FormValues = z.infer<typeof schema>;

export default function LoginPage() {
  const router = useRouter();
  const [submitting, setSubmitting] = useState(false);
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
      toast.success("Sesión iniciada");
      router.push("/dashboard");
      router.refresh();
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        toast.error("Credenciales inválidas");
        form.setError("password", { message: "Credenciales inválidas" });
      } else {
        const msg = e instanceof Error ? e.message : "Error desconocido";
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
          <CardTitle>Inicia sesión</CardTitle>
          <CardDescription>
            Plataforma unificada de despliegue y operación.
          </CardDescription>
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
                    <FormLabel>Correo</FormLabel>
                    <FormControl>
                      <Input
                        type="email"
                        autoComplete="email"
                        placeholder="tu@correo.com"
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
                    <FormLabel>Contraseña</FormLabel>
                    <FormControl>
                      <Input
                        type="password"
                        autoComplete="current-password"
                        placeholder="••••••••"
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
                {submitting ? "Entrando…" : "Entrar"}
              </Button>
            </form>
          </Form>
        </CardContent>
        <CardFooter className="justify-center text-xs text-muted-foreground">
          Aethra · v1
        </CardFooter>
      </Card>
    </main>
  );
}
