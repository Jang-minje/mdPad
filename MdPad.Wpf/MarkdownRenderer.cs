using System.IO;
using System.Net;
using System.Text.Json;
using Markdig;

namespace MdPad.Wpf;

public sealed class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    private readonly string _githubLightCss;
    private readonly string _githubDarkCss;
    private readonly string _highlightCss;
    private readonly string _highlightJs;

    public MarkdownRenderer()
    {
        var assetDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");
        _githubLightCss = ReadAsset(Path.Combine(assetDirectory, "github-markdown-light.min.css"));
        _githubDarkCss = ReadAsset(Path.Combine(assetDirectory, "github-markdown-dark.min.css"));
        _highlightCss = ReadAsset(Path.Combine(assetDirectory, "highlight-github.min.css"));
        _highlightJs = ReadAsset(Path.Combine(assetDirectory, "highlight.min.js"));
    }

    public string RenderPayload(
        string title,
        string markdown,
        string fontFamily,
        double fontSize,
        IReadOnlyDictionary<string, CodeBlockViewState>? codeBlockStates = null)
    {
        var articleHtml = Markdown.ToHtml(PrepareMarkdownForRender(markdown), Pipeline);
        return JsonSerializer.Serialize(new
        {
            title = string.IsNullOrWhiteSpace(title) ? "MD Pad" : title,
            articleHtml,
            fontFamily,
            fontSize,
            codeStates = codeBlockStates ?? new Dictionary<string, CodeBlockViewState>(),
        });
    }

    private static string PrepareMarkdownForRender(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        return NormalizeLeadingThematicBreakBlock(markdown);
    }

    private static string NormalizeLeadingThematicBreakBlock(string markdown)
    {
        var offset = markdown.Length > 0 && markdown[0] == '\uFEFF' ? 1 : 0;
        var firstLineEnd = markdown.IndexOf('\n', offset);
        if (firstLineEnd < 0)
        {
            return markdown;
        }

        var firstLine = markdown[offset..firstLineEnd].TrimEnd('\r');
        if (!string.Equals(firstLine.Trim(), "---", StringComparison.Ordinal))
        {
            return markdown;
        }

        var searchIndex = firstLineEnd + 1;
        while (searchIndex < markdown.Length)
        {
            var lineEnd = markdown.IndexOf('\n', searchIndex);
            var nextIndex = lineEnd < 0 ? markdown.Length : lineEnd + 1;
            var line = markdown[searchIndex..(lineEnd < 0 ? markdown.Length : lineEnd)].TrimEnd('\r');
            if (string.Equals(line.Trim(), "---", StringComparison.Ordinal))
            {
                var content = markdown[(firstLineEnd + 1)..searchIndex].TrimEnd('\r', '\n');
                var body = markdown[nextIndex..].TrimStart('\r', '\n');
                var prefix = offset == 1 ? "\uFEFF" : string.Empty;
                return $"{prefix}---\n\n{content}\n\n---\n\n{body}";
            }

            searchIndex = nextIndex;
        }

        return markdown;
    }

    public string RenderDocument(
        string title,
        string markdown,
        string fontFamily,
        double fontSize,
        ThemeMode theme,
        IReadOnlyDictionary<string, CodeBlockViewState>? codeBlockStates = null)
    {
        var payloadJson = RenderPayload(title, markdown, fontFamily, fontSize, codeBlockStates);
        var isDark = theme == ThemeMode.Dark;
        var pageBackground = isDark ? "#0d1117" : "#ffffff";
        var textColor = isDark ? "#e6edf3" : "#24292f";
        var mutedColor = isDark ? "#8b949e" : "#6b7280";
        var borderColor = isDark ? "#30363d" : "#d8dee4";
        var tableAltBackground = isDark ? "#161b22" : "#f6f8fa";
        var inlineCodeBackground = isDark ? "#6e768166" : "#f6f8fa";
        var codeBackground = isDark ? "#161b22" : "#f6f8fa";
        var toolbarBackground = isDark ? "#0d1117" : "#ffffff";
        var chipBackground = isDark ? "#21262d" : "#f6f8fa";
        var chipText = isDark ? "#e6edf3" : "#57606a";
        var linkColor = isDark ? "#58a6ff" : "#0969da";
        var quoteColor = isDark ? "#8b949e" : "#59636e";
        var markBackground = isDark ? "#9e6a03" : "#fff8c5";
        var markText = isDark ? "#e6edf3" : "#1f2328";
        var githubCss = isDark && !string.IsNullOrWhiteSpace(_githubDarkCss) ? _githubDarkCss : _githubLightCss;

        return $$"""
        <!doctype html>
        <html lang="ko">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <style>
            :root {
              --pad-font-family: "Malgun Gothic", "Segoe UI", sans-serif;
              --pad-font-size: 16px;
            }
            html, body {
              margin: 0;
              background: {{pageBackground}};
              color: {{textColor}};
              font-family: var(--pad-font-family);
              font-size: var(--pad-font-size);
            }
            .markdown-body {
              box-sizing: border-box;
              max-width: 1120px;
              margin: 0 auto;
              padding: 28px 34px 56px;
              font-family: var(--pad-font-family) !important;
              font-size: var(--pad-font-size) !important;
            }
            {{githubCss}}
            {{_highlightCss}}
            .markdown-body table {
              display: block;
              width: max-content;
              max-width: 100%;
              overflow-x: auto;
            }
            .markdown-body img {
              max-width: 100%;
              border-radius: 0 !important;
            }
            .markdown-body li.task-list-item,
            .markdown-body li:has(input[type="checkbox"]) {
              cursor: pointer;
            }
            .markdown-body,
            .markdown-body h1,
            .markdown-body h2,
            .markdown-body h3,
            .markdown-body h4,
            .markdown-body h5,
            .markdown-body h6 {
              color: {{textColor}} !important;
              background: transparent !important;
            }
            .markdown-body {
              color-scheme: {{(isDark ? "dark" : "light")}};
              background: {{pageBackground}} !important;
            }
            .markdown-body a {
              color: {{linkColor}} !important;
            }
            .markdown-body h1,
            .markdown-body h2 {
              border-bottom-color: {{borderColor}} !important;
            }
            .markdown-body h6,
            .markdown-body blockquote,
            .markdown-body .footnotes {
              color: {{quoteColor}} !important;
            }
            .markdown-body hr,
            .markdown-body table tr,
            .markdown-body table th,
            .markdown-body table td,
            .markdown-body blockquote {
              border-color: {{borderColor}} !important;
            }
            .markdown-body table tr {
              background: {{pageBackground}} !important;
            }
            .markdown-body table tr:nth-child(2n),
            .markdown-body table th {
              background: {{tableAltBackground}} !important;
            }
            .markdown-body mark {
              background: {{markBackground}} !important;
              color: {{markText}} !important;
            }
            .markdown-body code,
            .markdown-body tt {
              background: {{inlineCodeBackground}} !important;
              color: {{textColor}} !important;
            }
            .markdown-body pre,
            .markdown-body .highlight pre {
              background: {{codeBackground}} !important;
              color: {{textColor}} !important;
            }
            .markdown-body pre code,
            .markdown-body pre tt {
              background: transparent !important;
            }
            .markdown-body li:has(input[type="checkbox"]:checked),
            .markdown-body li.mn-checked {
              text-decoration: line-through !important;
              color: {{mutedColor}} !important;
            }
            .markdown-body li:has(input[type="checkbox"]:checked) *,
            .markdown-body li.mn-checked * {
              text-decoration: line-through !important;
            }
            .markdown-body input[type="checkbox"] {
              pointer-events: none;
              margin-right: 8px;
            }
            .code-wrap {
              margin: 16px 0;
              border: 1px solid {{borderColor}};
              border-radius: 10px;
              overflow: hidden;
              background: {{codeBackground}};
            }
            .code-toolbar {
              display: flex;
              justify-content: space-between;
              align-items: center;
              gap: 8px;
              padding: 8px 10px;
              border-bottom: 1px solid {{borderColor}};
              background: {{toolbarBackground}};
              font-size: max(11px, calc(var(--pad-font-size) * .82));
            }
            .code-toolbar-left {
              display: flex;
              align-items: center;
              gap: 8px;
              min-width: 0;
              flex: 1 1 auto;
            }
            .code-actions {
              display: flex;
              align-items: center;
              gap: 6px;
              min-width: 0;
              flex: 0 0 auto;
            }
            .code-title {
              flex: 1 1 auto;
              min-width: 0;
              overflow: hidden;
              text-overflow: ellipsis;
              white-space: nowrap;
              color: {{mutedColor}};
              font-size: inherit;
            }
            .code-chip {
              border: 1px solid {{borderColor}};
              border-radius: 999px;
              background: {{chipBackground}};
              color: {{chipText}};
              padding: 3px 9px;
              font-size: inherit;
              line-height: 1.2;
            }
            button.code-chip {
              cursor: pointer;
            }
            select.code-chip {
              max-width: 92px;
              outline: none;
            }
            .marc-control {
              display: inline-block;
              margin: 0 1px;
              padding: 0 3px;
              border-radius: 4px;
              background: {{(isDark ? "#3b2f12" : "#fff3bf")}};
              color: {{(isDark ? "#f2cc60" : "#9a6700")}};
              font-weight: 700;
            }
            mark.mdpad-search-highlight {
              background: {{markBackground}} !important;
              color: {{markText}} !important;
              border-radius: 2px;
              padding: 0 1px;
            }
            mark.mdpad-search-highlight.mdpad-search-current {
              background: #f59e0b !important;
              color: #111827 !important;
              box-shadow: 0 0 0 2px rgba(245, 158, 11, .25);
            }
            .markdown-body .mdpad-search-render-hit {
              border-radius: 4px;
              background: {{markBackground}} !important;
              color: {{markText}} !important;
              box-shadow: 0 0 0 2px rgba(245, 158, 11, .12);
            }
            .markdown-body .mdpad-search-render-hit.mdpad-search-current {
              background: #f59e0b !important;
              color: #111827 !important;
              box-shadow: 0 0 0 2px rgba(245, 158, 11, .25);
            }
            .code-wrap.is-current-search-hit {
              border-color: #f59e0b;
              box-shadow: 0 0 0 3px rgba(245, 158, 11, .28);
            }
            .code-wrap.is-search-hit {
              animation: mdpad-code-pulse 1.1s ease-in-out 0s 3;
              border-color: #f59e0b;
              box-shadow: 0 0 0 2px rgba(245, 158, 11, .22);
            }
            @keyframes mdpad-code-pulse {
              0%, 100% { box-shadow: 0 0 0 0 rgba(245, 158, 11, .0); }
              50% { box-shadow: 0 0 0 4px rgba(245, 158, 11, .35); }
            }
            .code-wrap.is-collapsed pre {
              display: none;
            }
            .code-wrap.is-wrapped pre code {
              white-space: pre-wrap !important;
              overflow-wrap: anywhere;
            }
            .code-wrap pre {
              margin: 0 !important;
              border: 0 !important;
              border-radius: 0 !important;
            }
            {{DarkHighlightCss(isDark)}}
          </style>
        </head>
        <body>
          <article class="markdown-body"></article>
          <script>{{_highlightJs}}</script>
          <script>
            (() => {
              const initialPayload = {{payloadJson}};
              const post = (message) => {
                try { window.chrome?.webview?.postMessage(message); } catch {}
              };
              const registerPowerShellHighlight = () => {
                if (!window.hljs || window.hljs.getLanguage?.('powershell')) return;
                window.hljs.registerLanguage('powershell', (hljs) => ({
                  name: 'PowerShell',
                  aliases: ['ps', 'ps1', 'pwsh'],
                  case_insensitive: true,
                  keywords: {
                    keyword: 'begin break catch class continue data do dynamicparam else elseif end exit filter finally for foreach from function if in param process return switch throw trap try until using while',
                    built_in: 'Write-Host Write-Output Write-Error Get-ChildItem Get-Item Get-Content Set-Content Remove-Item Copy-Item Move-Item New-Item Test-Path Select-Object Where-Object ForEach-Object Sort-Object Import-Csv Export-Csv ConvertTo-Json ConvertFrom-Json Invoke-WebRequest Invoke-RestMethod Start-Process Stop-Process Get-Process Get-Service',
                    literal: '$true $false $null'
                  },
                  contains: [
                    hljs.HASH_COMMENT_MODE,
                    hljs.QUOTE_STRING_MODE,
                    hljs.APOS_STRING_MODE,
                    { className: 'variable', begin: '\\$[A-Za-z_][\\w:]*' },
                    { className: 'built_in', begin: '\\b[A-Z][a-z]+-[A-Z][A-Za-z]+\\b' },
                    { className: 'number', begin: '\\b\\d+(\\.\\d+)?\\b' }
                  ]
                }));
              };
              const registerMarcHighlight = () => {
                if (!window.hljs || window.hljs.getLanguage?.('marc')) return;
                window.hljs.registerLanguage('marc', () => ({
                  name: 'MARC',
                  case_insensitive: true,
                  contains: [
                    { className: 'section', begin: '^[0-9]{3}', relevance: 10 },
                    { className: 'symbol', begin: '\\uE01F[a-z0-9]', relevance: 8 },
                    { className: 'meta', begin: '[\\uE01E\\uE01D]', relevance: 5 },
                    { className: 'number', begin: '\\b[0-9]{2}\\b', relevance: 1 }
                  ]
                }));
              };
              const toMarcDisplayText = (text) => (text || '')
                .replace(/\x1F/g, '\uE01F')
                .replace(/\x1E/g, '\uE01E')
                .replace(/\x1D/g, '\uE01D');
              const visualizeMarcControls = (root) => {
                const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
                  acceptNode: (node) => /[\x1F\x1E\x1D\uE01F\uE01E\uE01D]/.test(node.nodeValue || '')
                    ? NodeFilter.FILTER_ACCEPT
                    : NodeFilter.FILTER_REJECT
                });
                const nodes = [];
                while (walker.nextNode()) nodes.push(walker.currentNode);
                nodes.forEach((node) => {
                  const text = node.nodeValue || '';
                  const fragment = document.createDocumentFragment();
                  for (const ch of text) {
                    if (ch === '\x1F' || ch === '\x1E' || ch === '\x1D' || ch === '\uE01F' || ch === '\uE01E' || ch === '\uE01D') {
                      const span = document.createElement('span');
                      span.className = 'marc-control';
                      span.textContent = ch === '\x1F' || ch === '\uE01F' ? '␟' : ch === '\x1E' || ch === '\uE01E' ? '␞' : '␝';
                      fragment.appendChild(span);
                    } else {
                      fragment.appendChild(document.createTextNode(ch));
                    }
                  }
                  node.parentNode?.replaceChild(fragment, node);
                });
              };
              const article = document.querySelector('.markdown-body');
              registerPowerShellHighlight();
              registerMarcHighlight();
              const languages = ['text', 'txt', 'markdown', 'marc', 'bash', 'shell', 'powershell', 'sql', 'json', 'xml', 'html', 'css', 'javascript', 'typescript', 'python', 'csharp', 'java', 'cpp', 'yaml'];
              const codeHash = (text) => {
                let hash = 2166136261 >>> 0;
                for (let i = 0; i < (text || '').length; i += 1) {
                  hash ^= text.charCodeAt(i);
                  hash = Math.imul(hash, 16777619) >>> 0;
                }
                return hash.toString(16).padStart(8, '0');
              };
              const codeTitle = (text) => {
                const first = ((text || '').split(/\r?\n/)[0] || '').trim();
                if (first.startsWith('--')) return first.slice(2).trim();
                if (first.startsWith('#')) return first.replace(/^#+\s*/, '').trim();
                if (first.startsWith('/*')) return first.replace(/^\/\*\s*/, '').replace(/\s*\*\/$/, '').trim();
                return '';
              };
              const setCollapsedVisual = (wrap, toggle, collapsed) => {
                wrap.classList.toggle('is-collapsed', !!collapsed);
                toggle.textContent = collapsed ? '펼치기' : '접기';
              };
              const setWrappedVisual = (wrap, wrapped) => {
                wrap.classList.toggle('is-wrapped', !!wrapped);
              };
              const buildArticle = (target, payload) => {
                document.documentElement.style.setProperty('--pad-font-family', `"${payload.fontFamily}", "Malgun Gothic", "Segoe UI", sans-serif`);
                document.documentElement.style.setProperty('--pad-font-size', `${payload.fontSize}px`);
                document.title = payload.title || 'MD Pad';
                target.innerHTML = payload.articleHtml || '';
                target.querySelectorAll('li').forEach((item) => {
                  const checkbox = item.querySelector('input[type="checkbox"]');
                  if (checkbox) item.classList.toggle('mn-checked', !!checkbox.checked);
                });
                let codeIndex = 0;
                target.querySelectorAll('pre > code').forEach((code) => {
                  const pre = code.parentElement;
                  if (!pre || pre.parentElement?.classList.contains('code-wrap')) return;
                  const languageClass = Array.from(code.classList).find((item) => item.startsWith('language-')) || 'language-text';
                  const language = languageClass.replace(/^language-/, '') || 'text';
                  const text = (code.textContent || '').replace(/\r\n/g, '\n').replace(/\r/g, '\n');
                  const currentIndex = codeIndex++;
                  const stateKey = `${currentIndex}:${codeHash(text)}`;
                  const state = payload.codeStates?.[stateKey] || {};
                  const wrap = document.createElement('section');
                  wrap.className = 'code-wrap';
                  wrap.dataset.blockIndex = String(currentIndex);
                  wrap.dataset.stateKey = stateKey;
                  const toolbar = document.createElement('div');
                  toolbar.className = 'code-toolbar';
                  const left = document.createElement('div');
                  left.className = 'code-toolbar-left';
                  const right = document.createElement('div');
                  right.className = 'code-actions';
                  const toggle = document.createElement('button');
                  toggle.type = 'button';
                  toggle.className = 'code-chip';
                  toggle.dataset.role = 'collapse-toggle';
                  toggle.textContent = '접기';
                  const wrapButton = document.createElement('button');
                  wrapButton.type = 'button';
                  wrapButton.className = 'code-chip';
                  wrapButton.textContent = '줄바꿈';
                  const badge = document.createElement('select');
                  badge.className = 'code-chip';
                  const normalizedLanguage = language === 'text' ? 'txt' : language;
                  const optionSet = new Set([normalizedLanguage, ...languages]);
                  optionSet.forEach((item) => {
                    const option = document.createElement('option');
                    option.value = item;
                    option.textContent = item;
                    badge.appendChild(option);
                  });
                  badge.value = normalizedLanguage;
                  const title = document.createElement('span');
                  title.className = 'code-title';
                  title.textContent = codeTitle(text);
                  title.title = title.textContent;
                  const edit = document.createElement('button');
                  edit.type = 'button';
                  edit.className = 'code-chip';
                  edit.textContent = '편집';
                  const copy = document.createElement('button');
                  copy.type = 'button';
                  copy.className = 'code-chip';
                  copy.textContent = '복사';
                  toggle.addEventListener('click', (event) => {
                    event.stopPropagation();
                    const collapsed = !wrap.classList.contains('is-collapsed');
                    setCollapsedVisual(wrap, toggle, collapsed);
                    post({ type: 'set-code-collapsed', key: stateKey, collapsed });
                  });
                  wrapButton.addEventListener('click', (event) => {
                    event.stopPropagation();
                    const wrapped = !wrap.classList.contains('is-wrapped');
                    setWrappedVisual(wrap, wrapped);
                    post({ type: 'set-code-wrapped', key: stateKey, wrapped });
                  });
                  badge.addEventListener('change', (event) => {
                    event.stopPropagation();
                    post({ type: 'change-code-language', blockIndex: currentIndex, language: badge.value || 'txt' });
                  });
                  edit.addEventListener('click', (event) => {
                    event.stopPropagation();
                    post({ type: 'edit-code-block', blockIndex: currentIndex });
                  });
                  copy.addEventListener('click', async (event) => {
                    event.stopPropagation();
                    try { await navigator.clipboard.writeText(text); } catch {}
                    post({ type: 'copy-code', blockIndex: currentIndex, codeText: text });
                  });
                  left.appendChild(badge);
                  if (title.textContent) left.appendChild(title);
                  right.appendChild(edit);
                  right.appendChild(toggle);
                  right.appendChild(wrapButton);
                  right.appendChild(copy);
                  toolbar.appendChild(left);
                  toolbar.appendChild(right);
                  pre.parentNode.insertBefore(wrap, pre);
                  wrap.appendChild(toolbar);
                  wrap.appendChild(pre);
                  setCollapsedVisual(wrap, toggle, !!(state.collapsed ?? state.Collapsed));
                  setWrappedVisual(wrap, !!(state.wrapped ?? state.Wrapped));
                  if (normalizedLanguage === 'marc') code.textContent = toMarcDisplayText(text);
                  if (window.hljs) window.hljs.highlightElement(code);
                  if (normalizedLanguage === 'marc') visualizeMarcControls(code);
                });
              };
              window.mdPadRenderPayload = (payload) => {
                if (!article) return;
                buildArticle(article, payload);
              };
              window.mdPadRenderPayload(initialPayload);
              article.addEventListener('click', (event) => {
                const target = event.target instanceof Element ? event.target : event.target?.parentElement;
                if (!target) return;
                const link = target.closest('a[href]');
                if (link) {
                  event.preventDefault();
                  post({ type: 'open-link', href: link.href });
                  return;
                }
                const item = target.closest('li');
                if (!item || !item.querySelector('input[type="checkbox"]')) return;
                event.preventDefault();
                const clone = item.cloneNode(true);
                clone.querySelectorAll('input').forEach((node) => node.remove());
                post({ type: 'toggle-task', label: clone.textContent?.trim() || '' });
              });
              const clearSearchHighlights = () => {
                article.querySelectorAll('mark.mdpad-search-highlight').forEach((mark) => {
                  const parent = mark.parentNode;
                  if (!parent) return;
                  parent.replaceChild(document.createTextNode(mark.textContent || ''), mark);
                  parent.normalize();
                });
                article.querySelectorAll('.mdpad-search-render-hit').forEach((item) => {
                  item.classList.remove('mdpad-search-render-hit', 'mdpad-search-current');
                });
                article.querySelectorAll('.code-wrap.is-search-hit').forEach((wrap) => wrap.classList.remove('is-search-hit'));
                article.querySelectorAll('.code-wrap.is-current-search-hit').forEach((wrap) => wrap.classList.remove('is-current-search-hit'));
              };
              const searchState = { query: '', current: -1, hits: [] };
              const searchInfo = (query) => {
                const raw = (query || '').toString().trim();
                const terms = new Set();
                const structured = [];
                const addStructured = (selector, text, options = {}) => {
                  const value = (text || '').trim().toLowerCase();
                  if (value) structured.push({ selector, text: value, ...options });
                };
                const task = raw.match(/^[-*+]\s+\[[ xX]\]\s+(.+)$/);
                const unordered = task ? null : raw.match(/^[-*+]\s+(.+)$/);
                const ordered = raw.match(/^\d+[\.)]\s+(.+)$/);
                const inlineCode = raw.match(/^`([^`]+)`$/);
                const fencedCode = raw.match(/^```[^\r\n]*\r?\n([\s\S]*?)\r?\n```$/);
                const heading = raw.match(/^#{1,6}\s+(.+)$/);
                const blockquote = raw.match(/^>\s+(.+)$/);
                const strong = raw.match(/^(?:\*\*|__)(.+?)(?:\*\*|__)$/);
                const emphasis = !strong ? raw.match(/^(?:\*|_)(.+?)(?:\*|_)$/) : null;
                const strike = raw.match(/^~~(.+?)~~$/);
                const image = raw.match(/^!\[([^\]]*)\]\([^)]+\)$/);
                const link = !image ? raw.match(/^\[([^\]]+)\]\([^)]+\)$/) : null;
                const tableRow = raw.startsWith('|') && raw.endsWith('|')
                  ? raw.split('|').slice(1, -1).map((cell) => cell.trim()).filter((cell) => cell && !/^:?-{3,}:?$/.test(cell))
                  : [];
                addStructured('li', unordered?.[1]);
                addStructured('li', ordered?.[1]);
                addStructured('li', task?.[1]);
                addStructured('h1, h2, h3, h4, h5, h6', heading?.[1]);
                addStructured('blockquote', blockquote?.[1]);
                addStructured('strong, b', strong?.[1]);
                addStructured('em, i', emphasis?.[1]);
                addStructured('del, s', strike?.[1]);
                addStructured('a', link?.[1]);
                addStructured('img', image?.[1], { attr: 'alt' });
                addStructured('code', inlineCode?.[1], { excludeClosest: 'pre' });
                addStructured('.code-wrap pre code', fencedCode?.[1], { markClosest: '.code-wrap' });
                if (tableRow.length) {
                  structured.push({ selector: 'tr', cells: tableRow.map((cell) => cell.toLowerCase()) });
                }
                if (raw && !structured.length) terms.add(raw.toLowerCase());
                return {
                  terms: Array.from(terms),
                  structured
                };
              };
              const exactText = (element) => (element?.textContent || '').trim().toLowerCase();
              const markExactRenderedHits = ({ selector, text, attr, cells, excludeClosest, markClosest }) => {
                article.querySelectorAll(selector).forEach((element) => {
                  if (excludeClosest && element.closest(excludeClosest)) return;
                  const target = markClosest ? element.closest(markClosest) : element;
                  if (!target) return;
                  if (cells) {
                    const actualCells = Array.from(element.querySelectorAll('th, td'))
                      .map((cell) => exactText(cell))
                      .filter(Boolean);
                    if (actualCells.length === cells.length && actualCells.every((cell, index) => cell === cells[index])) {
                      target.classList.add('mdpad-search-render-hit');
                    }
                    return;
                  }
                  const actual = attr ? (element.getAttribute(attr) || '').trim().toLowerCase() : exactText(element);
                  if (actual === text) {
                    target.classList.add('mdpad-search-render-hit');
                  }
                });
              };
              const collectSearchHits = () => {
                searchState.hits = [
                  ...Array.from(article.querySelectorAll('mark.mdpad-search-highlight')),
                  ...Array.from(article.querySelectorAll('.mdpad-search-render-hit')),
                  ...Array.from(article.querySelectorAll('.code-wrap.is-search-hit'))
                ];
              };
              const highlightTextNode = (node, terms) => {
                const text = node.nodeValue || '';
                const lower = text.toLowerCase();
                const matches = [];
                terms.forEach((term) => {
                  let current = lower.indexOf(term);
                  while (current >= 0) {
                    matches.push({ index: current, length: term.length });
                    current = lower.indexOf(term, current + term.length);
                  }
                });
                matches.sort((a, b) => a.index - b.index || b.length - a.length);
                const filtered = [];
                let coveredUntil = -1;
                matches.forEach((match) => {
                  if (match.index >= coveredUntil) {
                    filtered.push(match);
                    coveredUntil = match.index + match.length;
                  }
                });
                if (!filtered.length) return;
                const fragment = document.createDocumentFragment();
                let cursor = 0;
                filtered.forEach((match) => {
                  if (match.index > cursor) fragment.appendChild(document.createTextNode(text.slice(cursor, match.index)));
                  const mark = document.createElement('mark');
                  mark.className = 'mdpad-search-highlight';
                  mark.textContent = text.slice(match.index, match.index + match.length);
                  fragment.appendChild(mark);
                  cursor = match.index + match.length;
                });
                if (cursor < text.length) fragment.appendChild(document.createTextNode(text.slice(cursor)));
                node.parentNode?.replaceChild(fragment, node);
              };
              window.mdPadHighlightSearch = (query) => {
                clearSearchHighlights();
                const term = (query || '').toString();
                searchState.query = term;
                searchState.current = -1;
                searchState.hits = [];
                if (!term) return;
                const { terms, structured } = searchInfo(term);
                if (!terms.length && !structured.length) return;
                structured.forEach(markExactRenderedHits);
                article.querySelectorAll('.code-wrap').forEach((wrap) => {
                  const code = wrap.querySelector('pre code');
                  const codeText = (code?.textContent || '').toLowerCase();
                  if (wrap.classList.contains('is-collapsed') && terms.some((item) => codeText.includes(item))) {
                    wrap.classList.add('is-search-hit');
                  }
                });
                const walker = document.createTreeWalker(article, NodeFilter.SHOW_TEXT, {
                  acceptNode: (node) => {
                    const parent = node.parentElement;
                    if (!parent || !node.nodeValue?.trim()) return NodeFilter.FILTER_REJECT;
                    if (parent.closest('.code-toolbar, script, style')) return NodeFilter.FILTER_REJECT;
                    const lower = node.nodeValue.toLowerCase();
                    return terms.some((item) => lower.includes(item))
                      ? NodeFilter.FILTER_ACCEPT
                      : NodeFilter.FILTER_REJECT;
                  }
                });
                const nodes = [];
                while (walker.nextNode()) nodes.push(walker.currentNode);
                nodes.forEach((node) => highlightTextNode(node, terms));
                collectSearchHits();
              };
              window.mdPadFindNext = (query, forward) => {
                const term = (query || '').toString();
                if (term !== searchState.query || searchState.hits.length === 0) {
                  window.mdPadHighlightSearch(term);
                }
                if (!searchState.hits.length) return;
                searchState.hits.forEach((hit) => hit.classList.remove('mdpad-search-current', 'is-current-search-hit'));
                searchState.current = forward
                  ? (searchState.current + 1) % searchState.hits.length
                  : (searchState.current - 1 + searchState.hits.length) % searchState.hits.length;
                const current = searchState.hits[searchState.current];
                if (current.classList.contains('code-wrap')) {
                  current.classList.add('is-current-search-hit');
                } else if (current.classList.contains('mdpad-search-render-hit')) {
                  current.classList.add('mdpad-search-current');
                } else {
                  current.classList.add('mdpad-search-current');
                }
                current.scrollIntoView({ block: 'center', inline: 'nearest' });
              };
              window.mdPadSetAllCodeCollapsed = (collapsed) => {
                article.querySelectorAll('.code-wrap').forEach((wrap) => {
                  const toggle = wrap.querySelector('button[data-role="collapse-toggle"]');
                  const key = wrap.dataset.stateKey || '';
                  if (!toggle) return;
                  setCollapsedVisual(wrap, toggle, !!collapsed);
                  post({ type: 'set-code-collapsed', key, collapsed: !!collapsed });
                });
              };
              window.addEventListener('wheel', (event) => {
                if (!event.ctrlKey) return;
                event.preventDefault();
                post({ type: 'adjust-font-size', delta: event.deltaY < 0 ? 1 : -1 });
              }, { passive: false });
            })();
          </script>
        </body>
        </html>
        """;
    }

    private static string ReadAsset(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static string DarkHighlightCss(bool isDark)
    {
        if (!isDark)
        {
            return string.Empty;
        }

        return """
        .hljs {
          color: #e6edf3 !important;
          background: #161b22 !important;
        }
        .hljs-comment,
        .hljs-quote {
          color: #8b949e !important;
        }
        .hljs-keyword,
        .hljs-selector-tag,
        .hljs-subst {
          color: #ff7b72 !important;
        }
        .hljs-number,
        .hljs-literal,
        .hljs-variable,
        .hljs-template-variable,
        .hljs-tag .hljs-attr {
          color: #79c0ff !important;
        }
        .hljs-string,
        .hljs-doctag {
          color: #a5d6ff !important;
        }
        .hljs-title,
        .hljs-section,
        .hljs-selector-id {
          color: #d2a8ff !important;
        }
        .hljs-type,
        .hljs-class .hljs-title {
          color: #ffa657 !important;
        }
        .hljs-tag,
        .hljs-name,
        .hljs-attribute {
          color: #7ee787 !important;
        }
        .hljs-regexp,
        .hljs-link {
          color: #a5d6ff !important;
        }
        .hljs-symbol,
        .hljs-bullet {
          color: #f2cc60 !important;
        }
        .hljs-built_in,
        .hljs-builtin-name {
          color: #ffa657 !important;
        }
        .hljs-meta {
          color: #8b949e !important;
        }
        .hljs-deletion {
          color: #ffdcd7 !important;
          background-color: #67060c !important;
        }
        .hljs-addition {
          color: #aff5b4 !important;
          background-color: #033a16 !important;
        }
        """;
    }
}
