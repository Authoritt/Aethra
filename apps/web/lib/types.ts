/**
 * Tipos compartidos con la API. Si en F2+ generamos clientes desde OpenAPI, este archivo
 * se reemplaza por el output del generador.
 */

export interface ProjectSummary {
  id: string;
  slug: string;
  name: string;
  description: string | null;
  color: string | null;
  icon: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ProjectDetail {
  id: string;
  slug: string;
  name: string;
  description: string | null;
  color: string | null;
  icon: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateProjectRequest {
  name: string;
  slug?: string;
  description?: string;
  color?: string;
  icon?: string;
}

export interface CreateProjectResponse {
  project: ProjectSummary;
  next_actions: { tool: string; why: string; suggested_args: unknown }[];
}

/* -------------------------------------------------------------------------- */
/* VMs (F2)                                                                   */
/* -------------------------------------------------------------------------- */

export type VmStatus = "Pending" | "Connected" | "Disconnected" | string;

export interface VmDto {
  id: string;
  slug: string;
  name: string;
  publicIp: string | null;
  privateIp: string | null;
  description: string | null;
  status: VmStatus;
  createdAt: string;
  updatedAt: string;
  lastConnectedAt: string | null;
  lastDisconnectedAt: string | null;
  hostname: string | null;
  kernelVersion: string | null;
  cpuModel: string | null;
  cpuCores: number | null;
  totalMemoryBytes: number | null;
  agentVersion: string | null;
  /** F12.3 — opt-in al pool de previews. Default true. Serializa como camelCase. */
  acceptsPreviews?: boolean;
}

export interface RegisterVmRequest {
  name: string;
  slug?: string;
  publicIp?: string;
  privateIp?: string;
  description?: string;
}

export interface RegisterVmResponse {
  vmId: string;
  slug: string;
  name: string;
  tokenPlaintext: string;
  installScript: string;
}

/* --- F11.4 auto-install via SSH --- */

export type VmInstallStatus =
  | "NotInstalled"
  | "Installing"
  | "Installed"
  | "Failed";

export type SshAuthMethod = "key" | "password";

export interface AutoInstallSshRequest {
  host: string;
  port: number;
  user: string;
  authMethod: SshAuthMethod;
  value: string;
}

export interface AutoInstallRequest {
  ssh?: AutoInstallSshRequest | null;
  installContainerRuntime: boolean;
  containerRuntime: "docker" | "podman";
  dryRun?: boolean;
}

export interface AutoInstallResponse {
  vmId: string;
  status: VmInstallStatus | "Planned";
  installUrl: string;
  streamHub: string;
  plan?: string | null;
  script?: string | null;
}

export interface InstallStatusResponse {
  vmId: string;
  status: VmInstallStatus;
  lastSeenAt: string | null;
  hasSavedCredentials: boolean;
  lastLogLines: string[];
}

export interface InstallScriptResponse {
  script: string;
  lines: string[];
  tokenPlaintext: string;
}

/** Payload del evento SignalR `VmInstallLog`. */
export interface VmInstallLogPayload {
  vmId: string;
  line: string;
  level: "info" | "warn" | "error" | "debug" | string;
  timestamp: string;
}

/** Payload del evento SignalR `VmInstallStatusChanged`. */
export interface VmInstallStatusChangedPayload {
  vmId: string;
  status: VmInstallStatus;
  errorCode: string | null;
  timestamp: string;
}

export interface VmMetricPoint {
  timestamp: string;
  cpuPercent: number;
  memoryUsedBytes: number;
  memoryTotalBytes: number;
  diskUsedBytes: number;
  diskTotalBytes: number;
  netBytesReceived: number;
  netBytesSent: number;
}

/* -------------------------------------------------------------------------- */
/* Reverse proxy routes (F3)                                                  */
/* -------------------------------------------------------------------------- */

export type CertStatus =
  | "none"
  | "pending"
  | "issued"
  | "failed"
  | "renewing";

export interface RouteDto {
  id: string;
  hostname: string;
  pathPrefix: string;
  backendUrl: string;
  tlsEnabled: boolean;
  certStatus: CertStatus;
  certExpiresAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateRouteRequest {
  hostname: string;
  pathPrefix?: string;
  backendUrl: string;
  tlsEnabled: boolean;
}

/* -------------------------------------------------------------------------- */
/* Managed services (F5)                                                      */
/* -------------------------------------------------------------------------- */

export type ManagedServiceStatus =
  | "provisioning"
  | "ready"
  | "failed"
  | "stopped";

export type ManagedServiceType = string;

export interface ManagedServiceSummaryDto {
  id: string;
  slug: string;
  name: string;
  type: ManagedServiceType;
  version: string;
  status: ManagedServiceStatus;
  targetVmId: string;
  containerName: string;
  bindingsCount: number;
}

export interface ManagedServiceDetailDto extends ManagedServiceSummaryDto {
  image: string;
  internalPort: number;
  networkName: string;
  exposedExternally: boolean;
  createdAt: string;
  updatedAt: string;
  provisionedAt: string | null;
  errorCode: string | null;
  errorMessage: string | null;
}

export interface ServiceTemplateDto {
  id: string;
  displayName: string;
  type: ManagedServiceType;
  version: string;
  image: string;
  internalPort: number;
  notes: string;
  category: string;
  description?: string | null;
  tags: string[];
  iconUrl?: string | null;
  bindingSupported: boolean;
  dependencies: string[];
  multiContainer: boolean;
}

export type BindingPermissions = "Owner" | "ReadWrite" | "ReadOnly";

export interface ServiceBindingDto {
  id: string;
  serviceId: string;
  instanceId: string;
  instanceSlug?: string;
  resourceName: string;
  permissions: BindingPermissions;
  envVarPrefix: string;
  hasMigrationsHook: boolean;
  createdAt: string;
  provisionedAt: string | null;
  revokedAt: string | null;
  lastRotatedAt: string | null;
}

export interface CreateServiceRequest {
  templateId: string;
  slug: string;
  name: string;
  targetVmId: string;
  exposedExternally?: boolean;
}

export interface CreateBindingRequest {
  instanceId: string;
  resourceName?: string;
  permissions: BindingPermissions;
  envVarPrefix?: string;
  migrationsHook?: {
    command: string;
    timeoutSeconds: number;
    failDeployOnError: boolean;
    runOn: "EachDeploy" | "FirstDeployOnly" | "ManualTrigger";
  };
}

/* -------------------------------------------------------------------------- */
/* Cloudflare DNS (F6)                                                        */
/* -------------------------------------------------------------------------- */

export type CloudflareZoneStatus =
  | "Unknown"
  | "Active"
  | "Pending"
  | "Suspended";

export type DnsRecordType = "A" | "AAAA" | "CNAME" | "TXT" | "MX";

export interface CloudflareZoneDto {
  id: string;
  externalZoneId: string;
  name: string;
  status: CloudflareZoneStatus;
  accountId: string;
  recordsCount: number;
  createdAt: string;
  updatedAt: string;
  lastSyncedAt: string | null;
}

export interface DnsRecordDto {
  id: string;
  zoneId: string;
  externalRecordId: string | null;
  type: DnsRecordType;
  name: string;
  content: string;
  ttl: number;
  proxied: boolean;
  comment: string | null;
  createdAt: string;
  updatedAt: string;
  syncedAt: string | null;
  lastError: string | null;
}

export interface CloudflareZoneDetailDto {
  id: string;
  externalZoneId: string;
  name: string;
  status: CloudflareZoneStatus;
  accountId: string;
  createdAt: string;
  updatedAt: string;
  lastSyncedAt: string | null;
  records: DnsRecordDto[];
}

export interface RegisterCloudflareZoneRequest {
  zoneId: string;
  apiToken: string;
}

export interface RotateCloudflareTokenRequest {
  apiToken: string;
}

export interface CreateDnsRecordRequest {
  type: DnsRecordType;
  name: string;
  content: string;
  ttl?: number;
  proxied?: boolean;
  comment?: string;
}

export interface UpdateDnsRecordRequest {
  content?: string;
  ttl?: number;
  proxied?: boolean;
  comment?: string;
}

/* -------------------------------------------------------------------------- */
/* Monitoring (F6)                                                            */
/* -------------------------------------------------------------------------- */

export type MonitorStatus = "Unknown" | "Up" | "Down" | "Degraded";

export type MonitorHttpMethod = "GET" | "HEAD" | "POST";

export interface MonitorSummaryDto {
  id: string;
  slug: string;
  name: string;
  url: string;
  httpMethod: MonitorHttpMethod;
  intervalSec: number;
  timeoutMs: number;
  status: MonitorStatus;
  isEnabled: boolean;
  lastCheckedAt: string | null;
  consecutiveFailures: number;
  instanceId: string | null;
  projectId: string | null;
}

export interface MonitorDetailDto {
  id: string;
  slug: string;
  name: string;
  url: string;
  httpMethod: MonitorHttpMethod;
  expectedStatusCodes: number[];
  intervalSec: number;
  timeoutMs: number;
  headers: Record<string, string> | null;
  bodyTemplate: string | null;
  instanceId: string | null;
  projectId: string | null;
  isEnabled: boolean;
  status: MonitorStatus;
  lastCheckedAt: string | null;
  consecutiveFailures: number;
  createdAt: string;
  updatedAt: string;
}

export interface MonitorCheckDto {
  id: string;
  monitorId: string;
  timestamp: string;
  status: MonitorStatus;
  httpStatusCode: number | null;
  latencyMs: number | null;
  errorMessage: string | null;
  responseSnippet: string | null;
}

export interface MonitorOverviewDto {
  total: number;
  up: number;
  down: number;
  degraded: number;
  unknown: number;
  disabled: number;
}

export interface CreateMonitorRequest {
  slug: string;
  name: string;
  url: string;
  httpMethod?: MonitorHttpMethod;
  expectedStatusCodes?: number[];
  intervalSec?: number;
  timeoutMs?: number;
  headers?: Record<string, string>;
  bodyTemplate?: string;
  instanceId?: string;
  projectId?: string;
}

export interface UpdateMonitorRequest {
  name?: string;
  url?: string;
  httpMethod?: MonitorHttpMethod;
  expectedStatusCodes?: number[];
  intervalSec?: number;
  timeoutMs?: number;
  headers?: Record<string, string>;
  clearHeaders?: boolean;
  bodyTemplate?: string;
  clearBodyTemplate?: boolean;
  instanceId?: string;
  clearInstanceId?: boolean;
  projectId?: string;
  clearProjectId?: boolean;
}

export interface MonitorStatusChangedPayload {
  monitorId: string;
  from: MonitorStatus;
  to: MonitorStatus;
  checkId: string;
  httpStatusCode: number | null;
  latencyMs: number | null;
  timestamp: string;
}

/* -------------------------------------------------------------------------- */
/* Notes (F6)                                                                 */
/* -------------------------------------------------------------------------- */
/* Convención: el API minimal-API de .NET 10 serializa con camelCase por      */
/* default. Los DTOs del módulo Notes los consumimos tal cual.                */

export type NoteScopeType = "Project" | "Environment" | "Application";

export interface NoteSummary {
  id: string;
  scopeType: NoteScopeType;
  scopeId: string;
  title: string;
  isPinned: boolean;
  imageCount: number;
  authorId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface NoteImage {
  imageId: string;
  originalFilename: string;
  contentType: string;
  sizeBytes: number;
  uploadedAt: string;
  url: string;
}

export interface NoteDetail {
  id: string;
  scopeType: NoteScopeType;
  scopeId: string;
  title: string;
  markdownBody: string;
  isPinned: boolean;
  authorId: string | null;
  createdAt: string;
  updatedAt: string;
  images: NoteImage[];
}

export interface PinnedFactDto {
  id: string;
  scopeType: NoteScopeType;
  scopeId: string;
  key: string;
  value: string;
  isSecret: boolean;
  description: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateNoteRequest {
  scopeType: NoteScopeType;
  scopeId: string;
  title: string;
  markdownBody: string;
}

export interface UpdateNoteRequest {
  title?: string;
  markdownBody?: string;
}

export interface UpsertPinnedFactRequest {
  scopeType: NoteScopeType;
  scopeId: string;
  key: string;
  value: string;
  isSecret: boolean;
  description?: string;
}

/* -------------------------------------------------------------------------- */
/* Identity — API keys                                                        */
/* -------------------------------------------------------------------------- */

export interface ApiKeySummary {
  id: string;
  name: string;
  keyPrefix: string;
  scopes: string[];
  createdAt: string;
  lastUsedAt?: string | null;
  expiresAt?: string | null;
  revokedAt?: string | null;
}

export interface CreateApiKeyResult extends ApiKeySummary {
  secret: string;
}

export interface CreateApiKeyRequest {
  name: string;
  scopes: string[];
  expiresAt?: string | null;
}

/* -------------------------------------------------------------------------- */
/* Identity — Users & Roles (F11.1 multi-user RBAC)                           */
/* -------------------------------------------------------------------------- */

export interface RoleRef {
  id: string;
  slug: string;
  displayName: string;
}

export interface UserSummary {
  id: string;
  email: string;
  displayName: string | null;
  roles: RoleRef[];
  isActive: boolean;
  lastLoginAt: string | null;
  createdAt: string;
  updatedAt: string;
  /** F12.3 — handle de GitHub (lo mapea el webhook handler de PR). */
  gitHubUsername?: string | null;
}

export interface CreatedUser {
  id: string;
  email: string;
  displayName: string | null;
  roles: RoleRef[];
}

export interface CreateUserRequest {
  email: string;
  password: string;
  displayName?: string | null;
  roleSlugs: string[];
}

export interface UpdateUserRequest {
  displayName?: string | null;
  roleSlugs?: string[];
}

export interface ResetPasswordRequest {
  newPassword: string;
}

export interface ResetPasswordResult {
  id: string;
  email: string;
}

export interface RoleDto {
  id: string;
  slug: string;
  displayName: string;
  scopes: string[];
  isSystem: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreatedRole {
  id: string;
  slug: string;
  displayName: string;
  scopes: string[];
}

export interface CreateRoleRequest {
  slug: string;
  displayName: string;
  scopes: string[];
}

export interface MeResponse {
  userId?: string | null;
  email: string;
  displayName: string | null;
  /** F12.3 — handle de GitHub del usuario actual (visible en profile). */
  gitHubUsername?: string | null;
  roles: string[];
  scopes: string[];
}

/* -------------------------------------------------------------------------- */
/* Settings (F9.1) — integrations, base domains, environments                 */
/* -------------------------------------------------------------------------- */
/* Convención: PascalCase en C# → camelCase en JSON (default de minimal APIs   */
/* de .NET 10). Por eso aquí usamos camelCase tal cual lo serializa la API.    */

export type IntegrationCredentialType =
  | "Cloudflare"
  | "GitHubPat"
  | "Smtp"
  | "Registry"
  | "GenericApiKey";

export interface IntegrationCredentialDto {
  id: string;
  name: string;
  type: IntegrationCredentialType;
  displayName: string;
  description: string | null;
  metadata: Record<string, string> | null;
  createdAt: string;
  rotatedAt: string | null;
  lastUsedAt: string | null;
}

export interface CreateIntegrationCredentialRequest {
  name: string;
  type: IntegrationCredentialType;
  displayName: string;
  plainValue: string;
  metadata?: Record<string, string> | null;
  description?: string | null;
}

export interface RotateIntegrationCredentialRequest {
  newPlainValue: string;
}

export interface BaseDomainDto {
  id: string;
  hostname: string;
  cloudflareZoneId: string | null;
  wildcardConfigured: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateBaseDomainRequest {
  hostname: string;
  cloudflareZoneId?: string | null;
}

export interface EnvironmentDefinitionDto {
  id: string;
  slug: string;
  displayName: string;
  order: number;
  createdAt: string;
}

export interface CreateEnvironmentDefinitionRequest {
  slug: string;
  displayName: string;
  order?: number | null;
}

export interface ReorderEnvironmentDefinitionsRequest {
  ids: string[];
}

/* -------------------------------------------------------------------------- */
/* Multi-tenant (F9.5) — Projects/Templates/Clients/Instances + Build/Deploy  */
/* -------------------------------------------------------------------------- */
/* Convencion: PascalCase en C# -> camelCase en JSON. Estos DTOs replican los  */
/* contratos REST del backend (agente A10).                                    */

export interface ProjectSummaryV2 {
  id: string;
  slug: string;
  name: string;
  color: string;
  icon: string;
  createdAt: string;
}

export interface ProjectDetailV2 extends ProjectSummaryV2 {
  description: string | null;
  updatedAt: string;
  templateCount: number;
  clientCount: number;
}

export interface CreateProjectV2Request {
  slug: string;
  name: string;
  description?: string | null;
  color?: string;
  icon?: string;
}

export type BuildType = "Dockerfile" | "DockerCompose" | "Nixpacks";

export interface TemplateBuildArg {
  key: string;
  value: string;
}

export interface TemplateSummary {
  id: string;
  projectId: string;
  slug: string;
  name: string;
  description: string | null;
  gitRepoUrl: string;
  branch: string;
  buildType: BuildType;
  createdAt: string;
  updatedAt: string;
  /**
   * El backend (ListTemplatesQuery) no proyecta este contador en la summary.
   * Se mantiene opcional para las vistas que lo muestren cuando este disponible.
   */
  instanceCount?: number;
}

/**
 * F12.3 — fila del mapping Environment→Branch heredado por Instances que no setean TrackedRef.
 */
export interface TemplateEnvironmentMapping {
  environment: string;
  branch: string;
}

export interface TemplateDetail extends TemplateSummary {
  description: string | null;
  // El API serializa estos campos PLANOS (no anidados bajo source/build).
  baseDirectory: string;
  watchPaths: string[];
  accessTokenCredentialName: string | null;
  dockerfilePath: string | null;
  composeFilePath: string | null;
  buildArgs: TemplateBuildArg[];
  /** F12.3 — mapping branch-per-environment. Default vacio. */
  environmentMapping: TemplateEnvironmentMapping[];
  /** F12.3 — opt-in al auto-create de Instances ephemerals al recibir pull_request.opened. */
  autoPreviewPullRequests: boolean;
  /** F13 — servicios multi-contenedor (deploy nativo). Vacío = template single-build. */
  services: TemplateServiceDef[];
  webhookSecret?: string;
  createdAt: string;
  updatedAt: string;
}

/** F13 — un servicio multi-contenedor del template (deploy nativo). */
export interface TemplateServiceDef {
  name: string;
  image: string;
  port: number;
  pathPrefixes: string[];
  env: TemplateBuildArg[];
  /** "registry" (imagen prebuilt) o "git" (Aethra clona y construye). */
  buildMode: string;
  dockerfilePath: string | null;
  /** F13.3 — volúmenes persistentes del servicio (ej. DataProtection keys). */
  volumes: TemplateServiceVolumeDef[];
}

/** F13.3 — un volumen persistente montado en un servicio del deploy nativo. */
export interface TemplateServiceVolumeDef {
  /** Nombre del named volume. Admite {instance} → slug. */
  name: string;
  containerPath: string;
  readOnly: boolean;
}

/** F13.9 — Cloudflare Tunnel gestionado remotamente (ingress por API, cero blip). */
export interface CloudflareTunnelDto {
  id: string;
  tunnelId: string;
  name: string;
  accountId: string;
  aethraService: string;
  fallbackService: string;
  fallbackNoTlsVerify: boolean;
  targetVmId: string | null;
  createdAt: string;
  updatedAt: string;
  lastSyncedAt: string | null;
  ingress: TunnelIngressRuleDto[];
}

/** Una regla de ingress del túnel (hostname null = catch-all). */
export interface TunnelIngressRuleDto {
  hostname: string | null;
  service: string;
  noTlsVerify: boolean;
}

export interface CreateTemplateRequest {
  slug: string;
  name: string;
  description?: string | null;
  source: {
    gitRepoUrl: string;
    branch: string;
    baseDirectory: string;
    watchPaths: string[];
  };
  build: {
    buildType: BuildType;
    dockerfilePath?: string | null;
    composeFilePath?: string | null;
    buildArgs: TemplateBuildArg[];
  };
}

export interface RotateWebhookSecretResponse {
  webhookSecret: string;
}

/**
 * F11.2 — Request body para `POST /api/templates/discover`. Inspecciona un repo y devuelve
 * que estrategia de build se puede usar (Dockerfile / DockerCompose / Nixpacks).
 */
export interface DiscoverTemplateRequest {
  gitRepoUrl: string;
  branch?: string | null;
}

/**
 * F11.2 — Respuesta del endpoint de discovery. `suggestedBuildType` se aplica al form para
 * prellenar el select; los puertos sugeridos se muestran como hint.
 */
export interface DiscoverTemplateResult {
  detectedLanguages: string[];
  hasDockerfile: boolean;
  hasCompose: boolean;
  hasNixpacksToml: boolean;
  suggestedBuildType: BuildType;
  exposedPorts: number[];
}

export interface ClientSummary {
  id: string;
  projectId: string;
  slug: string;
  displayName: string;
  description: string | null;
  contactEmail: string | null;
  billingTag: string | null;
  createdAt: string;
  updatedAt: string;
  /**
   * El backend (ListClientsQuery) no proyecta este contador en la summary.
   * Se mantiene opcional para las vistas que lo muestren cuando este disponible.
   */
  instanceCount?: number;
}

export interface ClientDetail extends ClientSummary {
  description: string | null;
  contactEmail: string | null;
  billingTag: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateClientRequest {
  slug: string;
  displayName: string;
  description?: string | null;
  contactEmail?: string | null;
  billingTag?: string | null;
}

export type PortProtocol = "Tcp" | "Udp";

export interface InstancePort {
  containerPort: number;
  hostPort: number | null;
  protocol: PortProtocol;
}

export interface InstanceVolume {
  name: string;
  containerPath: string;
  readOnly: boolean;
}

export interface InstanceHealthcheck {
  test: string[];
  intervalSeconds: number;
  retries: number;
  timeoutSeconds: number | null;
  startPeriodSeconds: number | null;
}

export interface InstanceSummary {
  id: string;
  templateId: string;
  clientId: string;
  clientSlug: string;
  slug: string;
  environment: string;
  targetVmId: string;
  containerName: string;
  autoDeployOnNewBuild: boolean;
  autoHostname: string | null;
  customDomain: string | null;
  primaryPort: number | null;
  createdAt: string;
  updatedAt: string;
  /** F12.3 — git ref que esta Instance trackea explicitamente (null = cascade). */
  trackedRef?: string | null;
  /** F12.3 — ref efectivo tras aplicar la cascada Template.EnvironmentMapping → DefaultBranch. */
  effectiveTrackedRef?: string | null;
  /** F12.3 — true si la Instance es ephemeral (PR preview). */
  isEphemeral?: boolean;
  /** F12.3 — UserId Aethra del creador (autor del PR para previews). */
  createdByUserId?: string | null;
}

export interface InstanceDetail extends InstanceSummary {
  ports: InstancePort[];
  volumes: InstanceVolume[];
  healthcheck: InstanceHealthcheck | null;
  createdAt: string;
  updatedAt: string;
  /** F12.3 — expira (para safety net del cleanup background). */
  expiresAt?: string | null;
}

export interface CreateInstanceRequest {
  clientId: string;
  slug: string;
  environment: string;
  targetVmId: string;
  ports: InstancePort[];
  volumes: InstanceVolume[];
  healthcheck?: InstanceHealthcheck | null;
  autoDeployOnNewBuild: boolean;
  customDomain?: string | null;
  /** F12.3 — opcional. Default heredado de la cascada. */
  trackedRef?: string | null;
}

export interface SetCustomDomainRequest {
  customDomain: string | null;
}

export interface SetCustomDomainResponse {
  customDomain: string | null;
}

export interface BuildSummary {
  id: string;
  templateId: string;
  gitSha: string;
  gitRef: string;
  trigger: string;
  status: string;
  createdAt: string;
  finishedAt: string | null;
  imageRef: string | null;
}

export interface BuildDetail extends BuildSummary {
  triggeredBy: string | null;
  startedAt: string | null;
  buildDurationMs: number | null;
  errorCode: string | null;
  errorMessage: string | null;
}

export interface BuildLogChunk {
  seq: number;
  timestamp: string;
  stream: string;
  line: string;
}

export interface DeploymentSummary {
  id: string;
  buildId: string;
  instanceId: string;
  trigger: string;
  status: string;
  createdAt: string;
  finishedAt: string | null;
}

export interface DeploymentDetail extends DeploymentSummary {
  newImageRef: string;
  oldImageRef: string | null;
  newContainerId: string | null;
  oldContainerId: string | null;
  errorCode: string | null;
  errorMessage: string | null;
}

/* -------------------------------------------------------------------------- */
/* Notifications (F11.3A)                                                     */
/* -------------------------------------------------------------------------- */

export type NotificationChannelType =
  | "Slack"
  | "Discord"
  | "Telegram"
  | "Email"
  | "Webhook";

export type NotificationDeliveryStatus = "Pending" | "Sent" | "Failed";

export interface NotificationChannelDto {
  id: string;
  name: string;
  type: NotificationChannelType;
  isActive: boolean;
  eventFilters: string[];
  config: unknown | null;
  createdAt: string;
  updatedAt: string;
  lastDeliveredAt: string | null;
}

export interface NotificationDeliveryDto {
  id: string;
  channelId: string;
  channelName: string;
  eventType: string;
  status: NotificationDeliveryStatus;
  attempts: number;
  error: string | null;
  createdAt: string;
  sentAt: string | null;
}

export interface TestChannelResultDto {
  success: boolean;
  error: string | null;
  attemptedAt: string;
}

export const NOTIFICATION_EVENT_TYPES = [
  "monitor.down",
  "monitor.recovered",
  "build.failed",
  "deployment.failed",
  "deployment.rolled_back",
  "cert.expired",
  "cert.failed",
] as const;

/* -------------------------------------------------------------------------- */
/* Service backups (F11.3B)                                                   */
/* -------------------------------------------------------------------------- */

export type ServiceBackupStatus = "Running" | "Completed" | "Failed";

export interface ServiceBackupDto {
  id: string;
  serviceId: string;
  startedAt: string;
  finishedAt: string | null;
  status: ServiceBackupStatus;
  sizeBytes: number | null;
  destinationPath: string;
  errorMessage: string | null;
}

export interface BackupPolicyDto {
  cronExpression: string;
  retentionCount: number;
  destination: string;
}

/* -------------------------------------------------------------------------- */
/* Scheduled jobs (F12.1A)                                                    */
/* -------------------------------------------------------------------------- */

export type ScheduledJobRunStatus =
  | "Running"
  | "Completed"
  | "Failed"
  | "TimedOut"
  | "Cancelled";

export interface ScheduledJobDto {
  id: string;
  serviceId: string;
  name: string;
  description: string | null;
  command: string;
  cronExpression: string;
  timeZone: string;
  enabled: boolean;
  maxConcurrent: number;
  timeoutSeconds: number;
  lastRunAt: string | null;
  nextRunAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ScheduledJobRunDto {
  id: string;
  jobId: string;
  startedAt: string;
  finishedAt: string | null;
  status: ScheduledJobRunStatus;
  exitCode: number | null;
  stdout: string | null;
  stderr: string | null;
  durationMs: number | null;
}
