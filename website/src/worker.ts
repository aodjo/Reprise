import { createRemoteJWKSet, jwtVerify } from 'jose';
import { normalizeAvatar } from './avatar';
export { Collector } from './collector';

/** Resources and confidential OAuth credentials supplied by Cloudflare. */
interface Env {
  DB: D1Database;
  MEDIA: R2Bucket;
  ASSETS: Fetcher;
  COLLECTORS: DurableObjectNamespace;
  APP_ORIGIN: string;
  GOOGLE_CLIENT_ID?: string;
  GOOGLE_CLIENT_SECRET?: string;
}

/** Public identity, excluding the provider subject and credentials. */
interface Listener { id: string; handle: string; display_name: string; public: number; avatar_url:string|null }
/** Authenticated, revocable desktop installation. */
interface Device { id: string; listener_id: string }
/** Google's published signing keys; jose caches and refreshes them. */
const googleKeys = createRemoteJWKSet(new URL('https://www.googleapis.com/oauth2/v3/certs'));
/** Milliseconds in the rolling chart window. */
const week = 7 * 86400_000;
/** Existing application routes cannot also be public profile handles. */
const reservedHandles = new Set(['api','auth','media','assets','login','connect','privacy','terms','profile','tracks','listeners']);
/** Whitelisted media key syntax prevents serving arbitrary bucket objects. */
const mediaKey = /^artwork\/[a-f0-9]{64}\.(png|jpg|webp)$/;
/** Profile images are versioned per owner and never use uploaded filenames. */
const avatarKey = /^avatars\/[A-Za-z0-9_-]{1,80}\/[a-f0-9-]{36}\.png$/;

/** Reports expected request failures without exposing internals. */
class HttpError extends Error {
  /** Creates a status-coded error safe to send to the client. */
  constructor(public status: number, message: string) { super(message); }
}

/** Encodes random bytes as URL-safe opaque tokens. */
function randomToken(): string {
  return btoa(String.fromCharCode(...crypto.getRandomValues(new Uint8Array(32))))
    .replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}

/** Hashes secrets before persistence; raw tokens never enter D1. */
async function hash(value: string | ArrayBuffer): Promise<string> {
  const bytes = typeof value === 'string' ? new TextEncoder().encode(value) : value;
  return [...new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))]
    .map(byte => byte.toString(16).padStart(2, '0')).join('');
}

/** Returns uncached JSON for account state and changing chart data. */
function json(value: unknown, status = 200): Response {
  return Response.json(value, { status, headers: { 'Cache-Control': 'no-store' } });
}

/** Parses one exact cookie, ignoring malformed unrelated cookies. */
function cookie(request: Request, name: string): string | undefined {
  return request.headers.get('Cookie')?.split(';').map(part => part.trim())
    .find(part => part.startsWith(`${name}=`))?.slice(name.length + 1);
}

/** Uses secure cookies on deployed HTTPS and permits loopback development. */
function setCookie(request: Request, name: string, value: string, age: number): string {
  const secure = new URL(request.url).protocol === 'https:' ? '; Secure' : '';
  return `${name}=${value}; Path=/; HttpOnly; SameSite=Lax; Max-Age=${age}${secure}`;
}

/** Restricts browser writes to the configured origin; device writes use bearer auth. */
function sameOrigin(request: Request, env: Env): void {
  if (request.headers.get('Origin') !== new URL(env.APP_ORIGIN).origin) {
    throw new HttpError(403, 'origin_rejected');
  }
}

/** Parses bounded JSON without accepting HTML forms as authenticated API writes. */
async function body(request: Request, limit = 16_384): Promise<Record<string, unknown>> {
  if (!request.headers.get('Content-Type')?.startsWith('application/json')) {
    throw new HttpError(415, 'json_required');
  }
  const buffer = await readBytes(request, limit);
  try {
    const value = JSON.parse(new TextDecoder().decode(buffer));
    if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error();
    return value;
  } catch { throw new HttpError(400, 'invalid_json'); }
}

/** Reads at most limit bytes, including when Content-Length is absent or dishonest. */
async function readBytes(request: Request, limit: number): Promise<ArrayBuffer> {
  if (Number(request.headers.get('Content-Length')) > limit) throw new HttpError(413, 'too_large');
  const reader = request.body?.getReader();
  if (!reader) return new ArrayBuffer(0);
  const chunks: Uint8Array[] = [];
  let size = 0;
  for (;;) {
    const result = await reader.read();
    if (result.done) break;
    size += result.value.byteLength;
    if (size > limit) { await reader.cancel(); throw new HttpError(413, 'too_large'); }
    chunks.push(result.value);
  }
  const bytes = new Uint8Array(size);
  let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  return bytes.buffer;
}

/** Validates user-supplied text before it is stored or rendered with textContent. */
function text(value: unknown, max: number, allowEmpty = false): string {
  if (typeof value !== 'string' || value.trim().length > max || (!allowEmpty && !value.trim())) {
    throw new HttpError(400, 'invalid_text');
  }
  return value.trim().replace(/[\u0000-\u001f\u007f]/g, '');
}

/** Resolves a live browser session without exposing its token to JavaScript. */
async function currentListener(request: Request, env: Env): Promise<Listener | null> {
  const token = cookie(request, 'reprise_session');
  if (!token || token.length > 100) return null;
  return env.DB.prepare(`SELECT l.id, l.handle, l.display_name, l.public,
    CASE WHEN l.avatar_key IS NOT NULL THEN '/media/'||l.avatar_key ELSE NULL END AS avatar_url FROM listeners l
    JOIN sessions s ON s.listener_id = l.id WHERE s.token_hash = ? AND s.expires_at > ?`)
    .bind(await hash(token), Date.now()).first<Listener>();
}

/** Requires a signed-in browser for profile and device consent. */
async function requireListener(request: Request, env: Env): Promise<Listener> {
  const listener = await currentListener(request, env);
  if (!listener) throw new HttpError(401, 'sign_in_required');
  return listener;
}

/** Authenticates an installation and immediately honors server-side revocation. */
async function requireDevice(request: Request, env: Env): Promise<Device> {
  const token = request.headers.get('Authorization')?.match(/^Bearer ([A-Za-z0-9_-]{43})$/)?.[1];
  if (!token) throw new HttpError(401, 'device_token_required');
  const device = await env.DB.prepare(`SELECT d.id, d.listener_id FROM devices d
    JOIN listeners l ON l.id = d.listener_id
    WHERE d.token_hash = ? AND d.revoked_at IS NULL`)
    .bind(await hash(token)).first<Device>();
  if (!device) throw new HttpError(401, 'device_token_revoked');
  return device;
}

/** Starts Google's authorization-code flow with state, nonce and PKCE. */
async function startGoogle(request: Request, env: Env): Promise<Response> {
  if (!env.GOOGLE_CLIENT_ID || !env.GOOGLE_CLIENT_SECRET) throw new HttpError(503, 'google_not_configured');
  const input = new URL(request.url);
  if (input.searchParams.get('accepted') !== '2026-11-21') throw new HttpError(400, 'terms_acceptance_required');
  const returnTo = input.searchParams.get('return_to') || '/';
  if (!/^\/(?:connect(?:\?code=[A-Z2-9-]+)?|profile)?$/.test(returnTo)) throw new HttpError(400, 'invalid_return');
  const state = randomToken(), nonce = randomToken(), verifier = randomToken();
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
  const challenge = btoa(String.fromCharCode(...new Uint8Array(digest)))
    .replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
  await env.DB.prepare(`INSERT INTO oauth_states VALUES (?, ?, ?, ?, ?)`)
    .bind(await hash(state), nonce, verifier, returnTo, Date.now() + 600_000).run();
  const url = new URL('https://accounts.google.com/o/oauth2/v2/auth');
  url.search = new URLSearchParams({ client_id: env.GOOGLE_CLIENT_ID,
    redirect_uri: new URL('/auth/google/callback', env.APP_ORIGIN).href,
    response_type: 'code', scope: 'openid profile', state, nonce,
    code_challenge: challenge, code_challenge_method: 'S256', prompt: 'select_account' }).toString();
  return new Response(null, { status: 302, headers: { Location: url.href,
    'Set-Cookie': setCookie(request, 'reprise_oauth', state, 600), 'Cache-Control': 'no-store' } });
}

/** Verifies Google's signed identity, consumes state, and creates an opaque session. */
async function finishGoogle(request: Request, env: Env): Promise<Response> {
  const url = new URL(request.url), state = url.searchParams.get('state');
  if (!state || state !== cookie(request, 'reprise_oauth')) throw new HttpError(400, 'invalid_oauth_state');
  const pending = await env.DB.prepare(`DELETE FROM oauth_states WHERE state_hash = ? AND expires_at > ?
    RETURNING nonce, verifier, return_to`).bind(await hash(state), Date.now())
    .first<{ nonce: string; verifier: string; return_to: string }>();
  if (!pending) throw new HttpError(400, 'expired_oauth_state');
  if (url.searchParams.has('error')) {
    const destination = new URL('/login', env.APP_ORIGIN);
    destination.searchParams.set('error', 'cancelled');
    destination.searchParams.set('return_to', pending.return_to);
    return Response.redirect(destination.href, 302);
  }
  const code = url.searchParams.get('code');
  if (!code || !env.GOOGLE_CLIENT_ID || !env.GOOGLE_CLIENT_SECRET) throw new HttpError(400, 'invalid_oauth_callback');
  const response = await fetch('https://oauth2.googleapis.com/token', { method: 'POST',
    body: new URLSearchParams({ code, client_id: env.GOOGLE_CLIENT_ID,
      client_secret: env.GOOGLE_CLIENT_SECRET, grant_type: 'authorization_code',
      redirect_uri: new URL('/auth/google/callback', env.APP_ORIGIN).href, code_verifier: pending.verifier }) });
  if (!response.ok) throw new HttpError(502, 'google_exchange_failed');
  const tokens = await response.json<{ id_token?: string }>();
  if (!tokens.id_token) throw new HttpError(502, 'google_identity_missing');
  let identity;
  try {
    identity = (await jwtVerify(tokens.id_token, googleKeys, { audience: env.GOOGLE_CLIENT_ID,
      issuer: ['https://accounts.google.com', 'accounts.google.com'], algorithms: ['RS256'] })).payload;
  } catch { throw new HttpError(401, 'google_identity_invalid'); }
  if (!identity.sub || identity.nonce !== pending.nonce) throw new HttpError(401, 'google_nonce_invalid');
  const id = crypto.randomUUID();
  const name = typeof identity.name === 'string' ? identity.name.slice(0, 60) : 'Reprise listener';
  await env.DB.prepare(`INSERT INTO listeners (id, google_sub, handle, display_name, created_at)
    VALUES (?, ?, ?, ?, ?) ON CONFLICT(google_sub) DO NOTHING`)
    .bind(id, identity.sub, `listener-${id.slice(0, 8)}`, name, Date.now()).run();
  const listener = await env.DB.prepare('SELECT id FROM listeners WHERE google_sub = ?')
    .bind(identity.sub).first<{ id: string }>();
  if (!listener) throw new HttpError(500, 'account_unavailable');
  await env.DB.prepare('UPDATE listeners SET terms_version = ?, terms_accepted_at = ? WHERE id = ?')
    .bind('2026-11-21', Date.now(), listener.id).run();
  const session = randomToken();
  await env.DB.prepare('INSERT INTO sessions VALUES (?, ?, ?)')
    .bind(await hash(session), listener.id, Date.now() + 30 * 86400_000).run();
  const headers = new Headers({ Location: new URL(pending.return_to, env.APP_ORIGIN).href, 'Cache-Control': 'no-store' });
  headers.append('Set-Cookie', setCookie(request, 'reprise_session', session, 30 * 86400));
  headers.append('Set-Cookie', setCookie(request, 'reprise_oauth', '', 0));
  return new Response(null, { status: 302, headers });
}

/** Carries the original ranking window and last sort key between track pages. */
interface TrackPageCursor { at:number;q:string;listeners:number;seconds:number;id:string }
/** Encodes Unicode search text and the last stable ordering key without server-side session storage. */
function encodeTrackCursor(cursor:TrackPageCursor):string {
  return btoa(String.fromCharCode(...new TextEncoder().encode(JSON.stringify(cursor)))).replaceAll('+','-').replaceAll('/','_').replaceAll('=','');
}
/** Rejects malformed cursors and prevents continuing a page under a different search query. */
function decodeTrackCursor(value:string,search:string,now:number):TrackPageCursor {
  try{
    if(value.length>2048||!/^[A-Za-z0-9_-]+$/.test(value))throw new Error();
    const decoded=JSON.parse(new TextDecoder().decode(Uint8Array.from(atob(value.replaceAll('-','+').replaceAll('_','/')),char=>char.charCodeAt(0)))) as TrackPageCursor;
    if(!decoded||!Number.isSafeInteger(decoded.at)||decoded.at<=0||decoded.at>now||decoded.q!==search
      ||!Number.isSafeInteger(decoded.listeners)||decoded.listeners<1||!Number.isFinite(decoded.seconds)||decoded.seconds<=0||decoded.seconds>Number.MAX_SAFE_INTEGER
      ||typeof decoded.id!=='string'||!/^[A-Za-z0-9_-]{1,128}$/.test(decoded.id))throw new Error();
    return decoded;
  }catch{throw new HttpError(400,'invalid_track_cursor');}
}

/** Returns real seven-day distinct-listener rankings with stable, bounded track pages. */
async function charts(url: URL, env: Env): Promise<Response> {
  const requested = Number(url.searchParams.get('limit') || 6);
  const limit = Number.isInteger(requested) ? Math.max(1, Math.min(requested, 100)) : 6;
  const search = (url.searchParams.get('q') || '').trim().slice(0, 300);
  const listenerSearch = (url.searchParams.get('listener_q') || '').trim().replace(/^@/,'').slice(0,300);
  const now=Date.now(),cursorValue=url.searchParams.get('track_cursor');
  const cursor=cursorValue?decodeTrackCursor(cursorValue,search,now):null;
  const anchor=cursor?.at??now,since=anchor-week,tracksOnly=url.searchParams.get('scope')==='tracks';
  const includeFeatured=!tracksOnly&&url.searchParams.get('featured')==='listener';
  /** Filtering happens after ranking so matching listeners retain their original positions. */
  const listenerRanking = `WITH ranked_listeners AS (
    SELECT l.id,l.handle,l.display_name,SUM(e.seconds) AS seconds,
      CASE WHEN l.avatar_key IS NOT NULL THEN '/media/'||l.avatar_key ELSE NULL END AS avatar_url,
      ROW_NUMBER() OVER (ORDER BY SUM(e.seconds) DESC,l.id ASC) AS rank
    FROM listeners l JOIN listen_events e ON e.listener_id=l.id
    WHERE e.ended_at>=? AND e.ended_at<=? AND l.public=1 AND e.review_state='accepted' GROUP BY l.id
  )`;
  const queries = [
    env.DB.prepare(`WITH qualified AS (
      SELECT e.track_id, e.listener_id, SUM(e.seconds) AS seconds FROM listen_events e
      WHERE e.ended_at >= ? AND e.ended_at <= ? AND e.review_state = 'accepted'
      GROUP BY e.track_id, e.listener_id HAVING SUM(e.seconds) >= 30
    ), ranked AS (
      SELECT t.id, t.title, t.artist, t.album, t.artwork_key, COUNT(*) AS listeners,
        SUM(q.seconds) AS seconds,
        ROW_NUMBER() OVER (ORDER BY COUNT(*) DESC, SUM(q.seconds) DESC, t.id ASC) AS rank
      FROM qualified q JOIN tracks t ON t.id = q.track_id GROUP BY t.id
    ) SELECT * FROM ranked WHERE (instr(lower(title), lower(?)) > 0 OR instr(lower(artist), lower(?)) > 0)
      AND (? IS NULL OR listeners < ? OR (listeners = ? AND seconds < ?) OR (listeners = ? AND seconds = ? AND id > ?))
      ORDER BY rank LIMIT ?`).bind(since,anchor,search,search,cursor?.id??null,cursor?.listeners??null,cursor?.listeners??null,
        cursor?.seconds??null,cursor?.listeners??null,cursor?.seconds??null,cursor?.id??null,limit+1),
  ];
  if(!tracksOnly)queries.push(env.DB.prepare(`${listenerRanking} SELECT * FROM ranked_listeners
      WHERE instr(lower(display_name),lower(?))>0 OR instr(lower(handle),lower(?))>0
      ORDER BY rank LIMIT ?`).bind(since,anchor,listenerSearch,listenerSearch,Math.max(5,limit)));
  if (includeFeatured) queries.push(env.DB.prepare(`WITH leader AS (
    SELECT l.id FROM listeners l JOIN listen_events e ON e.listener_id = l.id
    WHERE e.ended_at >= ? AND e.ended_at <= ? AND l.public = 1 AND e.review_state = 'accepted'
    GROUP BY l.id ORDER BY SUM(e.seconds) DESC, l.id ASC LIMIT 1
  ), album_tracks AS (
    SELECT t.id, t.album, t.artist, t.artwork_key, SUM(e.seconds) AS seconds
    FROM listen_events e JOIN leader l ON l.id = e.listener_id JOIN tracks t ON t.id = e.track_id
    WHERE e.ended_at >= ? AND e.ended_at <= ? AND e.review_state = 'accepted' AND trim(t.album) != '' GROUP BY t.id
  ), albums AS (
    SELECT album, artist, SUM(seconds) AS seconds FROM album_tracks GROUP BY album, artist
  ) SELECT a.album, a.artist, a.seconds, (
    SELECT t.artwork_key FROM album_tracks t WHERE t.album = a.album AND t.artist = a.artist
      AND t.artwork_key IS NOT NULL ORDER BY t.seconds DESC, t.id ASC LIMIT 1
  ) AS artwork_key FROM albums a ORDER BY a.seconds DESC, a.album ASC, a.artist ASC LIMIT 3`).bind(since,anchor,since,anchor));
  if (includeFeatured) queries.push(
    env.DB.prepare(`${listenerRanking} SELECT * FROM ranked_listeners ORDER BY rank LIMIT 1`).bind(since,anchor));
  const [tracks, listeners, albums, featured] = await env.DB.batch<Record<string, unknown>>(queries);
  const page=tracks.results.slice(0,limit),last=page.at(-1);
  const next=tracks.results.length>limit&&last?encodeTrackCursor({at:anchor,q:search,listeners:Number(last.listeners),seconds:Number(last.seconds),id:String(last.id)}):null;
  return json({ tracks: page.map(row => ({ ...row,
    artwork_url: row.artwork_key ? `/media/${row.artwork_key}` : null, artwork_key: undefined })),
    next_track_cursor:next,listeners: listeners?.results??[],
    featured_listener: featured ? featured.results[0]??null : undefined,
    featured_albums: albums?.results.map(row => ({ ...row,
      artwork_url: row.artwork_key ? `/media/${row.artwork_key}` : null, artwork_key: undefined })),
    period: { from: since, to: anchor } });
}

/** Keeps profile identity public while restricting private listening details to the owner. */
async function listenerProfile(listener: Listener, env: Env, url: URL, historyVisible=true): Promise<Response> {
  if(!historyVisible)return json({listener,history_visible:false,stats:null,recent:[],next_cursor:null,daily:[],albums:[],period:null});
  const now = Date.now(), since = now - week, day = 86400_000;
  const allScrobbles = url.searchParams.get('scrobbles') === 'all';
  const before = allScrobbles && url.searchParams.has('before') ? Number(url.searchParams.get('before')) : now+1;
  if (!Number.isSafeInteger(before) || before<=0 || before>now+1) throw new HttpError(400,'invalid_cursor');
  const today = Math.floor((now + 9 * 3600_000) / day) * day - 9 * 3600_000;
  const days = Array.from({ length: 7 }, (_, index) => today - (6 - index) * day);
  const [stats, rank, recent, daily, albums] = await env.DB.batch<Record<string, unknown>>([
    env.DB.prepare(`SELECT COALESCE(SUM(CASE WHEN ended_at >= ? THEN seconds ELSE 0 END),0) AS seconds,
      COALESCE(SUM(CASE WHEN ended_at < ? THEN seconds ELSE 0 END),0) AS previous_seconds,
      COUNT(DISTINCT CASE WHEN ended_at >= ? THEN track_id END) AS tracks
      FROM listen_events WHERE listener_id = ? AND ended_at >= ? AND ended_at <= ? AND review_state = 'accepted'`)
      .bind(since,since,since,listener.id,since-week,now),
    env.DB.prepare(`WITH ranked AS (
      SELECT l.id, ROW_NUMBER() OVER (ORDER BY SUM(e.seconds) DESC, l.id ASC) AS rank
      FROM listeners l JOIN listen_events e ON e.listener_id = l.id
      WHERE l.public = 1 AND e.ended_at >= ? AND e.ended_at <= ? AND e.review_state = 'accepted' GROUP BY l.id
    ) SELECT rank FROM ranked WHERE id = ?`).bind(since,now,listener.id),
    env.DB.prepare(`WITH ordered AS (
      SELECT *, LAG(track_id) OVER (ORDER BY started_at,ended_at,event_id) AS previous_track,
        LAG(ended_at) OVER (ORDER BY started_at,ended_at,event_id) AS previous_end
      FROM listen_events WHERE listener_id = ? AND ended_at >= ? AND ended_at <= ? AND review_state = 'accepted'
    ), grouped AS (
      SELECT *, SUM(CASE WHEN previous_track = track_id AND started_at <= previous_end + 20000 THEN 0 ELSE 1 END)
        OVER (ORDER BY started_at,ended_at,event_id ROWS UNBOUNDED PRECEDING) AS playback FROM ordered
    ), plays AS (
      SELECT track_id, MIN(started_at) AS started_at, MAX(ended_at) AS ended_at, SUM(seconds) AS seconds
      FROM grouped GROUP BY playback,track_id HAVING SUM(seconds) >= 30
    ) SELECT t.id,t.title,t.artist,t.album,t.artwork_key,p.started_at,p.ended_at,p.seconds
      FROM plays p JOIN tracks t ON t.id = p.track_id WHERE p.ended_at < ?
      ORDER BY p.ended_at DESC,t.id ASC LIMIT ?`).bind(listener.id,allScrobbles?now-30*day:since,now,before,allScrobbles?26:6),
    env.DB.prepare(`WITH days(start,finish) AS (VALUES ${days.map(() => '(?,?)').join(',')})
      SELECT d.start,COALESCE(SUM(e.seconds * (MIN(e.ended_at,d.finish)-MAX(e.started_at,d.start))
        / MAX(1,e.ended_at-e.started_at)),0) AS seconds FROM days d LEFT JOIN listen_events e
        ON e.listener_id = ? AND e.review_state = 'accepted' AND e.ended_at > d.start AND e.started_at < d.finish
      GROUP BY d.start ORDER BY d.start`).bind(...days.flatMap(start => [start,Math.min(start+day,now)]),listener.id),
    env.DB.prepare(`WITH album_tracks AS (
      SELECT t.id,t.album,t.artist,t.artwork_key,SUM(e.seconds) AS seconds FROM listen_events e JOIN tracks t ON t.id=e.track_id
      WHERE e.listener_id=? AND e.ended_at>=? AND e.ended_at<=? AND e.review_state='accepted' AND trim(t.album)!='' GROUP BY t.id
    ), albums AS (SELECT album,artist,SUM(seconds) AS seconds FROM album_tracks GROUP BY album,artist)
    SELECT a.album,a.artist,a.seconds,(SELECT t.artwork_key FROM album_tracks t WHERE t.album=a.album AND t.artist=a.artist
      AND t.artwork_key IS NOT NULL ORDER BY t.seconds DESC,t.id ASC LIMIT 1) AS artwork_key
      FROM albums a ORDER BY a.seconds DESC,a.album ASC,a.artist ASC LIMIT 3`).bind(listener.id,since,now),
  ]);
  /** Exposes only the existing public cover route, never a bucket key or token. */
  const cover = (row: Record<string, unknown>) => ({ ...row,artwork_key:undefined,artwork_url:row.artwork_key?`/media/${row.artwork_key}`:null });
  return json({listener,history_visible:true,stats:{...stats.results[0],rank:rank.results[0]?.rank??null},
    recent:recent.results.slice(0,allScrobbles?25:6).map(cover),next_cursor:allScrobbles&&recent.results.length>25?recent.results[24].ended_at:null,
    daily:daily.results.map(row=>({date:new Date(Number(row.start)+9*3600_000).toISOString().slice(0,10),seconds:row.seconds})),
    albums:albums.results.map(cover),period:{from:since,to:now}});
}

/** Validates app observations and forwards authenticated identity to the account coordinator. */
async function collectHeartbeat(request: Request, env: Env): Promise<Response> {
  const device = await requireDevice(request, env), input = await body(request);
  let observation = null;
  if (input.observation !== null && input.observation !== undefined) {
    if (typeof input.observation !== 'object' || Array.isArray(input.observation)) throw new HttpError(400, 'invalid_observation');
    const value = input.observation as Record<string, unknown>;
    const title = text(value.title, 300), artist = text(value.artist, 300), album = text(value.album, 300, true);
    const duration = value.duration_seconds == null ? null : Number(value.duration_seconds);
    const position = value.position_seconds == null ? null : Number(value.position_seconds);
    if (typeof value.playing !== 'boolean'
      || !['spotify', 'melon', 'apple-music', 'youtube-music', 'other'].includes(String(value.provider))
      || !['local_output', 'remote_state', 'app_state'].includes(String(value.evidence))
      || (value.evidence === 'remote_state' && value.provider !== 'spotify')
      || (duration !== null && (!Number.isFinite(duration) || duration <= 0 || duration > 86400))
      || (position !== null && (!Number.isFinite(position) || position < 0 || position > (duration ?? 86400) + 5))) {
      throw new HttpError(400, 'invalid_observation');
    }
    const canonical = [title, artist, album].map(part => part.normalize('NFKC').toLocaleLowerCase('en-US'));
    observation = { title, artist, album, track_id: await hash(JSON.stringify(canonical)),
      duration_seconds: duration, position_seconds: position, playing: value.playing,
      provider: value.provider, evidence: value.evidence };
  }
  if ((input.epoch !== undefined && (typeof input.epoch !== 'string' || input.epoch.length > 80))
    || (input.nonce !== undefined && (typeof input.nonce !== 'string' || input.nonce.length > 80))
    || (input.sequence !== undefined && (!Number.isSafeInteger(input.sequence) || Number(input.sequence) < 1))) {
    throw new HttpError(400, 'invalid_sequence');
  }
  return env.COLLECTORS.getByName(device.listener_id).fetch('https://collector/heartbeat', {
    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Reprise-Listener': device.listener_id },
    body: JSON.stringify({ device_id: device.id, observation, epoch: input.epoch, nonce: input.nonce, sequence: input.sequence }),
  });
}

/** Stores validated cover bytes in R2 only for a track the installation has reported. */
async function uploadArtwork(request: Request, env: Env, trackId: string): Promise<Response> {
  const device = await requireDevice(request, env);
  if (!/^[a-f0-9]{64}$/.test(trackId)) throw new HttpError(400, 'invalid_track');
  const track = await env.DB.prepare(`SELECT t.id FROM tracks t JOIN listen_events e ON e.track_id = t.id
    WHERE t.id = ? AND e.listener_id = ? LIMIT 1`).bind(trackId, device.listener_id).first();
  if (!track) throw new HttpError(404, 'track_not_reported');
  const bytes = await readBytes(request, 2 * 1024 * 1024), data = new Uint8Array(bytes);
  const png = data.length >= 8 && [137,80,78,71,13,10,26,10].every((n, i) => data[i] === n);
  const jpg = data.length >= 3 && data[0] === 255 && data[1] === 216 && data[2] === 255;
  const webp = data.length >= 12 && new TextDecoder().decode(data.slice(0, 4)) === 'RIFF'
    && new TextDecoder().decode(data.slice(8, 12)) === 'WEBP';
  if (!png && !jpg && !webp) throw new HttpError(415, 'unsupported_artwork');
  const extension = png ? 'png' : jpg ? 'jpg' : 'webp', key = `artwork/${await hash(bytes)}.${extension}`;
  await env.MEDIA.put(key, bytes, { httpMetadata: { contentType: `image/${jpg ? 'jpeg' : extension}` } });
  await env.DB.prepare('UPDATE tracks SET artwork_key = ? WHERE id = ?').bind(key, trackId).run();
  return json({ artwork_url: `/media/${key}` });
}

/** Replaces only the authenticated owner's image and safely handles concurrent changes. */
async function updateAvatar(request:Request,env:Env):Promise<Response> {
  sameOrigin(request,env);
  const listener=await requireListener(request,env);
  const previous=await env.DB.prepare('SELECT avatar_key FROM listeners WHERE id=?').bind(listener.id).first<{avatar_key:string|null}>();
  if(!previous)throw new HttpError(401,'sign_in_required');
  if(request.method==='DELETE'){
    const changed=await env.DB.prepare('UPDATE listeners SET avatar_key=NULL WHERE id=? AND avatar_key IS ? RETURNING id')
      .bind(listener.id,previous.avatar_key).first();
    if(!changed)throw new HttpError(409,'avatar_changed');
    if(previous.avatar_key)await env.MEDIA.delete(previous.avatar_key).catch(()=>{});
    return new Response(null,{status:204});
  }
  const bytes=await readBytes(request,2*1024*1024);
  let normalized:Uint8Array;
  try{normalized=await normalizeAvatar(bytes);}catch{throw new HttpError(415,'invalid_avatar');}
  const key=`avatars/${listener.id}/${crypto.randomUUID()}.png`;
  await env.MEDIA.put(key,normalized,{httpMetadata:{contentType:'image/png'}});
  const changed=await env.DB.prepare('UPDATE listeners SET avatar_key=? WHERE id=? AND avatar_key IS ? RETURNING id')
    .bind(key,listener.id,previous.avatar_key).first();
  if(!changed){await env.MEDIA.delete(key).catch(()=>{});throw new HttpError(409,'avatar_changed');}
  if(previous.avatar_key)await env.MEDIA.delete(previous.avatar_key).catch(()=>{});
  return json({avatar_url:`/media/${key}`});
}

/** Serves the current public profile image independently of listening-history visibility. */
async function profileAvatar(request:Request,env:Env,key:string):Promise<Response> {
  if(!avatarKey.test(key))throw new HttpError(404,'media_not_found');
  const listener=await env.DB.prepare('SELECT id FROM listeners WHERE avatar_key=?').bind(key).first<{id:string}>();
  if(!listener)throw new HttpError(404,'media_not_found');
  const object=await env.MEDIA.get(key);if(!object)throw new HttpError(404,'media_not_found');
  return new Response(request.method==='HEAD'?null:object.body,{headers:{'Content-Type':'image/png','Cache-Control':'private, no-store'}});
}

/** Serves public-profile or anonymously ranked artwork, keeping other private covers owner-only. */
async function artwork(request: Request, env: Env, key: string): Promise<Response> {
  if (!mediaKey.test(key)) throw new HttpError(404, 'media_not_found');
  const now=Date.now();
  const visible = await env.DB.prepare(`SELECT 1 FROM tracks t JOIN listen_events e ON e.track_id = t.id
    JOIN listeners l ON l.id = e.listener_id WHERE t.artwork_key = ? GROUP BY t.id,e.listener_id
    HAVING MAX(l.public)=1 OR SUM(CASE WHEN e.review_state='accepted' AND e.ended_at>=? AND e.ended_at<=?
      THEN e.seconds ELSE 0 END)>=30 LIMIT 1`).bind(key,now-week,now).first();
  if (!visible) {
    const owner = await currentListener(request,env);
    if (!owner || !await env.DB.prepare(`SELECT 1 FROM tracks t JOIN listen_events e ON e.track_id=t.id
      WHERE t.artwork_key=? AND e.listener_id=? LIMIT 1`).bind(key,owner.id).first()) throw new HttpError(404,'media_not_found');
  }
  const object = await env.MEDIA.get(key);
  if (!object) throw new HttpError(404, 'media_not_found');
  const headers = new Headers({ 'Cache-Control': visible?'public, max-age=3600':'private, no-store', ETag: object.httpEtag });
  object.writeHttpMetadata(headers);
  if (request.headers.get('If-None-Match') === object.httpEtag) return new Response(null, { status: 304, headers });
  return new Response(request.method === 'HEAD' ? null : object.body, { headers });
}

/** Starts bounded, expiring device pairing without accepting identity from the app. */
async function startDevice(request: Request, env: Env): Promise<Response> {
  const input = await body(request), name = text(input.name, 80);
  const count = await env.DB.prepare('SELECT COUNT(*) AS total FROM device_grants WHERE expires_at > ?')
    .bind(Date.now()).first<{ total: number }>();
  if ((count?.total ?? 0) > 5000) throw new HttpError(429, 'pairing_busy');
  const code = randomToken();
  const userCode = [...crypto.getRandomValues(new Uint8Array(8))]
    .map(n => 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'[n % 31]).join('');
  await env.DB.prepare(`INSERT INTO device_grants (code_hash, user_code, name, expires_at) VALUES (?, ?, ?, ?)`)
    .bind(await hash(code), userCode, name, Date.now() + 600_000).run();
  return json({ device_code: code, user_code: userCode, interval: 5, expires_in: 600,
    verification_uri: new URL(`/connect?code=${userCode}`, env.APP_ORIGIN).href });
}

/** Polls and atomically consumes an approved grant, returning its bearer token only once. */
async function pollDevice(request: Request, env: Env): Promise<Response> {
  const input = await body(request), code = text(input.device_code, 100), now = Date.now();
  const codeHash = await hash(code);
  const grant = await env.DB.prepare(`UPDATE device_grants SET last_poll_at = ?
    WHERE code_hash = ? AND expires_at > ? AND consumed_at IS NULL AND last_poll_at <= ?
    RETURNING listener_id, name`).bind(now, codeHash, now, now - 4500)
    .first<{ listener_id: string | null; name: string }>();
  if (!grant) {
    const existing = await env.DB.prepare('SELECT expires_at, consumed_at FROM device_grants WHERE code_hash = ?')
      .bind(codeHash).first<{ expires_at: number; consumed_at: number | null }>();
    if (!existing || existing.expires_at <= now || existing.consumed_at) throw new HttpError(410, 'device_code_expired');
    return json({ error: 'slow_down' }, 429);
  }
  if (!grant.listener_id) return json({ error: 'authorization_pending' }, 202);
  const token = randomToken(), id = crypto.randomUUID();
  const results = await env.DB.batch([
    env.DB.prepare(`INSERT INTO devices (id, listener_id, token_hash, name, created_at)
      SELECT ?, listener_id, ?, name, ? FROM device_grants WHERE code_hash = ? AND consumed_at IS NULL`)
      .bind(id, await hash(token), now, codeHash),
    env.DB.prepare('UPDATE device_grants SET consumed_at = ? WHERE code_hash = ? AND consumed_at IS NULL').bind(now, codeHash),
  ]);
  if (!results[0].meta.changes) throw new HttpError(410, 'device_code_consumed');
  return json({ access_token: token, token_type: 'Bearer', device_id: id });
}

/** Routes API, authentication, approved media, and static interface requests. */
async function route(request: Request, env: Env): Promise<Response> {
  const url = new URL(request.url), path = url.pathname, method = request.method;
  if (path === '/api/charts' && method === 'GET') return charts(url, env);
  if (path === '/api/config' && method === 'GET') return json({
    google_ready: Boolean(env.GOOGLE_CLIENT_ID && env.GOOGLE_CLIENT_SECRET),
    country: request.cf?.country || null,
  });
  if (path === '/auth/google' && method === 'GET') return startGoogle(request, env);
  if (path === '/auth/google/callback' && method === 'GET') return finishGoogle(request, env);
  if (path === '/api/me' && method === 'GET') return json({ listener: await currentListener(request, env) });
  if (path === '/api/logout' && method === 'POST') {
    sameOrigin(request, env);
    const token = cookie(request, 'reprise_session');
    if (token) await env.DB.prepare('DELETE FROM sessions WHERE token_hash = ?').bind(await hash(token)).run();
    return new Response(null, { status: 204, headers: { 'Set-Cookie': setCookie(request, 'reprise_session', '', 0) } });
  }
  if (path === '/api/account' && method === 'DELETE') {
    sameOrigin(request, env);
    const listener = await requireListener(request, env), input = await body(request);
    if (input.confirm !== listener.handle) throw new HttpError(400, 'account_confirmation_required');
    const devices = await env.DB.prepare('SELECT id FROM devices WHERE listener_id = ?').bind(listener.id).all<{ id: string }>();
    for (const device of devices.results) await env.COLLECTORS.getByName(listener.id).fetch('https://collector/revoke', {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Reprise-Listener': listener.id },
      body: JSON.stringify({ device_id: device.id }),
    });
    const removed=await env.DB.prepare('DELETE FROM listeners WHERE id = ? RETURNING avatar_key').bind(listener.id).first<{avatar_key:string|null}>();
    if(removed?.avatar_key)await env.MEDIA.delete(removed.avatar_key).catch(()=>{});
    return new Response(null, { status: 204, headers: { 'Set-Cookie': setCookie(request, 'reprise_session', '', 0) } });
  }
  if (path === '/api/device/start' && method === 'POST') return startDevice(request, env);
  if (path === '/api/device/token' && method === 'POST') return pollDevice(request, env);
  if (path === '/api/device/info' && method === 'GET') {
    await requireListener(request, env);
    const code = (url.searchParams.get('code') || '').replaceAll('-', '').toUpperCase();
    const pending = await env.DB.prepare(`SELECT name FROM device_grants
      WHERE user_code = ? AND expires_at > ? AND listener_id IS NULL AND consumed_at IS NULL`)
      .bind(code, Date.now()).first();
    if (!pending) throw new HttpError(404, 'device_code_invalid');
    return json(pending);
  }
  if (path === '/api/listens') throw new HttpError(410, 'use_online_collector');
  if (path === '/api/collector' && method === 'POST') return collectHeartbeat(request, env);
  if (path.startsWith('/api/artwork/') && method === 'PUT') return uploadArtwork(request, env, path.slice(13));
  if (path === '/api/profile/avatar' && ['PUT','DELETE'].includes(method)) return updateAvatar(request,env);
  if (path.startsWith('/media/avatars/') && ['GET','HEAD'].includes(method)) return profileAvatar(request,env,path.slice(7));
  if (path.startsWith('/media/') && ['GET', 'HEAD'].includes(method)) return artwork(request, env, path.slice(7));
  if (path === '/api/device/approve' && method === 'POST') {
    sameOrigin(request, env);
    const listener = await requireListener(request, env), input = await body(request);
    if (input.consent !== true) throw new HttpError(400, 'consent_required');
    const code = text(input.user_code, 12).replaceAll('-', '').toUpperCase();
    const approved = await env.DB.prepare(`UPDATE device_grants SET listener_id = ?
      WHERE user_code = ? AND expires_at > ? AND listener_id IS NULL AND consumed_at IS NULL RETURNING name`)
      .bind(listener.id, code, Date.now()).first<{ name: string }>();
    if (!approved) throw new HttpError(404, 'device_code_invalid');
    return json({ approved: true, name: approved.name });
  }
  if (path === '/api/devices' && method === 'GET') {
    const listener = await requireListener(request, env);
    const result = await env.DB.prepare(`SELECT id, name, created_at, last_seen_at FROM devices
      WHERE listener_id = ? AND revoked_at IS NULL ORDER BY created_at DESC`).bind(listener.id).all();
    return json({ devices: result.results });
  }
  if (path.startsWith('/api/devices/') && method === 'DELETE') {
    const bearer = request.headers.has('Authorization');
    const identity = bearer ? await requireDevice(request, env) : await requireListener(request, env);
    if (!bearer) sameOrigin(request, env);
    const id = path.slice(13), owner = 'listener_id' in identity ? identity.listener_id : identity.id;
    if (bearer && identity.id !== id) throw new HttpError(403, 'device_mismatch');
    await env.DB.prepare('UPDATE devices SET revoked_at = ? WHERE id = ? AND listener_id = ?')
      .bind(Date.now(), id, owner).run();
    await env.COLLECTORS.getByName(owner).fetch('https://collector/revoke', {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Reprise-Listener': owner },
      body: JSON.stringify({ device_id: id }),
    });
    return new Response(null, { status: 204 });
  }
  if (path === '/api/profile' && method === 'PATCH') {
    sameOrigin(request, env);
    const listener = await requireListener(request, env), input = await body(request);
    const handle = text(input.handle, 30).toLowerCase(), name = text(input.display_name, 60);
    if (!/^[a-z0-9][a-z0-9_-]{2,29}$/.test(handle) || typeof input.public !== 'boolean') throw new HttpError(400, 'invalid_profile');
    if (reservedHandles.has(handle)) throw new HttpError(409, 'handle_unavailable');
    const collision = await env.DB.prepare('SELECT id FROM listeners WHERE handle = ? AND id != ?').bind(handle, listener.id).first();
    if (collision) throw new HttpError(409, 'handle_unavailable');
    await env.DB.prepare('UPDATE listeners SET handle = ?, display_name = ?, public = ? WHERE id = ?')
      .bind(handle, name, input.public ? 1 : 0, listener.id).run();
    return json({ updated: true });
  }
  if (path === '/api/profile' && method === 'GET') return listenerProfile(await requireListener(request,env),env,url);
  if (path.startsWith('/api/listeners/') && method === 'GET') {
    const owner = await currentListener(request,env);
    const listener = await env.DB.prepare(`SELECT id, handle, display_name, public,
      CASE WHEN avatar_key IS NOT NULL THEN '/media/'||avatar_key ELSE NULL END AS avatar_url
      FROM listeners WHERE handle = ?`)
      .bind(decodeURIComponent(path.slice(15))).first<Listener>();
    if (!listener) throw new HttpError(404, 'listener_not_found');
    return listenerProfile(listener,env,url,Boolean(listener.public)||owner?.id===listener.id);
  }
  if (path.startsWith('/api/') || path.startsWith('/auth/') || path.startsWith('/media/')) throw new HttpError(404, 'not_found');
  if (!['GET', 'HEAD'].includes(method)) throw new HttpError(405, 'method_not_allowed');
  if (['/connect', '/connect/', '/connect.html'].includes(path)
    && !/^[A-Z2-9]{8}$/.test((url.searchParams.get('code') || '').replaceAll('-', '').toUpperCase())) {
    return new Response(null, { status: 302, headers: { Location: '/', 'Cache-Control': 'no-store' } });
  }
  const assetURL = new URL(request.url);
  if (path === '/profile' && !await currentListener(request,env)) {
    return new Response(null,{status:302,headers:{Location:'/login?return_to=%2Fprofile','Cache-Control':'no-store'}});
  }
  if (/^\/[a-z0-9][a-z0-9_-]{2,29}\/?$/.test(path) && !reservedHandles.has(path.replaceAll('/',''))) assetURL.pathname = '/profile';
  return env.ASSETS.fetch(new Request(assetURL, request));
}

/** Applies one consistent security policy to errors, HTML, media and APIs. */
function secure(response: Response): Response {
  const result = new Response(response.body, response);
  result.headers.set('X-Content-Type-Options', 'nosniff');
  result.headers.set('Referrer-Policy', 'same-origin');
  result.headers.set('X-Frame-Options', 'DENY');
  result.headers.set('Content-Security-Policy', "default-src 'self'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'self'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'");
  return result;
}

/** Worker entry points for live requests and expiring authentication records. */
export default {
  /** Dispatches requests while keeping tokens and confidential failures out of logs. */
  async fetch(request: Request, env: Env): Promise<Response> {
    try { return secure(await route(request, env)); }
    catch (error) {
      if (!(error instanceof HttpError)) console.error('Record Shop request failed', error instanceof Error ? error.name : 'unknown');
      if (new URL(request.url).pathname.startsWith('/auth/') && request.headers.get('Accept')?.includes('text/html')) {
        const destination = new URL('/login', env.APP_ORIGIN);
        destination.searchParams.set('error', error instanceof HttpError ? error.message : 'service_unavailable');
        return secure(new Response(null, { status: 302, headers: { Location: destination.href, 'Cache-Control': 'no-store' } }));
      }
      return secure(json({ error: error instanceof HttpError ? error.message : 'service_unavailable' }, error instanceof HttpError ? error.status : 503));
    }
  },
  /** Retains listening for thirty days and expires one-use OAuth/device grants daily. */
  async scheduled(_controller: ScheduledController, env: Env): Promise<void> {
    const now = Date.now();
    await env.DB.batch([
      env.DB.prepare('DELETE FROM oauth_states WHERE expires_at < ?').bind(now),
      env.DB.prepare('DELETE FROM device_grants WHERE expires_at < ?').bind(now),
      env.DB.prepare('DELETE FROM sessions WHERE expires_at < ?').bind(now),
      env.DB.prepare('DELETE FROM listen_events WHERE ended_at < ?').bind(now - 30 * 86400_000),
    ]);
    const orphaned = await env.DB.prepare(`SELECT DISTINCT artwork_key FROM tracks WHERE artwork_key IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM listen_events WHERE track_id = tracks.id) AND created_at < ?`)
      .bind(now - 30 * 86400_000).all<{ artwork_key: string }>();
    await env.DB.prepare(`DELETE FROM tracks WHERE NOT EXISTS
      (SELECT 1 FROM listen_events WHERE track_id = tracks.id) AND created_at < ?`).bind(now - 30 * 86400_000).run();
    for (const row of orphaned.results) {
      const referenced = await env.DB.prepare('SELECT 1 FROM tracks WHERE artwork_key = ? LIMIT 1').bind(row.artwork_key).first();
      if (!referenced) await env.MEDIA.delete(row.artwork_key);
    }
    let cursor:string|undefined;
    do{
      const objects=await env.MEDIA.list({prefix:'avatars/',limit:1000,cursor});
      for(const object of objects.objects){
        if(object.uploaded.getTime()>now-86400_000)continue;
        const referenced=await env.DB.prepare('SELECT id FROM listeners WHERE avatar_key=?').bind(object.key).first();
        if(!referenced)await env.MEDIA.delete(object.key);
      }
      cursor=objects.truncated?objects.cursor:undefined;
    }while(cursor);
  },
};
