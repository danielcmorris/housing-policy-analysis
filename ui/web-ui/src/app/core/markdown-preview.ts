import { marked } from 'marked';

marked.use({ gfm: true, breaks: true });

/* A document rendered as markdown in its own window. Split into open + write
   so callers that fetch the text asynchronously can open the window inside
   the click handler (popup blockers require a user gesture) and fill it when
   the text arrives. */

const esc = (s: string): string =>
  s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** Open the (blank) preview window. Call synchronously from a click handler;
    returns null if the browser blocked the popup. */
export function openPreviewWindow(): Window | null {
  return window.open('', '_blank');
}

export function writePreviewLoading(w: Window, title: string): void {
  w.document.write(`<!doctype html><html><head><meta charset="utf-8">
<title>${esc(title)}</title></head>
<body style="font:15px Georgia,serif;color:#57503f;background:#f6f4ef;padding:48px;">Loading document…</body></html>`);
  w.document.close();
}

/** Replace the window's content with the rendered markdown. The page carries
    a script-blocking CSP, so raw HTML in the document can't run code. */
export function writeMarkdownPreview(w: Window, title: string, banner: string, markdown: string): void {
  const html = marked.parse(markdown, { async: false }) as string;
  w.document.open();
  w.document.write(`<!doctype html>
<html><head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="script-src 'none'">
<title>${esc(title)}</title>
<style>
  body { margin: 0; background: #f6f4ef; font: 16px/1.65 Georgia, 'Times New Roman', serif; color: #26221c; }
  .page { max-width: 780px; margin: 0 auto; padding: 48px 32px 96px; background: #fffdf9;
          min-height: 100vh; box-shadow: 0 0 24px rgba(0,0,0,.06); box-sizing: border-box; }
  .banner { font: 700 11px/1 Arial, sans-serif; letter-spacing: .12em; text-transform: uppercase;
            color: #8a2321; border-bottom: 1px solid #d8d2c6; padding-bottom: 14px; margin-bottom: 32px; }
  h1, h2, h3, h4 { line-height: 1.25; margin: 1.6em 0 .5em; }
  h1 { font-size: 30px; } h2 { font-size: 24px; } h3 { font-size: 19px; }
  p { margin: 0 0 1em; }
  table { border-collapse: collapse; margin: 1.2em 0; font-size: 14px; width: 100%; }
  th, td { border: 1px solid #d8d2c6; padding: 6px 10px; text-align: left; vertical-align: top; }
  th { background: #efece4; }
  blockquote { border-left: 3px solid #8a2321; margin: 1.2em 0; padding: .2em 0 .2em 16px; color: #57503f; }
  code, pre { font: 13px/1.5 ui-monospace, Consolas, monospace; background: #efece4; }
  pre { padding: 12px 14px; overflow-x: auto; } code { padding: 1px 4px; }
  img { max-width: 100%; }
  hr { border: none; border-top: 1px solid #d8d2c6; margin: 2em 0; }
</style>
</head><body><div class="page">
<div class="banner">${esc(banner)}</div>
${html}
</div></body></html>`);
  w.document.close();
}
