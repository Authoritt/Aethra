"use client";

import { useTranslations } from "next-intl";
import { useEffect, useState } from "react";
import { KeyRound, Loader2, Plus, Save, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { EmptyState } from "@/components/ui/empty-state";
import { Input } from "@/components/ui/input";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { ApiError, api } from "@/lib/api";
import type {
  EnvScopeType,
  ScopedEnvVarDto,
  ScopedSecretDto,
} from "@/lib/types";

interface EnvVarRow {
  key: string;
  value: string;
  isBuildTime: boolean;
  isRuntime: boolean;
}

interface SecretRow {
  key: string;
  value: string;
}

export interface ScopedEnvVarsPanelProps {
  scopeType: EnvScopeType;
  scopeId: string;
}

export function ScopedEnvVarsPanel({
  scopeType,
  scopeId,
}: ScopedEnvVarsPanelProps) {
  const v = useTranslations("components.env_vars");
  const c = useTranslations("common");
  const scopeQuery = `scopeType=${encodeURIComponent(scopeType)}&scopeId=${encodeURIComponent(scopeId)}`;

  const [loading, setLoading] = useState(true);
  const [savingVars, setSavingVars] = useState(false);
  const [savingSecrets, setSavingSecrets] = useState(false);

  const [vars, setVars] = useState<EnvVarRow[]>([]);
  const [existingSecrets, setExistingSecrets] = useState<ScopedSecretDto[]>([]);
  const [newSecrets, setNewSecrets] = useState<SecretRow[]>([]);

  async function load() {
    setLoading(true);
    try {
      const [envData, secretData] = await Promise.all([
        api<ScopedEnvVarDto[]>(`/api/env-vars?${scopeQuery}`),
        api<ScopedSecretDto[]>(`/api/secrets?${scopeQuery}`),
      ]);
      setVars(
        envData.map((v) => ({
          key: v.key,
          value: v.value,
          isBuildTime: v.isBuildTime,
          isRuntime: v.isRuntime,
        })),
      );
      setExistingSecrets(secretData);
    } catch (e) {
      toast.error(formatError(e, "No se pudieron cargar variables y secretos"));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scopeType, scopeId]);

  /* ---- env vars ---- */

  function updateVar(idx: number, patch: Partial<EnvVarRow>) {
    setVars((prev) => prev.map((v, i) => (i === idx ? { ...v, ...patch } : v)));
  }

  function addVar() {
    setVars((prev) => [
      ...prev,
      { key: "", value: "", isBuildTime: false, isRuntime: true },
    ]);
  }

  function removeVarRow(idx: number) {
    setVars((prev) => prev.filter((_, i) => i !== idx));
  }

  async function saveVars() {
    setSavingVars(true);
    try {
      const payload = vars
        .filter((v) => v.key.trim().length > 0)
        .map((v) => ({
          key: v.key.trim(),
          value: v.value,
          isBuildTime: v.isBuildTime,
          isRuntime: v.isRuntime,
        }));
      await api(`/api/env-vars?${scopeQuery}`, {
        method: "PUT",
        body: JSON.stringify({ vars: payload }),
      });
      toast.success("Variables de entorno guardadas");
      await load();
    } catch (e) {
      toast.error(formatError(e, v("save_error")));
    } finally {
      setSavingVars(false);
    }
  }

  async function deleteVar(key: string) {
    if (!confirm(v("confirm_delete_var", { key }))) return;
    try {
      await api(
        `/api/env-vars?${scopeQuery}&key=${encodeURIComponent(key)}`,
        { method: "DELETE" },
      );
      toast.success("Variable eliminada");
      await load();
    } catch (e) {
      toast.error(formatError(e, v("delete_var_error")));
    }
  }

  /* ---- secrets ---- */

  function updateNewSecret(idx: number, patch: Partial<SecretRow>) {
    setNewSecrets((prev) =>
      prev.map((s, i) => (i === idx ? { ...s, ...patch } : s)),
    );
  }

  function addSecret() {
    setNewSecrets((prev) => [...prev, { key: "", value: "" }]);
  }

  function removeNewSecretRow(idx: number) {
    setNewSecrets((prev) => prev.filter((_, i) => i !== idx));
  }

  async function saveSecrets() {
    const payload = newSecrets
      .filter((s) => s.key.trim().length > 0)
      .map((s) => ({ key: s.key.trim(), value: s.value }));
    if (payload.length === 0) {
      toast.error(v("need_secret"));
      return;
    }
    setSavingSecrets(true);
    try {
      await api(`/api/secrets?${scopeQuery}`, {
        method: "PUT",
        body: JSON.stringify({ secrets: payload }),
      });
      toast.success("Secretos guardados");
      setNewSecrets([]);
      await load();
    } catch (e) {
      toast.error(formatError(e, v("save_secrets_error")));
    } finally {
      setSavingSecrets(false);
    }
  }

  async function deleteSecret(key: string) {
    if (!confirm(v("confirm_delete_secret", { key }))) return;
    try {
      await api(
        `/api/secrets?${scopeQuery}&key=${encodeURIComponent(key)}`,
        { method: "DELETE" },
      );
      toast.success("Secreto eliminado");
      await load();
    } catch (e) {
      toast.error(formatError(e, v("delete_secret_error")));
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-sm font-medium uppercase tracking-wider text-muted-foreground">
          Variables de entorno y secretos
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-8">
        {loading ? (
          <div className="flex items-center gap-2 text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" /> Cargando…
          </div>
        ) : (
          <>
            {/* Variables de entorno */}
            <section className="space-y-3">
              <div className="flex items-center justify-between">
                <h3 className="text-sm font-medium text-foreground">
                  Variables de entorno
                  <span className="ml-2 text-muted-foreground">
                    {vars.length}
                  </span>
                </h3>
                <div className="flex items-center gap-2">
                  <Button size="sm" variant="outline" onClick={addVar}>
                    <Plus className="mr-2 h-4 w-4" />
                    Agregar
                  </Button>
                  <Button size="sm" onClick={saveVars} disabled={savingVars}>
                    {savingVars ? (
                      <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                    ) : (
                      <Save className="mr-2 h-4 w-4" />
                    )}
                    {c("save")}
                  </Button>
                </div>
              </div>
              <div className="rounded-md border border-border bg-card">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead className="w-[30%]">Clave</TableHead>
                      <TableHead>Valor</TableHead>
                      <TableHead className="w-[90px]">Build</TableHead>
                      <TableHead className="w-[90px]">Runtime</TableHead>
                      <TableHead className="w-[60px]" />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {vars.length === 0 ? (
                      <TableRow>
                        <TableCell
                          colSpan={5}
                          className="h-20 text-center text-sm text-muted-foreground"
                        >
                          {v("empty_vars")}
                        </TableCell>
                      </TableRow>
                    ) : (
                      vars.map((v, idx) => (
                        <TableRow key={idx}>
                          <TableCell>
                            <Input
                              value={v.key}
                              onChange={(e) =>
                                updateVar(idx, { key: e.target.value })
                              }
                              placeholder="DATABASE_URL"
                              className="font-mono text-xs"
                            />
                          </TableCell>
                          <TableCell>
                            <Input
                              value={v.value}
                              onChange={(e) =>
                                updateVar(idx, { value: e.target.value })
                              }
                              placeholder="valor"
                              className="font-mono text-xs"
                            />
                          </TableCell>
                          <TableCell>
                            <Checkbox
                              checked={v.isBuildTime}
                              onCheckedChange={(c) =>
                                updateVar(idx, { isBuildTime: c === true })
                              }
                              aria-label="build-time"
                            />
                          </TableCell>
                          <TableCell>
                            <Checkbox
                              checked={v.isRuntime}
                              onCheckedChange={(c) =>
                                updateVar(idx, { isRuntime: c === true })
                              }
                              aria-label="runtime"
                            />
                          </TableCell>
                          <TableCell>
                            <Button
                              type="button"
                              variant="ghost"
                              size="icon"
                              onClick={() =>
                                v.key
                                  ? void deleteVar(v.key)
                                  : removeVarRow(idx)
                              }
                              aria-label="eliminar"
                            >
                              <Trash2 className="h-4 w-4 text-muted-foreground" />
                            </Button>
                          </TableCell>
                        </TableRow>
                      ))
                    )}
                  </TableBody>
                </Table>
              </div>
            </section>

            {/* Secretos */}
            <section className="space-y-3">
              <div className="flex items-center justify-between">
                <h3 className="text-sm font-medium text-foreground">
                  Secretos
                  <span className="ml-2 text-muted-foreground">
                    {existingSecrets.length}
                  </span>
                </h3>
                <div className="flex items-center gap-2">
                  <Button size="sm" variant="outline" onClick={addSecret}>
                    <Plus className="mr-2 h-4 w-4" />
                    Agregar
                  </Button>
                  <Button
                    size="sm"
                    onClick={saveSecrets}
                    disabled={savingSecrets || newSecrets.length === 0}
                  >
                    {savingSecrets ? (
                      <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                    ) : (
                      <Save className="mr-2 h-4 w-4" />
                    )}
                    {c("save")}
                  </Button>
                </div>
              </div>

              {existingSecrets.length === 0 && newSecrets.length === 0 ? (
                <EmptyState
                  icon={<KeyRound className="h-6 w-6" />}
                  title={v("empty_secrets")}
                  description={v("empty_secrets_desc")}
                />
              ) : (
                <div className="rounded-md border border-border bg-card">
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead className="w-[40%]">Clave</TableHead>
                        <TableHead>Valor</TableHead>
                        <TableHead className="w-[60px]" />
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {existingSecrets.map((s) => (
                        <TableRow key={`existing-${s.key}`}>
                          <TableCell className="font-mono text-xs">
                            {s.key}
                          </TableCell>
                          <TableCell className="font-mono text-xs text-muted-foreground">
                            •••
                          </TableCell>
                          <TableCell>
                            <Button
                              type="button"
                              variant="ghost"
                              size="icon"
                              onClick={() => void deleteSecret(s.key)}
                              aria-label="eliminar secreto"
                            >
                              <Trash2 className="h-4 w-4 text-muted-foreground" />
                            </Button>
                          </TableCell>
                        </TableRow>
                      ))}
                      {newSecrets.map((s, idx) => (
                        <TableRow key={`new-${idx}`}>
                          <TableCell>
                            <Input
                              value={s.key}
                              onChange={(e) =>
                                updateNewSecret(idx, { key: e.target.value })
                              }
                              placeholder="API_TOKEN"
                              className="font-mono text-xs"
                            />
                          </TableCell>
                          <TableCell>
                            <Input
                              type="password"
                              value={s.value}
                              onChange={(e) =>
                                updateNewSecret(idx, { value: e.target.value })
                              }
                              placeholder="••••••••"
                              className="font-mono text-xs"
                            />
                          </TableCell>
                          <TableCell>
                            <Button
                              type="button"
                              variant="ghost"
                              size="icon"
                              onClick={() => removeNewSecretRow(idx)}
                              aria-label="quitar fila"
                            >
                              <Trash2 className="h-4 w-4 text-muted-foreground" />
                            </Button>
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              )}
              <p className="text-xs text-muted-foreground">
                {v("secrets_note")}
              </p>
            </section>
          </>
        )}
      </CardContent>
    </Card>
  );
}

function formatError(e: unknown, fallback: string): string {
  if (e instanceof ApiError) {
    return (
      (e.body as { message?: string } | undefined)?.message ?? `Error ${e.status}`
    );
  }
  if (e instanceof Error) return e.message;
  return fallback;
}
