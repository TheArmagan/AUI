import { defineConfig } from 'vitepress'

const site = 'https://auidocs.thearmagan.workers.dev'
const ogImage = `${site}/og-image.png`
const description =
  'A UI engine for VRChat worlds. Write a view in an HTML-like file and a small stylesheet; the editor compiles them into a uGUI prefab with generated UdonSharp wiring.'

// Shiki has no grammar for .aui / .auis. The view syntax is XML-shaped and the stylesheet is
// CSS-shaped, which is exactly what the two TextMate grammars in the VS Code extension derive
// from, so the aliases under markdown.languageAlias colour every snippet correctly without
// shipping a grammar here.
export default defineConfig({
  title: 'AUI',
  description,
  lang: 'en-US',
  cleanUrls: true,
  lastUpdated: true,
  appearance: 'dark',
  sitemap: { hostname: site },

  // Site-wide defaults. transformPageData below overrides title, description and url per page,
  // so an embed of a deep link describes that page rather than the home page.
  head: [
    // SVG first for browsers that take it, then the PNGs, then favicon.ico as the last resort
    // for bare /favicon.ico requests from crawlers and older clients.
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/favicon.svg' }],
    ['link', { rel: 'icon', type: 'image/png', sizes: '32x32', href: '/favicon-32.png' }],
    ['link', { rel: 'icon', type: 'image/png', sizes: '16x16', href: '/favicon-16.png' }],
    ['link', { rel: 'apple-touch-icon', sizes: '180x180', href: '/apple-touch-icon.png' }],
    ['link', { rel: 'manifest', href: '/site.webmanifest' }],
    ['link', { rel: 'mask-icon', href: '/favicon.svg', color: '#E0A86A' }],

    ['meta', { name: 'theme-color', content: '#E0A86A' }],
    ['meta', { name: 'author', content: 'Armagan' }],

    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:site_name', content: 'AUI' }],
    ['meta', { property: 'og:locale', content: 'en_US' }],
    ['meta', { property: 'og:image', content: ogImage }],
    ['meta', { property: 'og:image:type', content: 'image/png' }],
    ['meta', { property: 'og:image:width', content: '1200' }],
    ['meta', { property: 'og:image:height', content: '630' }],
    [
      'meta',
      {
        property: 'og:image:alt',
        content: 'An AUI panel in a VRChat world, showing sliders, dropdowns, a text input and buttons.',
      },
    ],

    ['meta', { name: 'twitter:card', content: 'summary_large_image' }],
    ['meta', { name: 'twitter:image', content: ogImage }],
  ],

  transformPageData(pageData) {
    const isHome = pageData.relativePath === 'index.md'
    const title = isHome ? 'AUI' : `${pageData.title ?? 'AUI'} | AUI`
    const desc = pageData.description || pageData.frontmatter.description || description
    const url = `${site}/${pageData.relativePath
      .replace(/\/index\.md$/, '/')
      .replace(/\.md$/, '')
      .replace(/^index$/, '')}`

    pageData.frontmatter.head ??= []
    pageData.frontmatter.head.push(
      ['meta', { property: 'og:title', content: title }],
      ['meta', { property: 'og:description', content: desc }],
      ['meta', { property: 'og:url', content: url }],
      ['meta', { name: 'twitter:title', content: title }],
      ['meta', { name: 'twitter:description', content: desc }],
      ['link', { rel: 'canonical', href: url }],
    )
  },

  markdown: {
    languageAlias: {
      aui: 'xml',
      auis: 'css',
    },
    lineNumbers: false,
  },

  themeConfig: {
    logo: '/logo.svg',
    siteTitle: 'AUI',

    nav: [
      { text: 'Guide', link: '/guide/what-is-aui', activeMatch: '/guide/' },
      { text: 'Reference', link: '/reference/elements', activeMatch: '/reference/' },
      { text: 'Examples', link: '/examples/', activeMatch: '/examples/' },
      { text: 'Tooling', link: '/tooling/vscode', activeMatch: '/tooling/' },
      { text: 'Download', link: 'https://github.com/TheArmagan/AUI/releases/latest' },
      { text: 'Discord', link: 'https://discord.gg/armagan' },
    ],

    sidebar: {
      '/guide/': [
        {
          text: 'Getting started',
          items: [
            { text: 'What AUI is', link: '/guide/what-is-aui' },
            { text: 'Installation', link: '/guide/installation' },
            { text: 'Your first view', link: '/guide/first-view' },
            { text: 'The logic half', link: '/guide/the-logic-half' },
          ],
        },
        {
          text: 'Writing views',
          items: [
            { text: 'Bindings', link: '/guide/bindings' },
            { text: 'Handlers', link: '/guide/handlers' },
            { text: 'Styling', link: '/guide/styling' },
            { text: 'Layout', link: '/guide/layout' },
            { text: 'Languages', link: '/guide/languages' },
          ],
        },
        {
          text: 'Structure',
          items: [
            { text: 'Lists', link: '/guide/lists' },
            { text: 'Pages and tabs', link: '/guide/pages-and-tabs' },
            { text: 'Components', link: '/guide/components' },
          ],
        },
        {
          text: 'At runtime',
          items: [
            { text: 'Changing a view', link: '/guide/runtime' },
            { text: 'Attaching to the player', link: '/guide/attaching' },
            { text: 'Theming', link: '/guide/theming' },
            { text: 'Other Udon scripts', link: '/guide/interop' },
            { text: 'Testing', link: '/guide/testing' },
          ],
        },
        {
          text: 'Background',
          items: [
            { text: 'How it works', link: '/guide/how-it-works' },
            { text: 'Troubleshooting', link: '/guide/troubleshooting' },
          ],
        },
      ],

      '/reference/': [
        {
          text: 'Markup',
          items: [
            { text: 'Elements', link: '/reference/elements' },
            { text: 'Diagnostics', link: '/reference/diagnostics' },
          ],
        },
        {
          text: 'Stylesheet',
          items: [
            { text: 'Style properties', link: '/reference/properties' },
            { text: 'Utility classes', link: '/reference/utilities' },
            { text: 'Icons', link: '/reference/icons' },
            { text: 'Sprites', link: '/reference/sprites' },
          ],
        },
        {
          text: 'Code',
          items: [
            { text: 'View API', link: '/reference/view-api' },
            { text: 'Generated code', link: '/reference/generated-code' },
            { text: 'Services and settings', link: '/reference/services' },
          ],
        },
        {
          text: 'Editor',
          items: [{ text: 'Menu and windows', link: '/reference/menu' }],
        },
      ],

      '/examples/': [
        {
          text: 'Examples',
          items: [
            { text: 'Overview', link: '/examples/' },
            { text: 'Settings panel', link: '/examples/settings-panel' },
            { text: 'The gallery', link: '/examples/gallery-pages' },
            { text: 'Recipes', link: '/examples/recipes' },
          ],
        },
      ],

      '/tooling/': [
        {
          text: 'Tooling',
          items: [
            { text: 'VS Code extension', link: '/tooling/vscode' },
            { text: 'Language server', link: '/tooling/language-server' },
          ],
        },
      ],
    },

    // Downloads are the GitHub releases (and the VPM listing built from them); support is the
    // Discord.
    socialLinks: [
      {
        icon: 'github',
        link: 'https://github.com/TheArmagan/AUI',
        ariaLabel: 'GitHub',
      },
      {
        icon: 'discord',
        link: 'https://discord.gg/armagan',
        ariaLabel: 'Discord',
      },
    ],

    search: { provider: 'local' },

    footer: {
      message: 'Need help? <a href="https://discord.gg/armagan">Join the Discord</a>.',
      copyright: 'AUI, a UI engine for VRChat worlds. Released under the <a href="https://github.com/TheArmagan/AUI/blob/main/LICENSE">MPL-2.0</a>.',
    },

    outline: [2, 3],
  },
})
