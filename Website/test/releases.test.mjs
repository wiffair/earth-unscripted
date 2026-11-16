import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import {
  validateRelease,
  discoveryFeed,
  fromGitHubRelease,
} from "../src/releases.mjs";
import { createHandler } from "../src/routes.mjs";
const release = JSON.parse(
  await readFile(new URL("../data/beta.json", import.meta.url)),
);
const copy = () => structuredClone(release);
const asset = (item) => ({
  name: item.filename,
  size: item.bytes,
  digest: `sha256:${item.sha256}`,
  state: "uploaded",
  browser_download_url: item.url,
});
const upstream = () => ({
  tag_name: release.tag,
  draft: false,
  published_at: release.publishedAt,
  html_url: release.notesUrl,
  assets: release.downloads.map(asset),
});

test("promotes the exact paired game artifacts, ignoring extra pack assets", () => {
  const source = upstream();
  source.assets.push({ name: "VW-Splittie-2.0.0.eucar" });
  assert.deepEqual(
    fromGitHubRelease(source, release.access === "github-private"),
    release,
  );
});
test("a newer car-pack release is never a game update", () => {
  const source = upstream();
  source.tag_name = "vw-splittie-v2.0.0";
  assert.throws(
    () => fromGitHubRelease(source, true),
    /published game release/,
  );
});
test("incomplete, duplicate, draft and unhashed releases cannot be promoted", () => {
  for (const change of [
    (s) => (s.draft = true),
    (s) => s.assets.pop(),
    (s) => s.assets.push(s.assets[0]),
    (s) => (s.assets[0].digest = null),
    (s) => (s.assets[0].state = "new"),
  ]) {
    const s = upstream();
    change(s);
    assert.throws(() => fromGitHubRelease(s, true));
  }
});
test("URL substitution, channel confusion and automatic installation fail closed", () => {
  for (const change of [
    (s) => (s.downloads[0].url = "https://example.org/malware.zip"),
    (s) => (s.channel = "stable"),
    (s) => (s.automaticInstall = true),
    (s) => (s.downloads[0].bytes = -1),
    (s) => (s.downloads[0].sha256 = "x"),
  ]) {
    const s = copy();
    change(s);
    assert.throws(() => validateRelease(s));
  }
});
test("private release discovery preserves authentication and manual-only installation", () => {
  const feed = discoveryFeed(release);
  assert.equal(
    feed.requiresAuthentication,
    release.access === "github-private",
  );
  assert.equal(feed.automaticInstall, false);
  assert.equal(feed.installation, "manual");
  assert.equal(feed.signature, null);
  assert.deepEqual(feed.renderers, ["standard", "hd"]);
});
const handler = createHandler(release);
const env = {
  ASSETS: { fetch: async () => new Response("static page", { status: 404 }) },
};
const request = (path, init) =>
  new Request(`https://earthunscripted.com${path}`, init);
test("feed supports conditional requests and HEAD without returning a document", async () => {
  const result = await handler(request("/updates/v1/beta.json"), env);
  assert.equal(result.status, 200);
  assert.equal((await result.json()).version, release.version);
  const etag = result.headers.get("ETag");
  assert.ok(etag);
  const unchanged = await handler(
    request("/updates/v1/beta.json", {
      headers: { "If-None-Match": `"old", W/${etag}` },
    }),
    env,
  );
  assert.equal(unchanged.status, 304);
  assert.equal(await unchanged.text(), "");
  const head = await handler(
    request("/updates/v1/beta.json", { method: "HEAD" }),
    env,
  );
  assert.equal(head.headers.get("ETag"), etag);
  assert.equal(await head.text(), "");
});
test("download redirects use only promoted URLs and ignore redirect query parameters", async () => {
  const result = await handler(
    request("/downloads/beta/macOS-arm64?url=https://example.org"),
    env,
  );
  assert.equal(result.status, 302);
  assert.equal(result.headers.get("Location"), release.downloads[0].url);
  for (const path of [
    "/downloads/stable/macOS-arm64",
    "/downloads/beta/Linux",
    "/updates/v1/stable.json",
  ])
    assert.equal((await handler(request(path), env)).status, 404);
});
test("writes are rejected and ordinary pages use the asset binding", async () => {
  assert.equal(
    (await handler(request("/updates/v1/beta.json", { method: "POST" }), env))
      .status,
    405,
  );
  const result = await handler(request("/missing"), env);
  assert.equal(result.status, 404);
  assert.equal(await result.text(), "static page");
});

// A separate signed route must never fall back to unsigned discovery metadata.
test("install feed is unavailable until a signed catalogue is promoted", async () => {
  const result = await handler(request("/updates/install/v1/beta.json"), env);
  assert.equal(result.status, 503);
  assert.equal(result.headers.get("Cache-Control"), "no-store");
});
test("signed envelope is served unchanged with HEAD and conditional requests", async () => {
  const envelope = {
    format: "test fixture",
    payload: "unchanged",
    signature: "test",
  };
  const signed = createHandler(release, envelope);
  const result = await signed(request("/updates/install/v1/beta.json"), env);
  assert.deepEqual(await result.json(), envelope);
  const etag = result.headers.get("ETag");
  assert.equal(
    (
      await signed(
        request("/updates/install/v1/beta.json", {
          headers: { "If-None-Match": etag },
        }),
        env,
      )
    ).status,
    304,
  );
  assert.equal(
    await (
      await signed(
        request("/updates/install/v1/beta.json", { method: "HEAD" }),
        env,
      )
    ).text(),
    "",
  );
});

test("promoted install catalogue has a valid client-pinned signature", async () => {
  const { verifyInstallFeed } =
    await import("../tools/verify-install-feed.mjs");
  const envelope = JSON.parse(
    await readFile(new URL("../data/install-beta.json", import.meta.url)),
  );
  if (!envelope) return;
  const payload = await verifyInstallFeed(envelope, release);
  assert.equal(payload.game.version, release.version);
  const tampered = structuredClone(envelope);
  tampered.payload = Buffer.from(
    Buffer.from(tampered.payload, "base64").toString("utf8") + " ",
  ).toString("base64");
  await assert.rejects(verifyInstallFeed(tampered, release), /signature/);
  const wrongRelease = copy();
  wrongRelease.downloads[0].sha256 = "0".repeat(64);
  await assert.rejects(verifyInstallFeed(envelope, wrongRelease), /artifact/);
});
