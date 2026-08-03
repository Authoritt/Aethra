#!/usr/bin/env node
/**
 * Busca texto de UI en español escrito a pelo en el front, es decir strings que se
 * renderizan sin pasar por next-intl y que por tanto salen en español aunque el
 * usuario tenga la interfaz en inglés.
 *
 * Existe porque `grep` no sirve para esto y falló dos veces seguidas: la primera
 * dejó pasar el botón "Registrar machine" del empty state (misma página, otra
 * indentación) y la segunda un "No se pudo cargar la vista operacional de
 * machines." que era texto JSX suelto. En ambos casos el patrón decía limpio,
 * `tsc` pasaba, y la página seguía en español. Un chequeo cuyo modo de fallo es
 * el silencio necesita ser mecánico, no una expresión regular escrita a ojo cada
 * vez.
 *
 * Detecta por dos señales:
 *   - caracteres que sólo existen en español (acentos, ñ, ¿, ¡)
 *   - palabras funcionales españolas como palabra suelta (la, del, para, sin...)
 *
 * Los comentarios en español se permiten a propósito: son la convención del repo.
 *
 *   node scripts/detect-untranslated.mjs            # toda la consola
 *   node scripts/detect-untranslated.mjs <archivos> # sólo esos
 *
 * Sale con código 1 si encuentra algo, para poder usarlo como gate en CI.
 */
import fs from "node:fs";
import path from "node:path";

const ROOT = path.resolve(import.meta.dirname, "..", "apps", "web");
const STOPWORDS =
  /\b(la|el|los|las|una|unos|unas|del|para|con|sin|que|desde|hasta|sus|este|esta|estos|estas|cada|hay|por|pertenecen|todavía|aquí)\b/i;
const SPANISH_CHARS = /[áéíóúñÁÉÍÓÚÑ¿¡]/;
const TEXT_ATTRS = /(title|description|label|allLabel|placeholder|emptyLabel|alt)="([^"]+)"/;

function walk(dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (["node_modules", ".next", "ui"].includes(entry.name)) continue;
      walk(full, out);
    } else if (entry.name.endsWith(".tsx")) {
      out.push(full);
    }
  }
  return out;
}

function scan(file) {
  const hits = [];
  fs.readFileSync(file, "utf8")
    .split("\n")
    .forEach((line, index) => {
      // Los comentarios en español son la convención del repo, no un defecto.
      if (/^\s*(\/\/|\*|\/\*)/.test(line)) return;
      // Rutas, clases e imports arrastran falsos positivos.
      if (/import |from "|className=|href=|storageKey=/.test(line)) return;

      const looseText = line.match(/^\s*([A-ZÁÉÍÓÚ][^<>{}]*?)\s*$/)?.[1];
      const attrText = line.match(TEXT_ATTRS)?.[2];
      const candidate = looseText ?? attrText;
      if (!candidate) return;

      if (SPANISH_CHARS.test(candidate) || STOPWORDS.test(candidate)) {
        hits.push({ line: index + 1, text: candidate.slice(0, 100) });
      }
    });
  return hits;
}

const files = process.argv.length > 2 ? process.argv.slice(2) : walk(path.join(ROOT, "app")).concat(walk(path.join(ROOT, "components")));

let total = 0;
for (const file of files) {
  const hits = scan(file);
  if (!hits.length) continue;
  total += hits.length;
  console.log(`\n${path.relative(ROOT, file)}`);
  for (const hit of hits) console.log(`  ${hit.line}: ${hit.text}`);
}

if (total === 0) {
  console.log("Sin texto de UI en español fuera de los catálogos.");
  process.exit(0);
}
console.log(`\n${total} literales en español. Muévelos a messages/{en,es}.json y usa next-intl.`);
process.exit(1);
