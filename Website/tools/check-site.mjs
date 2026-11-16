import { readdir, readFile, stat } from "node:fs/promises";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
const root = resolve(dirname(fileURLToPath(import.meta.url)), "../dist");
const files = [];
async function walk(folder) {
  for (const name of await readdir(folder)) {
    const path = join(folder, name);
    if ((await stat(path)).isDirectory()) await walk(path);
    else files.push(path);
  }
}
await walk(root);
const known = new Set(files.map((path) => "/" + path.slice(root.length + 1)));
const pages = files.filter((path) => path.endsWith(".html"));
const errors = [];
const routes = new Set([
  "/downloads/beta/macOS-arm64",
  "/downloads/beta/Windows-x64",
]);
for (const page of pages) {
  const html = await readFile(page, "utf8");
  const rel = page.slice(root.length + 1);
  if ((html.match(/<h1\b/g) || []).length !== 1)
    errors.push(`${rel}: expected one main heading`);
  if (/\{\{[A-Z_]+\}\}/.test(html))
    errors.push(`${rel}: unresolved template value`);
  for (const tag of html.match(/<(?:a|img|script|link|audio)\b[^>]*>/g) || []) {
    const match = tag.match(/\b(?:src|href)="([^"]+)"/);
    if (!match) continue;
    const value = match[1];
    if (!value.startsWith("/")) continue;
    const path = value.split(/[?#]/)[0];
    if (
      path === "/" ||
      known.has(path) ||
      known.has(path + ".html") ||
      routes.has(path)
    )
      continue;
    errors.push(`${rel}: missing internal target ${path}`);
  }
}
if (errors.length) throw new Error(errors.join("\n"));
console.log(
  `Checked ${pages.length} pages: one main heading each; every internal page, image, script, audio and download target resolves.`,
);
