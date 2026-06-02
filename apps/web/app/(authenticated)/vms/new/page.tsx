"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { ArrowRight, Copy, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { PageHeader } from "@/components/layout/page-header";
import { ApiError, api } from "@/lib/api";
import type { RegisterVmResponse } from "@/lib/types";

function slugify(input: string): string {
  return input
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[̀-ͯ]/g, "")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 64);
}

export default function NewVmPage() {
  const router = useRouter();
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [slugTouched, setSlugTouched] = useState(false);
  const [publicIp, setPublicIp] = useState("");
  const [privateIp, setPrivateIp] = useState("");
  const [description, setDescription] = useState("");
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<RegisterVmResponse | null>(null);

  const suggestedSlug = useMemo(() => slugify(name), [name]);
  const effectiveSlug = slugTouched ? slug : suggestedSlug;

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    try {
      const response = await api<RegisterVmResponse>("/api/vms/", {
        method: "POST",
        body: JSON.stringify({
          name,
          slug: effectiveSlug || undefined,
          public_ip: publicIp || undefined,
          private_ip: privateIp || undefined,
          description: description || undefined,
        }),
      });
      toast.success("VM registrada");
      setResult(response);
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { detail?: string } | undefined)?.detail ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }

  if (result) {
    return <SuccessScreen result={result} />;
  }

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[{ label: "VMs", href: "/vms" }, { label: "Registrar" }]}
        title="Registrar VM"
        description="Genera un token de satélite para que el agente reporte métricas a Aethra."
      />
      <Card className="max-w-2xl">
        <CardContent className="p-6">
          <form onSubmit={onSubmit} className="flex flex-col gap-5">
            <div className="space-y-2">
              <Label htmlFor="name">Nombre *</Label>
              <Input
                id="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="vm-oracle-fra-01"
                required
                autoFocus
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="slug">Slug</Label>
              <Input
                id="slug"
                value={effectiveSlug}
                onChange={(e) => {
                  setSlug(e.target.value);
                  setSlugTouched(true);
                }}
                placeholder="vm-oracle-fra-01"
                pattern="[a-z0-9]+(-[a-z0-9]+)*"
                className="font-mono text-xs"
              />
              <p className="text-xs text-muted-foreground">
                URL-friendly. Sugerido desde el nombre si lo dejás vacío.
              </p>
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label htmlFor="public">IP pública</Label>
                <Input
                  id="public"
                  value={publicIp}
                  onChange={(e) => setPublicIp(e.target.value)}
                  placeholder="203.0.113.10"
                  className="font-mono text-xs"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="private">IP privada</Label>
                <Input
                  id="private"
                  value={privateIp}
                  onChange={(e) => setPrivateIp(e.target.value)}
                  placeholder="10.0.0.10"
                  className="font-mono text-xs"
                />
              </div>
            </div>
            <div className="space-y-2">
              <Label htmlFor="description">Descripción</Label>
              <Textarea
                id="description"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                rows={3}
                placeholder="Oracle Free Tier ARM, ámsterdam"
              />
            </div>

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="ghost"
                onClick={() => router.push("/vms")}
              >
                Cancelar
              </Button>
              <Button type="submit" disabled={loading || !name}>
                {loading ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                Registrar VM
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}

function SuccessScreen({ result }: { result: RegisterVmResponse }) {
  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "VMs", href: "/vms" },
          { label: result.name },
        ]}
        title={result.name}
        description={
          <span className="font-mono text-xs">{result.slug}</span>
        }
      />

      <Card className="mb-4 max-w-3xl border-warning/40 bg-warning/5">
        <CardContent className="p-4 text-sm">
          <p className="font-medium text-warning-foreground">
            Este token solo se muestra una vez.
          </p>
          <p className="mt-1 text-muted-foreground">
            Copialo y guárdalo en el satélite ahora. Si lo perdés tendrás que
            generar uno nuevo.
          </p>
        </CardContent>
      </Card>

      <div className="flex max-w-3xl flex-col gap-4">
        <CopyBlock label="Token de satélite" value={result.token_plaintext} oneLine />
        <CopyBlock label="Script de instalación" value={result.install_script} />
      </div>

      <div className="mt-6 flex max-w-3xl justify-end">
        <Button asChild>
          <Link href={`/vms/${result.vm_id}`}>
            Ir al detalle
            <ArrowRight className="ml-2 h-4 w-4" />
          </Link>
        </Button>
      </div>
    </div>
  );
}

function CopyBlock({
  label,
  value,
  oneLine,
}: {
  label: string;
  value: string;
  oneLine?: boolean;
}) {
  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      toast.success("Copiado al portapapeles");
    } catch {
      toast.error("No se pudo copiar");
    }
  }

  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between space-y-0 pb-2">
        <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
          {label}
        </CardTitle>
        <Button variant="outline" size="sm" onClick={copy}>
          <Copy className="mr-2 h-4 w-4" />
          Copiar
        </Button>
      </CardHeader>
      <CardContent>
        <pre
          className={`overflow-x-auto rounded-md border border-border bg-muted px-3 py-2 font-mono text-xs text-foreground ${
            oneLine ? "whitespace-nowrap" : "whitespace-pre"
          }`}
        >
          {value}
        </pre>
      </CardContent>
    </Card>
  );
}
