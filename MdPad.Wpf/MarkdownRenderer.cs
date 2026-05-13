using System.IO;
using System.Net;
using System.Text.Json;
using Markdig;

namespace MdPad.Wpf;

public sealed class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private readonly string _githubCss;
    private readonly string _highlightCss;
    private readonly string _highlightJs;

    public MarkdownRenderer()
    {
        var assetDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");
        _githubCss = ReadAsset(Path.Combine(assetDirectory, "github-markdown-light.min.css"));
        _highlightCss = ReadAsset(Path.Combine(assetDirectory, "highlight-github.min.css"));
        _highlightJs = ReadAsset(Path.Combine(assetDirectory, "highlight.min.js"));
    }

    public string RenderDocument(string title, string markdown, string fontFamily, double fontSize)
    {
        var articleHtml = Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
        var titleJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(title) ? "MD Pad" : title);
        var fontJson = JsonSerializer.Serialize(fontFamily);
        var articleJson = JsonSerializer.Serialize(articleHtml);

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
              background: #ffffff;
              color: #24292f;
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
            .markdown-body li:has(input[type="checkbox"]:checked),
            .markdown-body li.mn-checked {
              text-decoration: line-through !important;
              color: #6b7280 !important;
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
              border: 1px solid #d8dee4;
              border-radius: 10px;
              overflow: hidden;
              background: #f6f8fa;
            }
            .code-toolbar {
              display: flex;
              justify-content: space-between;
              align-items: center;
              gap: 8px;
              padding: 8px 10px;
              border-bottom: 1px solid #d8dee4;
              background: #ffffff;
            }
            .code-actions {
              display: flex;
              align-items: center;
              gap: 6px;
              min-width: 0;
            }
            .code-chip {
              border: 1px solid #d0d7de;
              border-radius: 999px;
              background: #f6f8fa;
              color: #57606a;
              padding: 3px 9px;
              font-size: 12px;
              line-height: 1.2;
            }
            button.code-chip {
              cursor: pointer;
            }
            select.code-chip {
              max-width: 92px;
              outline: none;
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
            {{_githubCss}}
            {{_highlightCss}}
          </style>
        </head>
        <body>
          <article class="markdown-body"></article>
          <script>{{_highlightJs}}</script>
          <script>
            (() => {
              const payload = {
                title: {{titleJson}},
                articleHtml: {{articleJson}},
                fontFamily: {{fontJson}},
                fontSize: {{fontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
              };
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
              const article = document.querySelector('.markdown-body');
              document.documentElement.style.setProperty('--pad-font-family', `"${payload.fontFamily}", "Malgun Gothic", "Segoe UI", sans-serif`);
              document.documentElement.style.setProperty('--pad-font-size', `${payload.fontSize}px`);
              document.title = payload.title;
              article.innerHTML = payload.articleHtml || '';
              registerPowerShellHighlight();
              const languages = ['text', 'txt', 'markdown', 'bash', 'shell', 'powershell', 'sql', 'json', 'xml', 'html', 'css', 'javascript', 'typescript', 'python', 'csharp', 'java', 'cpp', 'yaml'];
              article.querySelectorAll('li').forEach((item) => {
                const checkbox = item.querySelector('input[type="checkbox"]');
                if (checkbox) item.classList.toggle('mn-checked', !!checkbox.checked);
              });
              let codeIndex = 0;
              article.querySelectorAll('pre > code').forEach((code) => {
                const pre = code.parentElement;
                if (!pre || pre.parentElement?.classList.contains('code-wrap')) return;
                const languageClass = Array.from(code.classList).find((item) => item.startsWith('language-')) || 'language-text';
                const language = languageClass.replace(/^language-/, '') || 'text';
                const wrap = document.createElement('section');
                wrap.className = 'code-wrap';
                const toolbar = document.createElement('div');
                toolbar.className = 'code-toolbar';
                const right = document.createElement('div');
                right.className = 'code-actions';
                const toggle = document.createElement('button');
                toggle.type = 'button';
                toggle.className = 'code-chip';
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
                const copy = document.createElement('button');
                copy.type = 'button';
                copy.className = 'code-chip';
                copy.textContent = '복사';
                const currentIndex = codeIndex++;
                toggle.addEventListener('click', (event) => {
                  event.stopPropagation();
                  wrap.classList.toggle('is-collapsed');
                  toggle.textContent = wrap.classList.contains('is-collapsed') ? '펼치기' : '접기';
                });
                wrapButton.addEventListener('click', (event) => {
                  event.stopPropagation();
                  wrap.classList.toggle('is-wrapped');
                });
                badge.addEventListener('change', (event) => {
                  event.stopPropagation();
                  post({ type: 'change-code-language', blockIndex: currentIndex, language: badge.value || 'txt' });
                });
                copy.addEventListener('click', async (event) => {
                  event.stopPropagation();
                  const text = code.textContent || '';
                  try { await navigator.clipboard.writeText(text); } catch {}
                  post({ type: 'copy-code', blockIndex: currentIndex, codeText: text });
                });
                right.appendChild(toggle);
                right.appendChild(wrapButton);
                right.appendChild(copy);
                toolbar.appendChild(badge);
                toolbar.appendChild(right);
                pre.parentNode.insertBefore(wrap, pre);
                wrap.appendChild(toolbar);
                wrap.appendChild(pre);
                if (window.hljs) window.hljs.highlightElement(code);
              });
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
}
