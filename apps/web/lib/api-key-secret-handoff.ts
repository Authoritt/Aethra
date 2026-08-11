"use client";

const secrets = new Map<string, string>();

export function handOffApiKeySecret(id: string, secret: string) {
  secrets.set(id, secret);
}

export function consumeApiKeySecret(id: string): string | null {
  const secret = secrets.get(id) ?? null;
  secrets.delete(id);
  return secret;
}

export function clearApiKeySecret(id: string) {
  secrets.delete(id);
}
