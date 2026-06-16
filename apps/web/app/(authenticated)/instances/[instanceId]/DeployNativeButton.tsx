"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Boxes, ChevronDown, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { ApiError, api } from "@/lib/api";
import type { TemplateServiceDef } from "@/lib/types";

/**
 * F13 — dispara el deploy NATIVO multi-contenedor de la instancia (un contenedor por servicio
 * del template). Solo visible si el template define Services.
 */
export function DeployNativeButton({
  instanceId,
  hostname,
  services = [],
}: {
  instanceId: string;
  hostname?: string | null;
  services?: TemplateServiceDef[];
}) {
  const router = useRouter();
  const [busy, setBusy] = useState<string | null>(null);

  async function deploy(serviceName?: string) {
    const busyKey = serviceName ?? "__all__";
    setBusy(busyKey);
    try {
      const r = await api<{ healthy: boolean; services: string[] }>(
        `/api/instances/${encodeURIComponent(instanceId)}/deploy-native`,
        {
          method: "POST",
          body: JSON.stringify({
            ...(hostname ? { hostname } : {}),
            ...(serviceName ? { serviceName } : {}),
          }),
        },
      );
      toast.success(
        `Deploy nativo OK · ${r.services.length} servicio(s)${r.healthy ? " · healthy" : ""}`,
      );
      router.refresh();
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? ((e.body as { detail?: string; message?: string } | undefined)
              ?.detail ??
            (e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`)
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      toast.error(msg);
    } finally {
      setBusy(null);
    }
  }

  const isBusy = busy !== null;
  const icon = isBusy ? (
    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
  ) : (
    <Boxes className="mr-2 h-4 w-4" />
  );

  if (services.length <= 1) {
    return (
      <Button type="button" onClick={() => deploy()} disabled={isBusy}>
        {icon}
        Deploy nativo
      </Button>
    );
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button type="button" disabled={isBusy}>
          {icon}
          Deploy nativo
          <ChevronDown className="ml-2 h-4 w-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <DropdownMenuItem onClick={() => deploy()} disabled={isBusy}>
          Todos los servicios
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        {services.map((service) => (
          <DropdownMenuItem
            key={service.name}
            onClick={() => deploy(service.name)}
            disabled={isBusy}
          >
            {service.name}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
