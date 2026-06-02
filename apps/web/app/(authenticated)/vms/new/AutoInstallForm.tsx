"use client";

import { useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import {
  HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from "@microsoft/signalr";
import {
  AlertCircle,
  CheckCircle2,
  KeyRound,
  Loader2,
  PlayCircle,
} from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import {
  Form,
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";
import { LogsViewer, type LogEntry } from "@/components/aethra/logs-viewer";
import { ApiError, API_URL, api } from "@/lib/api";
import { cn } from "@/lib/utils";
import type {
  AutoInstallRequest,
  AutoInstallResponse,
  VmInstallLogPayload,
  VmInstallStatus,
  VmInstallStatusChangedPayload,
} from "@/lib/types";

const SSH_VALUE_MAX = 16 * 1024;

const schema = z.object({
  host: z
    .string()
    .min(1, "Requerido")
    .max(255, "Máximo 255 caracteres."),
  port: z
    .number({ message: "Debe ser un número" })
    .int()
    .min(1)
    .max(65535),
  user: z.string().min(1, "Requerido").max(64),
  authMethod: z.enum(["key", "password"]),
  value: z
    .string()
    .min(1, "Requerido")
    .max(SSH_VALUE_MAX, `Máximo ${SSH_VALUE_MAX} caracteres.`),
  containerRuntime: z.enum(["docker", "podman"]),
  installContainerRuntime: z.boolean(),
});

type FormValues = z.infer<typeof schema>;

interface Props {
  vmId: string;
  onFallbackManual: () => void;
}

/**
 * Tab 2 — Auto-instalar via SSH. Pide credenciales, dispara el provisioner del central
 * y muestra los logs en vivo via SignalR. Si falla, ofrece pasar al modo manual.
 */
export function AutoInstallForm({ vmId, onFallbackManual }: Props) {
  const [running, setRunning] = useState(false);
  const [installStatus, setInstallStatus] =
    useState<VmInstallStatus | "Planned" | null>(null);
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const seqRef = useRef(0);
  const connectionRef = useRef<HubConnection | null>(null);

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      host: "",
      port: 22,
      user: "ubuntu",
      authMethod: "key",
      value: "",
      containerRuntime: "docker",
      installContainerRuntime: false,
    },
  });

  const authMethod = form.watch("authMethod");

  // SignalR — subscribe al grupo de la VM para recibir logs de install.
  useEffect(() => {
    let cancelled = false;
    const connection = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/dashboard`, { withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    connectionRef.current = connection;

    connection.on("VmInstallLog", (payload: VmInstallLogPayload) => {
      if (payload.vmId !== vmId) return;
      seqRef.current += 1;
      const seq = seqRef.current;
      setLogs((prev) => [
        ...prev,
        {
          sequence: seq,
          timestamp: payload.timestamp,
          level: payload.level,
          text: payload.line,
        },
      ]);
    });
    connection.on(
      "VmInstallStatusChanged",
      (payload: VmInstallStatusChangedPayload) => {
        if (payload.vmId !== vmId) return;
        setInstallStatus(payload.status);
        if (payload.errorCode) setErrorCode(payload.errorCode);
        if (payload.status === "Installed") {
          setRunning(false);
          toast.success("Satélite conectado");
        } else if (payload.status === "Failed") {
          setRunning(false);
          toast.error(`Instalación falló (${payload.errorCode ?? "desconocido"})`);
        }
      },
    );

    connection
      .start()
      .then(async () => {
        if (cancelled) return;
        try {
          await connection.invoke("JoinVm", vmId);
        } catch {
          /* hub puede aún no aceptar JoinVm */
        }
      })
      .catch(() => {
        /* sin SignalR los logs no llegan en vivo; el polling de /status sigue */
      });

    return () => {
      cancelled = true;
      const c = connectionRef.current;
      connectionRef.current = null;
      if (c) {
        c.stop().catch(() => {});
      }
    };
  }, [vmId]);

  function appendLocalLog(line: string, level: LogEntry["level"] = "info") {
    seqRef.current += 1;
    setLogs((prev) => [
      ...prev,
      {
        sequence: seqRef.current,
        timestamp: new Date().toISOString(),
        level,
        text: line,
      },
    ]);
  }

  async function onSubmit(values: FormValues) {
    setRunning(true);
    setErrorCode(null);
    setInstallStatus("Installing");
    appendLocalLog(`Disparando install en ${values.host}:${values.port}…`);

    const body: AutoInstallRequest = {
      ssh: {
        host: values.host.trim(),
        port: values.port,
        user: values.user.trim(),
        authMethod: values.authMethod,
        value: values.value,
      },
      installContainerRuntime: values.installContainerRuntime,
      containerRuntime: values.containerRuntime,
    };

    try {
      const response = await api<AutoInstallResponse>(
        `/api/vms/${vmId}/install/auto`,
        { method: "POST", body: JSON.stringify(body) },
      );
      setInstallStatus(response.status as VmInstallStatus);
      appendLocalLog(`Install encolado. Status: ${response.status}`);
    } catch (e) {
      const msg =
        e instanceof ApiError
          ? (e.body as { message?: string } | undefined)?.message ??
            `Error ${e.status}`
          : e instanceof Error
            ? e.message
            : "Error desconocido";
      appendLocalLog(msg, "error");
      toast.error(msg);
      setErrorCode("request_failed");
      setRunning(false);
      setInstallStatus("Failed");
    }
  }

  const isFailed = installStatus === "Failed" || errorCode !== null;
  const isInstalled = installStatus === "Installed";

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardContent className="p-6">
          <Form {...form}>
            <form
              onSubmit={form.handleSubmit(onSubmit)}
              className="flex flex-col gap-5"
            >
              <div className="grid grid-cols-1 gap-4 md:grid-cols-[1fr_auto]">
                <FormField
                  control={form.control}
                  name="host"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Host *</FormLabel>
                      <FormControl>
                        <Input
                          {...field}
                          placeholder="203.0.113.10 o vm.example.com"
                          className="font-mono text-xs"
                          autoFocus
                        />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <FormField
                  control={form.control}
                  name="port"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Puerto</FormLabel>
                      <FormControl>
                        <Input
                          type="number"
                          name={field.name}
                          ref={field.ref}
                          onBlur={field.onBlur}
                          value={field.value}
                          onChange={(e) => {
                            const n = Number(e.target.value);
                            field.onChange(Number.isFinite(n) ? n : 0);
                          }}
                          className="w-24 font-mono text-xs"
                        />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              </div>

              <FormField
                control={form.control}
                name="user"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Usuario *</FormLabel>
                    <FormControl>
                      <Input
                        {...field}
                        placeholder="ubuntu / ec2-user / root"
                        className="font-mono text-xs"
                      />
                    </FormControl>
                    <FormDescription>
                      Debe tener acceso a <code>sudo</code> sin password (NOPASSWD) o
                      ser root.
                    </FormDescription>
                    <FormMessage />
                  </FormItem>
                )}
              />

              <FormField
                control={form.control}
                name="authMethod"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Método de autenticación *</FormLabel>
                    <Select
                      value={field.value}
                      onValueChange={(v) =>
                        field.onChange(v as "key" | "password")
                      }
                    >
                      <FormControl>
                        <SelectTrigger>
                          <SelectValue />
                        </SelectTrigger>
                      </FormControl>
                      <SelectContent>
                        <SelectItem value="key">
                          Clave privada (PEM)
                        </SelectItem>
                        <SelectItem value="password">Contraseña</SelectItem>
                      </SelectContent>
                    </Select>
                  </FormItem>
                )}
              />

              <FormField
                control={form.control}
                name="value"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>
                      {authMethod === "key" ? "Clave privada PEM *" : "Contraseña *"}
                    </FormLabel>
                    <FormControl>
                      {authMethod === "key" ? (
                        <KeyTextarea
                          value={field.value}
                          onChange={field.onChange}
                        />
                      ) : (
                        <Input
                          type="password"
                          value={field.value}
                          onChange={field.onChange}
                          placeholder="••••••••"
                        />
                      )}
                    </FormControl>
                    <FormDescription>
                      {authMethod === "key" ? (
                        <>
                          PEM completo (RSA / Ed25519 / ECDSA). Si tiene
                          passphrase, descifrala primero — Aethra no la solicita.
                        </>
                      ) : (
                        <>
                          La contraseña queda cifrada con DataProtection en BD.
                        </>
                      )}
                    </FormDescription>
                    <FormMessage />
                  </FormItem>
                )}
              />

              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                <FormField
                  control={form.control}
                  name="containerRuntime"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Container runtime</FormLabel>
                      <Select
                        value={field.value}
                        onValueChange={(v) =>
                          field.onChange(v as "docker" | "podman")
                        }
                      >
                        <FormControl>
                          <SelectTrigger>
                            <SelectValue />
                          </SelectTrigger>
                        </FormControl>
                        <SelectContent>
                          <SelectItem value="docker">Docker</SelectItem>
                          <SelectItem value="podman">Podman</SelectItem>
                        </SelectContent>
                      </Select>
                    </FormItem>
                  )}
                />
                <FormField
                  control={form.control}
                  name="installContainerRuntime"
                  render={({ field }) => (
                    <FormItem className="flex flex-row items-center gap-3 pt-7">
                      <FormControl>
                        <Checkbox
                          checked={field.value}
                          onCheckedChange={(v) => field.onChange(v === true)}
                        />
                      </FormControl>
                      <Label className="m-0 font-normal">
                        Instalar runtime si falta (apt/dnf)
                      </Label>
                    </FormItem>
                  )}
                />
              </div>

              <div className="flex items-center justify-between border-t border-border pt-4">
                <div className="text-xs text-muted-foreground">
                  {running ? (
                    <span className="inline-flex items-center gap-2">
                      <Loader2 className="h-3 w-3 animate-spin" /> Instalación
                      en curso…
                    </span>
                  ) : isInstalled ? (
                    <span className="inline-flex items-center gap-2 text-success">
                      <CheckCircle2 className="h-4 w-4" /> Satélite conectado.
                    </span>
                  ) : isFailed ? (
                    <span className="inline-flex items-center gap-2 text-destructive">
                      <AlertCircle className="h-4 w-4" /> Falló
                      {errorCode ? ` (${errorCode})` : ""}
                    </span>
                  ) : (
                    "Llena credenciales y dale Conectar."
                  )}
                </div>
                <div className="flex gap-2">
                  {isFailed ? (
                    <Button
                      type="button"
                      variant="outline"
                      onClick={onFallbackManual}
                    >
                      Probar comando manual
                    </Button>
                  ) : null}
                  <Button
                    type="submit"
                    disabled={running || isInstalled}
                    className="gap-2"
                  >
                    {running ? (
                      <Loader2 className="h-4 w-4 animate-spin" />
                    ) : (
                      <PlayCircle className="h-4 w-4" />
                    )}
                    {running ? "Instalando…" : "Conectar e instalar"}
                  </Button>
                </div>
              </div>
            </form>
          </Form>
        </CardContent>
      </Card>

      <LogsViewer
        entries={logs}
        isLive={running || installStatus === "Installing"}
        heightClassName={cn("min-h-[280px] max-h-[480px]")}
      />
    </div>
  );
}

function KeyTextarea({
  value,
  onChange,
}: {
  value: string;
  onChange: (v: string) => void;
}) {
  async function onFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    if (file.size > SSH_VALUE_MAX) {
      toast.error(`Clave demasiado grande (máximo ${SSH_VALUE_MAX} bytes).`);
      return;
    }
    const text = await file.text();
    onChange(text);
  }
  return (
    <div className="flex flex-col gap-2">
      <Textarea
        value={value}
        onChange={(e) => onChange(e.target.value)}
        rows={8}
        placeholder={"-----BEGIN OPENSSH PRIVATE KEY-----\n...\n-----END OPENSSH PRIVATE KEY-----"}
        className="font-mono text-[11px]"
      />
      <div className="flex items-center gap-2 text-xs text-muted-foreground">
        <KeyRound className="h-3 w-3" />
        <span>O pega el contenido arriba. También podés cargarlo:</span>
        <input
          type="file"
          accept=".pem,.key,.txt,*"
          onChange={onFile}
          className="text-xs"
        />
      </div>
    </div>
  );
}
