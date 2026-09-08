import { defineConfig } from 'vitepress'

// Shiki has no grammar for .aui / .auis. The view syntax is XML-shaped and the stylesheet is
// CSS-shaped, which is exactly what the two TextMate grammars in the VS Code extension derive
// from, so the aliases under markdown.languageAlias colour every snippet correctly without
// shipping a grammar here.
export default defineConfig({
  title: 'AUI',
  description:
    'Baked, i18n-first UI engine for UdonSharp worlds. HTML-like markup and a small stylesheet compile in the editor to a plain uGUI hierarchy with generated UdonSharp wiring.',
  lang: 'en-US',
  cleanUrls: true,
  lastUpdated: true,
  appearance: 'dark',

  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/favicon.svg' }],
    ['meta', { name: 'theme-color', content: '#E0A86A' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:title', content: 'AUI' }],
    [
      'meta',
      {
        property: 'og:description',
        content: 'A UI engine for VRChat worlds written in UdonSharp.',
      },
    ],
  ],

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

    // The source is private, so there is no repository link and no edit link. Support and
    // distribution both go through Discord.
    socialLinks: [
      {
        icon: 'discord',
        link: 'https://discord.gg/armagan',
        ariaLabel: 'Discord',
      },
    ],

    search: { provider: 'local' },

    footer: {
      message: 'Need help? <a href="https://discord.gg/armagan">Join the Discord</a>.',
      copyright: 'AUI, a UI engine for VRChat worlds.',
    },

    outline: [2, 3],
  },
})
