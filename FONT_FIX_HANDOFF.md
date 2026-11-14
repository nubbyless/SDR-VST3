# Console font - the 8.25pt form font was tried and reverted

**Status: REVERTED.**  The console is back on the .NET 10 default font
("Segoe UI, 9pt").  This file keeps the record of the experiment and how to put
it back, so nobody has to rediscover it.

## What was tried

One line in the `Console` constructor, `Project Files\Source\Console\console.cs`:

```csharp
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F);
```

placed **before** `InitializeComponent()`, so that the controls are created with
the font and inherit it.

## Why it was tried

.NET 10 changed the default font of a control to "Segoe UI, 9pt"; the .NET
Framework build of Thetis used "Microsoft Sans Serif, 8.25pt".  The console
never sets a form font, so every control without a font of its own inherited the
bigger one - **350 of the 639 controls in `console.resx`** are in that group -
and the text no longer fitted the boxes the designer and the console skin were
laid out for.

The visible symptom was in the VFO Sync box: `labelTS1` "VFO Lock:" (34x28),
`lblBandStack` "Band Stack" (40x28, text anchored bottom right) and
`lblTuneStep` "Tune Step:" (35x33) were the only controls in that box without
an explicit font.  Measured GDI text metrics at 96dpi:

| control | box | net48 (8.25pt) | net10 (Segoe UI 9pt) | effect |
|---|---|---|---|---|
| "VFO Lock:" | 34x28 | 34x26 | 28x**30** | "Lock:" cut off |
| "Band Stack" | 40x28 | 35x26 | 30x**30** | text rode up into the kHz box above it |
| "Tune Step:" | 35x33 | 35x26 | 30x**30** | 2px too tall, and 2px too close to the kHz box |

The DPI awareness change is what made it visible, not the cause: the net48
build was DPI unaware, so Windows bitmap stretched the whole window and text and
boxes always scaled together.  The net10 build is DPI aware (the SDK injects
`<dpiAware>true</dpiAware>`) and renders at the real resolution.

## Why it was reverted

It is a one-line change, but it moves **every** control that has no font of its
own back to 8.25pt, i.e. the whole console text gets ~10 % smaller at once.
The VFO Sync box problem is already solved where it actually was - the three
labels there now carry an explicit "Microsoft Sans Serif, 8.25pt" font (see the
commit "Console: the VFO Sync box labels use the console font"), and the same
treatment can be given to any other label that misbehaves.

The console is kept on the modern Segoe UI look, and the labels that do not fit
get an explicit font of their own.

## If you want it back

1. `Project Files\Source\Console\console.cs`, in the `Console` constructor,
   **before** `InitializeComponent()`:

```csharp
            // .NET 10 changed the default font of a control to "Segoe UI, 9pt"; the .NET
            // Framework build of Thetis used "Microsoft Sans Serif, 8.25pt".  The console
            // never sets a form font, so every control that has no font of its own got the
            // bigger one and the text no longer fitted the boxes the designer and the skin
            // were laid out for.  Put the console back on the font it was drawn with.
            // This has to happen BEFORE InitializeComponent(): the controls are then created
            // with the right font and inherit it.  Setting it afterwards fires a layout pass
            // while the console is still half built and the Andromeda button bar throws.
            // See FONT_FIX_HANDOFF.md for how to undo this.
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F);
```

2. **Do not put the line after `InitializeComponent()`.**  Changing the font of
   a parent fires a layout pass over every child, and while the console is
   still half built `Console.UpdateButtonBarButtons()` throws a
   NullReferenceException (`Andromeda\Andromeda.cs`, `panelButtonBar_Layout`) -
   the console then dies on startup with a "Fatal Error" in `ErrorLog.txt`.
   This was seen when the experiment was first tried.

3. The other forms are not affected by the form font: the setup form, the PS
   form and the rest are separate forms with their own font, and the label
   widenings done for them (see `LAYOUTCHECK_FIXLOG.md`) stay as they are.
   `menuStrip1.Font` is the one control in `console.resx` with an explicit
   "Segoe UI, 9pt" - the menu bar keeps that font either way.
