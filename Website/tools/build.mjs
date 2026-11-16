import { verifyInstallFeed } from "./verify-install-feed.mjs";
import { createHash } from "node:crypto";
import { cp, mkdir, readFile, writeFile, rm } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { resolve, dirname } from "node:path";
import { additionalPages } from "./pages.mjs";
import { validateRelease } from "../src/releases.mjs";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const release = validateRelease(
  JSON.parse(await readFile(`${root}/data/beta.json`, "utf8")),
);
await verifyInstallFeed(
  JSON.parse(await readFile(`${root}/data/install-beta.json`, "utf8")),
  release,
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
const template = await readFile(`${root}/templates/layout.html`, "utf8");
const cars = JSON.parse(await readFile(`${root}/data/cars.json`, "utf8"));
const radio = JSON.parse(await readFile(`${root}/data/radio.json`, "utf8"));
const content = async (name) =>
  readFile(`${root}/templates/${name}.html`, "utf8");
const heading = (text) =>
  text.replace(/<h2([^>]*)>([\s\S]*?)<\/h2>/, "<h1$1>$2</h1>");
const pages = [
  {
    path: "/",
    title: "Real places. Your adventure.",
    description:
      "Take the long way home. Explore real-world roads in Earth Unscripted: choose a car, find a destination, tune the radio and drive.",
    body: await content("home"),
  },
  {
    path: "/game",
    title: "The game",
    description:
      "Explore real roads, changing scenery, navigation, cars and original radio music in Earth Unscripted.",
    body: heading(await content("game")),
  },
  {
    path: "/download",
    title: "Download the beta",
    description:
      "Download Earth Unscripted for Mac or Windows, with Standard and HD graphics.",
    body: heading(await content("download")),
  },
  {
    path: "/project",
    title: "The project",
    description:
      "Source, documentation, contributions and licences for Earth Unscripted.",
    body: heading(await content("project")),
  },
  ...additionalPages(cars, radio),
  {
    path: "/privacy",
    title: "Privacy",
    description:
      "How this website handles privacy, hosting and external links.",
    body: '<section class="wrap document section"><p class="eyebrow dark">Earth Unscripted</p><h1>Privacy</h1><p>This website has no advertising, tracking pixels or analytics scripts. It does not set cookies or save information in your browser.</p><h2>Hosting</h2><p>Cloudflare serves the website and processes the connection information needed to deliver and protect it, including IP addresses and request logs. See <a href="https://www.cloudflare.com/privacypolicy/">Cloudflare’s privacy policy</a> for its handling of that data.</p><h2>Music previews</h2><p>Audio loads from this website when you choose to play it. Volume and playback position are kept only for the current page visit. The player sends no listening analytics.</p><h2>Downloads and project links</h2><p>Downloads, documentation and issue reports open on GitHub. Signing in and downloading there are covered by <a href="https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement">GitHub’s privacy statement</a>.</p><h2>The game</h2><p>The game fetches map, terrain, weather and optional transport data from external providers. The <a href="https://github.com/wiffair/earth-unscripted/blob/main/Docs/DATA_SERVICES.md">data services guide</a> describes those connections and optional accounts.</p></section>',
  },
  {
    path: "/404",
    title: "A different turning.",
    description: "That page is not here.",
    body: '<section class="wrap document section"><p class="eyebrow dark">Earth Unscripted</p><h1>A different turning.</h1><p>That page isn’t here. Head back to the game, downloads and project links.</p><a class="button dark-button" href="/">Back to the journey ↗</a></section>',
  },
];
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
await cp(
  `${root}/data/radio-preview-provenance.json`,
  `${root}/dist/assets/radio-preview-provenance.json`,
);
const assetNames = new Map();
for (const filename of ["site.css", "site.js", "radio.js"]) {
  const bytes = await readFile(`${root}/dist/assets/${filename}`);
  const dot = filename.lastIndexOf(".");
  const hashed =
    filename.slice(0, dot) +
    "." +
    createHash("sha256").update(bytes).digest("hex").slice(0, 12) +
    filename.slice(dot);
  await writeFile(`${root}/dist/assets/${hashed}`, bytes);
  await rm(`${root}/dist/assets/${filename}`);
  assetNames.set("/assets/" + filename, "/assets/" + hashed);
}
const nav = [
  ["/game", "The game"],
  ["/cars", "Car packs"],
  ["/radio", "Radio"],
  ["/project", "The project"],
  ["/download", "Get the beta"],
];
for (const page of pages) {
  let html = template;
  const pageValues = {
    ...values,
    PAGE_TITLE: escape(page.title),
    DESCRIPTION: escape(page.description),
    PATH: page.path,
    PAGE_CLASS:
      "page-" + (page.path === "/" ? "home" : page.path.split("/")[1]),
    PRELOAD:
      page.path === "/"
        ? '<link rel="preload" as="image" href="/assets/earth-unscripted-cover.png" fetchpriority="high">'
        : "",
    EXTRA_SCRIPT: page.path.startsWith("/radio")
      ? '<script src="/assets/radio.js" defer></script>'
      : "",
    NAVIGATION: nav
      .map(
        ([path, title]) =>
          `<a href="${path}"${path === "/download" ? ' class="nav-download"' : ""}${page.path.startsWith(path) ? ' aria-current="page"' : ""}>${title}${path === "/download" ? ' <span aria-hidden="true">↗</span>' : ""}</a>`,
      )
      .join(""),
    CONTENT: page.body,
  };
  // Content templates may contain release placeholders, so substitute those last.
  for (const key of [
    "CONTENT",
    ...Object.keys(pageValues).filter((k) => k !== "CONTENT"),
  ])
    html = html.replaceAll(`{{${key}}}`, pageValues[key]);
  for (const [original, hashed] of assetNames)
    html = html.replaceAll(original, hashed);
  if (/\{\{[A-Z_]+\}\}/.test(html))
    throw new Error("Unresolved page token: " + page.path);
  const output =
    page.path === "/" ? "index.html" : page.path.slice(1) + ".html";
  await mkdir(dirname(`${root}/dist/${output}`), { recursive: true });
  await writeFile(`${root}/dist/${output}`, html);
}
await writeFile(
  `${root}/dist/sitemap.xml`,
  '<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">' +
    pages
      .filter((p) => p.path !== "/404")
      .map((p) => `<url><loc>https://earthunscripted.com${p.path}</loc></url>`)
      .join("") +
    "</urlset>\n",
);
console.log(
  `Built ${pages.length} pages for ${release.version} (${release.access}).`,
);
