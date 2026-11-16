import { execFileSync } from "node:child_process";
import { writeFile } from "node:fs/promises";
import { fromGitHubRelease, repository } from "../src/releases.mjs";

// Deliberately require a specific game tag: the newest GitHub release may be a pack.
const args = process.argv.slice(2);
if (args.length !== 2 || args[0] !== "--tag")
  throw new Error("Usage: node tools/promote-release.mjs --tag v0.24.0-beta.6");
const gh = process.env.GH_BINARY || "gh";
const api = (path) =>
  JSON.parse(
    execFileSync(gh, ["api", path], {
      encoding: "utf8",
      maxBuffer: 2 * 1024 * 1024,
    }),
  );
const tag = args[1];
if (!/^v\d+\.\d+\.\d+-beta\.[1-9]\d*$/.test(tag))
  throw new Error("A specific beta game tag is required");
const source = api(`repos/${repository}/releases/tags/${tag}`);
const repo = api(`repos/${repository}`);
const release = fromGitHubRelease(source, repo.private);
// Cross-check GitHub asset digests against the maintainer's release checksum file.
const sums = execFileSync(
  gh,
  [
    "release",
    "download",
    tag,
    "--repo",
    repository,
    "--pattern",
    "SHA256SUMS.txt",
    "--output",
    "-",
  ],
  { encoding: "utf8", maxBuffer: 64 * 1024 },
);
for (const item of release.downloads) {
  const found = sums.split("\n").some((line) => {
    const match = line.trim().match(/^([a-f0-9]{64})\s+\*?(.+)$/);
    return match?.[1] === item.sha256 && match[2] === item.filename;
  });
  if (!found)
    throw new Error(`Release checksums disagree for ${item.filename}`);
}
await writeFile(
  new URL("../data/beta.json", import.meta.url),
  JSON.stringify(release, null, 2) + "\n",
);
console.log(
  `Prepared ${tag}. Review data/beta.json and release checks before committing/deploying.`,
);
