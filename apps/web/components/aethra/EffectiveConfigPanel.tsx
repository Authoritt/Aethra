import { useTranslations } from "next-intl";
import type { ReactNode } from "react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { AppEnvironmentEffectiveConfigDto, EffectiveConfigItemDto } from "@/lib/types";

export function EffectiveConfigPanel({
  action,
  config,
}: {
  action?: ReactNode;
  config: AppEnvironmentEffectiveConfigDto | null;
}) {
  const k = useTranslations("components.effective_config");
  if (!config) {
    return (
      <Card>
        <CardContent className="p-4">
          <EmptyState
            title={k("unavailable_title")}
            description={k("unavailable_desc")}
          />
        </CardContent>
      </Card>
    );
  }

  const envCount = config.items.filter((item) => item.kind === "env").length;
  const secretCount = config.items.filter((item) => item.kind === "secret").length;

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle>{k("title")}</CardTitle>
            <CardDescription>
              {k("description")}
            </CardDescription>
          </div>
          <div className="flex flex-wrap items-center justify-end gap-2">
            <div className="flex flex-wrap gap-2">
              <Badge variant="outline">{envCount} vars</Badge>
              <Badge variant="outline">{secretCount} secrets</Badge>
              {config.driftCount > 0 ? (
                <Badge variant="warning">{config.driftCount} drift</Badge>
              ) : (
                <Badge variant="success">sin drift</Badge>
              )}
            </div>
            {config.driftCount > 0 && action ? action : null}
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="rounded-md border border-border bg-muted/40 px-3 py-2 text-xs text-muted-foreground">
          {k("last_deploy")}{" "}
          <span className="font-mono text-foreground">
            {config.lastDeployedAt ? formatDate(config.lastDeployedAt) : k("no_deploy")}
          </span>
        </div>

        <div className="flex flex-wrap gap-2">
          {config.scopes
            .slice()
            .sort((a, b) => a.rank - b.rank)
            .map((scope) => (
              <Badge key={`${scope.scopeType}:${scope.scopeId}`} variant="secondary">
                {scope.label}
              </Badge>
            ))}
        </div>

        {config.items.length === 0 ? (
          <EmptyState
            title={k("empty_title")}
            description={k("empty_desc")}
          />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Key</TableHead>
                <TableHead>Tipo</TableHead>
                <TableHead>Valor</TableHead>
                <TableHead>Gana</TableHead>
                <TableHead>Uso</TableHead>
                <TableHead>Drift</TableHead>
                <TableHead>Overrides</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {config.items.map((item) => (
                <TableRow key={`${item.kind}:${item.key}`}>
                  <TableCell className="font-mono text-xs">{item.key}</TableCell>
                  <TableCell>
                    <Badge variant={item.kind === "secret" ? "warning" : "outline"}>
                      {item.kind === "secret" ? "secret" : "env"}
                    </Badge>
                  </TableCell>
                  <TableCell className="max-w-[320px] truncate font-mono text-xs">
                    {renderValue(item, k("no_value"))}
                  </TableCell>
                  <TableCell className="text-xs">{item.winningScopeLabel}</TableCell>
                  <TableCell>
                    <div className="flex flex-wrap gap-1">
                      {item.isBuildTime ? <Badge variant="outline">build</Badge> : null}
                      {item.isRuntime ? <Badge variant="outline">runtime</Badge> : null}
                    </div>
                  </TableCell>
                  <TableCell>
                    {item.changedSinceLastDeploy ? (
                      <Badge variant="warning">{renderChangeAction(item.changeAction)}</Badge>
                    ) : (
                      <Badge variant="outline">ok</Badge>
                    )}
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {item.overriddenCount > 0
                      ? `${item.overriddenCount} oculto(s): ${item.sources
                          .filter((source) => !source.wins)
                          .map((source) => source.scopeLabel)
                          .join(", ")}`
                      : k("no_overrides")}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}

function renderValue(item: EffectiveConfigItemDto, noValue: string) {
  if (item.kind === "secret") {
    return item.hasValue ? "********" : noValue;
  }
  return item.value ?? "";
}

function renderChangeAction(action: EffectiveConfigItemDto["changeAction"]) {
  if (action === "redeploy") {
    return "requiere redeploy";
  }
  if (action === "restart") {
    return "requiere restart";
  }
  return "revisar";
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("es-CO", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}
