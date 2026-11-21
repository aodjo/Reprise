import { DurableObject } from 'cloudflare:workers';

/** A normalized observation of playback; every field remains an untrusted client claim. */
export interface Observation {
  track_id: string;
  title: string;
  artist: string;
  album: string;
  duration_seconds: number | null;
  position_seconds: number | null;
  playing: boolean;
  provider: 'spotify' | 'melon' | 'apple-music' | 'youtube-music' | 'other';
  evidence: 'local_output' | 'remote_state' | 'app_state';
}

/** Only the authenticated Worker supplies device identity to this private actor. */
export interface Heartbeat {
  device_id: string;
  epoch?: string;
  sequence?: number;
  nonce?: string;
  observation: Observation | null;
}

/** Durable lease state, including a one-event outbox for idempotent D1 delivery. */
interface Lease {
  device: string;
  epoch: string;
  nonce: string;
  sequence: number;
  at: number;
  observation: Observation | null;
  requestHash?: string;
  response?: Record<string, unknown>;
}

/** Finalized server-clock interval that is safe to retry after a D1 failure. */
interface OutboxEvent {
  id: string; device: string; from: number; to: number; seconds: number;
  observation: Observation; review: 'accepted' | 'held'; reason: string | null;
}

/** Account state is stored atomically in the actor's SQLite-backed storage. */
interface State {
  listener: string;
  lease?: Lease;
  candidates: Record<string, { since: number; at: number }>;
  pending?: OutboxEvent;
}

/** A lease is only renewed by a valid, sequential owner heartbeat. */
const leaseTimeout = 30_000;
/** A long reporting gap never earns catch-up credit. */
const maximumInterval = 20_000;
/** Minimum heartbeat spacing prevents request floods from earning fractional bonuses. */
const minimumInterval = 5_000;

/** Hashes normalized requests so a network retry can recover the same next nonce. */
async function digest(value: unknown): Promise<string> {
  const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify(value)));
  return [...new Uint8Array(bytes)].map(n => n.toString(16).padStart(2, '0')).join('');
}

/** Coordinates one Reprise account across all reporting computers and providers. */
export class Collector extends DurableObject<{ DB: D1Database }> {
  /** Serializes validation, durable state changes, and outbox delivery for this account. */
  async fetch(request: Request): Promise<Response> {
    return this.ctx.blockConcurrencyWhile(async () => {
      let state = await this.ctx.storage.get<State>('state');
      const listener = request.headers.get('X-Reprise-Listener');
      if (!listener) return Response.json({ error: 'internal_identity_required' }, { status: 403 });
      state ??= { listener, candidates: {} };
      if (state.listener !== listener) return Response.json({ error: 'account_mismatch' }, { status: 403 });
      await this.ctx.storage.setAlarm(Date.now() + 60_000);
      await this.flush(state);
      if (new URL(request.url).pathname === '/revoke') {
        const { device_id: device } = await request.json<{ device_id: string }>();
        if (state.lease?.device === device) delete state.lease;
        delete state.candidates[device];
        await this.ctx.storage.put('state', state);
        return new Response(null, { status: 204 });
      }
      const input = await request.json<Heartbeat>();
      const now = Date.now();
      for (const [id, candidate] of Object.entries(state.candidates)) {
        if (now - candidate.at > leaseTimeout) delete state.candidates[id];
      }
      let lease = state.lease;
      const local = input.observation?.playing && input.observation.evidence === 'local_output';
      if (input.observation?.playing) {
        const candidate = state.candidates[input.device_id];
        state.candidates[input.device_id] = { since: candidate?.since ?? now, at: now };
      } else delete state.candidates[input.device_id];
      const candidate = state.candidates[input.device_id];
      const localTakeover = lease && lease.device !== input.device_id && local
        && lease.observation?.evidence !== 'local_output' && candidate && now - candidate.since >= 10_000;
      const activeTakeover = lease && lease.device !== input.device_id && input.observation?.playing
        && !lease.observation?.playing && candidate && now - candidate.since >= 10_000;
      if ((!lease || now - lease.at > leaseTimeout) && !input.observation?.playing) {
        delete state.lease;
        await this.ctx.storage.put('state', state);
        return Response.json({ role: 'observer', credited_seconds: 0, interval: 10 }, { status: 202 });
      }
      if (!lease || now - lease.at > leaseTimeout || localTakeover || activeTakeover) {
        lease = { device: input.device_id, epoch: crypto.randomUUID(), nonce: crypto.randomUUID(),
          sequence: 1, at: now, observation: input.observation };
        state.lease = lease;
        await this.ctx.storage.put('state', state);
        return Response.json(this.reply(lease, 0));
      }
      if (lease.device !== input.device_id) {
        await this.ctx.storage.put('state', state);
        return Response.json({ role: 'observer', credited_seconds: 0, interval: 10 }, { status: 202 });
      }
      const requestHash = await digest(input);
      if (lease.requestHash === requestHash && lease.response) return Response.json(lease.response);
      if (!input.epoch && !input.nonce && input.sequence === undefined) {
        // Recovering the current lease does not renew it or accrue time.
        return Response.json(this.reply(lease, 0));
      }
      if (input.epoch !== lease.epoch || input.nonce !== lease.nonce || input.sequence !== lease.sequence) {
        return Response.json({ error: 'lease_reset', credited_seconds: 0 }, { status: 409 });
      }
      const elapsed = now - lease.at;
      if (elapsed < minimumInterval) return Response.json({ error: 'slow_down' }, { status: 429 });
      const previous = lease.observation, current = input.observation;
      let credit = 0;
      let review: 'accepted' | 'held' = 'accepted';
      let reason: string | null = null;
      if (elapsed <= maximumInterval && previous?.playing && current?.playing
        && previous.track_id === current.track_id && previous.provider === current.provider
        && previous.evidence === current.evidence && previous.position_seconds !== null && current.position_seconds !== null) {
        const movement = current.position_seconds - previous.position_seconds;
        const seconds = elapsed / 1000;
        // Seeking, stale state, impossible progress and unknown positions receive no credit.
        if (movement > 0 && Math.abs(movement - seconds) <= Math.max(3, seconds * 0.35)) {
          credit = Math.min(seconds, movement);
        } else if (movement < -2 || movement > seconds + 5) {
          review = 'held'; reason = 'position_discontinuity';
        }
      }
      if (current && (credit > 0 || review === 'held')) {
        if (current.provider === 'melon' && current.evidence !== 'local_output') {
          credit = 0; review = 'held'; reason = 'local_output_unconfirmed';
        }
        state.pending = { id: `${lease.epoch}:${lease.sequence}`, device: input.device_id,
          from: lease.at, to: now, seconds: credit || Math.min(elapsed / 1000, 20),
          observation: current, review, reason };
      }
      lease.at = now;
      lease.observation = current;
      lease.sequence += 1;
      lease.nonce = crypto.randomUUID();
      lease.requestHash = requestHash;
      lease.response = this.reply(lease, credit, review === 'held' ? reason : null);
      await this.ctx.storage.put('state', state);
      await this.flush(state);
      return Response.json(lease.response);
    });
  }

  /** Removes inactive observations so a disconnected actor retains no indefinite playback metadata. */
  async alarm(): Promise<void> {
    const state = await this.ctx.storage.get<State>('state');
    if (!state) return;
    const listener = await this.env.DB.prepare('SELECT id FROM listeners WHERE id = ?').bind(state.listener).first();
    if (!listener) { await this.ctx.storage.deleteAll(); return; }
    await this.flush(state);
    if (state.lease && Date.now() - state.lease.at < 60_000) {
      await this.ctx.storage.setAlarm(state.lease.at + 60_000);return;
    }
    await this.ctx.storage.deleteAll();
  }

  /** Returns the next one-use challenge; it is never a claim of provider verification. */
  private reply(lease: Lease, seconds: number, reason: string | null = null): Record<string, unknown> {
    return { role: 'owner', epoch: lease.epoch, nonce: lease.nonce, next_sequence: lease.sequence,
      interval: 10, credited_seconds: seconds, review_reason: reason,
      track_id: lease.observation?.track_id ?? null };
  }

  /** Commits an outbox item with a unique key before acknowledging its durable removal. */
  private async flush(state: State): Promise<void> {
    const event = state.pending;
    if (!event) return;
    const track = event.observation;
    await this.env.DB.batch([
      this.env.DB.prepare(`INSERT INTO tracks (id, title, artist, album, duration_seconds, created_at)
        VALUES (?, ?, ?, ?, ?, ?) ON CONFLICT(id) DO NOTHING`)
        .bind(track.track_id, track.title, track.artist, track.album, track.duration_seconds, event.to),
      this.env.DB.prepare(`INSERT INTO listen_events
        (device_id, event_id, listener_id, track_id, started_at, ended_at, seconds, evidence, provider, review_state, review_reason)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?) ON CONFLICT(device_id, event_id) DO NOTHING`)
        .bind(event.device, event.id, state.listener, track.track_id, event.from, event.to, event.seconds,
          track.evidence, track.provider, event.review, event.reason),
      this.env.DB.prepare('UPDATE devices SET last_seen_at = ? WHERE id = ?').bind(event.to, event.device),
    ]);
    delete state.pending;
    await this.ctx.storage.put('state', state);
  }
}
