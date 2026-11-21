export {};
/** Applies the same country-aware initial language policy as the chart screen. */
async function startLegalPage() {
  let country = null, stored = null;
  try { country = (await (await fetch('/api/config')).json()).country; } catch { /* Unknown country defaults to English. */ }
  try { stored = localStorage.getItem('reprise.language'); } catch { /* Storage is optional. */ }
  const explicit = new URLSearchParams(location.search).get('lang');
  const initial = explicit || (country === 'KR' && stored ? stored : 'en-US');
  const picker = document.querySelector<HTMLSelectElement>('#legal-language')!;
  /** Changes both the visible legal document and its language metadata. */
  function apply(value: string) {
    const language = value === 'ko-KR' ? value : 'en-US';
    document.documentElement.lang = language;
    document.querySelectorAll<HTMLElement>('[data-language]').forEach(node => { node.hidden = node.dataset.language !== language; });
    picker.value = language;
    document.title = `${document.querySelector(`[data-language="${language}"] h1`)!.textContent} · Reprise`;
  }
  picker.onchange = event => { apply((event.target as HTMLSelectElement).value);try { localStorage.setItem('reprise.language',(event.target as HTMLSelectElement).value); } catch {} };
  apply(initial);
}
void startLegalPage();
