---
layout: home
description: A UI engine for VRChat worlds. Write a view in an HTML-like file and a small stylesheet; the editor compiles them into a uGUI prefab with generated UdonSharp wiring, and every string is a translation key.

hero:
  name: AUI
  text: UI for VRChat worlds
  tagline: Write your UI like HTML and CSS. AUI builds the uGUI prefab and the UdonSharp code for you. Every text is a translation key, so translating your world is just writing a JSON file.
  image:
    src: /logo.svg
    alt: AUI
  actions:
    - theme: brand
      text: Get started
      link: /guide/installation
    - theme: alt
      text: What AUI is
      link: /guide/what-is-aui
    - theme: alt
      text: Element reference
      link: /reference/elements

features:
  - title: Markup and a stylesheet
    details: A view is an .aui file of nested elements and an .auis stylesheet with classes, states, variables and transitions. Both are checked as you type.
    icon: '<svg viewBox="0 0 24 24"><path d="m18 16 4-4-4-4"/><path d="m6 8-4 4 4 4"/><path d="m14.5 4-5 16"/></svg>'
    link: /guide/first-view
  - title: Nothing is parsed in-world
    details: Layout and the cascade are solved in the editor and baked into tables. At runtime the view re-runs those tables in Udon, so it stays cheap.
    icon: '<svg viewBox="0 0 24 24"><path d="M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z"/></svg>'
    link: /guide/how-it-works
  - title: Translated by default
    details: Every visible string is a key. Players switch language in-game and every live view repaints, with fonts baked for exactly the glyphs your languages use.
    icon: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"/><path d="M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20"/><path d="M2 12h20"/></svg>'
    link: /guide/languages
  - title: Typed code, both directions
    details: Handlers and bindings become real C# members on a generated partial class, so your world scripts talk to a view through properties, not strings.
    icon: '<svg viewBox="0 0 24 24"><path d="M10 12.5 8 15l2 2.5"/><path d="m14 12.5 2 2.5-2 2.5"/><path d="M14 2v4a2 2 0 0 0 2 2h4"/><path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7z"/></svg>'
    link: /guide/interop
  - title: Changes at runtime
    details: Show and hide elements, add classes, set style values, rewrite the palette, create rows from a pool, open a modal. The solver re-runs; no element is instantiated in-world.
    icon: '<svg viewBox="0 0 24 24"><path d="M20 7h-9"/><path d="M14 17H5"/><circle cx="17" cy="17" r="3"/><circle cx="7" cy="7" r="3"/></svg>'
    link: /guide/runtime
  - title: Editor tooling
    details: A language server behind a VS Code extension gives diagnostics, completion for keys and classes, go to definition, rename, quick fixes and a live preview of the generated C#.
    icon: '<svg viewBox="0 0 24 24"><path d="M12 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.375 2.625a1 1 0 0 1 3 3l-9.013 9.014a2 2 0 0 1-.853.505l-2.873.84a.5.5 0 0 1-.62-.62l.84-2.873a2 2 0 0 1 .506-.852z"/></svg>'
    link: /tooling/vscode
---

![A panel built with AUI, running in a VRChat world project](/img/gallery-hub.png){.shot}

## In short

```aui
<view name="Hello" width="300" height="140">
  <text key="hello.title" class="title"/>
  <text key="hello.greeting" args="{playerName}"/>
  <panel class="row">
    <spacer/>
    <button onclick="Wave" key="hello.wave" icon="hand"/>
  </panel>
</view>
```

```csharp
public partial class Hello : AuiView
{
    protected override void OnStart()
    {
        PlayerName = Networking.LocalPlayer.displayName;
    }

    public void Wave() { /* ... */ }
}
```

Save the file. The editor writes `Hello.g.cs`, scaffolds `Hello.cs` once, and builds
`Hello.prefab`. Drag the prefab into the scene.

[Install it](/guide/installation) or read [what AUI is and is not](/guide/what-is-aui) first.
