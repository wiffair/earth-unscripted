import { cp, mkdir, readFile, writeFile, rm } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { resolve, dirname } from "node:path";
import { validateRelease } from "../src/releases.mjs";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const release = validateRelease(
  JSON.parse(await readFile(`${root}/data/beta.json`, "utf8")),
);
const escape = (value) =>
  String(value).replace(
    /[&<>"']/g,
    (char) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        char
      ],
  );
const privateBeta = release.access === "github-private";
const cards = release.downloads
  .map((item) => {
    const mac = item.target === "macOS-arm64";
    const symbol = mac
      ? '<path d="M8 5h8v10H8zM5 18h14v2H5zM6 3h12v14H6z" fill-rule="evenodd"/>'
      : '<path d="M2 4.5 11 3v8H2zm10.5-1.7L22 1v10h-9.5zM2 12.5h9v8L2 19zm10.5 0H22V23l-9.5-1.8z"/>';
    return `<article class="download-card"><div class="platform-line"><svg viewBox="0 0 24 24" aria-hidden="true">${symbol}</svg><h3>${mac ? "For Mac" : "For Windows"}</h3></div><p>${escape(item.minimumOS)} · Standard + HD included</p><a class="button" href="/downloads/beta/${item.target}">Download for ${mac ? "Mac" : "Windows"} <span aria-hidden="true">↓</span></a><span class="file-info">ZIP · ${(item.bytes / 1e6).toFixed(0)} MB${privateBeta ? " · GitHub access required" : ""}</span><details><summary>Verify your download (SHA-256)</summary><code>${item.sha256}</code></details></article>`;
  })
  .join("\n");
const values = {
  VERSION: escape(release.version),
  RELEASE_DATE: new Intl.DateTimeFormat("en-GB", {
    day: "numeric",
    month: "long",
    year: "numeric",
    timeZone: "UTC",
  }).format(new Date(release.publishedAt)),
  RELEASE_URL: escape(release.notesUrl),
  YEAR: String(new Date().getUTCFullYear()),
  ACCESS_BADGE: privateBeta ? "Private beta" : "Public beta",
  ACCESS_NOTE: privateBeta
    ? '<strong>Testing with us?</strong> Sign in to GitHub with an invited account before downloading. The beta is currently available to collaborators. <a href="https://github.com/login?return_to=%2Fwiffair%2Fearth-unscripted">Sign in to GitHub ↗</a>'
    : "<strong>Ready to explore?</strong> Choose the download for your computer below.",
  DOWNLOAD_CARDS: cards,
};
let html = await readFile(`${root}/templates/index.html`, "utf8");
for (const [key, value] of Object.entries(values))
  html = html.replaceAll(`{{${key}}}`, value);
if (/\{\{[A-Z_]+\}\}/.test(html)) throw new Error("Unresolved page token");
await rm(`${root}/dist`, { recursive: true, force: true });
await mkdir(`${root}/dist`, { recursive: true });
await cp(`${root}/public`, `${root}/dist`, { recursive: true });
await cp(
  `${root}/../Brand/splash.png`,
  `${root}/dist/assets/earth-unscripted-cover.png`,
);
await cp(
  `${root}/../Brand/icon.png`,
  `${root}/dist/assets/earth-unscripted-icon.png`,
);
await writeFile(`${root}/dist/index.html`, html);
const shell = (title, content) =>
  `<!doctype html><html lang="en-GB"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>${title} — Earth Unscripted</title><meta name="theme-color" content="#102b2c"><link rel="icon" href="/assets/favicon.svg"><link rel="stylesheet" href="/assets/site.css"></head><body><header class="site-header"><a class="wordmark" href="/"><img src="/assets/favicon.svg" width="38" height="38" alt=""><span>EARTH <b>UNSCRIPTED</b></span></a><nav aria-label="Main navigation"><a class="nav-download" href="/#download">Get the beta ↗</a></nav></header><main class="wrap document section"><p class="eyebrow dark">Earth Unscripted</p><h1>${title}</h1>${content}<a class="button dark-button" href="/">Back to the journey ↗</a></main><footer class="site-footer wrap"><p class="footer-small">© ${values.YEAR} Earth Unscripted</p></footer></body></html>`;
await writeFile(
  `${root}/dist/privacy.html`,
  shell(
    "Privacy",
    '<p>This website has no advertising, tracking pixels or analytics scripts. It does not set cookies or save information in your browser.</p><h2>Hosting</h2><p>Cloudflare serves the website and processes the connection information needed to deliver and protect it, including IP addresses and request logs. See <a href="https://www.cloudflare.com/privacypolicy/">Cloudflare’s privacy policy</a> for its handling of that data.</p><h2>Downloads and project links</h2><p>Downloads, documentation and issue reports open on GitHub. Signing in and downloading there are covered by <a href="https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement">GitHub’s privacy statement</a>.</p><h2>The game</h2><p>The game fetches map, terrain, weather and optional transport data from external providers. The project’s <a href="https://github.com/wiffair/earth-unscripted/blob/main/Docs/DATA_SERVICES.md">data services guide</a> describes those connections and optional accounts.</p>',
  ),
);
await writeFile(
  `${root}/dist/404.html`,
  shell(
    "A different turning.",
    "<p>That page isn’t here. Head back to the game, downloads and project links.</p>",
  ),
);
console.log(
  `Built website for ${release.version} (${release.access}); ${html.length} HTML characters.`,
);
