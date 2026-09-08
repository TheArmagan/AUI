# Diagnostics

Every message AUI can produce. Codes are stable: one is never renumbered or reused.

Messages look like this in the console, and are clickable:

```
[AUI] Assets/UI/Shop.aui:14:23 error AUI201: unknown style property 'colour'
```

| Range | Area |
|---|---|
| 0xx | markup syntax |
| 1xx | markup semantics |
| 2xx | stylesheet |
| 3xx | languages |
| 4xx | layout |
| 5xx | build and codegen (editor only) |
| 6xx | fonts (editor only) |

`AUI/Validate` compiles every view and prints all of them, including warnings a successful build
would not stop for.

## 0xx Markup syntax

| Code | Meaning and fix |
|---|---|
| `AUI001` | Unexpected character. Usually a stray `<` or `>` in text; escape it as `&lt;` / `&gt;` |
| `AUI002` | A tag was never closed. Close it, or make it self-closing (`<text ... />`) |
| `AUI003` | Closing tag does not match the open one |
| `AUI004` | More than one root element. A `.aui` has exactly one `<view>` |
| `AUI005` | Attribute value is not quoted. Write `max="8"`, not `max=8` |
| `AUI006` | The same attribute twice on one element |
| `AUI007` | The file has no content |
| `AUI008` | A bare `&` that is not an entity. Write `&amp;` |
| `AUI009` | Unknown entity. Supported: `&lt; &gt; &amp; &quot; &apos; &nbsp; &#NN; &#xHH;` |
| `AUI010` | Text outside the root element |
| `AUI011` | A `<!--` with no `-->` |
| `AUI012` | An attribute quote was never closed |

## 1xx Markup semantics

| Code | Meaning and fix |
|---|---|
| `AUI100` | Unknown tag, and no `.aui` of that name exists. Check spelling; the message suggests a near match |
| `AUI101` | That attribute is not allowed on this element. See [elements](/reference/elements) |
| `AUI102` | Two elements share an `id` |
| `AUI103` | `name` does not equal the file stem. Rename one of them |
| `AUI104` | The root element is not `<view>` |
| `AUI105` | A `<repeat>` must have exactly one template child, plus an optional `<empty>` |
| `AUI106` | A `<tab>`'s `page` names no page in the group |
| `AUI107` | A nested view was used but `Name.aui` does not exist |
| `AUI108` | One binding name used with two different types. Rename one |
| `AUI109` | A handler value is not a C# identifier |
| `AUI110` | A required attribute is missing (a `<view>` without `name`, for instance) |
| `AUI111` | An attribute value does not parse for that attribute |
| `AUI112` | The element is not allowed there. See [where each element may go](/reference/elements#where-each-element-may-go) |
| `AUI113` | Views nest each other in a cycle |
| `AUI114` | A `<pages>` group nothing drives. Add a `<tabs>`, or select pages from code |
| `AUI115` | A `<text>` has both `key` and `text`/literal content. Pick one |
| `AUI116` | Malformed `{binding}`, or a binding whose name collides with an `AuiView` member |
| `AUI117` | Literal, untranslated text. **Informational**, so you can find every string that will stay English |
| `AUI118` | `{item.x}` used outside a `<repeat>` or `<template>` |
| `AUI119` | No such icon. See [icons](/reference/icons) |
| `AUI120` | A repeat's `max` is outside 1 to 64 |
| `AUI121` | An attribute that is not a declared `<prop>` on a child that declares props |
| `AUI122` | Malformed `<prop>` declaration |

## 2xx Stylesheet

| Code | Meaning and fix |
|---|---|
| `AUI200` | Stylesheet syntax error |
| `AUI201` | Unknown property. See [style properties](/reference/properties) |
| `AUI202` | The value does not parse for that property's kind |
| `AUI203` | `var(--x)` with no `:root` declaration. Define it, or use `var(--x, fallback)` |
| `AUI204` | Unknown pseudo-state. Only `hover`, `pressed`, `selected`, `disabled` |
| `AUI205` | Unsupported selector, usually a descendant combinator (`.card text`). Give the child its own class |
| `AUI206` | A rule no element matches. **Warning**; often a typo or a leftover |
| `AUI207` | A class used in markup that no sheet defines |
| `AUI208` | `<link href>` points at a file that does not exist |
| `AUI209` | Malformed `transition` value |
| `AUI210` | A state rule sets a property that cannot be animated. Only `color`, `background`, `border`, `icon-color`, `opacity`, `scale`, `offset` are allowed |
| `AUI211` | More than 64 regular classes visible to one view. Move one-off looks into `style=""` or star classes |
| `AUI212` | A bake-only property on a class only reachable at runtime. `radius`, `font-weight`, `page-transition` and `show` cannot change in-world |
| `AUI213` | A bound style value mixing bindings with literal text (`width: {n}mm`), or an unbindable property |
| `AUI214` | More than 256 star classes |

## 3xx Languages

| Code | Meaning and fix |
|---|---|
| `AUI300` | Key missing in some languages. **Warning**, and it lists which |
| `AUI301` | Key missing in the **default** language. An error; the view will not build |
| `AUI302` | Language JSON syntax error. Arrays and nulls are not allowed |
| `AUI303` | `key=` names a key no language file defines |
| `AUI304` | A `{placeholder}` in the string with no matching `args` binding |
| `AUI305` | No language files found. Create at least the default one |
| `AUI306` | The same flattened key defined twice |
| `AUI307` | Placeholders differ between languages, or from what `args` supplies |

See [languages](/guide/languages).

## 4xx Layout

| Code | Meaning and fix |
|---|---|
| `AUI400` | Content overflows even after proportional shrinking. Enlarge the view, or give the text a `max-width` and `wrap="true"` |
| `AUI401` | A solved rectangle has negative size |
| `AUI402` | `position: absolute` with no edge set |
| `AUI403` | An explicit size **and** the opposite edge. The size wins |
| `AUI404` | An edge on a non-absolute element. Ignored |
| `AUI405` | `display` or `position` on the root view |

## 5xx Build and codegen

| Code | Meaning and fix |
|---|---|
| `AUI500` | The class in `Name.g.cs` was not found. Usually a compile error elsewhere; fix it and save again |
| `AUI501` | The sibling UdonSharp program asset is missing. `AUI/Create missing program assets` |
| `AUI502` | A handler named in markup has no method. Add it with the [right signature](/guide/handlers#every-signature) |
| `AUI503` | The prefab builder failed. Read the accompanying exception |
| `AUI504` | A required symbol was not exported to the Udon heap |

## 6xx Fonts

| Code | Meaning and fix |
|---|---|
| `AUI600` | The character set changed since the last bake. Run `AUI/Bake Fonts` |
| `AUI601` | A character has no glyph in any atlas or fallback. This is what boxes in-world look like at build time |
| `AUI602` | A source font under `Fonts/src` is missing |

## Next

- [Troubleshooting](/guide/troubleshooting) for symptoms rather than codes.
- [The VS Code extension](/tooling/vscode), which shows these as you type and offers quick fixes
  for many of them.
