"use client";

import { useMemo, useState } from "react";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
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

interface Props {
  onRegistered: (r: RegisterVmResponse) => void;
}

/**
 * Tab 1 — Metadata. Crea la VM. Cuando termina invoca `onRegistered` para que el
 * page padre cambie a la tab 2 con los datos necesarios para instalar el satélite.
 */
export function MetadataForm({ onRegistered }: Props) {
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [slugTouched, setSlugTouched] = useState(false);
  const [publicIp, setPublicIp] = useState("");
  const [privateIp, setPrivateIp] = useState("");
  const [description, setDescription] = useState("");
  const [loading, setLoading] = useState(false);

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
      onRegistered(response);
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string; detail?: string } | undefined)
              ?.message ??
            (e.body as { detail?: string } | undefined)?.detail ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }

  return (
    <Card>
      <CardContent className="p-6">
        <form onSubmit={onSubmit} className="flex flex-col gap-5">
          <div className="space-y-2">
            <Label htmlFor="name">Nombre *</Label>
            <Input
              id="name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="vm-prod-01"
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
              placeholder="vm-prod-01"
              pattern="[a-z0-9]+(-[a-z0-9]+)*"
              className="font-mono text-xs"
            />
            <p className="text-xs text-muted-foreground">
              URL-friendly. Se sugiere desde el nombre si lo dejas vacío.
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
              placeholder="ARM, 4 vCPU, 24 GB RAM"
            />
          </div>

          <div className="flex justify-end gap-2 pt-2">
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
  );
}
