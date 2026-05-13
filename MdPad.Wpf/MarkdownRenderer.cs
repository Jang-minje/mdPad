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

    public string RenderDocument(string title, string markdown, string fontFamily, double fontSize, ThemeMode theme)
    {
        var articleHtml = Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
        var titleJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(title) ? "MD Pad" : title);
        var fontJson = JsonSerializer.Serialize(fontFamily);
        var articleJson = JsonSerializer.Serialize(articleHtml);
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
            }
            .code-actions {
              display: flex;
              align-items: center;
              gap: 6px;
              min-width: 0;
            }
            .code-chip {
              border: 1px solid {{borderColor}};
              border-radius: 999px;
              background: {{chipBackground}};
              color: {{chipText}};
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
            {{DarkHighlightCss(isDark)}}
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
