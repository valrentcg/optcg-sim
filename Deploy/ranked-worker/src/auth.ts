// Verifies a Unity Gaming Services (Unity Authentication) access token so a match
// report can be bound to the REAL authenticated player. This is the crux of the
// "not forgeable" requirement: reporter identity comes from a Unity-signed JWT the
// client cannot mint, not from a shared app secret. A cheater therefore cannot
// submit the opponent's half of a dual-report, so cannot move any rating alone.
//
// UGS player-auth tokens are RS256 JWTs. We fetch the JWKS directly from the
// issuer's well-known endpoint, cache the keys, and verify signature + expiry + issuer.

const UNITY_ISSUER = "https://player-auth.services.api.unity.com";
// Unity's player-auth does NOT serve an OpenID discovery document
// (/.well-known/openid-configuration → 404); it publishes the signing keys
// directly at this JWKS URL. Fetch it straight — going via discovery 404'd and
// made verifyUnityToken throw "discovery 404", 401ing every authenticated call.
const JWKS_URL = `${UNITY_ISSUER}/.well-known/jwks.json`;
const JWKS_TTL_MS = 60 * 60 * 1000; // 1h

interface Jwk { kid: string; kty: string; n: string; e: string; alg?: string; use?: string; }

let jwksCache: { keys: Jwk[]; fetchedAt: number } | null = null;

function b64urlToBytes(s: string): Uint8Array {
  s = s.replace(/-/g, "+").replace(/_/g, "/");
  while (s.length % 4) s += "=";
  const bin = atob(s);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}
function b64urlToJson(s: string): any {
  return JSON.parse(new TextDecoder().decode(b64urlToBytes(s)));
}

async function getJwks(): Promise<Jwk[]> {
  if (jwksCache && Date.now() - jwksCache.fetchedAt < JWKS_TTL_MS) return jwksCache.keys;
  const jwksRes = await fetch(JWKS_URL, { cf: { cacheTtl: 3600 } as any });
  if (!jwksRes.ok) throw new Error(`jwks ${jwksRes.status}`);
  const jwks = await jwksRes.json<{ keys: Jwk[] }>();
  jwksCache = { keys: jwks.keys, fetchedAt: Date.now() };
  return jwks.keys;
}

export interface VerifiedToken { playerId: string; }

/// Verifies `Authorization: Bearer <token>` and returns the player id (`sub`).
/// Throws on any failure — caller maps that to 401.
export async function verifyUnityToken(authHeader: string | null): Promise<VerifiedToken> {
  if (!authHeader || !authHeader.startsWith("Bearer ")) throw new Error("missing bearer");
  const token = authHeader.slice(7).trim();
  const parts = token.split(".");
  if (parts.length !== 3) throw new Error("malformed jwt");

  const header = b64urlToJson(parts[0]);
  const payload = b64urlToJson(parts[1]);
  if (header.alg !== "RS256") throw new Error("unexpected alg");

  // Claims first (cheap) — issuer + expiry, small clock skew allowance.
  const now = Math.floor(Date.now() / 1000);
  if (typeof payload.exp === "number" && payload.exp < now - 30) throw new Error("token expired");
  if (payload.iss && payload.iss !== UNITY_ISSUER) throw new Error("bad issuer");
  if (!payload.sub) throw new Error("no sub");

  const jwks = await getJwks();
  const jwk = jwks.find((k) => k.kid === header.kid) ?? jwks[0];
  if (!jwk) throw new Error("no signing key");

  const key = await crypto.subtle.importKey(
    "jwk",
    { kty: jwk.kty, n: jwk.n, e: jwk.e, alg: "RS256", ext: true },
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    false,
    ["verify"],
  );
  const data = new TextEncoder().encode(`${parts[0]}.${parts[1]}`);
  const sig = b64urlToBytes(parts[2]);
  const ok = await crypto.subtle.verify("RSASSA-PKCS1-v1_5", key, sig, data);
  if (!ok) throw new Error("bad signature");

  return { playerId: String(payload.sub) };
}
