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
/**
 * Palabras de UI en español que no llevan acento y no son stopwords, así que las
 * dos señales de arriba no las ven. Se añadió después de que la primera versión
 * de este script diera 7 hallazgos en cuatro páginas que tenían además "Buscar",
 * "Filtrar", "Limpiar", "Todos" y "Todas" — el detector escrito para arreglar el
 * grep repitió el fallo del grep, a menor escala.
 *
 * Sólo van términos sin colisión con inglés: "Ver" sí, "No" no.
 */
const SPANISH_UI_WORDS =
  /\b(Buscar|Filtrar|Limpiar|Guardar|Crear|Editar|Eliminar|Borrar|Cerrar|Abrir|Enviar|Cancelar|Anadir|Todos|Todas|Ninguno|Ninguna|Ver|Volver|Siguiente|Anterior|Ambiente|Ambientes|Maquina|Maquinas|Servicios|Usuario|Contrasena|Correo|Nombre|Descripcion|Estado|Actualizar|Registrar|Desplegar|Configurar|codigo|tecnicas|tecnicos|publicos|publicas)\b/;
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

      const candidates = [];
      // Texto JSX suelto: <p>Sin datos</p> partido en varias líneas.
      const looseText = line.match(/^\s*([A-ZÁÉÍÓÚ][^<>{}]*?)\s*$/)?.[1];
      if (looseText) candidates.push(looseText);
      // Atributo de texto visible en una sola línea.
      const attrText = line.match(TEXT_ATTRS)?.[2];
      if (attrText) candidates.push(attrText);
      // Literales dentro de expresiones JSX: description={cond ? "..." : "..."}.
      // Se añadió después de que el detector diera limpia una página cuyo empty
      // state decía "Cuando existan servicios gestionados apareceran con sus
      // consumidores." — la cadena vivía en un ternario, así que no era ni texto
      // suelto ni atributo de una línea, y las dos reglas anteriores no la veían.
      for (const m of line.matchAll(/"([^"]{6,})"/g)) candidates.push(m[1]);

      for (const candidate of candidates) {
        if (SPANISH_CHARS.test(candidate) || STOPWORDS.test(candidate) || SPANISH_UI_WORDS.test(candidate)) {
          hits.push({ line: index + 1, text: candidate.slice(0, 100) });
          return;
        }
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
