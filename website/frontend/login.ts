export {};

/** Available translations for the standalone Google sign-in screen. */
const copy: Record<string,Record<string,string>> = {
  'ko-KR':{back:'차트로 돌아가기',title:'Reprise에 로그인',connection:'로그인 후 Reprise 앱을 연결합니다.',agreement:'만 14세 이상이며 이용약관에 동의하고 개인정보 처리방침을 확인했습니다.',terms:'이용약관',privacy:'개인정보 처리방침',google:'Google로 계속하기',loading:'로그인을 준비하고 있어요.',language:'언어',unavailable:'지금은 로그인할 수 없어요. 잠시 후 새로고침해 주세요.',cancelled:'로그인이 취소되었어요. 다시 시도할 수 있습니다.',expired:'로그인 요청이 만료되었어요. 다시 시작해 주세요.',failed:'Google 로그인을 완료하지 못했어요. 다시 시도해 주세요.',redirecting:'Google 로그인으로 이동하고 있어요.'},
  'en-US':{back:'Back to charts',title:'Sign in to Reprise',connection:'Sign in to connect your Reprise app.',agreement:'I am at least 14, agree to the Terms of service and have read the Privacy policy.',terms:'Terms of service',privacy:'Privacy policy',google:'Continue with Google',loading:'Preparing sign-in.',language:'Language',unavailable:'Sign-in is unavailable. Please refresh and try again shortly.',cancelled:'Sign-in was cancelled. You can try again.',expired:'Your sign-in request expired. Please start again.',failed:'Google sign-in could not be completed. Please try again.',redirecting:'Opening Google sign-in.'},
};
/** Requires expected page markup and preserves specific native element types. */
function node<T extends HTMLElement>(selector:string):T {
  const value=document.querySelector<T>(selector);
  if(!value)throw new Error(`Missing login element: ${selector}`);
  return value;
}
/** Only these application destinations may survive authentication. */
const params=new URLSearchParams(location.search);
/** Restricts post-login navigation to local profile or device pairing routes. */
const candidate=params.get('return_to') || '/profile';
/** Never forwards arbitrary URLs or protocol-relative redirect destinations. */
const returnTo=/^\/(?:connect(?:\?code=[A-Z2-9-]+)?|profile)?$/.test(candidate)?candidate:'/profile';
/** Current locale and readiness come from the public deployment configuration. */
let language='ko-KR',ready=false;
/** Native controls remain keyboard accessible and require explicit terms consent. */
const form=node<HTMLFormElement>('#login-form'),agreement=node<HTMLInputElement>('#login-agreement'),button=node<HTMLButtonElement>('#google-login'),status=node<HTMLParagraphElement>('#login-status'),picker=node<HTMLSelectElement>('#login-language');
/** Maps non-sensitive OAuth errors to helpful messages. */
function message():string {
  const error=params.get('error');
  if(!ready)return copy[language].unavailable;
  if(error==='cancelled')return copy[language].cancelled;
  if(error==='expired_oauth_state'||error==='invalid_oauth_state')return copy[language].expired;
  return error?copy[language].failed:'';
}
/** Changes the page language without resetting the user's checkbox or return destination. */
function applyLanguage(value:string):void {
  language=value==='ko-KR'?value:'en-US';document.documentElement.lang=language;picker.value=language;
  document.querySelectorAll<HTMLElement>('[data-copy]').forEach(item=>{item.textContent=copy[language][item.dataset.copy!];});
  document.title=`${copy[language].title} · Reprise`;status.textContent=message();
  button.disabled=!ready||!agreement.checked;
}
/** Fetches deployment state and preserves an existing authenticated pairing flow. */
async function start():Promise<void> {
  let country:string|null=null,stored:string|null=null;
  try {
    const response=await fetch('/api/config');if(!response.ok)throw new Error();
    const configuration=await response.json() as {google_ready:boolean;country:string|null};
    ready=configuration.google_ready;country=configuration.country;
  }catch{ready=false;}
  try{stored=localStorage.getItem('reprise.language');}catch{/* Optional browser storage. */}
  applyLanguage(params.get('lang') || (country==='KR'&&stored?stored:'en-US'));
  node('#connection-note').hidden=!returnTo.startsWith('/connect');
  try {
    const response=await fetch('/api/me');
    if(response.ok&&(await response.json() as {listener:unknown}).listener)location.replace(returnTo);
  }catch{/* The user can still retry sign-in. */}
}
agreement.onchange=()=>{button.disabled=!ready||!agreement.checked;};
picker.onchange=()=>{applyLanguage(picker.value);try{localStorage.setItem('reprise.language',language);}catch{}};
form.onsubmit=event=>{
  event.preventDefault();if(!ready||!agreement.checked)return;
  button.disabled=true;status.textContent=copy[language].redirecting;
  location.assign(`/auth/google?accepted=2026-11-21&return_to=${encodeURIComponent(returnTo)}`);
};
void start();
