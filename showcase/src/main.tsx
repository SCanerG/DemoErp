import { useEffect, useRef, useState, type ReactNode } from 'react';
import { createRoot } from 'react-dom/client';
import { copy } from './copy';
import { advance, initialScenario, localize, localeHref, repository, source, steps, screens, layers, decisions, documents, type Locale } from './model';
import media from './media.json';
import './style.css';

const imageMeta=(id:string)=>media.find(m=>m.id===id)!;
const imageSet=(id:string)=>[...new Map(imageMeta(id).outputs.map(o=>[o.width,'/media/'+o.name+' '+o.width+'w'])).values()].join(', ');
const ids=['workflows','screens','architecture','quality'];
function External({href,children}:{href:string;children:ReactNode}) {
  return <a href={href} target="_blank" rel="noopener noreferrer">{children}<span aria-hidden="true"> ↗</span></a>;
}
function App() {
  const locale:Locale=location.pathname.startsWith('/en')?'en':'tr';
  const c=copy[locale];
  const t=(v:readonly[string,string])=>localize(v,locale);
  const [theme,setTheme]=useState<'dark'|'light'>(()=>{try{return localStorage.getItem('demoerp-theme')==='light'?'light':'dark';}catch{return 'dark';}});
  const [menu,setMenu]=useState(false);
  const menuButton=useRef<HTMLButtonElement>(null);
  const [hash,setHash]=useState(location.hash);
  useEffect(()=>{const update=()=>setHash(location.hash);window.addEventListener('hashchange',update);return ()=>window.removeEventListener('hashchange',update);},[]);
  useEffect(()=>{if(!menu)return;const escape=(e:KeyboardEvent)=>{if(e.key==='Escape'){setMenu(false);menuButton.current?.focus();}};window.addEventListener('keydown',escape);return ()=>window.removeEventListener('keydown',escape);},[menu]);
  const [step,setStep]=useState(0);
  const [scenario,setScenario]=useState(()=>initialScenario());
  const [filter,setFilter]=useState('all');
  const [device,setDevice]=useState('all');
  const [selected,setSelected]=useState<(typeof screens)[number]|null>(null);
  const dialog=useRef<HTMLDialogElement>(null);
  const opener=useRef<HTMLButtonElement|null>(null);
  useEffect(()=>{document.documentElement.dataset.theme=theme;try{localStorage.setItem('demoerp-theme',theme);}catch{/* Storage is optional. */}},[theme]);
  useEffect(()=>{if(selected)dialog.current?.showModal();},[selected]);
  const close=()=>{dialog.current?.close();setSelected(null);opener.current?.focus();};
  const groups=['all','overview','operations','manufacturing','commercial'];
  const filtered=screens.filter(s=>(filter==='all'||s.group===filter)&&(device==='all'||(device==='mobile'&&s.mobile)||(device==='desktop'&&!s.mobile)));
  const chosen=steps[step];
  return <>
    <a className="skip" href="#main">{c.skip}</a>
    <header className="header">
      <a className="brand" href={'/'+locale+'/'} aria-label="DemoErp · SCGENCO"><img src="/media/scg-monogram.webp" alt="" width="38" height="38"/><span>SCGENCO<span className="brand-sub">/ DemoErp</span></span></a>
      <nav id="mobile-links" aria-label={c.menu} className={menu?'nav open':'nav'}>{ids.map((id,i)=><a key={id} href={'#'+id} onClick={()=>setMenu(false)}>{c.nav[i]}</a>)}</nav>
      <div className="tools"><a className="language" href={localeHref(locale==='tr'?'en':'tr',hash)} hrefLang={locale==='tr'?'en':'tr'} lang={locale==='tr'?'en':'tr'} aria-label={locale==='tr'?'Switch to English':'Türkçeye geç'}>{locale==='tr'?'EN':'TR'}</a><button onClick={()=>setTheme(theme==='dark'?'light':'dark')} aria-label={c.theme}>{theme==='dark'?'☀':'☾'}</button><button ref={menuButton} className="menu-button" aria-expanded={menu} aria-controls="mobile-links" onClick={()=>setMenu(!menu)}>{c.menu}</button></div>
    </header>
    <main id="main">
      <section className="hero section">
        <div className="hero-copy"><p className="eyebrow"><span className="dot"/> {c.badge}</p><p className="product-name">DemoErp<span> / 01</span></p><h1>{c.hero.split('\n').map((line,i)=><span key={line} className={i===1?'gradient-text':''}>{line}</span>)}</h1><p className="intro">{c.intro}</p><div className="actions"><a className="button primary" href="#workflows">{c.explore} <span aria-hidden="true">↗</span></a><a className="button secondary" href="#screens">{c.realScreens}</a></div><p className="quiet">{c.staticNote}</p><ul className="stack" aria-label={locale==='tr'?'Gerçek ERP teknolojileri':'Real ERP technologies'}>{['.NET 10','React 19','PostgreSQL 17','TypeScript'].map(s=><li key={s}>{s}</li>)}</ul></div>
        <div className="hero-visual"><div className="window-bar"><span aria-hidden="true">● ● ●</span><span>DemoErp / Dashboard</span><span aria-hidden="true">↗</span></div><a href="#screens" className="hero-capture"><img src="/media/dashboard-en-960.webp" srcSet="/media/dashboard-en-640.webp 640w, /media/dashboard-en-960.webp 960w, /media/dashboard-en-1440.webp 1440w" sizes="(max-width: 850px) 92vw, 50vw" width="1440" height="2376" fetchPriority="high" alt={c.heroCaption}/></a><p className="capture-caption">{c.heroCaption}</p><div className="visual-tag"><span className="dot"/> {c.noBackend}</div></div>
      </section>
      <div className="section-divider"><span>SCGENCO / ENGINEERING</span><span>.NET · REACT · POSTGRESQL</span></div>
      <section id="workflows" className="section"><p className="eyebrow">{c.flowEyebrow}</p><h2>{c.flowTitle}</h2><p className="section-intro">{c.flowIntro}</p>
        <div className="workflow-steps" role="group" aria-label={c.nav[0]}>{steps.map((s,i)=><button key={s.path+i} aria-pressed={step===i} onClick={()=>setStep(i)}><span className="step-number">0{i+1}</span>{t(s.title)}<span aria-hidden="true">↗</span></button>)}</div>
        <div className="workflow-detail" aria-live="polite"><div><p className="eyebrow">{t(chosen.module)}</p><h3>{t(chosen.title)}</h3><p>{t(chosen.business)}</p><External href={source(chosen.path)}>{c.implementation}</External></div><dl><dt>{c.data}</dt><dd>{t(chosen.data)}</dd><dt>{c.technical}</dt><dd>{t(chosen.technical)}</dd></dl></div>
        <div className="simulation"><div className="sim-heading"><div><p className="eyebrow">{c.fictional}</p><h3>{c.simTitle}</h3></div><span className="pill">{locale==='tr'?'Örnek veri':'Sample data'}</span></div><p className="sim-note">{c.simNote}</p>
          <div className="simulation-controls"><label>{c.batch}<select value={scenario.batch} onChange={e=>setScenario(initialScenario(Number(e.target.value)))}>{[1,5,10].map(n=><option key={n} value={n}>{n} {c.unit}</option>)}</select></label><p className="quiet">{c.batchNote}</p></div>
          <dl className="balances">{[c.raw,c.work,c.finished,c.shipped].map((label,i)=><div key={label}><dt>{label}</dt><dd data-testid={'balance-'+i}>{[scenario.raw,scenario.work,scenario.finished,scenario.shipped][i]}</dd></div>)}</dl>
          <div className="sim-progress" aria-live="polite"><p>{c.stage}: <strong>{scenario.stage}/6</strong> · {scenario.stage?t(steps[scenario.stage-1].title):c.empty}</p></div>
          <div className="actions"><button className="button primary" disabled={scenario.stage===6} onClick={()=>{setStep(scenario.stage);setScenario(advance(scenario));}}>{scenario.stage===6?c.complete:c.next} <span aria-hidden="true">→</span></button><button className="button secondary" onClick={()=>setScenario(initialScenario(scenario.batch))}>{c.reset}</button></div>
        </div>
      </section>
      <section id="screens" className="section"><p className="eyebrow">{c.galleryEyebrow}</p><h2>{c.galleryTitle}</h2><p className="section-intro">{c.galleryIntro}</p>
        <div className="gallery-toolbar"><div className="filters" role="group" aria-label={c.nav[1]}>{groups.map((g,i)=><button key={g} aria-pressed={filter===g} onClick={()=>setFilter(g)}>{c.filters[i]}</button>)}</div><div className="device-select"><label htmlFor="device-filter">{locale==='tr'?'Cihaz':'Device'}</label><select id="device-filter" value={device} onChange={e=>setDevice(e.target.value)}>{['all','desktop','mobile'].map((v,i)=><option key={v} value={v}>{c.devices[i]}</option>)}</select></div></div>
        <div className="gallery-grid">{filtered.map(s=><article className={'screen-card '+(s.mobile?'mobile-capture':'')} key={s.id}><button className="screen-open" aria-label={c.open+': '+t(s.label)} onClick={e=>{opener.current=e.currentTarget;setSelected(s);}}><div className="screen-image"><img src={'/media/'+s.id+'-640.webp'} srcSet={imageSet(s.id)} sizes="(max-width: 650px) 90vw, (max-width: 1000px) 45vw, 30vw" width={imageMeta(s.id).width} height={imageMeta(s.id).height} loading="lazy" alt={t(s.description)}/><span className="expand" aria-hidden="true">↗</span></div><div className="screen-info"><h3>{t(s.label)}</h3><span className="quiet">{s.mobile?c.devices[2]:c.devices[1]}</span></div></button></article>)}</div>{!filtered.length&&<p role="status">{c.noScreens}</p>}<p className="quiet video-note">{c.videoNote}</p>
      </section>
      <section id="architecture" className="section"><p className="eyebrow">{c.archEyebrow}</p><h2>{c.archTitle}</h2>
        <div className="architecture-grid"><article className="architecture-real"><h3>{c.realArch}</h3><p className="quiet">{c.layerHint}</p><ol className="layers">{layers.map((layer,i)=><li key={layer.name}><details><summary><span className="step-number">0{i+1}</span>{layer.name}<span aria-hidden="true">+</span></summary><p>{t(layer.detail)}</p><External href={source(layer.path)}>{c.implementation}</External></details></li>)}</ol></article><aside className="static-architecture"><span className="pill">STATIC / 0 API</span><h3>{c.staticArch}</h3><div className="static-nodes"><span>React</span><span aria-hidden="true">↓</span><span>{locale==='tr'?'Statik dosyalar':'Static assets'}</span><span aria-hidden="true">↓</span><span>Cloudflare Pages</span></div><p>{c.staticArchText}</p></aside></div>
        <h3 className="subheading">{c.decisionsTitle}</h3><div className="decisions">{decisions.map((d,i)=><details key={d.path+i}><summary><span className="step-number">0{i+1}</span>{t(d.title)}<span aria-hidden="true">+</span></summary><div className="decision-body"><dl>{[[c.problem,d.problem],[c.approach,d.approach],[c.why,d.why],[c.evidence,d.evidence]].map(([label,text])=><div key={label as string}><dt>{label as string}</dt><dd>{t(text as readonly[string,string])}</dd></div>)}</dl><div className="actions"><External href={source(d.path)}>{c.implementation}</External><External href={source(d.test)}>{c.tests}</External></div></div></details>)}</div>
      </section>
      <section id="quality" className="section"><p className="eyebrow">{c.qualityEyebrow}</p><h2>{c.qualityTitle}</h2><div className="test-counts">{[417,16,68].map((n,i)=><div key={n}><strong>{n}</strong><span>{c.testLabels[i]}</span></div>)}</div><p className="evidence-note">{c.historical}</p><p className="section-intro">{c.tools}</p><div className="actions"><External href={source('VERIFICATION.md')}>{c.report}</External><External href={repository+'/actions'}>{c.actions}</External></div>
        <div className="docs-block"><h3>{c.docs}</h3><div className="doc-links">{documents.map(([path,tr,en])=><External key={path} href={source(path)}>{locale==='tr'?tr:en}</External>)}</div></div>
        <div className="local-install"><div><p className="eyebrow">LOCAL / FULL STACK</p><h3>{c.localTitle}</h3><p>{c.localText}</p><External href={source(locale==='tr'?'README.tr.md':'README.md')}>{c.install}</External></div><div><pre><code>docker compose up --build</code></pre><p className="quiet">{c.localNote}</p></div></div>
      </section>
    </main>
    <footer className="section footer"><div><a className="brand" href={'https://scgenco.com/'+locale+'/'}><img src="/media/scg-monogram.webp" width="32" height="32" alt=""/><span>{c.back} ↗</span></a><p>{c.footer}</p></div><div><p>{c.review}</p><External href={repository}>{c.source}</External><p className="quiet">{c.noBackend}</p></div></footer>
    <dialog ref={dialog} className="image-dialog" aria-labelledby="dialog-title" onCancel={close} onKeyDown={e=>{
      if(e.key!=='Tab')return;
      const controls=Array.from(e.currentTarget.querySelectorAll<HTMLElement>('button:not(:disabled), a[href]')).filter(node=>node.getClientRects().length>0);
      const first=controls[0], last=controls[controls.length-1];
      if(e.shiftKey && document.activeElement===first){e.preventDefault();last?.focus();}
      else if(!e.shiftKey && document.activeElement===last){e.preventDefault();first?.focus();}
    }} onClick={e=>{if(e.target===e.currentTarget)close();}}>{selected&&<><div className="dialog-toolbar"><h2 id="dialog-title">{t(selected.label)}</h2><button autoFocus onClick={close}>{c.close} ×</button></div><p>{t(selected.description)}</p><div className="dialog-image"><img src={'/media/'+selected.id+'-1440.webp'} alt={t(selected.description)}/></div><External href={source('docs/screenshots/'+selected.id+'.png')}>{c.original}</External></>}</dialog>
  </>;
}
createRoot(document.getElementById('root')!).render(<App/>);
