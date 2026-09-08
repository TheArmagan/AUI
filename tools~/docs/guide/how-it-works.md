# How it works

You do not need this page to use AUI, but it explains the two things people ask about first: why
saving a view reloads the domain, and what actually ships to the world.

## The pipeline

One function compiles a view, and both the Unity importer and the language server call it. The
stages, in order:

1. **Parse** the markup into a tree, collecting syntax diagnostics.
2. **Collect stylesheets** in tier order: the package theme, the built-in utility classes, every
   `<link href>` (or the sibling `Name.auis` if nothing is linked), then `<style>` blocks.
3. **Expand** inline components and pools, so what follows sees ordinary elements.
4. **Cascade**: resolve the final style of every element.
5. **Analyze**: build the semantic model, which is where bindings, handlers, repeats, page groups,
   selects, modals and nested views are discovered, with their types and signatures.
6. **Check translation keys** against the merged language table.
7. **Index map**: assign every runtime element its index.
8. **Rule tables**: turn the cascade into the flat tables the runtime re-resolves from.
9. **Layout solve**: measure real text with the baked fonts, across every language, and place
   every rectangle.
10. **Codegen**: emit `Name.g.cs`.

Then everything that shapes the prefab is hashed. The prefab is skipped on rebuild when that hash
still matches.

## Why a domain reload

An asset importer cannot write a `.cs` file and then add a component of that class in the same
pass, because the class does not exist until Unity has compiled it. So the work is split:

- The **importer** only compiles and records diagnostics. No side effects.
- A **queue** then runs in two steps around the reload:
  - **Step A** writes `Name.g.cs` if its text changed, and scaffolds `Name.cs` once. If anything
    was written, it stops there and lets Unity recompile. The queue survives the reload.
  - **Step B** creates the program asset, runs one UdonSharp compile for the whole batch, and
    builds the prefabs. Children build before parents.

That is why the console shows a pause in the middle. It only happens when the generated **source**
changed: the `.g.cs` header hash covers the generated code alone, so a layout-only edit (moving a
panel, changing a colour) rebuilds the prefab without reloading the domain.

## What ships to the world

The markup does not ship. What ends up in the prefab is:

- a plain uGUI hierarchy: `RectTransform`, `Image`, `TextMeshProUGUI`, the usual components
- an `AuiSignal` per interactive element, relaying uGUI events into the view
- the generated behaviour, holding integer tables: element parents and siblings, per-element tag
  and class masks, the rule table, layout sizes per language, page and repeat and modal tables

At runtime the view re-runs those tables. Nothing is parsed from text in-world, and no element is
instantiated.

## Caching

A save costs one compile. The importer's result is kept and reused by the queue, validated by the
hashes of the files it read. Text measurements are cached for the session. The workspace scan only
repeats when a view or icon is added, removed or moved. The language table is only rebuilt when a
language file changes.

The console reports where the time went:

```
[AUI] drain: 3 view(s), 1 built, 412 ms (scan 0, compile 96 with 2 cached, udonsharp 210, build 106)
```

`udonsharp` is paid once per drain, and only when a view is new.

## The constraints that shaped it

A few Udon facts explain design decisions that otherwise look odd:

- **No `Update`.** Events arrive through the per-element signal relay, and delayed work uses
  `SendCustomEventDelayedSeconds` with sequence guards.
- **Fields written by another behaviour must be public and serialized.** That is why a view has
  many `[HideInInspector] public` fields and why event payloads travel through `emitS`/`emitI`/
  `emitF`/`emitB`.
- **Parameters are heap slots**, so a re-entrant method would overwrite its own arguments. Hence
  the stash fields in the base class.
- **TMP's measurement API is not exposed**, so text is measured through `LayoutUtility`.
- **Udon cannot swap a font asset**, which is why font weight is bake-only.

## Next

- [Generated code](/reference/generated-code) for exactly what the `.g.cs` contains.
- [Menu and windows](/reference/menu) for the build controls.
- [Troubleshooting](/guide/troubleshooting) when the pipeline does something you did not expect.
