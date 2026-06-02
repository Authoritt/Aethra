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
  created_at: string;
  updated_at: string;
}

export interface ProjectDetail {
  id: string;
  slug: string;
  name: string;
  description: string | null;
  color: string | null;
  icon: string | null;
  created_at: string;
  updated_at: string;
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
  public_ip: string | null;
  private_ip: string | null;
  description: string | null;
  status: VmStatus;
  created_at: string;
  updated_at: string;
  last_connected_at: string | null;
  last_disconnected_at: string | null;
  hostname: string | null;
  kernel_version: string | null;
  cpu_model: string | null;
  cpu_cores: number | null;
  total_memory_bytes: number | null;
  agent_version: string | null;
}

export interface RegisterVmRequest {
  name: string;
  slug?: string;
  public_ip?: string;
  private_ip?: string;
  description?: string;
}

export interface RegisterVmResponse {
  vm_id: string;
  slug: string;
  name: string;
  token_plaintext: string;
  install_script: string;
}

export interface VmMetricPoint {
  timestamp: string;
  cpu_percent: number;
  memory_used_bytes: number;
  memory_total_bytes: number;
  net_bytes_received: number;
  net_bytes_sent: number;
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
  backend_url: string;
  tls_enabled: boolean;
  cert_status: CertStatus;
  cert_expires_at: string | null;
  created_at: string;
  updated_at: string;
}

export interface CreateRouteRequest {
  hostname: string;
  backend_url: string;
  tls_enabled: boolean;
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
  target_vm_id: string;
  container_name: string;
  bindings_count: number;
}

export interface ManagedServiceDetailDto extends ManagedServiceSummaryDto {
  image: string;
  internal_port: number;
  network_name: string;
  exposed_externally: boolean;
  provisioned_at: string | null;
  error_code: string | null;
  error_message: string | null;
  created_at: string;
}

export interface ServiceTemplateDto {
  id: string;
  display_name: string;
  type: ManagedServiceType;
  version: string;
  image: string;
  internal_port: number;
  notes: string;
}

export type BindingPermissions = "Owner" | "ReadWrite" | "ReadOnly";

export interface ServiceBindingDto {
  id: string;
  service_id: string;
  application_id: string;
  application_slug?: string;
  resource_name: string;
  permissions: BindingPermissions;
  env_var_prefix: string;
  has_migrations_hook: boolean;
  provisioned_at: string | null;
  revoked_at: string | null;
}

export interface CreateServiceRequest {
  template_id: string;
  slug: string;
  name: string;
  target_vm_id: string;
  exposed_externally?: boolean;
}

export interface CreateBindingRequest {
  application_id: string;
  resource_name?: string;
  permissions: BindingPermissions;
  env_var_prefix?: string;
  migrations_hook?: {
    command: string;
    timeout_seconds: number;
    fail_on_error: boolean;
    run_on: "binding_create" | "deploy" | "manual";
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
  external_zone_id: string;
  name: string;
  status: CloudflareZoneStatus;
  account_id: string;
  records_count: number;
  created_at: string;
  updated_at: string;
  last_synced_at: string | null;
}

export interface DnsRecordDto {
  id: string;
  zone_id: string;
  external_record_id: string | null;
  type: DnsRecordType;
  name: string;
  content: string;
  ttl: number;
  proxied: boolean;
  comment: string | null;
  created_at: string;
  updated_at: string;
  synced_at: string | null;
  last_error: string | null;
}

export interface CloudflareZoneDetailDto {
  id: string;
  external_zone_id: string;
  name: string;
  status: CloudflareZoneStatus;
  account_id: string;
  created_at: string;
  updated_at: string;
  last_synced_at: string | null;
  records: DnsRecordDto[];
}

export interface RegisterCloudflareZoneRequest {
  zone_id: string;
  api_token: string;
}

export interface RotateCloudflareTokenRequest {
  api_token: string;
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
  http_method: MonitorHttpMethod;
  interval_sec: number;
  timeout_ms: number;
  status: MonitorStatus;
  is_enabled: boolean;
  last_checked_at: string | null;
  consecutive_failures: number;
  application_id: string | null;
  project_id: string | null;
}

export interface MonitorDetailDto {
  id: string;
  slug: string;
  name: string;
  url: string;
  http_method: MonitorHttpMethod;
  expected_status_codes: number[];
  interval_sec: number;
  timeout_ms: number;
  headers: Record<string, string> | null;
  body_template: string | null;
  application_id: string | null;
  project_id: string | null;
  is_enabled: boolean;
  status: MonitorStatus;
  last_checked_at: string | null;
  consecutive_failures: number;
  created_at: string;
  updated_at: string;
}

export interface MonitorCheckDto {
  id: string;
  monitor_id: string;
  timestamp: string;
  status: MonitorStatus;
  http_status_code: number | null;
  latency_ms: number | null;
  error_message: string | null;
  response_snippet: string | null;
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
  http_method?: MonitorHttpMethod;
  expected_status_codes?: number[];
  interval_sec?: number;
  timeout_ms?: number;
  headers?: Record<string, string>;
  body_template?: string;
  application_id?: string;
  project_id?: string;
}

export interface UpdateMonitorRequest {
  name?: string;
  url?: string;
  http_method?: MonitorHttpMethod;
  expected_status_codes?: number[];
  interval_sec?: number;
  timeout_ms?: number;
  headers?: Record<string, string>;
  clear_headers?: boolean;
  body_template?: string;
  clear_body_template?: boolean;
  application_id?: string;
  clear_application_id?: boolean;
  project_id?: string;
  clear_project_id?: boolean;
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
  key_prefix: string;
  scopes: string[];
  created_at: string;
  last_used_at?: string | null;
  expires_at?: string | null;
  revoked_at?: string | null;
}

export interface CreateApiKeyResult extends ApiKeySummary {
  secret: string;
}

export interface CreateApiKeyRequest {
  name: string;
  scopes: string[];
  expires_at?: string | null;
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

export interface TemplateDetail extends TemplateSummary {
  description: string | null;
  source: {
    gitRepoUrl: string;
    branch: string;
    baseDirectory: string;
    watchPaths: string[];
  };
  build: {
    buildType: BuildType;
    dockerfilePath: string | null;
    composeFilePath: string | null;
    buildArgs: TemplateBuildArg[];
  };
  webhookSecret?: string;
  createdAt: string;
  updatedAt: string;
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
}

export interface InstanceDetail extends InstanceSummary {
  ports: InstancePort[];
  volumes: InstanceVolume[];
  healthcheck: InstanceHealthcheck | null;
  createdAt: string;
  updatedAt: string;
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
