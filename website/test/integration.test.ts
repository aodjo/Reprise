import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { readFile, readdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { setTimeout as delay } from 'node:timers/promises';
import { Miniflare } from 'miniflare';
import { encode as encodePng, decode as decodePng } from 'fast-png';

/** Isolated workerd runtime: no fixture reaches the local preview or production database. */
let runtime: Miniflare;
/** Direct test-only D1 access provisions identities without bypasses in the shipped Worker. */
let db: Awaited<ReturnType<Miniflare['getD1Database']>>;
/** Opaque test installation and session secrets never used outside this ephemeral runtime. */
const tokenA = 'a'.repeat(43), tokenB = 'b'.repeat(43), browserSession = 'test-browser-session';
/** Matches the configured test origin for CSRF checks. */
const origin = 'http://localhost:8787';
/** Hashes fixture credentials through the same standardized SHA-256 format. */
function hash(value: string): string { return createHash('sha256').update(value).digest('hex'); }
/** Sends requests through the real Worker entry point. */
async function send(path: string, body?: unknown, token?: string, method = 'POST', session = false): Promise<Response> {
  const headers: Record<string,string> = { Origin: origin };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (token) headers.Authorization = `Bearer ${token}`;
  if (session) headers.Cookie = `reprise_session=${browserSession}`;
  return runtime.dispatchFetch(origin + path,{ method,headers,redirect:'manual',body:body === undefined ? undefined : JSON.stringify(body) }) as unknown as Response;
}
/** Minimal external lease response understood by desktop installations. */
interface Lease { role: string; epoch: string; nonce: string; next_sequence: number; credited_seconds: number; track_id: string }
/** Constructs an observation from fixture playback without sending a claimed elapsed time. */
function observation(position: number, provider = 'spotify', evidence = 'remote_state') {
  return { title:'Integration track',artist:'Integration artist',album:'Integration album',duration_seconds:3600,
    position_seconds:position,playing:true,provider,evidence };
}
/** Forms the exact next one-use challenge request. */
function beat(lease: Lease, position: number) {
  return { epoch:lease.epoch,nonce:lease.nonce,sequence:lease.next_sequence,observation:observation(position) };
}

before(async () => {
  const script = await readFile('dist/worker.js','utf8');
  runtime = new Miniflare({ workers:[{config:{name:'record-shop-test',compatibilityDate:'2026-11-18',
    manifest:{mainModule:'worker.js',modules:{'worker.js':{type:'esm',contents:script}}},
    env:{APP_ORIGIN:{type:'text',value:origin},GOOGLE_CLIENT_ID:{type:'text',value:'test-client'},GOOGLE_CLIENT_SECRET:{type:'text',value:'test-secret'},
      DB:{type:'d1',name:'integration'},MEDIA:{type:'r2',name:'integration'},ASSETS:{type:'assets'},
      COLLECTORS:{type:'durable-object',worker:'record-shop-test',exportName:'Collector'}},
    exports:{Collector:{type:'durable-object',storage:'sqlite'}},
    assets:{directory:new URL('../public',import.meta.url).pathname,hasUserWorker:true,runWorkerFirst:true}}}]});
  db = await runtime.getD1Database('DB');
  for (const file of (await readdir('migrations')).sort()) {
    const sql = await readFile(`migrations/${file}`,'utf8');
    for (const statement of sql.split(';').map(value => value.trim()).filter(Boolean)) await db.prepare(statement).run();
  }
});
after(async () => { await runtime?.dispose(); });

test('empty deployment exposes no sample tracks or fabricated user',async () => {
  const response = await send('/api/charts',undefined,undefined,'GET');
  assert.equal(response.status,200);
  const data = await response.json() as {tracks:unknown[];listeners:unknown[]};
  assert.deepEqual(data.tracks,[]);assert.deepEqual(data.listeners,[]);
  assert.deepEqual(await (await send('/api/me',undefined,undefined,'GET')).json(),{listener:null});
});

test('home, legal pages, fonts and exact Figma assets are served',async () => {
  for (const path of ['/','/example-listener','/profile.css','/listeners','/listeners.css','/tracks','/tracks.css','/assets/chart-search.svg','/login','/connect?code=ABCDEFGH','/connect.css','/connect.js','/login.css','/login.js','/privacy','/terms','/app.js','/legal.js','/assets/logo-header-mark.svg','/assets/logo-footer-word.svg','/assets/wall.png','/assets/vinyl.png','/assets/fonts/IBMPlexMono-Regular.ttf']) {
    const response = await send(path,undefined,undefined,'GET');
    assert.equal(response.status,200,path);assert.ok((await response.arrayBuffer()).byteLength > 0,path);
    assert.equal(response.headers.get('X-Content-Type-Options'),'nosniff');
  }
  const ownProfile=await send('/profile',undefined,undefined,'GET');
  assert.equal(ownProfile.status,302);assert.equal(new URL(ownProfile.headers.get('Location')!,origin).pathname,'/login');
  assert.equal((await send('/api/profile',undefined,undefined,'GET')).status,401);
  for (const path of ['/connect','/connect?lang=ko-KR','/connect?code=','/connect/','/connect.html']) {
    const response = await send(path,undefined,undefined,'GET');
    assert.equal(response.status,302,path);assert.equal(response.headers.get('Location'),'/',path);
  }
  const privacy = await (await send('/privacy',undefined,undefined,'GET')).text();
  assert.ok(privacy.includes('me@junx.dev'));assert.ok(privacy.includes('Junsung Lee'));assert.ok(privacy.includes('data-language="en-US"'));
});

test('Google flow requires terms, uses PKCE/state/nonce, and rejects forged callbacks',async () => {
  assert.equal((await send('/auth/google',undefined,undefined,'GET')).status,400);
  const response = await send('/auth/google?accepted=2026-11-21',undefined,undefined,'GET');
  assert.equal(response.status,302);
  const target = new URL(response.headers.get('Location')!);
  assert.equal(target.hostname,'accounts.google.com');assert.equal(target.searchParams.get('code_challenge_method'),'S256');
  assert.ok(target.searchParams.get('nonce'));assert.ok(response.headers.get('Set-Cookie')?.includes('HttpOnly'));
  assert.equal((await send('/auth/google/callback?state=forged&code=fake',undefined,undefined,'GET')).status,400);
  const browserError = await runtime.dispatchFetch(origin+'/auth/google/callback?state=forged&code=fake',{
    redirect:'manual',headers:{Accept:'text/html'},
  });
  assert.equal(browserError.status,302);
  assert.equal(new URL(browserError.headers.get('Location')!).pathname,'/login');
  assert.equal((await send('/auth/google?accepted=2026-11-21&return_to=https://evil.example',undefined,undefined,'GET')).status,400);
});

test('direct elapsed-time ingestion and unauthenticated collectors are rejected',async () => {
  assert.equal((await send('/api/listens',{seconds:86400})).status,410);
  assert.equal((await send('/api/collector',{observation:observation(0)})).status,401);
  assert.equal((await send('/api/device/approve',{user_code:'ABCDEFGH',consent:true})).status,401);
});

test('listener search preserves global ranks, public visibility and the featured leader',async () => {
  const fixtures=[['search-first','Search Leader',1,3],['search-second','검색 리스너',1,2],['search-hidden','Hidden listener',0,4]] as const;
  const now=Date.now();
  try {
    await db.prepare('INSERT INTO tracks (id,title,artist,album,created_at) VALUES (?,?,?,?,?)')
      .bind('search-track','Search test record','Search test artist','Search test album',now).run();
    for(const [id,name,visible,intervals] of fixtures){
      await db.prepare('INSERT INTO listeners (id,google_sub,handle,display_name,public,created_at) VALUES (?,?,?,?,?,?)').bind(id,id,id,name,visible,now).run();
      await db.prepare('INSERT INTO devices (id,listener_id,token_hash,name,created_at) VALUES (?,?,?,?,?)').bind(id,id,hash(id),id,now).run();
      for(let index=0;index<intervals;index++)await db.prepare('INSERT INTO listen_events (device_id,event_id,listener_id,track_id,started_at,ended_at,seconds) VALUES (?,?,?,?,?,?,30)')
        .bind(id,`${id}-${index}`,id,'search-track',now-(index+1)*30000,now-index*30000).run();
    }
    for(const query of ['검색','@SEARCH-SECOND']){
      const result=await (await send(`/api/charts?featured=listener&listener_q=${encodeURIComponent(query)}`,undefined,undefined,'GET')).json() as {
        listeners:{handle:string;rank:number}[];featured_listener:{handle:string};featured_albums:unknown[];
      };
      assert.equal(result.listeners.length,1);assert.equal(result.listeners[0].handle,'search-second');assert.equal(result.listeners[0].rank,2);
      assert.equal(result.featured_listener.handle,'search-first');assert.equal(result.featured_albums.length,1);
    }
    for(const query of ['hidden','missing-listener','%','_']){
      const result=await (await send(`/api/charts?featured=listener&listener_q=${encodeURIComponent(query)}`,undefined,undefined,'GET')).json() as {listeners:unknown[];featured_listener:{handle:string}};
      assert.deepEqual(result.listeners,[]);assert.equal(result.featured_listener.handle,'search-first');
    }
    const leader=await (await send('/api/charts?listener_q=LEADER',undefined,undefined,'GET')).json() as {listeners:{rank:number}[]};
    assert.equal(leader.listeners.length,1);assert.equal(leader.listeners[0].rank,1);
    const totals=await (await send('/api/charts?scope=tracks',undefined,undefined,'GET')).json() as {tracks:{listeners:number;seconds:number}[]};
    assert.equal(totals.tracks[0].listeners,3,'Private listeners still contribute once to anonymous track totals');
    assert.equal(totals.tracks[0].seconds,270);
    const privateProfile=await (await send('/api/listeners/search-hidden?scrobbles=all',undefined,undefined,'GET')).json() as Record<string,unknown>;
    assert.equal((privateProfile.listener as {handle:string}).handle,'search-hidden');
    assert.equal(privateProfile.history_visible,false);assert.equal(privateProfile.stats,null);assert.equal(privateProfile.period,null);
    for(const field of ['recent','daily','albums'])assert.deepEqual(privateProfile[field],[]);
    assert.equal(privateProfile.next_cursor,null);
    for(const id of ['search-hidden','search-first'])await db.prepare('INSERT INTO sessions VALUES (?,?,?)').bind(hash(id+'-session'),id,now+600000).run();
    for(const [viewer,visible] of [['search-hidden',true],['search-first',false]] as const){
      const response=await runtime.dispatchFetch(origin+'/api/listeners/search-hidden?scrobbles=all',{
        headers:{Cookie:`reprise_session=${viewer}-session`},
      });
      assert.equal(response.status,200);
      const data=await response.json() as {history_visible:boolean;stats:{seconds:number}|null;recent:unknown[]};
      assert.equal(data.history_visible,visible);
      if(visible){assert.equal(data.stats!.seconds,120);assert.equal(data.recent.length,1);}
      else{assert.equal(data.stats,null);assert.deepEqual(data.recent,[]);}
    }
    for(let index=0;index<30;index++){
      const end=now-8*86400_000-index*60000;
      await db.prepare('INSERT INTO listen_events (device_id,event_id,listener_id,track_id,started_at,ended_at,seconds) VALUES (?,?,?,?,?,?,30)')
        .bind('search-first',`history-${index}`,'search-first','search-track',end-30000,end).run();
    }
    const expired=now-31*86400_000;
    await db.prepare('INSERT INTO listen_events (device_id,event_id,listener_id,track_id,started_at,ended_at,seconds) VALUES (?,?,?,?,?,?,30)')
      .bind('search-first','expired-history','search-first','search-track',expired-30000,expired).run();
    const normal=await (await send('/api/listeners/search-first',undefined,undefined,'GET')).json() as {recent:unknown[]};
    assert.equal(normal.recent.length,1,'The profile keeps its recent seven-day preview');
    const first=await (await send('/api/listeners/search-first?scrobbles=all',undefined,undefined,'GET')).json() as {recent:{ended_at:number}[];next_cursor:number};
    assert.equal(first.recent.length,25);assert.ok(first.next_cursor>0);
    const second=await (await send(`/api/listeners/search-first?scrobbles=all&before=${first.next_cursor}`,undefined,undefined,'GET')).json() as {recent:{ended_at:number}[];next_cursor:null};
    assert.equal(second.recent.length,6);assert.equal(second.next_cursor,null);
    assert.equal(new Set([...first.recent,...second.recent].map(play=>play.ended_at)).size,31,'Paging neither repeats nor skips playbacks and excludes expired records');
    assert.equal((await send('/api/listeners/search-first?scrobbles=all&before=invalid',undefined,undefined,'GET')).status,400);
  } finally {
    for(const [id] of fixtures)await db.prepare('DELETE FROM listeners WHERE id=?').bind(id).run();
    await db.prepare('DELETE FROM tracks WHERE id=?').bind('search-track').run();
  }
});

test('track scrolling returns bounded stable pages with Unicode search and deterministic ties',async () => {
  const now=Date.now(),search='페이지';
  try{
    await db.prepare('INSERT INTO listeners (id,google_sub,handle,display_name,public,created_at) VALUES (?,?,?,?,1,?)')
      .bind('page-owner','page-owner','page-owner','Page owner',now).run();
    await db.prepare('INSERT INTO devices (id,listener_id,token_hash,name,created_at) VALUES (?,?,?,?,?)')
      .bind('page-device','page-owner',hash('page-device'),'Page device',now).run();
    for(let index=0;index<35;index++){
      const id=`page-${String(index).padStart(2,'0')}`;
      await db.prepare('INSERT INTO tracks (id,title,artist,album,created_at) VALUES (?,?,?,?,?)').bind(id,`${search} ${index}`,'Page artist','Page album',now).run();
      await db.prepare('INSERT INTO listen_events (device_id,event_id,listener_id,track_id,started_at,ended_at,seconds) VALUES (?,?,?,?,?,?,30)')
        .bind('page-device',id,'page-owner',id,now-40000,now-10000).run();
    }
    const first=await (await send(`/api/charts?scope=tracks&limit=20&q=${encodeURIComponent(search)}`,undefined,undefined,'GET')).json() as {
      tracks:{id:string;rank:number}[];next_track_cursor:string;listeners:unknown[];period:{from:number;to:number};
    };
    assert.equal(first.tracks.length,20);assert.deepEqual(first.listeners,[]);assert.ok(first.next_track_cursor);
    assert.deepEqual(first.tracks.map(track=>track.rank),Array.from({length:20},(_,index)=>index+1));
    await db.prepare('INSERT INTO tracks (id,title,artist,album,created_at) VALUES (?,?,?,?,?)').bind('page-added',`${search} added`,'Page artist','Page album',now).run();
    for(const id of ['page-added','page-34'])await db.prepare('INSERT INTO listen_events (device_id,event_id,listener_id,track_id,started_at,ended_at,seconds) VALUES (?,?,?,?,?,?,30)')
      .bind('page-device',`new-${id}`,'page-owner',id,first.period.to-29999,first.period.to+1).run();
    const second=await (await send(`/api/charts?scope=tracks&limit=20&q=${encodeURIComponent(search)}&track_cursor=${encodeURIComponent(first.next_track_cursor)}`,undefined,undefined,'GET')).json() as typeof first;
    assert.equal(second.tracks.length,15);assert.equal(second.next_track_cursor,null);assert.deepEqual(second.period,first.period);
    const combined=[...first.tracks,...second.tracks];assert.equal(new Set(combined.map(track=>track.id)).size,35);
    assert.deepEqual(combined.map(track=>track.id),Array.from({length:35},(_,index)=>`page-${String(index).padStart(2,'0')}`));
    assert.equal((await send(`/api/charts?q=different&track_cursor=${encodeURIComponent(first.next_track_cursor)}`,undefined,undefined,'GET')).status,400);
    assert.equal((await send('/api/charts?track_cursor=invalid',undefined,undefined,'GET')).status,400);
    const empty=await (await send('/api/charts?scope=tracks&q=not-a-track',undefined,undefined,'GET')).json() as typeof first;
    assert.deepEqual(empty.tracks,[]);assert.equal(empty.next_track_cursor,null);
  }finally{
    await db.prepare('DELETE FROM listeners WHERE id=?').bind('page-owner').run();
    await db.prepare("DELETE FROM tracks WHERE id LIKE 'page-%'").run();
  }
});

test('device consent, one-time exchange, server clock, replay resistance and source handoff', {timeout:120_000},async t => {
  await db.prepare('INSERT INTO listeners (id,google_sub,handle,display_name,public,created_at) VALUES (?,?,?,?,1,?)')
    .bind('listener-test','google-test','integration','Integration listener',Date.now()).run();
  await db.prepare('INSERT INTO sessions VALUES (?,?,?)').bind(hash(browserSession),'listener-test',Date.now()+600000).run();
  for (const [id,token] of [['device-a',tokenA],['device-b',tokenB]]) await db.prepare('INSERT INTO devices (id,listener_id,token_hash,name,created_at) VALUES (?,?,?,?,?)')
    .bind(id,'listener-test',hash(token),'Test installation',Date.now()).run();

  await t.test('browser writes require origin and explicit consent',async () => {
    const forged = await runtime.dispatchFetch(origin+'/api/profile',{method:'PATCH',headers:{Cookie:`reprise_session=${browserSession}`,'Content-Type':'application/json'},body:'{}'});
    assert.equal(forged.status,403);
    assert.equal((await send('/api/profile',{handle:'integration',display_name:'Integration listener',public:false},undefined,'PATCH',true)).status,200);
    const grant = await (await send('/api/device/start',{name:'Pairing test'})).json() as {device_code:string;user_code:string};
    assert.equal((await send('/api/device/approve',{user_code:grant.user_code,consent:false},undefined,'POST',true)).status,400);
    assert.equal((await send('/api/device/approve',{user_code:grant.user_code,consent:true},undefined,'POST',true)).status,200);
    const identity=await (await send('/api/me',undefined,undefined,'GET',true)).json() as {listener:{public:number}};
    assert.equal(identity.listener.public,0,'Connecting an installation must not republish a private profile');
    const exchange = await send('/api/device/token',{device_code:grant.device_code});assert.equal(exchange.status,200);
    assert.equal((await send('/api/device/token',{device_code:grant.device_code})).status,410);
    assert.equal((await send('/api/profile',{handle:'integration',display_name:'Integration listener',public:true},undefined,'PATCH',true)).status,200);
  });

  const idle = await send('/api/collector',{observation:null},tokenB);
  assert.equal((await idle.json() as Lease).role,'observer','An idle installation cannot reserve the account');
  let lease = await (await send('/api/collector',{observation:observation(0)},tokenA)).json() as Lease;
  let avatarURL='';
  assert.equal(lease.role,'owner');assert.equal(lease.credited_seconds,0);
  const observer = await send('/api/collector',{observation:observation(0)},tokenB);
  assert.equal(observer.status,202);assert.equal((await observer.json() as Lease).role,'observer');
  assert.equal((await send('/api/collector',beat(lease,1),tokenA)).status,429);
  assert.equal((await send('/api/collector',{...beat(lease,1),nonce:'forged'},tokenA)).status,409);
  let position = 0;
  for (let i=0;i<3;i++) {
    await delay(10_150);position += 10.15;
    const payload = beat(lease,position), response = await send('/api/collector',payload,tokenA);
    assert.equal(response.status,200);lease = await response.json() as Lease;
    assert.ok(lease.credited_seconds > 9 && lease.credited_seconds <= 11);
    const replay = await (await send('/api/collector',payload,tokenA)).json() as Lease;
    assert.deepEqual(replay,lease,'A network retry returns the same challenge without another write');
  }
  await t.test('D1 aggregates one account, despite replay and mirrored reports',async () => {
    const events = await db.prepare("SELECT COUNT(*) AS count,SUM(seconds) AS seconds FROM listen_events WHERE review_state='accepted'").first<{count:number;seconds:number}>();
    assert.equal(events?.count,3);assert.ok(events!.seconds >= 30 && events!.seconds < 33);
    const charts = await (await send('/api/charts',undefined,undefined,'GET')).json() as {tracks:{listeners:number;title:string}[]};
    assert.equal(charts.tracks[0].listeners,1);assert.equal(charts.tracks[0].title,'Integration track');
    const search = await (await send('/api/charts?q=INTEGRATION%20ARTIST',undefined,undefined,'GET')).json() as {tracks:{rank:number;title:string}[]};
    assert.equal(search.tracks[0].title,'Integration track');assert.equal(search.tracks[0].rank,1);
    for (const query of ['missing-track', '%', '_']) {
      const result = await (await send(`/api/charts?q=${encodeURIComponent(query)}`,undefined,undefined,'GET')).json() as {tracks:unknown[]};
      assert.deepEqual(result.tracks,[],'Search matches literal titles and artists without wildcard expansion');
    }
    const featured = await (await send('/api/charts?featured=listener',undefined,undefined,'GET')).json() as {featured_albums:{album:string;artist:string;seconds:number;artwork_url:string|null}[]};
    assert.equal(featured.featured_albums.length,1);
    assert.equal(featured.featured_albums[0].album,'Integration album');assert.equal(featured.featured_albums[0].artist,'Integration artist');
    assert.ok(featured.featured_albums[0].seconds>=30);assert.equal(featured.featured_albums[0].artwork_url,null);
    const profile=await (await send('/api/listeners/integration',undefined,undefined,'GET')).json() as {
      stats:{seconds:number;previous_seconds:number;rank:number};recent:{title:string;seconds:number}[];daily:{seconds:number;date:string}[];albums:{album:string}[];
    };
    assert.ok(profile.stats.seconds>=30);assert.equal(profile.stats.previous_seconds,0);assert.equal(profile.stats.rank,1);
    assert.equal(profile.recent.length,1,'Heartbeats form one continuous scrobble');assert.equal(profile.recent[0].title,'Integration track');
    assert.equal(profile.daily.length,7);assert.ok(profile.daily.some(day=>day.seconds>=30));assert.equal(profile.albums[0].album,'Integration album');
    assert.equal((await send('/api/profile',{handle:'listeners',display_name:'Integration listener',public:true},undefined,'PATCH',true)).status,409);
  });
  await t.test('R2 stores validated real bytes, rejecting arbitrary uploads',async () => {
    assert.equal((await runtime.dispatchFetch(origin+`/api/artwork/${lease.track_id}`,{method:'PUT',headers:{Authorization:`Bearer ${tokenA}`},body:'<svg/>'})).status,415);
    const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jR1sAAAAASUVORK5CYII=','base64');
    const uploaded = await runtime.dispatchFetch(origin+`/api/artwork/${lease.track_id}`,{method:'PUT',headers:{Authorization:`Bearer ${tokenA}`},body:png});
    assert.equal(uploaded.status,200);const {artwork_url:url} = await uploaded.json() as {artwork_url:string};
    const image = await send(url,undefined,undefined,'GET');assert.equal(image.status,200);assert.deepEqual(Buffer.from(await image.arrayBuffer()),png);
  });
  await t.test('profile images validate pixels, strip metadata, replace and revoke old R2 objects',async () => {
    const pixels=new Uint8Array([255,0,0,255,0,255,0,255,0,0,255,255,255,255,255,255]);
    const png=encodePng({width:2,height:2,channels:4,data:pixels,text:{Comment:'not-to-be-stored'}});
    /** Uses the real browser upload endpoint with an ephemeral authenticated session. */
    const upload=(body:Uint8Array|string,session=true,requestOrigin=origin)=>runtime.dispatchFetch(origin+'/api/profile/avatar',{
      method:'PUT',headers:{Origin:requestOrigin,...(session?{Cookie:`reprise_session=${browserSession}`}:{})},body,
    });
    assert.equal((await upload(png,false)).status,401);assert.equal((await upload(png,true,'https://evil.example')).status,403);
    assert.equal((await upload('<svg xmlns="http://www.w3.org/2000/svg"/>')).status,415);
    assert.equal((await upload(png.slice(0,30))).status,415);
    assert.equal((await upload(new Uint8Array(2*1024*1024+1))).status,413);
    const oversized=png.slice();new DataView(oversized.buffer,oversized.byteOffset,oversized.byteLength).setUint32(16,513);
    assert.equal((await upload(oversized)).status,415);
    const first=await upload(png);assert.equal(first.status,200);
    const oldURL=(await first.json() as {avatar_url:string}).avatar_url;
    const photo=await send(oldURL,undefined,undefined,'GET');assert.equal(photo.status,200);
    const decoded=decodePng(new Uint8Array(await photo.arrayBuffer()));
    assert.deepEqual(decoded.data,pixels);assert.deepEqual(decoded.text,{});
    const second=await upload(png);assert.equal(second.status,200);avatarURL=(await second.json() as {avatar_url:string}).avatar_url;
    assert.notEqual(avatarURL,oldURL);assert.equal((await send(oldURL,undefined,undefined,'GET')).status,404);
    assert.equal(await (await runtime.getR2Bucket('MEDIA')).get(oldURL.slice(7)),null);
    const identity=await (await send('/api/me',undefined,undefined,'GET',true)).json() as {listener:{avatar_url:string}};
    const profile=await (await send('/api/listeners/integration',undefined,undefined,'GET')).json() as {listener:{avatar_url:string}};
    const chart=await (await send('/api/charts?featured=listener',undefined,undefined,'GET')).json() as {listeners:{avatar_url:string}[];featured_listener:{avatar_url:string}};
    assert.equal(identity.listener.avatar_url,avatarURL);assert.equal(profile.listener.avatar_url,avatarURL);
    assert.equal(chart.listeners[0].avatar_url,avatarURL);assert.equal(chart.featured_listener.avatar_url,avatarURL);
    assert.equal((await send('/api/profile/avatar',undefined,undefined,'DELETE',true)).status,204);
    assert.equal((await send(avatarURL,undefined,undefined,'GET')).status,404);
    assert.equal(await (await runtime.getR2Bucket('MEDIA')).get(avatarURL.slice(7)),null);
    const removed=await (await send('/api/me',undefined,undefined,'GET',true)).json() as {listener:{avatar_url:null}};
    assert.equal(removed.listener.avatar_url,null);
    const restored=await upload(png);assert.equal(restored.status,200);avatarURL=(await restored.json() as {avatar_url:string}).avatar_url;
  });
  await delay(5_100);
  let result = await send('/api/collector',beat(lease,1000),tokenA);assert.equal(result.status,200);
  lease = await result.json() as Lease;assert.equal(lease.credited_seconds,0);
  assert.equal((await db.prepare("SELECT COUNT(*) AS count FROM listen_events WHERE review_state='held'").first<{count:number}>())?.count,1);
  const local = {observation:observation(0,'melon','local_output')};
  assert.equal((await send('/api/collector',local,tokenB)).status,202);
  await delay(10_150);
  result = await send('/api/collector',{observation:observation(10.15,'melon','local_output')},tokenB);
  const localLease = await result.json() as Lease;assert.equal(localLease.role,'owner');assert.equal(localLease.credited_seconds,0);
  assert.equal((await send('/api/collector',beat(lease,1005),tokenA)).status,202,'Late old-owner reports cannot reclaim the lease');
  await t.test('private profiles preserve anonymous totals and collection while protecting identity and personal history',async () => {
    const baseline=await (await send('/api/charts',undefined,undefined,'GET')).json() as {tracks:{id:string;listeners:number;seconds:number}[]};
    assert.equal((await send('/api/profile',{handle:'integration',display_name:'Integration listener',public:false},undefined,'PATCH',true)).status,200);
    const hidden = await (await send('/api/charts?featured=listener',undefined,undefined,'GET')).json() as {listeners:unknown[];featured_albums:unknown[];tracks:{id:string;listeners:number;seconds:number}[]};
    assert.deepEqual(hidden.listeners,[]);assert.deepEqual(hidden.featured_albums,[]);
    assert.deepEqual(hidden.tracks,baseline.tracks,'Changing profile visibility must not remove already counted tracks or alter their totals');
    for(const path of ['/api/listeners/integration','/api/listeners/integration?scrobbles=all']){
      const response=await send(path,undefined,undefined,'GET');assert.equal(response.status,200);
      const data=await response.json() as {listener:{handle:string};history_visible:boolean;stats:null;recent:unknown[];daily:unknown[];albums:unknown[];next_cursor:null;period:null};
      assert.equal(data.listener.handle,'integration');assert.equal(data.history_visible,false);assert.equal(data.stats,null);
      assert.deepEqual(data.recent,[]);assert.deepEqual(data.daily,[]);assert.deepEqual(data.albums,[]);assert.equal(data.next_cursor,null);assert.equal(data.period,null);
    }
    assert.equal((await send('/api/listeners/integration',undefined,undefined,'GET',true)).status,200);
    assert.equal((await send('/api/listeners/integration?scrobbles=all',undefined,undefined,'GET',true)).status,200);
    assert.equal((await send('/api/profile?scrobbles=all',undefined,undefined,'GET',true)).status,200);
    const own=await send('/api/profile',undefined,undefined,'GET',true);assert.equal(own.status,200);
    const ownData=await own.json() as {stats:{rank:number|null};recent:{artwork_url:string|null}[]};
    assert.equal(ownData.stats.rank,null);
    assert.equal((await send(avatarURL,undefined,undefined,'GET')).status,200,'Profile images remain public when listening statistics are private');
    const ownAvatar=await send(avatarURL,undefined,undefined,'GET',true);assert.equal(ownAvatar.status,200);
    assert.equal(ownAvatar.headers.get('Cache-Control'),'private, no-store');
    const privateCover=ownData.recent[0].artwork_url!;
    const rankedCover=await send(privateCover,undefined,undefined,'GET');assert.equal(rankedCover.status,200);
    assert.equal(rankedCover.headers.get('Cache-Control'),'public, max-age=3600','Anonymous chart entries retain their album covers');
    const visibleCover=await send(privateCover,undefined,undefined,'GET',true);assert.equal(visibleCover.status,200);
    assert.equal(visibleCover.headers.get('Cache-Control'),'public, max-age=3600');
    await delay(10_150);
    const continued=await send('/api/collector',{...beat(localLease,20.3),observation:observation(20.3,'melon','local_output')},tokenB);
    assert.equal(continued.status,200);
    const credited=await continued.json() as Lease;assert.ok(credited.credited_seconds>9,'Verified playback keeps accruing while the profile is private');
    const updated=await (await send('/api/charts',undefined,undefined,'GET')).json() as {tracks:{listeners:number;seconds:number}[];listeners:unknown[]};
    assert.equal(updated.tracks[0].listeners,1);assert.ok(updated.tracks[0].seconds>baseline.tracks[0].seconds);assert.deepEqual(updated.listeners,[]);
    for(const [id,seconds,age,review] of [['short-private',10,0,'accepted'],['old-private',30,8*86400_000,'accepted'],['held-private',30,0,'held']] as const){
      const key=`artwork/${hash(id)}.png`,end=Date.now()-age;
      await db.prepare('INSERT INTO tracks (id,title,artist,album,artwork_key,created_at) VALUES (?,?,?,?,?,?)').bind(id,id,'Private artist','Private album',key,end).run();
      await db.prepare('INSERT INTO listen_events (device_id,event_id,listener_id,track_id,started_at,ended_at,seconds,review_state) VALUES (?,?,?,?,?,?,?,?)')
        .bind('device-b',id,'listener-test',id,end-seconds*1000,end,seconds,review).run();
      await (await runtime.getR2Bucket('MEDIA')).put(key,encodePng({width:1,height:1,channels:4,depth:8,data:new Uint8Array([1,2,3,255])}),{httpMetadata:{contentType:'image/png'}});
      assert.equal((await send('/media/'+key,undefined,undefined,'GET')).status,404,'Unranked private covers remain owner-only');
      const ownerCover=await send('/media/'+key,undefined,undefined,'GET',true);assert.equal(ownerCover.status,200);assert.equal(ownerCover.headers.get('Cache-Control'),'private, no-store');
    }
    const eligible=await (await send('/api/charts',undefined,undefined,'GET')).json() as {tracks:{id:string}[]};
    assert.deepEqual(eligible.tracks.map(track=>track.id),[baseline.tracks[0].id],'Private totals still exclude short, old and held playback');
    assert.equal((await send('/api/account',{confirm:'integration'},undefined,'DELETE',true)).status,204);
    assert.equal((await send(avatarURL,undefined,undefined,'GET')).status,404);
    assert.equal(await (await runtime.getR2Bucket('MEDIA')).get(avatarURL.slice(7)),null);
    assert.equal((await send('/api/collector',local,tokenB)).status,401);
    assert.equal((await db.prepare('SELECT COUNT(*) AS count FROM listen_events').first<{count:number}>())?.count,0);
  });
});
