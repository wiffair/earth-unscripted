import { discoveryFeed, validateRelease } from "./releases.mjs";

/** @param {import('./releases.mjs').Release} release @param {object | null} [signedEnvelope] */
export function createHandler(release, signedEnvelope = null) {
  validateRelease(release);
  const discovery = JSON.stringify(discoveryFeed(release), null, 2) + "\n";
  const install = signedEnvelope ? JSON.stringify(signedEnvelope) + "\n" : null;
  /** @param {Request} request @param {{ASSETS: {fetch: (request: Request) => Promise<Response>}}} env */
  return async function fetch(request, env) {
    const url = new URL(request.url);
    if (!["GET", "HEAD"].includes(request.method))
      return new Response(null, {
        status: 405,
        headers: { Allow: "GET, HEAD" },
      });
    const headers = new Headers({
      "X-Content-Type-Options": "nosniff",
      "Referrer-Policy": "strict-origin-when-cross-origin",
      "Cache-Control": "public, max-age=300",
    });
    const jsonPath = `/updates/v1/${release.channel}.json`;
    const installPath = `/updates/install/v1/${release.channel}.json`;
    if (url.pathname === installPath && !install) {
      headers.set("Cache-Control", "no-store");
      return new Response(
        request.method === "HEAD" ? null : "Installer release unavailable",
        { status: 503, headers },
      );
    }
    if (url.pathname === jsonPath || url.pathname === installPath) {
      const feed = url.pathname === installPath ? install : discovery;
      const digest = await crypto.subtle.digest(
        "SHA-256",
        new TextEncoder().encode(feed ?? ""),
      );
      const etag =
        '"' +
        [...new Uint8Array(digest)]
          .map((v) => v.toString(16).padStart(2, "0"))
          .join("") +
        '"';
      headers.set("Content-Type", "application/json; charset=utf-8");
      headers.set("ETag", etag);
      if (
        (request.headers.get("If-None-Match") ?? "")
          .split(",")
          .map((value) => value.trim())
          .some((value) => value === "*" || value.replace(/^W\//, "") === etag)
      )
        return new Response(null, { status: 304, headers });
      return new Response(request.method === "HEAD" ? null : feed, { headers });
    }
    if (url.pathname.startsWith("/downloads/")) {
      const item = release.downloads.find(
        (d) => url.pathname === `/downloads/${release.channel}/${d.target}`,
      );
      if (!item)
        return new Response(
          request.method === "HEAD" ? null : "Download not found",
          { status: 404, headers },
        );
      headers.set("Location", item.url);
      return new Response(null, { status: 302, headers });
    }
    if (url.pathname.startsWith("/updates/"))
      return new Response(
        request.method === "HEAD" ? null : "Release feed not found",
        { status: 404, headers },
      );
    if (url.pathname === "/healthz") {
      headers.set("Content-Type", "application/json");
      return new Response(
        request.method === "HEAD"
          ? null
          : JSON.stringify({ status: "ok", version: release.version }),
        { headers },
      );
    }
    return env.ASSETS.fetch(request);
  };
}
