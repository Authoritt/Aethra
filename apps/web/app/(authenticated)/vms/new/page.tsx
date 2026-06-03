"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { ArrowRight, Copy } from "lucide-react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { PageHeader } from "@/components/layout/page-header";
import type { RegisterVmResponse } from "@/lib/types";
import { MetadataForm } from "./MetadataForm";
import { AutoInstallForm } from "./AutoInstallForm";
import { ManualScriptTab } from "./ManualScriptTab";

/**
 * Página de registro y aprovisionamiento de VM. F11.4: 3 tabs.
 *
 * 1) Metadata — formulario original (nombre, slug, IPs, descripción) — registra la VM
 *    y emite el token UNA SOLA VEZ.
 * 2) Auto-instalar via SSH — pide credenciales SSH y ejecuta el provisioner del central,
 *    streaming logs en vivo por SignalR.
 * 3) Comando manual — bash one-liner para correr en la VM si Aethra no puede SSH-ear.
 *
 * La tab "Metadata" está siempre visible. Las otras dos se habilitan tras crear la VM.
 */
export default function NewVmPage() {
  const t = useTranslations("pages.vms_new");
  const tBreadcrumbs = useTranslations("breadcrumbs");
  const tCommon = useTranslations("common");
  const router = useRouter();
  const [tab, setTab] = useState<string>("metadata");
  const [registered, setRegistered] = useState<RegisterVmResponse | null>(null);

  function onRegistered(r: RegisterVmResponse) {
    setRegistered(r);
    setTab("auto");
    toast.success(t("toast_created", { name: r.name }));
  }

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: tBreadcrumbs("vms"), href: "/vms" },
          { label: registered ? registered.name : t("breadcrumb") },
        ]}
        title={registered ? registered.name : t("title")}
        description={
          registered ? (
            <span className="font-mono text-xs">{registered.slug}</span>
          ) : (
            t("description")
          )
        }
        actions={
          registered ? (
            <Button asChild variant="outline" size="sm">
              <Link href={`/vms/${registered.vm_id}`}>
                {tCommon("details")} <ArrowRight className="ml-2 h-4 w-4" />
              </Link>
            </Button>
          ) : (
            <Button variant="ghost" size="sm" onClick={() => router.push("/vms")}>
              {t("cancel")}
            </Button>
          )
        }
      />

      {registered ? (
        <TokenReminder result={registered} />
      ) : null}

      <Tabs value={tab} onValueChange={setTab} className="mt-2 max-w-4xl">
        <TabsList className="h-auto w-full justify-start gap-1 p-1">
          <TabsTrigger value="metadata" className="px-4 py-2">
            1. {t("tab_metadata")}
          </TabsTrigger>
          <TabsTrigger
            value="auto"
            disabled={!registered}
            className="px-4 py-2"
          >
            2. {t("tab_auto")}
          </TabsTrigger>
          <TabsTrigger
            value="manual"
            disabled={!registered}
            className="px-4 py-2"
          >
            3. {t("tab_manual")}
          </TabsTrigger>
        </TabsList>

        <TabsContent value="metadata" className="mt-4">
          {registered ? (
            <Card>
              <CardContent className="p-6 text-sm text-muted-foreground">
                VM ya creada — usa las pestañas <strong>Auto-instalar</strong> o{" "}
                <strong>Comando manual</strong> para instalar el satélite.
              </CardContent>
            </Card>
          ) : (
            <MetadataForm onRegistered={onRegistered} />
          )}
        </TabsContent>

        <TabsContent value="auto" className="mt-4">
          {registered ? (
            <AutoInstallForm
              vmId={registered.vm_id}
              onFallbackManual={() => setTab("manual")}
            />
          ) : null}
        </TabsContent>

        <TabsContent value="manual" className="mt-4">
          {registered ? (
            <ManualScriptTab
              vmId={registered.vm_id}
              initialToken={registered.token_plaintext}
            />
          ) : null}
        </TabsContent>
      </Tabs>
    </div>
  );
}

function TokenReminder({ result }: { result: RegisterVmResponse }) {
  return (
    <Card className="mb-4 max-w-4xl border-warning/40 bg-warning/5">
      <CardContent className="p-4 text-sm">
        <p className="font-medium text-warning-foreground">
          Token emitido (mostrado UNA sola vez).
        </p>
        <p className="mt-1 text-muted-foreground">
          Si vas a usar la auto-instalación, Aethra lo enviará a la VM por SSH.
          Si vas a usar comando manual, copialo aquí o desde la tab 3.
        </p>
        <CopyableValue label="Token" value={result.token_plaintext} oneLine />
      </CardContent>
    </Card>
  );
}

function CopyableValue({
  label,
  value,
  oneLine,
}: {
  label: string;
  value: string;
  oneLine?: boolean;
}) {
  const display = useMemo(() => value, [value]);
  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      toast.success("Copiado al portapapeles");
    } catch {
      toast.error("No se pudo copiar");
    }
  }
  return (
    <Card className="mt-2 bg-card">
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
          {display}
        </pre>
      </CardContent>
    </Card>
  );
}
