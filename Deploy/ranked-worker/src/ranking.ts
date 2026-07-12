// Authoritative ranked math — the server-side twin of the Unity client's
// Glicko2.cs + RankedStore.cs. This is now the SOURCE OF TRUTH: the client only
// reports results and reads standings; every rating/bounty change is computed
// here where a modded client can't touch it.
//
// Kept a faithful, boring port of the C# so the two never drift. If you change a
// constant here, change it in RankedStore.cs too (and vice-versa).

// ─────────────────────────────── Glicko-2 ───────────────────────────────

const G_SCALE = 173.7178;
const G_TAU = 0.5;
const G_MIN_RD = 30.0;
const G_MAX_RD = 350.0;
export const G_DEFAULT_RATING = 1500.0;
export const G_DEFAULT_RD = 350.0;
export const G_DEFAULT_VOL = 0.06;

const clampD = (x: number, lo: number, hi: number) => (x < lo ? lo : x > hi ? hi : x);

function volF(x: number, delta2: number, phi2: number, v: number, a: number): number {
  const ex = Math.exp(x);
  const num = ex * (delta2 - phi2 - v - ex);
  const den = 2.0 * (phi2 + v + ex) * (phi2 + v + ex);
  return num / den - (x - a) / (G_TAU * G_TAU);
}

export interface GlickoResult { rating: number; rd: number; volatility: number; }

// One-game update; score = 1 win / 0 loss / 0.5 draw. Human 1500/350 scale in and out.
export function glickoUpdate(
  rating: number, rd: number, vol: number,
  oppRating: number, oppRd: number, score: number,
): GlickoResult {
  const mu = (rating - 1500.0) / G_SCALE;
  const phi = clampD(rd, G_MIN_RD, G_MAX_RD) / G_SCALE;
  const muJ = (oppRating - 1500.0) / G_SCALE;
  const phiJ = clampD(oppRd, G_MIN_RD, G_MAX_RD) / G_SCALE;

  const g = 1.0 / Math.sqrt(1.0 + (3.0 * phiJ * phiJ) / (Math.PI * Math.PI));
  const e = 1.0 / (1.0 + Math.exp(-g * (mu - muJ)));
  const v = 1.0 / (g * g * e * (1.0 - e));
  const delta = v * g * (score - e);

  const sigma = vol <= 0 ? G_DEFAULT_VOL : vol;
  const a = Math.log(sigma * sigma);
  const phi2 = phi * phi;
  const delta2 = delta * delta;

  let A = a;
  let B: number;
  if (delta2 > phi2 + v) {
    B = Math.log(delta2 - phi2 - v);
  } else {
    let k = 1.0;
    while (volF(a - k * G_TAU, delta2, phi2, v, a) < 0.0) k += 1.0;
    B = a - k * G_TAU;
  }

  let fA = volF(A, delta2, phi2, v, a);
  let fB = volF(B, delta2, phi2, v, a);
  let guard = 0;
  while (Math.abs(B - A) > 1e-6 && guard++ < 100) {
    const C = A + ((A - B) * fA) / (fB - fA);
    const fC = volF(C, delta2, phi2, v, a);
    if (fC * fB <= 0.0) { A = B; fA = fB; } else { fA *= 0.5; }
    B = C; fB = fC;
  }
  const newVol = Math.exp(A / 2.0);

  const phiStar = Math.sqrt(phi2 + newVol * newVol);
  const newPhi = 1.0 / Math.sqrt(1.0 / (phiStar * phiStar) + 1.0 / v);
  const newMu = mu + newPhi * newPhi * g * (score - e);

  return {
    rating: newMu * G_SCALE + 1500.0,
    rd: clampD(newPhi * G_SCALE, G_MIN_RD, G_MAX_RD),
    volatility: newVol,
  };
}

// ──────────────────────────── Bounty ladder ────────────────────────────

export interface Tier {
  name: string;
  floor: number;
  ceil: number;
  baseGain: number;
  rampagePeak: number;
  vivreActive: boolean;
  vivreLosses: number;
  mmrThreshold: number;
}

export const BOUNTY_CAP = 5_564_800_000;
const PLACEMENT_GAMES = 5;
const LOSS_FACTOR = 0.8;
const GAP_K = 0.25;
const GAP_CLAMP = 2;
const RAMPAGE_STREAK = 3;
const MAX_PLACEMENT_TIER = 3;

export const TIERS: Tier[] = [
  { name: "Apprentice",       floor: 0,             ceil: 15_000_000,    baseGain: 600_000,    rampagePeak: 0.60, vivreActive: true,  vivreLosses: 2, mmrThreshold: -Infinity },
  { name: "Rookie",           floor: 15_000_000,    ceil: 50_000_000,    baseGain: 1_500_000,  rampagePeak: 0.55, vivreActive: true,  vivreLosses: 2, mmrThreshold: 1150 },
  { name: "Notorious",        floor: 50_000_000,    ceil: 100_000_000,   baseGain: 3_000_000,  rampagePeak: 0.45, vivreActive: true,  vivreLosses: 3, mmrThreshold: 1280 },
  { name: "Supernova",        floor: 100_000_000,   ceil: 300_000_000,   baseGain: 8_000_000,  rampagePeak: 0.35, vivreActive: true,  vivreLosses: 4, mmrThreshold: 1410 },
  { name: "New World Pirate", floor: 300_000_000,   ceil: 700_000_000,   baseGain: 16_000_000, rampagePeak: 0.25, vivreActive: false, vivreLosses: 0, mmrThreshold: 1540 },
  { name: "Warlord",          floor: 700_000_000,   ceil: 1_500_000_000, baseGain: 32_000_000, rampagePeak: 0.15, vivreActive: false, vivreLosses: 0, mmrThreshold: 1670 },
  { name: "Conqueror",        floor: 1_500_000_000, ceil: 3_000_000_000, baseGain: 60_000_000, rampagePeak: 0.08, vivreActive: false, vivreLosses: 0, mmrThreshold: 1800 },
  { name: "Yonko",            floor: 3_000_000_000, ceil: 5_000_000_000, baseGain: 90_000_000, rampagePeak: 0.03, vivreActive: false, vivreLosses: 0, mmrThreshold: 1950 },
  { name: "Pirate King",      floor: 5_000_000_000, ceil: BOUNTY_CAP,    baseGain: 40_000_000, rampagePeak: 0.00, vivreActive: false, vivreLosses: 0, mmrThreshold: 2150 },
];

export function tierIndexForBounty(bounty: number): number {
  let idx = 0;
  for (let i = 0; i < TIERS.length; i++) if (bounty >= TIERS[i].floor) idx = i;
  return idx;
}
function impliedTierForRating(rating: number): number {
  let idx = 0;
  for (let i = 0; i < TIERS.length; i++) if (rating >= TIERS[i].mmrThreshold) idx = i;
  return idx;
}
function startingBounty(rating: number): number {
  const i = Math.min(impliedTierForRating(rating), MAX_PLACEMENT_TIER);
  const t = TIERS[i];
  return t.floor + Math.floor((t.ceil - t.floor) / 2);
}
const clampI = (x: number, lo: number, hi: number) => (x < lo ? lo : x > hi ? hi : x);

// ──────────────────────────── Profile + apply ───────────────────────────

export interface RankedProfile {
  rating: number; rd: number; volatility: number;
  bounty: number; peakBounty: number;
  placementGamesLeft: number; winStreak: number;
  vivreCharge: number; vivreReady: boolean;
  seasonId: number; games: number;
  lastDeltaBounty: number; lastVivreSaved: boolean;
}

export function freshProfile(): RankedProfile {
  return {
    rating: G_DEFAULT_RATING, rd: G_DEFAULT_RD, volatility: G_DEFAULT_VOL,
    bounty: 0, peakBounty: 0,
    placementGamesLeft: PLACEMENT_GAMES, winStreak: 0,
    vivreCharge: 0, vivreReady: false,
    seasonId: 0, games: 0,
    lastDeltaBounty: 0, lastVivreSaved: false,
  };
}

function seasonReset(p: RankedProfile): void {
  const t = tierIndexForBounty(p.bounty);
  const drop = t <= 2 ? 0 : t <= 5 ? 1 : 2;
  const newT = Math.max(0, t - drop);
  p.bounty = TIERS[newT].floor;
  const compress = t <= 2 ? 0.0 : t <= 5 ? 0.25 : 0.5;
  p.rating += (G_DEFAULT_RATING - p.rating) * compress;
  p.rd = Math.max(p.rd, 150.0);
  p.winStreak = 0;
  p.vivreCharge = 0;
  p.vivreReady = false;
  p.lastDeltaBounty = 0;
  p.lastVivreSaved = false;
}

// Faithful port of RankedStore.Apply — mutates `p`.
export function applyMatch(
  p: RankedProfile, won: boolean, oppRating: number, oppRd: number, seasonId: number,
): void {
  if (seasonId > 0) {
    if (p.seasonId === 0 && p.games === 0) p.seasonId = seasonId;
    else if (p.seasonId !== seasonId) { seasonReset(p); p.seasonId = seasonId; }
  }

  const r = glickoUpdate(p.rating, p.rd, p.volatility, oppRating, oppRd, won ? 1.0 : 0.0);
  p.rating = r.rating; p.rd = r.rd; p.volatility = r.volatility;
  p.games++;

  p.winStreak = won
    ? (p.winStreak > 0 ? p.winStreak + 1 : 1)
    : (p.winStreak < 0 ? p.winStreak - 1 : -1);

  if (p.placementGamesLeft > 0) {
    p.placementGamesLeft--;
    if (p.placementGamesLeft === 0) {
      p.bounty = startingBounty(p.rating);
      p.peakBounty = Math.max(p.peakBounty, p.bounty);
      p.lastDeltaBounty = p.bounty;
    } else {
      p.lastDeltaBounty = 0;
    }
    p.lastVivreSaved = false;
    return;
  }

  const tIndex = tierIndexForBounty(p.bounty);
  const tier = TIERS[tIndex];
  const implied = impliedTierForRating(p.rating);
  const gap = clampI(implied - tIndex, -GAP_CLAMP, GAP_CLAMP);

  if (won) {
    const mult = p.winStreak >= RAMPAGE_STREAK ? 1.0 + tier.rampagePeak : 1.0;
    let delta = Math.round(tier.baseGain * (1.0 + GAP_K * gap) * mult);
    if (delta < 0) delta = 0;
    p.bounty += delta;
    p.lastDeltaBounty = delta;
    p.lastVivreSaved = false;
  } else {
    let loss = Math.round(tier.baseGain * LOSS_FACTOR * (1.0 - GAP_K * gap));
    if (loss < 0) loss = 0;
    const wouldDemote = tIndex > 0 && p.bounty - loss < tier.floor;
    if (tier.vivreActive && wouldDemote && p.vivreReady) {
      p.vivreReady = false;
      p.vivreCharge = 0;
      p.lastDeltaBounty = 0;
      p.lastVivreSaved = true;
    } else {
      p.bounty -= loss;
      p.lastDeltaBounty = -loss;
      p.lastVivreSaved = false;
      if (tier.vivreActive) {
        p.vivreCharge++;
        if (p.vivreCharge >= tier.vivreLosses) p.vivreReady = true;
      }
    }
  }

  if (p.bounty < 0) p.bounty = 0;
  if (p.bounty > BOUNTY_CAP) p.bounty = BOUNTY_CAP;
  p.peakBounty = Math.max(p.peakBounty, p.bounty);
}
