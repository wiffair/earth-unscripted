import { createPublicKey, verify } from "node:crypto";
import { readFile } from "node:fs/promises";

export async function verifyInstallFeed(envelope, release) {
  if (!envelope) return; // Before the first installer release, the route is unavailable.
  const key = JSON.parse(
    await readFile(new URL("../data/update-public-key.json", import.meta.url)),
  );
  const trust = await readFile(
    new URL(
      "../../Unity/Assets/EarthUnscripted/Updates/UpdateTrust.cs",
      import.meta.url,
    ),
    "utf8",
  );
  const expected = (name) =>
    trust.match(new RegExp(name + '=\"([^\"]+)\"'))?.[1];
  const base64url = (value) =>
    value.replace(/=+$/, "").replaceAll("+", "-").replaceAll("/", "_");
  if (
    key.kid !== expected("KeyId") ||
    key.n !== base64url(expected("Modulus") ?? "") ||
    key.e !== base64url(expected("Exponent") ?? "")
  )
    throw new Error("Website signing key differs from the shipped client");
  if (
    envelope.format !== "EarthUnscriptedSignedUpdates" ||
    envelope.schemaVersion !== 1 ||
    envelope.keyId !== key.kid
  )
    throw new Error("Unknown signed update envelope");
  const bytes = Buffer.from(envelope.payload, "base64");
  if (
    bytes.length > 2 * 1024 * 1024 ||
    !verify(
      "RSA-SHA256",
      bytes,
      createPublicKey({ key, format: "jwk" }),
      Buffer.from(envelope.signature, "base64"),
    )
  )
    throw new Error("Invalid update signature");
  const data = JSON.parse(bytes.toString("utf8"));
  const now = Date.now();
  if (
    data.format !== "EarthUnscriptedUpdates" ||
    data.schemaVersion !== 1 ||
    !Number.isSafeInteger(data.sequence) ||
    data.sequence < 1 ||
    Date.parse(data.issuedUtc) > now + 900000 ||
    !(Date.parse(data.expiresUtc) > now)
  )
    throw new Error("Invalid or expired signed update catalogue");
  if (
    data.game.version !== release.version ||
    data.game.channel !== release.channel ||
    data.game.downloads.length !== release.downloads.length
  )
    throw new Error("Signed and manual releases differ");
  for (const item of release.downloads) {
    const signed = data.game.downloads.find((a) => a.target === item.target);
    if (
      !signed ||
      ["url", "bytes", "sha256", "filename"].some((k) => signed[k] !== item[k])
    )
      throw new Error("Signed artifact differs from promoted GitHub artifact");
  }
  return data;
}
