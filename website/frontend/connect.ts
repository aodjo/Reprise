export {};

/** Copy for the standalone installation-approval screen. */
const translations:Record<string,Record<string,string>>={
  'ko-KR':{title:'Reprise 앱 연결',device:'연결할 기기',description:'연결하면 이 기기의 재생 정보가 기록되고 곡 차트에 집계됩니다. 프로필은 공개되며, 청취 기록과 통계의 공개 여부는 프로필 설정에서 관리합니다.',cancel:'취소',approve:'연결',google:'Google로 계속하기',checking:'연결할 기기를 확인하고 있어요.',missing:'Reprise 앱 설정에서 계정 연결을 눌러 이 페이지를 열어주세요.',expired:'연결 요청이 만료되었거나 유효하지 않아요. Reprise 앱에서 다시 연결해 주세요.',failure:'요청을 처리하지 못했어요. 잠시 후 다시 시도해 주세요.',retry:'다시 시도',connecting:'연결 중…',success:'연결되었습니다. Reprise 앱으로 돌아가 주세요.',profile:'내 프로필',terms:'이용약관',privacy:'개인정보 처리방침',identity:'로그인한 계정',signIn:'Google 계정으로 로그인한 후 앱을 연결하세요.'},
  'en-US':{title:'Connect Reprise',device:'Device to connect',description:'Connecting records playback from this device and contributes to track totals. Your profile stays public; manage listening-history and statistics visibility in your profile settings.',cancel:'Cancel',approve:'Connect',google:'Continue with Google',checking:'Checking the device.',missing:'Open this page from the account connection option in your Reprise app settings.',expired:'This connection request is invalid or has expired. Start connecting again in Reprise.',failure:'The request could not be completed. Please try again shortly.',retry:'Try again',connecting:'Connecting…',success:'Connected. You can return to your Reprise app.',profile:'My profile',terms:'Terms of service',privacy:'Privacy policy',identity:'Signed in as',signIn:'Sign in with Google to connect your Reprise app.'},
};
/** Requires page controls with their native DOM types. */
function node<T extends HTMLElement>(selector:string):T{const value=document.querySelector<T>(selector);if(!value)throw new Error(`Missing connection element: ${selector}`);return value;}
/** The native app supplies the pending request; users never enter or copy a code. */
const params=new URLSearchParams(location.search);
/** Retains the existing app protocol without exposing an input step. */
const code=(params.get('code')||'').replaceAll('-','').toUpperCase();
/** A malformed URL cannot enable approval or become a post-login destination. */
const validCode=/^[A-Z2-9]{8}$/.test(code);
/** Native form controls provide explicit approval with one click. */
const form=node<HTMLFormElement>('#connect-form'),button=node<HTMLButtonElement>('#connect-submit'),status=node<HTMLParagraphElement>('#connect-status'),picker=node<HTMLSelectElement>('#connect-language');
/** Only the live session and installation lookup establish readiness. */
let language='ko-KR',identity:{handle:string}|null=null,verified=false,busy=true,connected=false,statusKey='checking',needsReload=false;
/** Returns one localized label. */
function t(key:string):string{return translations[language][key];}
/** Reflects readiness without adding an extra confirmation or consent control. */
function render():void{
  button.textContent=t(busy?(verified?'connecting':'checking'):needsReload?'retry':identity?'approve':'google');
  button.disabled=busy||connected||!validCode||(!needsReload&&Boolean(identity)&&!verified);
  node('#connect-identity').textContent=identity?`${t('identity')} · @${identity.handle}`:t('signIn');
  status.textContent=statusKey?t(statusKey):'';
}
/** Changes language without resetting the pending installation or approval state. */
function applyLanguage(value:string):void{
  language=value==='ko-KR'?value:'en-US';document.documentElement.lang=language;picker.value=language;
  document.querySelectorAll<HTMLElement>('[data-copy]').forEach(item=>{item.textContent=t(item.dataset.copy!);});
  document.title=`${t('title')} · Reprise`;render();
}
/** Checks the real pending installation automatically before enabling the sole connect action. */
async function checkDevice():Promise<void>{
  verified=false;busy=true;statusKey='checking';render();
  try{
    const response=await fetch(`/api/device/info?code=${encodeURIComponent(code)}`,{credentials:'same-origin'});
    if(response.status===401){identity=null;statusKey='';return;}
    if(!response.ok)throw new Error(response.status===404?'expired':'failure');
    const device=await response.json() as {name:string};
    node('#device-name').textContent=device.name;node('#verified-device').hidden=false;
    verified=true;statusKey='';
  }catch(error){statusKey=error instanceof Error&&error.message==='expired'?'expired':'failure';needsReload=statusKey==='failure';}
  finally{busy=false;render();}
}
/** The connect click itself approves the sharing described immediately above the button. */
form.onsubmit=async event=>{
  event.preventDefault();if(busy||connected||!validCode)return;
  if(needsReload){location.reload();return;}
  if(!identity){location.assign(`/login?return_to=${encodeURIComponent(`/connect?code=${code}`)}&lang=${language}`);return;}
  if(!verified)return;
  busy=true;statusKey='';render();
  try{
    const response=await fetch('/api/device/approve',{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json'},body:JSON.stringify({user_code:code,consent:true})});
    if(response.status===401){identity=null;verified=false;return;}
    if(!response.ok)throw new Error(response.status===404?'expired':'failure');
    connected=true;form.hidden=true;node('#connect-success').hidden=false;
  }catch(error){statusKey=error instanceof Error&&error.message==='expired'?'expired':'failure';if(statusKey==='expired')verified=false;}
  finally{busy=false;render();}
};
picker.onchange=()=>{applyLanguage(picker.value);try{localStorage.setItem('reprise.language',language);}catch{}};
/** Loads the real signed-in account and uses the app-provided request without manual entry. */
async function start():Promise<void>{
  let country:string|null=null,stored:string|null=null;
  const results=await Promise.allSettled([fetch('/api/config').then(response=>response.json()) as Promise<{country:string|null}>,fetch('/api/me').then(async response=>{if(!response.ok)throw new Error();return response.json();}) as Promise<{listener:{handle:string}|null}>]);
  if(results[0].status==='fulfilled')country=results[0].value.country;
  if(results[1].status==='fulfilled')identity=results[1].value.listener;
  try{stored=localStorage.getItem('reprise.language');}catch{}
  busy=false;statusKey=validCode?'':'missing';needsReload=validCode&&results[1].status==='rejected';if(needsReload)statusKey='failure';
  applyLanguage(params.get('lang')||(country==='KR'&&stored?stored:'en-US'));
  if(validCode&&identity)await checkDevice();
}
void start();
