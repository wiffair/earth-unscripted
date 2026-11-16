export const repository = "wiffair/earth-unscripted";
const github = `https://github.com/${repository}`;
const appVersion =
  /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-beta\.([1-9]\d*))?$/;
const targets = ["macOS-arm64", "Windows-x64"];

/** @typedef {{target: string, filename: string, url: string, bytes: number, sha256: string, minimumOS: string}} Download */
/** @typedef {{schemaVersion: number, product: string, channel: string, version: string, tag: string, publishedAt: string, notesUrl: string, access: string, automaticInstall: boolean, downloads: Download[]}} Release */

/** Validate the promoted release before it can become a download or feed. @param {Release} release */
export function validateRelease(release) {
  if (release.schemaVersion !== 1 || release.product !== "earth-unscripted")
    throw new Error("Unknown release schema/product");
  if (
    !appVersion.test(release.version) ||
    release.tag !== `v${release.version}`
  )
    throw new Error("Invalid game version/tag");
  const channel = release.version.includes("-beta.") ? "beta" : "stable";
  if (release.channel !== channel)
    throw new Error("Channel does not match version");
  if (!Number.isFinite(Date.parse(release.publishedAt)))
    throw new Error("Invalid publication date");
  if (release.notesUrl !== `${github}/releases/tag/${release.tag}`)
    throw new Error("Unexpected release notes host");
  if (!["github-private", "public"].includes(release.access))
    throw new Error("Unknown access policy");
  // This catalogue is discovery metadata, never authority to install executable code.
  if (release.automaticInstall !== false)
    throw new Error("Signed installer feed is not enabled");
  if (release.downloads.length !== targets.length)
    throw new Error("Both desktop targets are required");
  for (const target of targets) {
    const matches = release.downloads.filter((item) => item.target === target);
    if (matches.length !== 1)
      throw new Error(`Missing/duplicate target: ${target}`);
    const item = matches[0];
    const expected = `EarthUnscripted-${release.version}-${target}.zip`;
    if (
      item.filename !== expected ||
      item.url !== `${github}/releases/download/${release.tag}/${expected}`
    )
      throw new Error("Unapproved download URL");
    if (
      !/^[a-f0-9]{64}$/.test(item.sha256) ||
      !Number.isSafeInteger(item.bytes) ||
      item.bytes <= 0
    )
      throw new Error("Missing artifact integrity");
    if (!item.minimumOS) throw new Error("Missing minimum OS");
  }
  return release;
}

/** @param {Release} release */
export function discoveryFeed(release) {
  validateRelease(release);
  return {
    ...release,
    feedType: "release-discovery",
    requiresAuthentication: release.access === "github-private",
    renderers: ["standard", "hd"],
    signature: null,
    installation: "manual",
  };
}

/** Convert GitHub's API model; pack releases and drafts cannot become game updates.
 * @param {any} source @param {boolean} isPrivate */
export function fromGitHubRelease(source, isPrivate) {
  const version = String(source.tag_name ?? "").replace(/^v/, "");
  if (
    source.draft ||
    !source.tag_name?.startsWith("v") ||
    !appVersion.test(version) ||
    !source.published_at
  )
    throw new Error("Not a published game release");
  const downloads = targets.map((target) => {
    const filename = `EarthUnscripted-${version}-${target}.zip`;
    const matches =
      source.assets?.filter(
        (/** @type {any} */ item) => item.name === filename,
      ) ?? [];
    if (matches.length !== 1 || matches[0].state !== "uploaded")
      throw new Error(`Missing uploaded artifact: ${filename}`);
    const item = matches[0];
    return {
      target,
      filename,
      url: item.browser_download_url,
      bytes: item.size,
      sha256: String(item.digest ?? "").replace(/^sha256:/, ""),
      minimumOS:
        target === "macOS-arm64"
          ? "macOS 15; Apple silicon"
          : "Windows 10/11; x64",
    };
  });
  return validateRelease({
    schemaVersion: 1,
    product: "earth-unscripted",
    channel: version.includes("-beta.") ? "beta" : "stable",
    version,
    tag: source.tag_name,
    publishedAt: source.published_at,
    notesUrl: source.html_url,
    access: isPrivate ? "github-private" : "public",
    automaticInstall: false,
    downloads,
  });
}
