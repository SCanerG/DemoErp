import { Fragment, type ReactNode } from 'react';
// Deliberately small, non-HTML Markdown subset. URLs/images are labels only: never execute HTML or fetch model URLs.
function inline(text: string): ReactNode[] {
  const pattern = /(\*\*([^*\n]+)\*\*|__([^_\n]+)__|`([^`\n]+)`|\*([^*\n]+)\*|_([^_\n]+)_|!?\[([^\]\n]*)\]\([^\s)]*\))/g;
  const parts: ReactNode[] = []; let start = 0;
  for (const match of text.matchAll(pattern)) {
    if (match.index > start) parts.push(text.slice(start, match.index));
    const key = match.index;
    parts.push(match[2] || match[3] ? <strong key={key}>{match[2] ?? match[3]}</strong>
      : match[4] ? <code key={key} className="rounded bg-slate-100 px-1">{match[4]}</code>
      : match[5] || match[6] ? <em key={key}>{match[5] ?? match[6]}</em> : <Fragment key={key}>{match[7]}</Fragment>);
    start = match.index + match[0].length;
  }
  if (start < text.length) parts.push(text.slice(start));
  return parts;
}
export function AiResponseText({ text }: { text: string }) {
  const lines = text.slice(0, 8000).replace(/\r\n/g, '\n').split('\n'); const blocks: ReactNode[] = [];
  for (let i = 0; i < lines.length;) {
    const line = lines[i]; const key = i;
    if (!line.trim() || /^\s*([-*_])\1{2,}\s*$/.test(line)) { i++; continue; }
    if (/^\s*```/.test(line)) {
      const content: string[] = []; i++;
      while (i < lines.length && !/^\s*```/.test(lines[i])) content.push(lines[i++]);
      i++; blocks.push(<pre key={key} className="overflow-auto whitespace-pre-wrap rounded bg-slate-50 p-3 text-sm">{content.join('\n')}</pre>); continue;
    }
    if (/^\s*#{1,6}\s+/.test(line)) { blocks.push(<h3 key={key} className="font-semibold">{inline(line.replace(/^\s*#{1,6}\s+/, '').replace(/\s+#+$/, ''))}</h3>); i++; continue; }
    const list = line.match(/^\s*(?:([-*+])|\d+[.)])\s+(.+)$/);
    if (list) {
      const ordered = !list[1]; const items: ReactNode[] = [];
      while (i < lines.length) {
        const m = lines[i].match(/^\s*(?:([-*+])|\d+[.)])\s+(.+)$/);
        if (!m || !m[1] !== ordered) break;
        items.push(<li key={i}>{inline(m[2])}</li>); i++;
      }
      blocks.push(ordered ? <ol key={key} className="list-decimal space-y-2 pl-5">{items}</ol> : <ul key={key} className="list-disc space-y-2 pl-5">{items}</ul>); continue;
    }
    const paragraph: ReactNode[] = [];
    do {
      if (paragraph.length) paragraph.push(<br key={`br-${i}`} />);
      paragraph.push(<Fragment key={i}>{inline(lines[i].replace(/^\s*>\s?/, ''))}</Fragment>); i++;
    } while (i < lines.length && lines[i].trim() && !/^\s*(?:#{1,6}\s|```|[-*+]\s|\d+[.)]\s)/.test(lines[i]));
    blocks.push(<p key={key}>{paragraph}</p>);
  }
  return <div className="space-y-3 whitespace-normal break-words leading-7">{blocks}</div>;
}
