/** Supported interface locales. */
type Language = 'ko-KR' | 'en-US';
/** Public profile fields returned by the Worker. */
interface Listener { id: string; handle: string; display_name: string; public?: number; seconds: number; rank?: number; avatar_url?:string|null }
/** A ranked track backed by accepted D1 intervals. */
interface Track { id: string; title: string; artist: string; album: string; artwork_url: string | null; listeners: number; rank: number }
/** Complete chart response, including the server's rolling window. */
interface Charts { tracks: Track[]; next_track_cursor:string|null; listeners: Listener[]; featured_listener?: Listener|null; featured_albums?: Album[]; period: { from: number; to: number } }
/** Album totals and artwork derived from the leading public listener's accepted intervals. */
interface Album { album: string; artist: string; seconds: number; artwork_url: string | null }
/** Public deployment readiness and request country, without credentials. */
interface Configuration { google_ready: boolean; country: string | null }
/** Optional signed-in identity. */
interface Identity { listener: Listener | null }
/** Account-owned connected installations. */
interface DeviceList { devices: { id: string; name: string }[] }
/** Public profile statistics. */
interface Profile {
  listener: Listener; history_visible:boolean; stats: { seconds: number; previous_seconds: number; tracks: number; rank: number|null }|null;
  recent: Scrobble[]; next_cursor:number|null; daily: {date:string;seconds:number}[]; albums: Album[];
}
/** One continuous accepted playback, rather than an individual ten-second heartbeat. */
interface Scrobble { id:string; title:string; artist:string; album:string; artwork_url:string|null; started_at:number; ended_at:number; seconds:number }
/** Requires an expected document node instead of silently ignoring broken markup. */
function query<T extends HTMLElement = HTMLElement>(selector: string): T {
  const node = document.querySelector<T>(selector);
  if (!node) throw new Error(`Missing interface element: ${selector}`);
  return node;
}

/** Human-readable interface strings; chart contents always come from the live API. */
const messages: Record<Language, Record<string, string>> = {
  'ko-KR': {
    skip:'차트로 건너뛰기',trackNav:'곡 차트',listenerNav:'리스너 차트',profile:'내 프로필',signIn:'로그인',
    featured:'이번 주 가장 많이 들은 곡',emptyFeature:'아무도 청취하지 않았어요.',listenerUnit:'명의 리스너',tracks:'곡 순위',
    tracksDescription:'최근 7일간 곡을 들은 리스너 수',listeners:'리스너 순위',listenersDescription:'최근 7일 청취 시간',
    more:'더보기',allListeners:'전체 리스너 보기',introduction:'메뉴바 음악 컨트롤러, Reprise.',privacy:'개인정보 처리방침',
    language:'언어',terms:'이용약관',agreement:'만 14세 이상이며 이용약관에 동의하고 개인정보 처리방침을 확인했습니다.',deleteAccount:'계정 삭제',deleteConfirm:'삭제하려면 사용자 이름을 입력하세요. 계정과 청취 기록이 삭제됩니다.',loading:'차트를 불러오고 있어요.',emptyTracks:'아직 집계된 곡이 없어요.',emptyListeners:'첫 번째 리스너를 기다리고 있어요.',
    emptyArtist:'Reprise에서 음악을 듣고 기록을 남겨보세요.',retry:'다시 시도',unavailable:'차트를 불러오지 못했어요. 잠시 후 다시 시도해 주세요.',
    google:'Google로 계속하기',loginDescription:'Google 계정으로 로그인하고 Reprise 앱을 연결하세요.',
    loginUnavailable:'Google 로그인 설정이 아직 완료되지 않았어요.',close:'닫기',minutes:'분',trackUnit:'곡',
    handle:'사용자 이름',displayName:'표시 이름',publicProfile:'청취 기록과 통계 공개',privateAggregation:'프로필은 계속 공개되며, 꺼도 내 청취 기록 저장과 곡 집계는 유지됩니다.',save:'저장',saved:'저장했어요.',
    signOut:'로그아웃',devices:'연결된 기기',disconnect:'연결 해제',noDevices:'Reprise 앱의 설정에서 Google 계정을 연결하세요.',noDevicesTitle:'아직 연결된 기기가 없어요',cancel:'취소',
    profileError:'프로필을 불러오지 못했어요.',formError:'요청을 처리하지 못했어요. 다시 시도해 주세요.',handleUnavailable:'이미 사용 중인 사용자 이름이에요.',
    invalidProfile:'사용자 이름은 영문 소문자·숫자·밑줄·하이픈으로 3~30자여야 해요.',
    connect:'Reprise 연결',connectDescription:'연결할 Reprise 앱에 표시된 코드인지 확인해 주세요.',code:'연결 코드',
    consent:'이 기기에서 재생 정보를 전송하고 곡 차트 집계에 참여하는 데 동의합니다. 프로필과 청취 기록의 공개 여부는 별도로 설정합니다.',
    approve:'이 기기 연결',approved:'연결했어요. Reprise 앱으로 돌아가 주세요.',expired:'연결 코드가 만료되었거나 유효하지 않아요. 앱에서 새 코드를 받아주세요.',
    consentRequired:'청취 기록 전송과 곡 집계 동의가 필요해요.',recentTime:'최근 7일 청취 시간',recentTracks:'최근 7일 들은 곡',
    privacyIntro:'이 방침은 reprise.mx 웹사이트와 선택적으로 연결한 Reprise 청취 기록 기능에 적용됩니다.',
    privacyIdentity:'Google 로그인 식별자, 표시 이름, 선택한 사용자 이름을 저장합니다. Google 비밀번호와 음악 서비스 로그인 정보는 수집하지 않습니다.',
    privacyPlayback:'앱 연결에 동의한 경우 곡명, 아티스트, 앨범, 재생 위치·상태, 재생 출처 및 기기 연결 정보를 처리합니다. 앨범 이미지는 R2에 저장합니다. 오디오 녹음은 전송하지 않습니다.',
    privacyPublic:'표시 이름, 사용자 이름과 프로필 이미지는 공개됩니다. 공개 설정을 켜면 청취 기록과 통계도 공개됩니다. 곡별 리스너 수와 청취 시간은 공개 설정과 관계없이 개인을 식별하는 정보 없이 합산됩니다. 기기 이름과 로그인 식별자는 공개하지 않습니다.',
    privacyRetention:'청취 구간은 30일간 보관하고 정기 삭제합니다. 차트는 최근 7일을 집계합니다. 공개를 끄면 리스너 순위에서 제외되고 청취 기록과 통계는 본인만 볼 수 있습니다. 프로필과 곡 집계는 유지됩니다. 기기 연결을 해제하면 해당 기기의 전송이 차단됩니다.',
    privacyStorage:'웹 브라우저에는 언어 선택을 저장합니다. 로그인 세션은 JavaScript가 읽을 수 없는 쿠키로 관리합니다. 데이터 처리는 Google 로그인과 Cloudflare 인프라를 이용합니다.',
    privacyContact:'개인정보 관련 문의와 삭제 요청은 GitHub의 Reprise 저장소 이슈로 접수할 수 있습니다.',
    privacyData:'처리하는 정보',privacyControl:'공개 범위와 보관',privacyBrowser:'브라우저와 서비스',cancelled:'Google 로그인이 취소되었어요.',
    emptyMore:'현재 집계된 순위를 모두 표시하고 있어요.',
    searchTracks:'곡 제목 또는 아티스트 검색',noSearchResults:'검색 결과가 없어요.',
    searchListeners:'표시 이름 또는 사용자 이름 검색',noListenerResults:'검색 결과가 없어요.',
    allScrobbles:'전체 스크로블',viewAll:'전체 보기',backToProfile:'프로필 보기',loadingMore:'불러오는 중…',
    changeAvatar:'이미지 변경',removeAvatar:'이미지 삭제',avatarHint:'JPG, PNG, WebP · 최대 5MB',avatarUploading:'업로드 중…',avatarSaved:'프로필 이미지를 변경했어요.',avatarRemoved:'기본 이미지로 변경했어요.',avatarFormat:'JPG, PNG 또는 WebP 이미지를 선택해 주세요.',avatarTooLarge:'5MB 이하의 이미지를 선택해 주세요.',avatarInvalid:'이미지를 읽지 못했어요. 다른 이미지를 선택해 주세요.',avatarChanged:'이미지가 다른 요청에서 변경되었어요. 프로필을 다시 열어 시도해 주세요.',
    weeklyListeners:'주간 리스너 순위',leaderAlbums:'1위 리스너의 음반',viewProfile:'프로필 보기 →',firstPlace:'1위',listeningTime:'청취 시간',noAlbums:'아직 집계된 음반이 없어요.',
    recentScrobbles:'최근 스크로블',dailyListening:'일별 청취 시간(분)',favouriteAlbums:'자주 듣는 음반',editProfile:'프로필 편집',vsLastWeek:'전주 대비',noPreviousWeek:'전주 기록 없음',today:'오늘',yesterday:'어제',noScrobbles:'아직 기록된 스크로블이 없어요.',profileNotFound:'프로필을 찾을 수 없어요.',privateProfile:'청취 기록 비공개',historyHidden:'청취 기록과 통계를 공개하지 않았어요.',
  },
  'en-US': {
    skip:'Skip to charts',trackNav:'Track chart',listenerNav:'Listener chart',profile:'My profile',signIn:'Sign in',
    featured:'This week’s most listened-to track',emptyFeature:'A quiet record shop, for now',listenerUnit:'listeners',tracks:'Track chart',
    tracksDescription:'Listeners who played each track in the last 7 days',listeners:'Listener chart',listenersDescription:'Listening time in the last 7 days',
    more:'View more',allListeners:'View all listeners',introduction:'Your menu bar music controller, Reprise.',privacy:'Privacy policy',
    language:'Language',terms:'Terms of service',agreement:'I am at least 14, agree to the Terms of service and have read the Privacy policy.',deleteAccount:'Delete account',deleteConfirm:'Enter your username to delete your account and listening records.',loading:'Loading the charts.',emptyTracks:'No tracks have been counted yet.',emptyListeners:'Waiting for our first listener.',
    emptyArtist:'Listen with Reprise to start your record.',retry:'Try again',unavailable:'The charts could not be loaded. Please try again shortly.',
    google:'Continue with Google',loginDescription:'Sign in with Google and connect your Reprise app.',
    loginUnavailable:'Google sign-in has not been configured yet.',close:'Close',minutes:'min',trackUnit:'tracks',
    handle:'Username',displayName:'Display name',publicProfile:'Share my listening history and statistics',privateAggregation:'Your profile stays public. Private listening history and anonymous track totals continue.',save:'Save',saved:'Saved.',
    signOut:'Sign out',devices:'Connected devices',disconnect:'Disconnect',noDevices:'Connect your Google account in Reprise app settings.',noDevicesTitle:'No devices connected yet',cancel:'Cancel',
    profileError:'This profile could not be loaded.',formError:'The request could not be completed. Please try again.',handleUnavailable:'This username is already taken.',
    invalidProfile:'Use 3–30 lowercase letters, numbers, underscores or hyphens for your username.',
    connect:'Connect Reprise',connectDescription:'Check that this code matches the one shown in your Reprise app.',code:'Connection code',
    consent:'I agree to send playback information from this device and contribute to track totals. Profile and listening-history visibility are controlled separately.',
    approve:'Connect this device',approved:'Connected. You can return to Reprise.',expired:'This code is invalid or has expired. Request a new code in the app.',
    consentRequired:'Consent to sending playback information and contributing to track totals is required.',recentTime:'Listening time · last 7 days',recentTracks:'Tracks · last 7 days',
    privacyIntro:'This policy covers reprise.mx and the optional Reprise listening-history connection.',
    privacyIdentity:'We store your Google sign-in identifier, display name and chosen username. We do not collect your Google password or music-service credentials.',
    privacyPlayback:'After you approve an app connection, we process track titles, artists, albums, playback positions and states, playback sources and device connection information. Album artwork is stored in R2. Audio recordings are not transmitted.',
    privacyPublic:'Your display name, username and profile image stay public. Public sharing additionally exposes your listening history and statistics. Track listener counts and listening-time totals include private activity without identifying the listener. Device names and sign-in identifiers are not public.',
    privacyRetention:'Listening intervals are retained for 30 days and deleted on a regular schedule. Charts cover the last 7 days. Turning off sharing keeps your profile public while restricting listening history and statistics to you and hiding your listener ranking. Anonymous track totals continue. Disconnecting a device stops its further reports.',
    privacyStorage:'Your language choice is stored in your browser. Sign-in sessions use cookies inaccessible to JavaScript. Processing uses Google sign-in and Cloudflare infrastructure.',
    privacyContact:'For privacy questions or deletion requests, open an issue in the Reprise repository on GitHub.',
    privacyData:'Information we process',privacyControl:'Sharing and retention',privacyBrowser:'Browser and services',cancelled:'Google sign-in was cancelled.',
    emptyMore:'All currently ranked entries are shown.',
    searchTracks:'Search tracks or artists',noSearchResults:'No matching tracks found.',
    searchListeners:'Search name or username',noListenerResults:'No matching listeners found.',
    allScrobbles:'All scrobbles',viewAll:'View all',backToProfile:'View profile',loadingMore:'Loading…',
    changeAvatar:'Change image',removeAvatar:'Remove image',avatarHint:'JPG, PNG, WebP · Up to 5MB',avatarUploading:'Uploading…',avatarSaved:'Profile image updated.',avatarRemoved:'Default image restored.',avatarFormat:'Choose a JPG, PNG or WebP image.',avatarTooLarge:'Choose an image no larger than 5MB.',avatarInvalid:'This image could not be read. Please choose another image.',avatarChanged:'Another request changed the image. Reopen your profile and try again.',
    weeklyListeners:'Weekly listener ranking',leaderAlbums:'The top listener’s records',viewProfile:'View profile →',firstPlace:'No. 1',listeningTime:'Listening time',noAlbums:'No albums have been recorded yet.',
    recentScrobbles:'Recent scrobbles',dailyListening:'Daily listening time (min)',favouriteAlbums:'Favourite records',editProfile:'Edit profile',vsLastWeek:'Compared with last week',noPreviousWeek:'No previous-week records',today:'Today',yesterday:'Yesterday',noScrobbles:'No scrobbles recorded yet.',profileNotFound:'This profile could not be found.',privateProfile:'Private listening history',historyHidden:'Listening history and statistics are private.',
  },
};

/** Current language and API-backed screen state. */
let language: Language = 'ko-KR';
let chart: Charts | null = null;
let me: Listener | null = null;
let configuration: Configuration | null = null;
/** Profile details stay separate from chart data and are never synthesized for missing accounts. */
let profileData: Profile|null = null;
/** Track pages fetch bounded batches; the home preview remains six rows. */
const isTrackChart = document.body.classList.contains('tracks-page'), isListenerChart = document.body.classList.contains('listeners-page'), trackPageSize=20, chartLimit = isTrackChart ? trackPageSize : isListenerChart ? 100 : 6;
/** Tracks the active search generation and the continuation returned by the server. */
let nextTrackCursor:string|null=null,trackPageQuery='',trackPageLoading=false,trackPageReady=false,trackPageFailed=false;
/** Observes the end of the visible rows instead of downloading the entire chart. */
let trackObserver:IntersectionObserver|null=null;
/** The dedicated profile template serves both the signed-in route and public username routes. */
const isProfilePage = document.body.classList.contains('profile-page');
/** The full-history view shares the profile URL and template, with cursor-based loading. */
const isAllScrobbles = isProfilePage && new URLSearchParams(location.search).get('view')==='scrobbles';
/** Prevents duplicate page requests when the load-more action is clicked repeatedly. */
let loadingScrobbles = false;
/** Full listening history uses the same automatic end-of-list loading as the track chart. */
let scrobblesObserver:IntersectionObserver|null=null,scrobblesPageFailed=false;
if(isAllScrobbles)document.body.classList.add('scrobbles-page');
/** A small in-memory cache avoids resampling unchanged album covers on chart refreshes. */
const featurePalettes = new Map<string,{hue:number;saturation:number}>();
/** Discards older search responses after the query changes or a newer request starts. */
let chartRequest = 0;
/** Native modal supplies focus trapping, Escape handling and focus restoration. */
const panel = query<HTMLDialogElement>('#panel');
/** Returns one translated label. */
function t(key: string) { return messages[language][key] ?? key; }
/** Constructs DOM nodes without interpreting API strings as markup. */
function element<K extends keyof HTMLElementTagNameMap>(tag: K, className = '', text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}
/** Sends same-origin requests while surfacing stable backend error codes. */
async function api<T = unknown>(path: string, options: RequestInit = {}): Promise<T> {
  const headers = new Headers(options.headers);
  if (options.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
  const response = await fetch(path, { credentials: 'same-origin', ...options, headers });
  if (response.status === 204) return null as T;
  const result: unknown = await response.json();
  if (!response.ok) {
    const code = result && typeof result === 'object' && 'error' in result ? String(result.error) : 'service_unavailable';
    throw new Error(code);
  }
  return result as T;
}
/** Formats integers in the active locale. */
function number(value: number) { return new Intl.NumberFormat(language).format(value); }
/** Derives a monogram from a real display name, with stable palette selection. */
function avatar(listener: Listener) {
  const name = listener.display_name || listener.handle;
  const words = name.trim().split(/\s+/u);
  const initials = (words.length > 1 ? words[0][0] + words.at(-1)![0] : [...name].slice(0,2).join('')).toUpperCase();
  const color = [...listener.id].reduce((sum, char) => sum + char.charCodeAt(0), 0) % 5;
  const fallback=element('span', `monogram color-${color}`, initials);
  if(!listener.avatar_url)return fallback;
  const image=element('img',`monogram avatar-image color-${color}`);image.src=listener.avatar_url;image.alt='';image.width=37;image.height=37;image.decoding='async';
  image.onerror=()=>image.replaceWith(fallback);return image;
}
/** Converts the selected raster image into a centered, metadata-free PNG before uploading. */
async function prepareAvatar(file:File):Promise<Blob> {
  if(!['image/jpeg','image/png','image/webp'].includes(file.type))throw new Error('avatar_format');
  if(file.size>5*1024*1024)throw new Error('avatar_too_large');
  let image:ImageBitmap;
  try{image=await createImageBitmap(file);}catch{throw new Error('invalid_avatar');}
  try{
    const canvas=document.createElement('canvas');canvas.width=512;canvas.height=512;
    const context=canvas.getContext('2d');if(!context)throw new Error('invalid_avatar');
    const size=Math.min(image.width,image.height);if(!size)throw new Error('invalid_avatar');
    context.drawImage(image,(image.width-size)/2,(image.height-size)/2,size,size,0,0,512,512);
    return await new Promise<Blob>((resolve,reject)=>canvas.toBlob(blob=>blob?resolve(blob):reject(new Error('invalid_avatar')),'image/png'));
  }finally{image.close();}
}
/** Updates every loaded reference to the signed-in avatar without changing profile form values. */
function applyAvatar(url:string|null):void {
  if(!me)return;
  me.avatar_url=url;renderAccount();
  if(profileData?.listener.id===me.id){profileData.listener.avatar_url=url;renderProfile();}
  if(chart){
    for(const listener of chart.listeners)if(listener.id===me.id)listener.avatar_url=url;
    if(chart.featured_listener?.id===me.id)chart.featured_listener.avatar_url=url;
    renderCharts();
  }
}
/** Converts an RGB sample into normalized saturation and lightness with a hue in degrees. */
function artworkColor(red:number,green:number,blue:number):{hue:number;saturation:number;lightness:number} {
  const r=red/255,g=green/255,b=blue/255,maximum=Math.max(r,g,b),minimum=Math.min(r,g,b),difference=maximum-minimum;
  const lightness=(maximum+minimum)/2;
  if(!difference)return {hue:0,saturation:0,lightness};
  const hue=maximum===r?((g-b)/difference+6)%6:maximum===g?(b-r)/difference+2:(r-g)/difference+4;
  return {hue:hue*60,saturation:difference/(1-Math.abs(2*lightness-1)),lightness};
}
/** Restores a neutral backdrop when artwork is absent, unreadable, or still loading. */
function resetFeatureTheme():void {
  const feature=query('.feature');feature.style.removeProperty('--feature-base');feature.style.removeProperty('--feature-tone');
}
/** Samples the actual same-origin cover and applies its dominant chromatic hue to a dark backdrop. */
function applyFeatureTheme(art:HTMLImageElement,source:string):void {
  if(art.getAttribute('src')!==source||!art.complete||!art.naturalWidth)return;
  try{
    let palette=featurePalettes.get(source);
    if(!palette){
      const canvas=document.createElement('canvas');canvas.width=32;canvas.height=32;
      const context=canvas.getContext('2d',{willReadFrequently:true});if(!context){resetFeatureTheme();return;}
      context.drawImage(art,0,0,32,32);
      const pixels=context.getImageData(0,0,32,32).data;
      const buckets=Array.from({length:24},()=>({weight:0,red:0,green:0,blue:0}));
      for(let index=0;index<pixels.length;index+=4){
        if(pixels[index+3]<128)continue;
        const color=artworkColor(pixels[index],pixels[index+1],pixels[index+2]);
        if(color.lightness<.08||color.lightness>.92||color.saturation<.12)continue;
        const weight=color.saturation*(1-Math.abs(color.lightness-.5));
        const bucket=buckets[Math.floor(color.hue/15)%24];bucket.weight+=weight;
        bucket.red+=pixels[index]*weight;bucket.green+=pixels[index+1]*weight;bucket.blue+=pixels[index+2]*weight;
      }
      const dominant=buckets.reduce((best,bucket)=>bucket.weight>best.weight?bucket:best);
      const color=dominant.weight?artworkColor(dominant.red/dominant.weight,dominant.green/dominant.weight,dominant.blue/dominant.weight):{hue:0,saturation:0};
      palette={hue:Math.round(color.hue),saturation:Math.round(Math.min(.72,color.saturation)*100)};
      if(featurePalettes.size>=16)featurePalettes.delete(featurePalettes.keys().next().value!);
      featurePalettes.set(source,palette);
    }
    const feature=query('.feature');
    feature.style.setProperty('--feature-base',`hsl(${palette.hue} ${palette.saturation*.45}% 8%)`);
    feature.style.setProperty('--feature-tone',`hsl(${palette.hue} ${palette.saturation}% 21%)`);
  }catch{resetFeatureTheme();}
}
/** Applies translated interface labels and then re-renders API-derived content. */
function applyLanguage(value: string) {
  language = value === 'ko-KR' ? value : 'en-US';
  document.documentElement.lang = language;
  document.title = `Reprise · ${t(isListenerChart ? 'listenerNav' : 'trackNav')}`;
  document.querySelectorAll<HTMLElement>('[data-i18n]').forEach(node => { node.textContent = t(node.dataset.i18n!); });
  query<HTMLSelectElement>('#language').value = language;
  query('#close-panel').ariaLabel = t('close');
  if (isTrackChart) query<HTMLInputElement>('#track-search').placeholder = t('searchTracks');
  if (isListenerChart) query<HTMLInputElement>('#listener-search').placeholder = t('searchListeners');
  if (chart) renderCharts();
  if (isTrackChart) renderTrackPagination();
  if (profileData) renderProfile();
  renderAccount();
}
/** Displays real data, an explicit empty state, or an actionable service error. */
async function loadCharts() {
  if (isProfilePage) return loadProfile();
  const request = ++chartRequest;
  if(isTrackChart){nextTrackCursor=null;trackPageReady=false;trackPageFailed=false;trackPageLoading=true;renderTrackPagination();}
  try {
    const search = isTrackChart ? query<HTMLInputElement>('#track-search').value.trim() : '';
    const listenerSearch = isListenerChart ? query<HTMLInputElement>('#listener-search').value.trim() : '';
    const result = await api<Charts>(`/api/charts?limit=${chartLimit}&q=${encodeURIComponent(search)}${isTrackChart?'&scope=tracks':''}${isListenerChart ? `&featured=listener&listener_q=${encodeURIComponent(listenerSearch)}` : ''}`);
    if (request !== chartRequest) return;
    chart = result;
    if(isTrackChart){nextTrackCursor=result.next_track_cursor;trackPageQuery=search;trackPageReady=true;}
    renderCharts();
    query('#service-status').hidden = true;
  } catch {
    if (request !== chartRequest) return;
    const status = query('#service-status');
    status.replaceChildren(element('p','',t('unavailable')));
    const retry = element('button','text-button',t('retry'));
    retry.onclick = loadCharts;
    status.append(retry);status.hidden = false;
    for (const id of ['tracks-empty','listeners-empty']) {
      const empty = document.getElementById(id);if (empty) empty.textContent = t('unavailable');
    }
    if (isListenerChart) query('#leader-empty').textContent = t('unavailable');
  }finally{if(isTrackChart&&request===chartRequest){trackPageLoading=false;renderTrackPagination();}}
}
/** Keeps loading and retry states accessible and re-arms the observer after each appended page. */
function renderTrackPagination():void {
  if(!isTrackChart)return;
  const area=query('#track-pagination'),sentinel=query('#track-sentinel'),retry=query<HTMLButtonElement>('#track-page-retry');
  query('#track-list').setAttribute('aria-busy',String(trackPageLoading));
  area.hidden=!trackPageReady||(!nextTrackCursor&&!trackPageLoading);
  query('#track-page-status').textContent=trackPageLoading?t('loadingMore'):trackPageFailed?t('formError'):'';
  retry.hidden=!trackPageFailed;retry.disabled=trackPageLoading;retry.textContent=t('retry');
  trackObserver?.unobserve(sentinel);
  if(!area.hidden&&nextTrackCursor&&!trackPageLoading&&!trackPageFailed)trackObserver?.observe(sentinel);
  if(!trackObserver&&!area.hidden&&!trackPageLoading&&!trackPageFailed)requestAnimationFrame(checkListEnds);
}
/** Appends one stable chart page and ignores responses from an obsolete search. */
async function loadMoreTracks():Promise<void> {
  if(!isTrackChart||!chart||!trackPageReady||!nextTrackCursor||trackPageLoading)return;
  const request=chartRequest,cursor=nextTrackCursor;
  trackPageLoading=true;trackPageFailed=false;renderTrackPagination();
  try{
    const result=await api<Charts>(`/api/charts?scope=tracks&limit=${trackPageSize}&q=${encodeURIComponent(trackPageQuery)}&track_cursor=${encodeURIComponent(cursor)}`);
    if(request!==chartRequest||!chart)return;
    const existing=new Set(chart.tracks.map(track=>track.id)),added=result.tracks.filter(track=>!existing.has(track.id)),offset=chart.tracks.length;
    chart={...chart,tracks:[...chart.tracks,...added],next_track_cursor:result.next_track_cursor};nextTrackCursor=result.next_track_cursor;
    query('#track-list').append(...added.map((track,index)=>trackRow(track,offset+index)));
  }catch{if(request===chartRequest)trackPageFailed=true;}
  finally{if(request===chartRequest){trackPageLoading=false;renderTrackPagination();}}
}
/** Creates a chart row with an R2 image only when real artwork exists. */
function trackRow(track: Track, index: number) {
  const row = element('li','track-row');
  row.append(element('span','order mono',String(track.rank ?? index + 1).padStart(2,'0')));
  if (track.artwork_url) {
    const image = element('img','track-artwork');
    image.src = track.artwork_url;image.alt = track.album || track.title;image.width = 43;image.height = 43;
    image.loading = 'lazy';row.append(image);
  } else {
    const missing = element('span','track-artwork artwork-missing','♪');missing.ariaHidden = 'true';row.append(missing);
  }
  const names = element('div','name-credit');
  names.append(element('p','track-name',track.title),element('p','track-artist',track.artist));
  row.append(names,element('span','track-count mono',number(track.listeners)));
  return row;
}
/** Creates a real public listener row that opens their server-backed profile. */
function listenerRow(listener: Listener, index: number) {
  const row = element('li','listener-row'), button = element('button','listener-button');button.type = 'button';
  const names = element('div','name-credit');
  names.append(element('p','listener-name',listener.display_name),element('p','listener-handle',`@${listener.handle}`));
  button.append(element('span','order mono',String(listener.rank ?? index + 1).padStart(2,'0')),avatar(listener),names,
    element('span','listener-count mono',`${number(Math.floor(listener.seconds / 60))}${language === 'ko-KR' ? '' : ' '}${t('minutes')}`));
  button.onclick = () => showListener(listener.handle);row.append(button);return row;
}
/** Keeps the Figma layout while binding every title, count and profile to D1 results. */
function renderCharts() {
  if (!chart) return;
  if (isListenerChart) { renderListenerChart();return; }
  const formatDate = (value: number) => new Intl.DateTimeFormat('en-US',{month:'2-digit',day:'2-digit',timeZone:'Asia/Seoul'})
    .format(new Date(value)).replace('/','.');
  query('#chart-period').textContent = `${formatDate(chart.period.from)} — ${formatDate(chart.period.to)}`;
  query('#track-list').replaceChildren(...chart.tracks.map(trackRow));
  if (isTrackChart) {
    const empty = query('#tracks-empty');empty.hidden = chart.tracks.length > 0;
    empty.textContent = t(query<HTMLInputElement>('#track-search').value.trim() ? 'noSearchResults' : 'emptyTracks');
    return;
  }
  query('#listener-list').replaceChildren(...chart.listeners.slice(0,5).map(listenerRow));
  for (const [id,items,label] of [['tracks-empty',chart.tracks,'emptyTracks'],['listeners-empty',chart.listeners,'emptyListeners']] as const) {
    const empty = query(`#${id}`);empty.hidden = items.length > 0;empty.textContent = t(label);
  }
  const featured = chart.tracks[0], title = query('#featured-title');
  title.textContent = featured?.title || t('emptyFeature');title.classList.toggle('is-empty',!featured);
  query('#featured-artist').textContent = featured?.artist || '';
  query('#featured-album').textContent = featured?.album || t('emptyArtist');
  query('#featured-count').hidden = !featured;
  query('#featured-count .mono').textContent = featured ? number(featured.listeners) : '';
  const art = query<HTMLImageElement>('#featured-artwork');
  const placeholder=art.nextElementSibling as HTMLElement,source=featured?.artwork_url;
  if(source){
    art.onload=()=>{if(art.getAttribute('src')!==source)return;art.hidden=false;placeholder.hidden=true;applyFeatureTheme(art,source);};
    art.onerror=()=>{if(art.getAttribute('src')!==source)return;art.hidden=true;placeholder.hidden=false;resetFeatureTheme();};
    if(art.getAttribute('src')!==source){art.hidden=true;placeholder.hidden=false;resetFeatureTheme();art.src=source;}
    if(art.complete&&art.naturalWidth){art.hidden=false;placeholder.hidden=true;applyFeatureTheme(art,source);}
  }else{art.onload=null;art.onerror=null;art.hidden=true;art.removeAttribute('src');placeholder.hidden=false;resetFeatureTheme();}
}
/** Builds the featured listener and records directly from the matching server response. */
function renderListenerChart() {
  if (!chart) return;
  const date = new Intl.DateTimeFormat(language,{month:'long',day:'numeric',timeZone:'Asia/Seoul'});
  query('#chart-period').textContent = `${date.format(chart.period.from)} – ${date.format(chart.period.to)}`;
  query('#listener-list').replaceChildren(...chart.listeners.map(listenerRow));
  const leader = chart.featured_listener, card = query<HTMLButtonElement>('#featured-listener');
  const empty = query('#listeners-empty');empty.hidden = chart.listeners.length>0;
  empty.textContent = t(query<HTMLInputElement>('#listener-search').value.trim()?'noListenerResults':'emptyListeners');
  card.hidden = !leader;query('#leader-albums').hidden = !leader;
  query('#leader-empty').hidden = Boolean(leader);query('#leader-empty').textContent = t('emptyListeners');
  if (!leader) { card.onclick = null;query('#favourite-albums').replaceChildren();query('#listener-sleeves').replaceChildren();return; }
  query('#featured-listener-name').textContent = leader.display_name;
  query('#featured-listener-time').textContent = `${number(Math.floor(leader.seconds / 60))}${language === 'ko-KR' ? '' : ' '}${t('minutes')}`;
  card.ariaLabel = `${leader.display_name} · ${t('viewProfile')}`;
  card.onclick = () => showListener(leader.handle);
  const albums = chart.featured_albums || [], grid = query('#favourite-albums');grid.replaceChildren();
  query('#albums-empty').hidden = albums.length > 0;
  for (const album of albums) {
    const item = element('li','favourite-album');
    if (album.artwork_url) {
      const cover = element('img','favourite-album-cover');cover.src = album.artwork_url;
      cover.alt = album.album;cover.width = 131;cover.height = 131;item.append(cover);
    } else {
      const cover = element('span','favourite-album-cover artwork-missing','♪');cover.ariaHidden = 'true';item.append(cover);
    }
    const title = element('p','',album.album);title.title = album.album;item.append(title);
    const artist = element('p','album-artist',album.artist);artist.title = album.artist;item.append(artist);grid.append(item);
  }
  const stack = query('#listener-sleeves');stack.replaceChildren();
  for (const [position,album] of [['back',albums[1]],['middle',albums[2]],['front',albums[0]]] as const) {
    if (!album?.artwork_url) continue;
    const wrapper = element('div',`listener-sleeve-position sleeve-${position}`), sleeve = element('div','listener-sleeve');
    const cover = element('img');cover.src = album.artwork_url;cover.alt = '';cover.width = 100;cover.height = 100;
    sleeve.append(cover);wrapper.append(sleeve);stack.append(wrapper);
  }
}
/** Reflects the signed-in identity without fabricating a default user. */
function renderAccount() {
  closeAccountMenu();
  const button = query('#account-button');
  button.replaceChildren(me ? avatar(me) : element('span','',t('signIn')));
  button.ariaLabel = me ? `${t('profile')}: ${me.display_name}` : t('signIn');
  if (me) {
    button.setAttribute('aria-haspopup','menu');button.setAttribute('aria-controls','account-menu');button.setAttribute('aria-expanded','false');
  } else {
    button.removeAttribute('aria-haspopup');button.removeAttribute('aria-controls');button.removeAttribute('aria-expanded');
  }
}
/** Dismisses the account dropdown and optionally returns keyboard focus to its trigger. */
function closeAccountMenu(restoreFocus = false) {
  query('#account-menu').hidden = true;
  query('#account-button').setAttribute('aria-expanded','false');
  if (restoreFocus) query('#account-button').focus();
}
/** Opens the signed-in dropdown or starts normal sign-in for a guest. */
function toggleAccountMenu() {
  if (!me) return showLogin();
  if (!query('#account-menu').hidden) return closeAccountMenu(true);
  query('#account-menu-error').hidden = true;
  query('#account-menu').hidden = false;
  query('#account-button').setAttribute('aria-expanded','true');
  query('#account-menu-profile').focus();
}
/** Revokes the real browser session and updates all account controls. */
async function signOut() {
  await api('/api/logout',{method:'POST'});
  me = null;closeAccountMenu(true);renderAccount();panel.close();
  if (isProfilePage) { location.replace('/');return; }
  await loadCharts();
}
/** Resets and opens the shared accessible dialog. */
function openPanel(title: string) {
  closeAccountMenu();
  panel.classList.remove('account-panel');
  query('#panel-title').textContent = title;
  query('#panel-content').replaceChildren();
  query('#panel-message').textContent = '';
  if (!panel.open) panel.showModal();
  return query('#panel-content');
}
/** Maps backend validation codes to useful localized messages. */
function showError(error: unknown) {
  const keys: Record<string, string> = {handle_unavailable:'handleUnavailable',invalid_profile:'invalidProfile',device_code_invalid:'expired',consent_required:'consentRequired'};
  query('#panel-message').textContent = t(keys[error instanceof Error ? error.message : ''] || 'formError');
}
/** Starts only Google's real authorization flow; missing credentials remain explicit. */
function showLogin(returnTo = '/profile') {
  location.assign(`/login?return_to=${encodeURIComponent(returnTo)}&lang=${language}`);
}
/** Creates a labeled form control without accepting HTML from profile data. */
function field(label: string, name: string, value: string, max: number) {
  const wrapper = element('label','form-field',label), input = element('input');
  input.name = name;input.value = value;input.required = true;input.maxLength = max;wrapper.append(input);return wrapper;
}
/** Shows account editing and device revocation for the authenticated user. */
async function showAccount() {
  if (!me) return showLogin(location.pathname === '/connect' ? location.pathname + location.search : '/profile');
  const content = openPanel(t('editProfile'));
  panel.classList.add('account-panel');
  const settings = element('div','account-settings'), form = element('form','account-form');
  const imageSection=element('div','account-avatar'),preview=element('div','account-avatar-preview');preview.append(avatar(me));
  const imageControls=element('div','account-avatar-controls'),imageActions=element('div','account-avatar-actions');
  const changeImage=element('button','secondary-button',t('changeAvatar')),removeImage=element('button','account-avatar-remove',t('removeAvatar'));
  changeImage.type='button';removeImage.type='button';removeImage.disabled=!me.avatar_url;
  const fileInput=element('input');fileInput.type='file';fileInput.accept='image/jpeg,image/png,image/webp';fileInput.hidden=true;
  const imageStatus=element('p','account-avatar-status');imageStatus.setAttribute('role','status');
  imageActions.append(changeImage,removeImage);imageControls.append(imageActions,element('p','account-avatar-hint',t('avatarHint')),imageStatus,fileInput);
  imageSection.append(preview,imageControls);settings.append(imageSection);
  /** Commits an avatar change independently from unsaved profile text fields. */
  const updateImage=async(file?:File)=>{
    changeImage.disabled=true;removeImage.disabled=true;changeImage.textContent=t('avatarUploading');imageStatus.textContent='';
    try{
      if(file){const body=await prepareAvatar(file);const result=await api<{avatar_url:string}>('/api/profile/avatar',{method:'PUT',headers:{'Content-Type':'image/png'},body});applyAvatar(result.avatar_url);}
      else{await api('/api/profile/avatar',{method:'DELETE'});applyAvatar(null);}
      if(imageSection.isConnected&&me){preview.replaceChildren(avatar(me));imageStatus.textContent=t(file?'avatarSaved':'avatarRemoved');}
    }catch(error){
      const keys:Record<string,string>={avatar_format:'avatarFormat',avatar_too_large:'avatarTooLarge',invalid_avatar:'avatarInvalid',avatar_changed:'avatarChanged'};
      imageStatus.textContent=t(keys[error instanceof Error?error.message:'']||'formError');
    }finally{changeImage.disabled=false;changeImage.textContent=t('changeAvatar');removeImage.disabled=!me?.avatar_url;}
  };
  changeImage.onclick=()=>fileInput.click();removeImage.onclick=()=>void updateImage();
  fileInput.onchange=()=>{const file=fileInput.files?.[0];fileInput.value='';if(file)void updateImage(file);};
  const fields = element('div','account-fields');
  fields.append(field(t('displayName'),'display_name',me.display_name,60),field(t('handle'),'handle',me.handle,30));
  const consent = element('label','account-sharing'), checkbox = element('input');
  checkbox.type = 'checkbox';checkbox.checked = Boolean(me.public);
  const toggle = element('span','account-toggle');toggle.ariaHidden = 'true';
  consent.append(element('span','account-sharing-label',t('publicProfile')),checkbox,toggle);
  const actions = element('div','account-save-actions'), save = element('button','primary-button',t('save'));
  const sharing = element('div','account-sharing-setting'),hint=element('p','account-sharing-hint',t('privateAggregation'));
  hint.id='account-sharing-hint';checkbox.setAttribute('aria-describedby',hint.id);sharing.append(consent,hint);
  save.type = 'submit';actions.append(save);form.append(fields,sharing,actions);settings.append(form);
  form.onsubmit = async event => {
    event.preventDefault();save.disabled = true;
    try {
      const values = new FormData(form);
      await api('/api/profile',{method:'PATCH',body:JSON.stringify({display_name:values.get('display_name'),handle:values.get('handle'),public:checkbox.checked})});
      me = (await api<Identity>('/api/me')).listener;renderAccount();
      if (isProfilePage && me && profileData?.listener.id===me.id && !/^\/profile(?:\/|\.html)?$/.test(location.pathname)) history.replaceState(null,'',`/${me.handle}`);
      query('#panel-message').textContent = t('saved');await loadCharts();
    } catch(error) { showError(error); } finally { save.disabled = false; }
  };
  const devices = element('section','account-devices'), heading = element('div','account-section-heading');
  const count = element('span','device-count mono','—');
  heading.append(element('h3','',t('devices')),count);
  const deviceList = element('div','account-device-list');devices.append(heading,deviceList);settings.append(devices);
  /** Restores a clear empty state after the last connected installation is revoked. */
  const showEmptyDevices = () => {
    const empty = element('div','devices-empty');
    empty.append(element('p','devices-empty-title',t('noDevicesTitle')),element('p','',t('noDevices')));
    deviceList.replaceChildren(empty);count.textContent = '0';
  };
  deviceList.append(element('p','devices-loading',t('loading')));
  const sessionActions = element('div','account-session-actions');
  const logout = element('button','secondary-button',t('signOut'));logout.type = 'button';
  logout.onclick = async () => {
    logout.disabled = true;
    try { await signOut(); }
    catch(error) { showError(error);logout.disabled = false; }
  };
  const remove = element('button','account-delete-button',t('deleteAccount'));remove.type = 'button';
  remove.onclick = () => {
    const area = openPanel(t('deleteAccount')), deletion = element('form','account-deletion-form');
    deletion.append(element('p','dialog-copy',t('deleteConfirm')),field(t('handle'),'confirm','',30));
    const deletionActions = element('div','account-delete-actions');
    const cancel = element('button','secondary-button',t('cancel'));cancel.type='button';cancel.onclick=showAccount;
    const confirm = element('button','primary-button danger-button',t('deleteAccount'));confirm.type = 'submit';
    deletionActions.append(cancel,confirm);deletion.append(deletionActions);area.append(deletion);
    deletion.onsubmit = async event => {
      event.preventDefault();confirm.disabled = true;
      try { await api('/api/account',{method:'DELETE',body:JSON.stringify({confirm:new FormData(deletion).get('confirm')})});me=null;renderAccount();panel.close();if(isProfilePage){location.replace('/');return;}await loadCharts(); }
      catch(error) { showError(error);confirm.disabled=false; }
    };
  };
  sessionActions.append(logout,remove);settings.append(sessionActions);content.append(settings);
  try {
    const result = await api<DeviceList>('/api/devices');
    if (!panel.open || !devices.isConnected) return;
    deviceList.replaceChildren();count.textContent=String(result.devices.length);
    if (!result.devices.length) showEmptyDevices();
    for (const device of result.devices) {
      const row = element('div','account-device-row'), revoke = element('button','secondary-button device-revoke',t('disconnect'));
      revoke.type = 'button';
      revoke.onclick = async () => {
        revoke.disabled = true;
        try {
          await api(`/api/devices/${encodeURIComponent(device.id)}`,{method:'DELETE'});row.remove();
          const remaining=deviceList.children.length;count.textContent=String(remaining);
          if(!remaining)showEmptyDevices();
        } catch(error) { showError(error);revoke.disabled = false; }
      };
      row.append(element('span','account-device-name',device.name),revoke);deviceList.append(row);
    }
  } catch(error) { deviceList.replaceChildren(element('p','devices-loading',t('formError')));count.textContent='—';showError(error); }
}
/** Fetches either the owner's private profile or the visible public username profile. */
async function loadProfile(): Promise<void> {
  const request = ++chartRequest;
  scrobblesPageFailed=false;
  const own = /^\/profile(?:\/|\.html)?$/.test(location.pathname);
  const handle = location.pathname.replace(/^\/|\/$/g,'');
  try {
    const result = await api<Profile>((own?'/api/profile':`/api/listeners/${encodeURIComponent(handle)}`)+(isAllScrobbles?'?scrobbles=all':''));
    if(request!==chartRequest)return;
    profileData=result;query('#profile-body').hidden=false;query('#service-status').hidden=true;renderProfile();
  } catch(error) {
    if(request!==chartRequest)return;
    if(own&&error instanceof Error&&error.message==='sign_in_required'){showLogin();return;}
    profileData=null;query('#profile-body').hidden=true;
    const status=query('#service-status');status.hidden=false;
    status.replaceChildren(element('p','',t(error instanceof Error&&error.message==='listener_not_found'?'profileNotFound':'profileError')));
    const retry=element('button','text-button',t('retry'));retry.onclick=loadProfile;status.append(retry);
  }
}
/** Appends the next page without resetting the user's position or replacing earlier records. */
async function loadMoreScrobbles():Promise<void> {
  if(!profileData?.history_visible||profileData.next_cursor===null||loadingScrobbles)return;
  const cursor=profileData.next_cursor,request=chartRequest;
  const own=/^\/profile(?:\/|\.html)?$/.test(location.pathname),handle=location.pathname.replace(/^\/|\/$/g,'');
  loadingScrobbles=true;scrobblesPageFailed=false;renderScrobbleControls();
  try{
    const result=await api<Profile>(`${own?'/api/profile':`/api/listeners/${encodeURIComponent(handle)}`}?scrobbles=all&before=${cursor}`);
    if(request!==chartRequest||!profileData)return;
    profileData={...profileData,recent:[...profileData.recent,...result.recent],next_cursor:result.next_cursor};
    query('#recent-list').append(...result.recent.map(scrobbleRow));
  }catch{if(request===chartRequest)scrobblesPageFailed=true;}
  finally{loadingScrobbles=false;renderScrobbleControls();}
}
/** Updates history actions without replacing visible rows during pagination. */
function renderScrobbleControls():void {
  if(!profileData)return;
  query('#recent-heading').textContent=t(isAllScrobbles?'allScrobbles':'recentScrobbles');
  const historyLink=query<HTMLAnchorElement>('#profile-view-all'),destination=new URL(location.href);
  if(isAllScrobbles)destination.searchParams.delete('view');else destination.searchParams.set('view','scrobbles');
  historyLink.href=destination.pathname+destination.search;historyLink.textContent=t(isAllScrobbles?'backToProfile':'viewAll');
  const area=query('#scrobbles-pagination'),sentinel=query('#scrobbles-sentinel'),retry=query<HTMLButtonElement>('#scrobbles-retry');
  area.hidden=!profileData.history_visible||!isAllScrobbles||(profileData.next_cursor===null&&!loadingScrobbles);
  query('#recent-list').setAttribute('aria-busy',String(loadingScrobbles));
  query('#scrobbles-message').textContent=loadingScrobbles?t('loadingMore'):scrobblesPageFailed?t('formError'):'';
  retry.hidden=!scrobblesPageFailed;retry.disabled=loadingScrobbles;retry.textContent=t('retry');
  scrobblesObserver?.unobserve(sentinel);
  if(!area.hidden&&!loadingScrobbles&&!scrobblesPageFailed)scrobblesObserver?.observe(sentinel);
  if(!scrobblesObserver&&!area.hidden&&!loadingScrobbles&&!scrobblesPageFailed)requestAnimationFrame(checkListEnds);
}
/** Provides automatic scroll loading in browsers without IntersectionObserver. */
function checkListEnds():void {
  const visible=(selector:string)=>{const bounds=query(selector).getBoundingClientRect();return bounds.top<=innerHeight+240&&bounds.bottom>=-240;};
  if(isTrackChart&&!trackObserver&&trackPageReady&&nextTrackCursor&&!trackPageLoading&&!trackPageFailed&&visible('#track-sentinel'))void loadMoreTracks();
  if(isAllScrobbles&&!scrobblesObserver&&profileData?.next_cursor&&!loadingScrobbles&&!scrobblesPageFailed&&visible('#scrobbles-sentinel'))void loadMoreScrobbles();
}
/** Creates one playback row for both the recent preview and full-history pages. */
function scrobbleRow(play:Scrobble):HTMLLIElement {
  const row=element('li','track-row recent-row');row.append(element('span','order mono','·'));
  if(play.artwork_url){const cover=element('img','track-artwork');cover.src=play.artwork_url;cover.alt=play.album||play.title;cover.width=43;cover.height=43;row.append(cover);}
  else{const cover=element('span','track-artwork artwork-missing','♪');cover.ariaHidden='true';row.append(cover);}
  const names=element('div','name-credit');names.append(element('p','track-name',play.title),element('p','track-artist',play.artist));
  const time=element('time','recent-time mono',scrobbleTime(play.ended_at));time.dateTime=new Date(play.ended_at).toISOString();row.append(names,time);return row;
}
/** Formats real playback timestamps in the same Korean day boundary as the daily chart. */
function scrobbleTime(value:number):string {
  const offset=9*3600_000,day=new Date(value+offset).toISOString().slice(0,10);
  const today=new Date(Date.now()+offset).toISOString().slice(0,10),yesterday=new Date(Date.now()+offset-86400_000).toISOString().slice(0,10);
  const label=day===today?t('today'):day===yesterday?t('yesterday'):new Intl.DateTimeFormat(language,{month:'numeric',day:'numeric',timeZone:'Asia/Seoul'}).format(value);
  return `${label} ${new Intl.DateTimeFormat(language,{hour:'2-digit',minute:'2-digit',hour12:false,timeZone:'Asia/Seoul'}).format(value)}`;
}
/** Draws seven real daily totals with native SVG attributes and accessible numeric descriptions. */
function renderDailyChart(daily:Profile['daily']):void {
  const namespace='http://www.w3.org/2000/svg',svg=document.createElementNS(namespace,'svg');
  const title=document.createElementNS(namespace,'title'),description=document.createElementNS(namespace,'desc');
  title.id='daily-chart-title';title.textContent=t('dailyListening');description.id='daily-chart-description';
  const minutes=new Intl.NumberFormat(language,{maximumFractionDigits:1});
  description.textContent=daily.map(day=>`${day.date}: ${minutes.format(day.seconds/60)} ${t('minutes')}`).join('; ');
  svg.setAttribute('viewBox','0 0 374 126');svg.setAttribute('role','img');svg.setAttribute('aria-labelledby','daily-chart-title daily-chart-description');svg.append(title,description);
  const maximum=Math.max(1,...daily.map(day=>day.seconds));
  daily.forEach((day,index)=>{
    const height=day.seconds/maximum*100,bar=document.createElementNS(namespace,'rect'),label=document.createElementNS(namespace,'text');
    bar.setAttribute('x',String(index*55));bar.setAttribute('y',String(100-height));bar.setAttribute('width','44');bar.setAttribute('height',String(height));bar.setAttribute('rx','2');
    bar.setAttribute('class',index===daily.length-1?'daily-bar today':'daily-bar');
    const tooltip=document.createElementNS(namespace,'title');tooltip.textContent=`${day.date} · ${minutes.format(day.seconds/60)} ${t('minutes')}`;bar.append(tooltip);
    label.setAttribute('x',String(index*55+22));label.setAttribute('y','122');label.setAttribute('text-anchor','middle');
    label.textContent=new Intl.DateTimeFormat(language,{weekday:'short',timeZone:'Asia/Seoul'}).format(new Date(`${day.date}T12:00:00+09:00`));svg.append(bar,label);
  });
  query('#daily-chart').replaceChildren(svg);
}
/** Renders profile identity, rank, grouped recent plays, and favourite albums from the live API. */
function renderProfile():void {
  if(!profileData)return;
  const {listener,stats,recent,daily,albums}=profileData;
  renderScrobbleControls();
  document.title=`${listener.display_name} · Reprise`;
  query('#profile-avatar').replaceChildren(avatar(listener));query('#profile-name').textContent=listener.display_name;
  const url=query<HTMLAnchorElement>('#profile-url');url.href=`/${listener.handle}`;url.textContent=`reprise.mx/${listener.handle}`;
  query('#profile-edit').hidden=me?.id!==listener.id;
  const notice=query('#profile-private');notice.hidden=Boolean(listener.public);
  notice.textContent=t(profileData.history_visible?'privateProfile':'historyHidden');
  query('#profile-metrics').hidden=!profileData.history_visible;
  query('#profile-details').hidden=!profileData.history_visible;
  if(!profileData.history_visible||!stats){
    for(const selector of ['#profile-time','#profile-change','#profile-rank','#recent-list','#daily-chart','#profile-albums'])query(selector).replaceChildren();
    return;
  }
  query('#profile-time').textContent=`${number(Math.floor(stats.seconds/60))}${language==='ko-KR'?'':' '}${t('minutes')}`;
  const change=stats.previous_seconds>0?(stats.seconds-stats.previous_seconds)/stats.previous_seconds*100:null;
  query('#profile-change').textContent=change===null?t('noPreviousWeek'):`${t('vsLastWeek')} ${change>0?'+':''}${new Intl.NumberFormat(language,{maximumFractionDigits:1}).format(change)}%`;
  query('#profile-rank').textContent=stats.rank===null?'—':language==='ko-KR'?`${number(stats.rank)}위`:`#${number(stats.rank)}`;
  query('#recent-list').replaceChildren(...recent.map(scrobbleRow));query('#recent-empty').hidden=recent.length>0;
  renderDailyChart(daily);
  const grid=query('#profile-albums');grid.replaceChildren();query('#profile-albums-empty').hidden=albums.length>0;
  for(const album of albums){
    const item=element('li','profile-album');
    if(album.artwork_url){const cover=element('img','profile-album-cover');cover.src=album.artwork_url;cover.alt=album.album;cover.width=128;cover.height=128;item.append(cover);}
    else{const cover=element('span','profile-album-cover artwork-missing','♪');cover.ariaHidden='true';item.append(cover);}
    const title=element('p','',album.album);title.title=album.album;item.append(title);
    const artist=element('p','album-artist',album.artist);artist.title=album.artist;item.append(artist);grid.append(item);
  }
}
/** Navigates to the listener's real shareable profile. */
function showListener(handle: string) { location.assign(`/${encodeURIComponent(handle)}`); }
/** Starts the screen from real account, country and chart responses. */
async function start() {
  const results = await Promise.allSettled([api<Configuration>('/api/config'),api<Identity>('/api/me')]);
  configuration = results[0].status === 'fulfilled' ? results[0].value : null;
  me = results[1].status === 'fulfilled' ? results[1].value.listener : null;
  let stored;
  try { stored = localStorage.getItem('reprise.language'); } catch { /* Storage may be disabled. */ }
  const preview = new URLSearchParams(location.search).get('lang');
  applyLanguage(preview || (configuration?.country === 'KR' && stored ? stored : 'en-US'));
  await loadCharts();
  if (new URLSearchParams(location.search).get('auth') === 'cancelled') {
    const content = openPanel(t('signIn'));content.append(element('p','dialog-copy',t('cancelled')));
  }
}

query<HTMLSelectElement>('#language').onchange = event => {
  applyLanguage((event.target as HTMLSelectElement).value);
  try { localStorage.setItem('reprise.language',language); } catch { /* Reading remains available without storage. */ }
  if (panel.open) panel.close();
};
document.querySelectorAll<HTMLButtonElement>('[data-action="account"]').forEach(button => { button.onclick = () => location.assign('/profile'); });
query('#account-button').onclick = toggleAccountMenu;
query('#account-menu-profile').onclick = () => location.assign('/profile');
query<HTMLButtonElement>('#account-menu-logout').onclick = async () => {
  const button = query<HTMLButtonElement>('#account-menu-logout');button.disabled = true;
  query('#account-menu-error').hidden = true;
  try { await signOut(); }
  catch { const error = query('#account-menu-error');error.textContent = t('formError');error.hidden = false; }
  finally { button.disabled = false; }
};
document.addEventListener('pointerdown',event => {
  if (event.target instanceof Node && !query('.account-control').contains(event.target)) closeAccountMenu();
});
query('.account-control').addEventListener('focusout',event => {
  if (!(event.relatedTarget instanceof Node) || !query('.account-control').contains(event.relatedTarget)) closeAccountMenu();
});
query('.account-control').addEventListener('keydown',event => {
  if (!me) return;
  if (event.key === 'Escape') { closeAccountMenu(true);event.preventDefault();return; }
  if (event.key === 'Tab') { closeAccountMenu(true);return; }
  if (!['ArrowDown','ArrowUp','Home','End'].includes(event.key)) return;
  event.preventDefault();
  if (query('#account-menu').hidden) {
    toggleAccountMenu();
    if (event.key === 'ArrowUp' || event.key === 'End') query('#account-menu-logout').focus();
    return;
  }
  const items = [...query('#account-menu').querySelectorAll<HTMLButtonElement>('button:not(:disabled)')];
  const index = items.indexOf(document.activeElement as HTMLButtonElement);
  const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1
    : (index + (event.key === 'ArrowUp' ? -1 : 1) + items.length) % items.length;
  items[next]?.focus();
});
query('#close-panel').onclick = () => panel.close();
panel.addEventListener('close',() => { query('#panel-content').className = ''; });
panel.addEventListener('click',event => {
  if (event.target !== panel) return;
  const bounds = panel.getBoundingClientRect();
  if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) panel.close();
});
if (isProfilePage) {
  query('#profile-edit').onclick = showAccount;
  query('#scrobbles-retry').onclick = loadMoreScrobbles;
  if(isAllScrobbles&&typeof IntersectionObserver!=='undefined')scrobblesObserver=new IntersectionObserver(entries=>{if(entries.some(entry=>entry.isIntersecting)&&!scrobblesPageFailed)void loadMoreScrobbles();},{rootMargin:'240px 0px'});
} else if (isTrackChart || isListenerChart) {
  /** Debounces real server searches while preserving normal input-method composition. */
  let searchTimer: ReturnType<typeof setTimeout>;
  /** Invalidates pending requests as soon as a new query is entered. */
  const searchChart = () => {
    clearTimeout(searchTimer);chartRequest++;
    if(isTrackChart){
      nextTrackCursor=null;trackPageReady=false;trackPageLoading=false;trackPageFailed=false;chart=null;
      query('#track-list').replaceChildren();query('#tracks-empty').hidden=false;query('#tracks-empty').textContent=t('loading');renderTrackPagination();
    }
    searchTimer = setTimeout(() => void loadCharts(),200);
  };
  /** Both chart searches share the same debounce and input-method behavior. */
  const searchInput = query<HTMLInputElement>(isTrackChart?'#track-search':'#listener-search');
  searchInput.addEventListener('input',event => { if (!(event as InputEvent).isComposing) searchChart(); });
  searchInput.addEventListener('compositionend',searchChart);
  if(isTrackChart){
    if(typeof IntersectionObserver!=='undefined')trackObserver=new IntersectionObserver(entries=>{if(entries.some(entry=>entry.isIntersecting)&&!trackPageFailed)void loadMoreTracks();},{rootMargin:'240px 0px'});
    query('#track-page-retry').onclick=loadMoreTracks;
  }
} else {
  query('#more-tracks').onclick = () => location.assign('/tracks');
  query('#more-listeners').onclick = () => location.assign('/listeners');
}
if((isTrackChart||isAllScrobbles)&&typeof IntersectionObserver==='undefined'){
  window.addEventListener('scroll',checkListEnds,{passive:true});window.addEventListener('resize',checkListEnds);
}
setInterval(() => { if (!document.hidden && !panel.open && !isAllScrobbles && !isTrackChart) void loadCharts(); },60_000);
void start();
